using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CleaveTests
{
    private sealed class Dice(int hits = 2, int blocks = 0) : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public int DefenceRolls { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackRolls++ < hits ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceRolls++ < blocks ? DefenceFace.Block : DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException("Cleave must not roll dice.");
    }

    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState Scenario(int targetHp = 10) => new()
    {
        Physical = new(new Board(5, 5, [new(new(2, 2), new(2, 3), EdgeKind.ClosedDoor)]),
            [new("barbarian", new(2, 2)), new("target", new(3, 2)), new("other", new(1, 1))]),
        Types = [UnitType.Barbarian(), new("enemy", 0, 0, 0, 2, 10) { Unique = true }, new("other-enemy", 0, 0, 0, 2, 10) { Unique = true }],
        Units = [UnitType.Barbarian().CreateUnit("barbarian", "heroes"),
            new("target", "enemy", "enemies", targetHp), new("other", "other-enemy", "third-side", 2)]
    };

    private static EngineResult Choose(GameState state, string? key, IRandomProvider? dice = null, bool auto = false) =>
        TestGame.Advance(state, new Choice(key), dice ?? new Dice(), auto);
    private static EngineResult Attack(GameState? state = null, int hits = 2, int blocks = 0)
    {
        var started = TestGame.StartRound(state ?? Scenario(), new Dice(), false);
        var moved = Choose(started.State, "stay");
        return Choose(moved.State, "attack:target", new Dice(hits, blocks));
    }
    private static Unit Barbarian(GameState state) => state.Units.Single(u => u.Id == "barbarian");
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 2, true)]
    [InlineData(3, 2, false)]
    [InlineData(3, 3, true)]
    public void ConfiguredTriggerAndDamageDriveUnfamiliarTypeAndSurvivePendingFollowUp(int trigger, int hits, bool offered)
    {
        var scenario = Scenario();
        var type = new UnitType("unfamiliar-cleaver", 0, 1, 4, 0, 5, FreeActions: UnitFreeAction.OpenDoor)
        { Unique = true,
            Cleave = new(MaxUses: 2) { TriggerDamage = trigger, Damage = 3 }
        };
        scenario.Types[0] = type;
        scenario.Units[0] = type.CreateUnit("barbarian", "heroes");
        var attack = Attack(Restore(scenario), hits: hits, blocks: trigger == 1 ? 1 : 0);
        Assert.Equal(offered, attack.State.CleavePending);
        var restored = Restore(attack.State);
        Assert.Equal(type.Cleave, restored.Types[0].Cleave);
        var entry = Assert.Single(restored.Types[0].CardEntries(), e => e.Id == "cleave");
        Assert.Equal($"After this Unit's Attack, if it dealt {trigger} or more damage to a Unit, you may choose an adjacent enemy and immediately deal 3 damage to it.", entry.Description);
        Assert.Equal("2/game", entry.UseLimitText);
        if (!offered) return;

        Assert.Equal(DecisionKind.Cleave, restored.Pending!.Kind);
        Assert.True(restored.Pending.AllowsNone);
        var declined = Choose(Restore(restored), null);
        Assert.Equal(new AbilityUses(2, 2), Barbarian(declined.State).CleaveUses);
        var dice = new Dice();
        var result = Choose(restored, "cleave:other", dice);
        Assert.Equal(0, result.State.Units[2].CurrentHp);
        Assert.Equal(new[] { "CleaveResolved", "UnitDefeated" }, TestGame.OperationEvents(result).Select(e => e.Kind));
        Assert.Equal(3, TestGame.OperationEvents(result)[0].Damage);
        Assert.Equal("Cleave", TestGame.OperationEvents(result)[0].AbilityName);
        Assert.Equal(new AbilityUses(2, 1), Barbarian(result.State).CleaveUses);
        Assert.Equal(0, dice.AttackRolls);
        Assert.Equal(0, dice.DefenceRolls);
        Assert.Equal(type.Cleave, TestGame.OperationSteps(result)[0].StateAfter.Types[0].Cleave);
        Assert.False(result.State.CleavePending);
    }

    [Fact]
    public void ContentAndBareUnitsInitializeSeparateTwoUseCounter()
    {
        var type = UnitType.Barbarian();
        Assert.Equal(new Cleave(2), type.Cleave);
        Assert.Equal(new AbilityUses(2, 2), type.CreateUnit("b", "side").CleaveUses);
        Assert.DoesNotContain(type.BonusActions, a => a.Name == "Cleave");
        Assert.Null(UnitType.Goblin().Cleave);
        var scenario = Scenario();
        scenario.Units[0] = new("barbarian", type.Id, "heroes", 5);
        var started = TestGame.StartRound(scenario, new Dice(), false);
        Assert.Equal(new AbilityUses(2, 2), Barbarian(started.State).CleaveUses);
        Assert.Null(Barbarian(scenario).CleaveUses);
        Assert.Equal(new Cleave(2), Restore(started.State).Types[0].Cleave);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 0, true)]
    [InlineData(4, 1, true)]
    public void TriggerUsesUncappedHitsMinusBlocks(int hits, int blocks, bool offered)
    {
        var result = Attack(hits: hits, blocks: blocks);
        Assert.Equal(Math.Max(0, hits - blocks), Assert.Single(TestGame.OperationEvents(result)).Damage);
        Assert.Equal(offered, result.State.CleavePending);
        Assert.Equal(offered ? DecisionKind.Cleave : DecisionKind.Activation, result.NextInput!.Kind);
    }

    [Fact]
    public void OverkillAndDefeatFinishBeforeOnlyLivingTargetsAreOffered()
    {
        var result = Attack(Scenario(targetHp: 1));
        Assert.Equal(new[] { "AttackResolved", "UnitDefeated" }, TestGame.OperationEvents(result).Select(e => e.Kind));
        Assert.Equal(2, TestGame.OperationEvents(result)[0].Damage);
        Assert.Equal(0, result.State.Units[1].CurrentHp);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "target");
        Assert.Equal("other", Assert.Single(result.NextInput!.Candidates).TargetId);
        Assert.True(result.NextInput.AllowsNone); // Even one target requires agency.
        Assert.Null(TestGame.OperationSteps(result)[0].StateAfter.Pending);
        Assert.DoesNotContain(TestGame.OperationSteps(result)[0].StateAfter.Physical.Figures, f => f.Id == "target");
        Assert.DoesNotContain(TestGame.OperationSteps(result)[1].StateAfter.Physical.Figures, f => f.Id == "target");
    }

    [Fact]
    public void EachAdjacentHostileHasOneCandidateAndFriendsAndDistantUnitsAreExcluded()
    {
        var scenario = Scenario();
        scenario.Types.Add(new("friend-type", 0, 0, 0, 2, 10) { Unique = true });
        scenario.Types.Add(new("far-type", 0, 0, 0, 2, 10) { Unique = true });
        scenario.Units.AddRange([new("friend", "friend-type", "heroes", 10), new("far", "far-type", "enemies", 10)]);
        scenario.Physical.Figures.AddRange([new("friend", new(2, 1)), new("far", new(4, 4))]);
        var result = Attack(scenario);
        Assert.Equal(new[] { "target", "other" }, result.NextInput!.Candidates.Select(c => c.TargetId));
        Assert.All(result.NextInput.Candidates, c =>
        {
            Assert.Equal(ActivationChoiceKind.Cleave, c.Kind);
            Assert.True(c.Relevant);
            Assert.Null(c.Action);
            Assert.Null(c.BonusAction);
        });
        Assert.Empty(GameEngine.GameplayCandidates(result.State, Barbarian(result.State)));
        foreach (var illegal in new[] { "end-turn", "bonus-action:Rage", "stay", "attack:target", "open-door:2,2:2,3", "cleave:friend" })
            Assert.Throws<ArgumentException>(() => Choose(result.State, illegal));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.WallWithWindow, true)]
    public void CandidatesUseSharedLosIncludingBothDiagonalPassages(EdgeKind edge, bool offered)
    {
        var scenario = Scenario(targetHp: 1);
        scenario.Physical.Board.Edges.AddRange([
            new(new(2, 2), new(1, 2), edge), new(new(2, 2), new(2, 1), edge)]);
        var result = Attack(scenario);
        Assert.Equal(offered, result.State.CleavePending);
        Assert.Equal(SpatialRules.AreAdjacent(scenario.Physical.Board, new(2, 2), new(1, 1)), offered);
        if (!offered)
        {
            scenario.Physical.Board.Edges.RemoveAt(scenario.Physical.Board.Edges.Count - 1);
            Assert.True(Attack(scenario).State.CleavePending); // One open passage suffices.
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseSpendsOneAndDealsDirectDamageWithNormalDefeatAndSnapshots(bool kill)
    {
        var scenario = Scenario();
        if (kill) scenario.Units[2] = scenario.Units[2] with { CurrentHp = 1 };
        var attack = Attack(scenario);
        var restored = Restore(attack.State);
        restored.Pending!.Candidates.Clear(); // Client data cannot remove authoritative choices.
        var dice = new Dice();
        var result = Choose(restored, "cleave:other", dice);
        Assert.False(result.State.CleavePending);
        Assert.Equal(new AbilityUses(2, 1), Barbarian(result.State).CleaveUses);
        Assert.Equal(new AbilityUses(2, 2), Barbarian(attack.State).CleaveUses);
        Assert.Equal(kill ? 0 : 1, result.State.Units[2].CurrentHp);
        Assert.Equal(0, dice.AttackRolls);
        Assert.Equal(0, dice.DefenceRolls);
        var cleave = TestGame.OperationEvents(result)[0];
        Assert.Equal("CleaveResolved", cleave.Kind);
        Assert.Equal(1, cleave.Damage);
        Assert.Equal("other", cleave.TargetId);
        Assert.DoesNotContain(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved");
        Assert.Equal(kill ? new[] { "CleaveResolved", "UnitDefeated" } : new[] { "CleaveResolved" }, TestGame.OperationEvents(result).Select(e => e.Kind));
        Assert.Equal(new AbilityUses(2, 1), Barbarian(TestGame.OperationSteps(result)[0].StateAfter).CleaveUses);
        Assert.False(TestGame.OperationSteps(result)[0].StateAfter.CleavePending);
        if (kill) Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "other");
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Contains(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        Assert.Contains(result.NextInput.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        Assert.Contains(result.NextInput.Candidates, c => c.BonusAction?.Name == "Rage" && !c.Relevant);
        Assert.Throws<ArgumentException>(() => Choose(result.State, "cleave:target"));
    }

    [Fact]
    public void DeclinePreservesUsesAndResumesNormalChoicesIncludingWithAutoProgression()
    {
        var attack = Attack();
        var refreshed = GameEngine.RefreshChoices(Restore(attack.State), new Dice(), true);
        Assert.Equal(DecisionKind.Cleave, refreshed.NextInput!.Kind);
        var declined = Choose(refreshed.State, null, auto: true);
        Assert.False(declined.State.CleavePending);
        Assert.Empty(TestGame.OperationEvents(declined));
        Assert.Equal(new AbilityUses(2, 2), Barbarian(declined.State).CleaveUses);
        Assert.Equal(DecisionKind.Activation, declined.NextInput!.Kind);
        Assert.Contains(declined.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
    }

    [Fact]
    public void ExhaustedUsesOrNoLivingAdjacentHostileSuppressFollowUp()
    {
        var exhausted = Scenario();
        exhausted.Units[0] = exhausted.Units[0] with { CleaveUses = new(2, 0) };
        Assert.False(Attack(exhausted).State.CleavePending);
        var alone = Scenario(targetHp: 1);
        alone.Physical.Figures[2] = alone.Physical.Figures[2] with { Position = new(4, 4) };
        Assert.False(Attack(alone).State.CleavePending);
    }

    [Fact]
    public void RageThenAttackThenCleaveDoesNotUseBonusActionSystem()
    {
        var started = TestGame.StartRound(Scenario(), new Dice(), false);
        var raging = Choose(started.State, "bonus-action:Rage");
        var moved = Choose(raging.State, "stay");
        var dice = new Dice(hits: 7);
        var attacked = Choose(moved.State, "attack:target", dice);
        Assert.Equal(7, dice.AttackRolls); // Fury plus Rage.
        Assert.Equal(DecisionKind.Cleave, attacked.NextInput!.Kind);
        var cleaved = Choose(attacked.State, "cleave:other");
        Assert.Equal(new AbilityUses(2, 1), Barbarian(cleaved.State).CleaveUses);
        Assert.Equal(new AbilityUses(2, 1), Barbarian(cleaved.State).BonusActionUses["Rage"]);
        Assert.Equal(new[] { "Rage" }, cleaved.State.BonusActionsUsedThisActivation);
        Assert.Equal(7, cleaved.State.EffectiveAtkOf("barbarian"));
        Assert.Equal(DecisionKind.Activation, cleaved.NextInput!.Kind);
    }

    [Fact]
    public void EachAttackCanCreateAnIndependentContinuationWithinSameActivation()
    {
        var first = Attack();
        var used = Choose(first.State, "cleave:target");
        // Model a future extra Action opportunity without adding a new rule.
        used.State.ActionDone = false;
        var extra = GameEngine.RefreshChoices(used.State, new Dice(), false);
        var second = Choose(extra.State, "attack:target");
        Assert.Equal(DecisionKind.Cleave, second.NextInput!.Kind);
        var exhausted = Choose(second.State, "cleave:target");
        Assert.Equal(new AbilityUses(2, 0), Barbarian(exhausted.State).CleaveUses);
        exhausted.State.ActionDone = false;
        var third = GameEngine.RefreshChoices(exhausted.State, new Dice(), false);
        Assert.False(Choose(third.State, "attack:target").State.CleavePending);
    }
}
