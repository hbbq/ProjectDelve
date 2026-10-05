using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class FireballTests
{
    private sealed class Dice(int hits = 3, params DefenceFace[] defence) : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public int DefenceRolls { get; private set; }
        public List<string> Order { get; } = [];
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie()
        {
            Order.Add("attack");
            return AttackRolls++ < hits ? AttackFace.Hit : AttackFace.Miss;
        }
        public DefenceFace RollDefenceDie()
        {
            Order.Add("defence");
            var index = DefenceRolls++;
            return index < defence.Length ? defence[index] : DefenceFace.Miss;
        }
        public int RollD6() => throw new InvalidOperationException();
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState Scenario(UnitType? type = null)
    {
        type ??= UnitType.Wizard();
        return new()
        {
            Physical = new(new Board(8, 7, []), [new("wizard", new(1, 2)), new("a", new(3, 2)), new("b", new(4, 3))]),
            Types = [type, new("target", 0, 0, 0, 1, 8)],
            Units = [type.CreateUnit("wizard", "blue"), new("a", "target", "red", 8), new("b", "target", "blue", 8)]
        };
    }
    private static EngineResult Choose(GameState state, string? key, Dice? dice = null) =>
        GameEngine.Advance(state, new Choice(key), dice ?? new(), false);
    private static EngineResult Action(GameState? state = null) =>
        Choose(GameEngine.StartRound(state ?? Scenario(), new Dice(), false).State, "stay");
    private static Candidate CellChoice(EngineResult result, int x = 3, int y = 2) =>
        result.NextInput!.Candidates.Single(c => c.Key == $"fireball:{x},{y}");
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void ContentInitializesBareAndCreatedUnitsAndPrintsExactCardText()
    {
        var type = UnitType.Wizard();
        Assert.Equal(new Fireball(2), type.Fireball);
        Assert.Equal(new AbilityUses(2, 2), type.CreateUnit("w", "side").FireballUses);
        Assert.Null(UnitType.Grunt().Fireball);
        var entry = Assert.Single(type.CardEntries(), e => e.Id == "fireball");
        Assert.Equal("Fireball [2/game]\nAction\n\nChoose a Cell within RNG and LOS.\nAttack all Units on or adjacent to that Cell.",
            $"{entry.Name} [{entry.UseLimitText}]\n{entry.Category}\n\n{entry.Description}");
        var scenario = Scenario();
        scenario.Units[0] = new("wizard", type.Id, "blue", 4);
        var action = Action(scenario);
        var restored = Restore(action.State);
        Assert.Equal(new AbilityUses(2, 2), restored.Units[0].FireballUses);
        Assert.Equal(new[] { "a", "b" }, CellChoice(action).TargetIds);
        Assert.Null(scenario.Units[0].FireballUses);
    }

    [Theory]
    [InlineData("uses")] [InlineData("action")] [InlineData("move")]
    public void SubmissionRevalidatesUsesAndNormalActionTiming(string condition)
    {
        var action = Action();
        switch (condition)
        {
            case "uses": action.State.Units[0] = action.State.Units[0] with { FireballUses = new(2, 0) }; break;
            case "action": action.State.ActionDone = true; break;
            case "move": action.State.MoveDone = false; break;
        }
        Assert.Throws<ArgumentException>(() => Choose(Restore(action.State), "fireball:3,2"));
        Assert.DoesNotContain(GameEngine.RefreshChoices(action.State, new Dice(), false).NextInput!.Candidates,
            c => c.Action == UnitAction.Fireball);
    }

    [Fact]
    public void SpendsOneUseAndActionWithoutSpendingFocusAndCannotAttackAgain()
    {
        var action = Action();
        var result = Choose(action.State, "fireball:3,2", new Dice(0));
        Assert.True(result.State.ActionDone);
        Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].FireballUses);
        Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].BonusActionUses["Focus"]);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Action);
        Assert.Throws<ArgumentException>(() => Choose(result.State, "attack:a"));
        Assert.Equal(new AbilityUses(2, 2), action.State.Units[0].FireballUses);
    }

    [Theory]
    [InlineData(4, 5, 2, true)] [InlineData(4, 5, 3, false)]
    [InlineData(1, 2, 2, true)] [InlineData(1, 2, 3, false)]
    [InlineData(0, 1, 2, true)] [InlineData(0, 2, 2, false)]
    public void RangeUsesRangedManhattanEvenAtOneOrZero(int range, int x, int y, bool legal)
    {
        var action = Action(Scenario(UnitType.Wizard() with { Rng = range }));
        Assert.Equal(legal, action.NextInput!.Candidates.Any(c => c.Key == $"fireball:{x},{y}"));
    }

    [Fact]
    public void EffectiveRangeAndGeometryIgnoreInterveningUnits()
    {
        var action = Action();
        Assert.DoesNotContain(action.NextInput!.Candidates, c => c.Key == "fireball:6,2");
        action.State.ModifiersThisTurn.Add(new(Stat.Rng, 1));
        var refreshed = GameEngine.RefreshChoices(action.State, new Dice(), false);
        Assert.NotNull(CellChoice(refreshed, 6, 2)); // a lies on the ray, but does not block it.
        Assert.Contains(refreshed.NextInput!.Candidates, c => c.Key == "fireball:5,2");
        Assert.DoesNotContain(refreshed.NextInput.Candidates, c => c.Key == "attack:b"); // Friendly target.
    }

    [Theory]
    [InlineData(EdgeKind.Wall)] [InlineData(EdgeKind.ClosedDoor)] [InlineData(EdgeKind.OpenDoor)]
    public void TargetCellRequiresGeometricLos(EdgeKind kind)
    {
        var state = Scenario();
        state.Physical.Board.Edges.Add(new(new(1, 2), new(2, 2), kind));
        var action = Action(state);
        Assert.Equal(kind == EdgeKind.OpenDoor, action.NextInput!.Candidates.Any(c => c.Key == "fireball:3,2"));
    }

    [Fact]
    public void BlockingTerrainPreventsTargetCellAndEmptyZeroTargetExplosionIsLegal()
    {
        var state = Scenario();
        state.Physical.Board.Terrain.Add(new(new(2, 2), TerrainKind.Tree));
        var action = Action(state);
        Assert.DoesNotContain(action.NextInput!.Candidates, c => c.Key == "fireball:3,2");
        var empty = CellChoice(action, 1, 5);
        Assert.Empty(empty.TargetIds);
        Assert.False(empty.Relevant);
        var dice = new Dice();
        var result = Choose(action.State, empty.Key, dice);
        var attack = Assert.Single(result.Events, e => e.Kind == "AttackResolved").Attack!;
        Assert.Empty(attack.Targets);
        Assert.Equal(3, dice.AttackRolls);
        Assert.Equal(0, dice.DefenceRolls);
        Assert.True(result.State.ActionDone);
        Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].FireballUses);
    }

    [Theory]
    [InlineData(3, 2, true)] [InlineData(3, 1, true)] [InlineData(4, 1, true)]
    [InlineData(4, 2, true)] [InlineData(4, 3, true)] [InlineData(3, 3, true)]
    [InlineData(2, 3, true)] [InlineData(2, 2, true)] [InlineData(2, 1, true)]
    [InlineData(5, 2, false)]
    public void ExplosionIncludesCenterAndEightNeighborsOnly(int x, int y, bool targeted)
    {
        var state = Scenario();
        state.Units.RemoveAt(2); state.Physical.Figures.RemoveAt(2);
        state.Physical.Figures[1] = new("a", new(x, y));
        Assert.Equal(targeted, CellChoice(Action(state)).TargetIds.Contains("a"));
    }

    [Fact]
    public void FriendlyFireAndSelfAreRelevantAndSelfDeathDoesNotStopFixedTargetResolution()
    {
        var state = Scenario();
        state.Physical.Figures[1] = new("a", new(2, 2));
        state.Physical.Figures[2] = new("b", new(2, 3));
        state.Units[0] = state.Units[0] with { CurrentHp = 1 };
        var action = Action(state);
        Assert.Equal(new[] { "wizard", "a", "b" }, CellChoice(action, 1, 2).TargetIds);
        Assert.True(CellChoice(action, 1, 2).Relevant);
        var result = Choose(action.State, "fireball:1,2");
        var attack = Assert.Single(result.Events, e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(new[] { "wizard", "a", "b" }, attack.Targets.Select(t => t.TargetId));
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "wizard");
        Assert.Equal(5, result.State.Units[1].CurrentHp);
        Assert.Equal(5, result.State.Units[2].CurrentHp);
        Assert.False(result.State.CleavePending);
        Assert.Null(result.State.MoveAfterAttackAllowance);
    }

    [Theory]
    [InlineData(EdgeKind.Wall)] [InlineData(EdgeKind.ClosedDoor)]
    public void ExplosionLosProtectsAdjacentUnit(EdgeKind kind)
    {
        var state = Scenario();
        state.Physical.Figures[2] = new("b", new(4, 2));
        state.Physical.Board.Edges.Add(new(new(3, 2), new(4, 2), kind));
        Assert.Equal(new[] { "a" }, CellChoice(Action(state)).TargetIds);
    }

    [Fact]
    public void FriendlyOnlyExplosionIsRelevantAndBlockingTerrainCanProtectItsUnit()
    {
        var state = Scenario();
        state.Units[1] = state.Units[1] with { SideId = "blue" };
        Assert.True(CellChoice(Action(state)).Relevant);
        state.Physical.Board.Terrain.Add(new(new(4, 3), TerrainKind.Tree));
        Assert.Equal(new[] { "a" }, CellChoice(Action(state)).TargetIds);
    }

    [Fact]
    public void LastUseIsSpentEvenOnMissAndDoesNotRefillNextRound()
    {
        var state = Scenario();
        state.Units[0] = state.Units[0] with { FireballUses = new(2, 1) };
        var result = Choose(Action(state).State, "fireball:3,2", new Dice(0));
        Assert.Equal(new AbilityUses(2, 0), result.State.Units[0].FireballUses);
        Assert.All(result.Events.Single(e => e.Kind == "AttackResolved").Attack!.Targets, t => Assert.Equal(0, t.Damage));
        while (!result.State.RoundComplete)
        {
            var request = result.NextInput!;
            var key = request.Kind == DecisionKind.SelectUnit ? request.Candidates[0].Key
                : request.Candidates.Any(c => c.Key == "stay") ? "stay" : "end-turn";
            result = Choose(result.State, key);
        }
        result = Choose(GameEngine.StartRound(Restore(result.State), new Dice(), false).State, "stay");
        Assert.Equal(new AbilityUses(2, 0), result.State.Units[0].FireballUses);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Action == UnitAction.Fireball);
    }

    [Theory]
    [InlineData(false, 3)] [InlineData(true, 4)]
    public void OneSharedGeneralEffectiveAttackRollWithSeparateDefenceDamageAndPlayback(bool focus, int atk)
    {
        var action = Action();
        if (focus) action = Choose(action.State, "bonus-action:Focus");
        var dice = new Dice(4, DefenceFace.Block, DefenceFace.Miss);
        var result = Choose(Restore(action.State), "fireball:3,2", dice);
        var attack = Assert.Single(result.Events, e => e.Kind == "AttackResolved");
        Assert.Null(attack.TargetId);
        Assert.Equal("Fireball", attack.AbilityName);
        Assert.Equal(atk, attack.Attack!.AttackDice);
        Assert.Equal(atk, dice.AttackRolls);
        Assert.Equal(2, dice.DefenceRolls);
        Assert.Equal(new[] { new AttackTargetResult("a", 1, 1, atk - 1), new AttackTargetResult("b", 1, 0, atk) }, attack.Attack.Targets);
        Assert.Equal(Enumerable.Repeat("attack", atk).Concat(["defence", "defence"]), dice.Order);
        Assert.Equal(9 - atk, result.ResolutionSteps[0].StateAfter.Units[1].CurrentHp);
        Assert.Equal(8, result.ResolutionSteps[0].StateAfter.Units[2].CurrentHp);
        Assert.Equal(8 - atk, result.ResolutionSteps[1].StateAfter.Units[2].CurrentHp);
        Assert.Equal(attack.Attack.Targets.ToArray(), JsonSerializer.Deserialize<EngineResult>(JsonSerializer.Serialize(result))!
            .Events.Single(e => e.Kind == "AttackResolved").Attack!.Targets.ToArray());
    }

    [Fact]
    public void GeneralModifiersApplyButTargetSpecificBackstabDoesNot()
    {
        var type = UnitType.Wizard() with { Backstab = new(), Fury = new() };
        var state = Scenario(type);
        state.Units[2] = state.Units[2] with { SideId = "red" };
        state.Physical.Figures[1] = new("a", new(2, 2));
        state.Physical.Figures[2] = new("b", new(2, 3));
        state.Units.Add(new("friend", "target", "blue", 8));
        state.Physical.Figures.Add(new("friend", new(3, 1)));
        var action = Action(state);
        action.State.ModifiersThisTurn.Add(new(Stat.Atk, 2));
        Assert.Equal(6, action.State.EffectiveAtkOf("wizard"));
        Assert.Equal(7, action.State.EffectiveAtkAgainst("wizard", "a"));
        var dice = new Dice(0);
        var result = Choose(action.State, "fireball:3,2", dice);
        Assert.Equal(6, dice.AttackRolls);
        Assert.Equal(6, Assert.Single(result.Events, e => e.Kind == "AttackResolved").Attack!.AttackDice);
    }

    [Fact]
    public void DefenceIsFixedBeforeResolutionAndDeathsRemoveAllFiguresWithFixedMembership()
    {
        var state = Scenario();
        var aura = UnitType.Cleric("aura") with { Def = 0 };
        state.Types.Add(aura);
        state.Units[1] = aura.CreateUnit("a", "blue") with { CurrentHp = 1 };
        state.Units[2] = state.Units[2] with { CurrentHp = 1 };
        Assert.Equal(2, state.EffectiveDefOf("b"));
        var action = Action(state);
        action.State.Pending!.Candidates.Clear(); // Informational candidates cannot alter membership.
        var result = Choose(Restore(action.State), "fireball:3,2");
        Assert.Equal(new[] { "AttackTargetResolved", "UnitDied", "AttackTargetResolved", "UnitDied", "AttackResolved" }, result.Events.Select(e => e.Kind));
        var attack = result.Events.Last().Attack!;
        Assert.Equal(new[] { "a", "b" }, attack.Targets.Select(t => t.TargetId));
        Assert.Equal(new[] { 0, 2 }, attack.Targets.Select(t => t.DefenceDice));
        Assert.All(attack.Targets, t => Assert.Equal(3, t.Damage));
        Assert.Single(result.State.Physical.Figures);
        Assert.Equal(1, result.ResolutionSteps[1].StateAfter.Units[2].CurrentHp);
        Assert.DoesNotContain(result.ResolutionSteps[1].StateAfter.Physical.Figures, f => f.Id == "a");
        Assert.Contains(result.ResolutionSteps[2].StateAfter.Physical.Figures, f => f.Id == "b");
        Assert.DoesNotContain(result.ResolutionSteps[3].StateAfter.Physical.Figures, f => f.Id == "b");
    }

    [Theory]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(-1, false)] [InlineData(-1, true)]
    public void DefenceSnapshotSurvivesAuraSourceDeathOrUndyingAndLaterAttackUsesCurrentState(int modifier, bool undying)
    {
        var state = Scenario();
        var cleric = UnitRoster.Cleric("cleric") with
        {
            Def = 0,
            AdjacentFriendlyUnitsDefenceBonus = new(modifier),
            Undying = undying ? new() : null
        };
        state.Types.Add(cleric);
        state.Units[1] = cleric.CreateUnit("a", "red") with { CurrentHp = 1 };
        state.Units[2] = state.Units[2] with { SideId = "red" };
        // This Wizard activates next, before the Cleric can stand up.
        var nextWizard = UnitRoster.Wizard("next-wizard");
        state.Types.Insert(1, nextWizard);
        state.Units.Add(nextWizard.CreateUnit("next", "blue"));
        state.Physical.Figures.Add(new("next", new(4, 4)));
        var defenceAtStart = 1 + modifier;
        Assert.Equal(defenceAtStart, state.EffectiveDefOf("b"));
        var dice = new Dice();
        var result = Choose(Action(state).State, "fireball:3,2", dice);

        var attack = Assert.Single(result.Events, e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(new[] { new AttackTargetResult("a", 0, 0, 3),
            new AttackTargetResult("b", defenceAtStart, 0, 3) }, attack.Targets);
        Assert.Equal(3, dice.AttackRolls); // One shared roll even when the first target changes state.
        Assert.Equal(defenceAtStart, dice.DefenceRolls);
        Assert.Equal(Enumerable.Repeat("attack", 3).Concat(Enumerable.Repeat("defence", defenceAtStart)), dice.Order);
        Assert.Equal(new[] { "AttackTargetResolved", undying ? "PostureChanged" : "UnitDied",
            "AttackTargetResolved", "AttackResolved" }, result.Events.Select(e => e.Kind));
        Assert.Equal(1, result.State.EffectiveDefOf("b"));

        // Intermediate playback exposes the first outcome before the second target's damage.
        for (var i = 0; i < 2; i++)
        {
            var intermediate = result.ResolutionSteps[i].StateAfter;
            Assert.Equal(undying ? 1 : 0, intermediate.Units[1].CurrentHp);
            Assert.Equal(8, intermediate.Units[2].CurrentHp);
            Assert.Equal(1, intermediate.EffectiveDefOf("b"));
            if (undying)
                Assert.Equal(Posture.Lying, intermediate.Physical.Figures.Single(f => f.Id == "a").Posture);
        }
        Assert.Equal(5, result.ResolutionSteps[2].StateAfter.Units[2].CurrentHp);
        Assert.Equal(undying, result.State.Physical.Figures.Any(f => f.Id == "a"));

        result = Choose(Restore(result.State), "end-turn");
        Assert.Equal("next", result.State.CurrentUnitId);
        result = Choose(result.State, "stay");
        var nextDice = new Dice(0);
        result = Choose(result.State, "attack:b", nextDice);
        var nextAttack = Assert.Single(result.Events, e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(3, nextAttack.AttackDice);
        Assert.Equal(new AttackTargetResult("b", 1, 0, 0), Assert.Single(nextAttack.Targets));
        Assert.Equal(1, nextDice.DefenceRolls);
    }

    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, false)]
    public void FollowUpsOccurOnceAndCleaveNeverSumsDamage(int blocks, bool qualifies)
    {
        var type = UnitType.Wizard() with { Cleave = new(), MoveAfterAttack = new(1) };
        var state = Scenario(type);
        state.Physical.Figures[1] = new("a", new(2, 2));
        state.Physical.Figures[2] = new("b", new(2, 3));
        state.Units[2] = state.Units[2] with { SideId = "red" };
        var dice = new Dice(2, blocks > 0 ? DefenceFace.Block : DefenceFace.Miss,
            blocks > 1 ? DefenceFace.Block : DefenceFace.Miss);
        var result = Choose(Action(state).State, "fireball:3,2", dice);
        Assert.Single(result.Events, e => e.Kind == "AttackResolved");
        Assert.Equal(qualifies, result.State.CleavePending);
        Assert.Equal(qualifies ? DecisionKind.Cleave : DecisionKind.Move, result.NextInput!.Kind);
        if (qualifies)
        {
            result = Choose(Restore(result.State), "cleave:a");
            Assert.Single(result.Events, e => e.Kind == "CleaveResolved");
            Assert.False(result.State.CleavePending);
            Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].CleaveUses);
            Assert.Equal(DecisionKind.Move, result.NextInput!.Kind);
        }
        result = Choose(Restore(result.State), null);
        Assert.True(Assert.Single(result.Events, e => e.Kind == "MovementCompleted").IsMoveAfterAttack);
        Assert.Null(result.State.MoveAfterAttackAllowance);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.False(result.State.CleavePending);
    }
}
