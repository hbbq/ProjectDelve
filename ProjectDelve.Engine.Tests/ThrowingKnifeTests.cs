using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ThrowingKnifeTests
{
    private sealed class Random : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(int targetX = 3, UnitType? rogue = null)
    {
        rogue ??= UnitType.Rogue();
        return new()
        {
            Physical = new(new Board(8, 3, []), [new("rogue", new(0, 0)), new("enemy", new(targetX, 0))]),
            Types = [rogue, new("enemy-type", 0, 0, 0, 0, 20)],
            Units = [rogue.CreateUnit("rogue", "blue"), new("enemy", "enemy-type", "red", 20)]
        };
    }

    private static EngineResult Start(GameState state) => GameEngine.StartRound(state, new Random(), false);
    private static EngineResult Choose(GameState state, string key, Random? random = null, bool auto = false) =>
        GameEngine.Advance(state, new Choice(key), random ?? new(), auto);
    private static Candidate Ability(EngineResult result, string name = "Throwing Knife") =>
        Assert.Single(result.NextInput!.Candidates, c => c.BonusAction?.Name == name);
    private static AbilityUses Uses(GameState state, string name = "Throwing Knife") =>
        state.Units.Single(u => u.Id == "rogue").BonusActionUses[name];
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Moved(GameState state) => Choose(Start(state).State, "stay");

    [Fact]
    public void RogueStartsWithIndependentTwoOfTwoUsesForDashAndThrowingKnife()
    {
        var state = State();
        var knife = Assert.Single(state.Types[0].BonusActions, a => a.Name == "Throwing Knife");
        Assert.Equal(2, knife.MaxUses);
        Assert.Equal(new[] { new ModifierThisTurn(Stat.Rng, 2), new ModifierThisTurn(Stat.Atk, -1) }, knife.Modifiers);
        Assert.Equal(new AbilityUses(2, 2), Uses(state));
        Assert.Equal(new AbilityUses(2, 2), Uses(state, "Dash"));

        state.Units[0] = new("rogue", state.Types[0].Id, "blue", 4);
        var started = Start(state);
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State));
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State, "Dash"));
        Assert.Empty(state.Units[0].BonusActionUses);
    }

    [Fact]
    public void CompletePackageIsAppliedImmediatelyAndSerializedRangeAndAttackDriveNormalAttack()
    {
        var started = Start(State());
        Assert.False(Ability(started).Relevant); // No legal Attack before Move.
        var used = Choose(started.State, Ability(started).Key);
        Assert.Equal("Throwing Knife", Assert.Single(used.State.BonusActionsUsedThisActivation));
        Assert.False(used.State.MoveDone);
        Assert.False(used.State.ActionDone);
        Assert.Equal(new AbilityUses(2, 1), Uses(used.State));
        Assert.Equal(new AbilityUses(2, 2), Uses(used.State, "Dash"));
        Assert.Equal(3, used.State.EffectiveRngOf("rogue"));
        Assert.Equal(2, used.State.EffectiveAtkOf("rogue"));
        Assert.Equal(4, used.State.EffectiveMovOf("rogue"));
        Assert.Equal(1, used.State.Types[0].Rng);
        Assert.Equal(3, used.State.Types[0].Atk);
        Assert.Equal(Ability(started).BonusAction!.Modifiers, used.State.ModifiersThisTurn);
        Assert.Equal("Throwing Knife", Assert.Single(used.Events).AbilityName);
        var snapshot = Assert.Single(used.ResolutionSteps).StateAfter;
        Assert.Equal(3, snapshot.EffectiveRngOf("rogue"));
        Assert.Equal(2, snapshot.EffectiveAtkOf("rogue"));
        Assert.DoesNotContain(used.NextInput!.Candidates, c => c.BonusAction?.Name == "Throwing Knife");
        Assert.NotNull(Ability(used, "Dash"));
        Assert.Throws<ArgumentException>(() => Choose(used.State, "bonus-action:Throwing Knife"));
        Assert.Empty(started.State.ModifiersThisTurn);
        Assert.Equal(new AbilityUses(2, 2), Uses(started.State));

        var restored = Restore(used.State);
        Assert.Equal(3, restored.EffectiveRng["rogue"]);
        Assert.Equal(2, restored.EffectiveAtk["rogue"]);
        var moved = Choose(restored, "stay");
        Assert.Contains(moved.NextInput!.Candidates, c => c.TargetId == "enemy" && c.Action == UnitAction.NormalAttack);
        var random = new Random();
        var attacked = Choose(Restore(moved.State), "attack:enemy", random);
        Assert.Equal(2, random.AttackRolls);
        Assert.Equal(2, Assert.Single(attacked.Events, e => e.Kind == "AttackResolved").Damage);
        attacked = Choose(attacked.State, "end-turn");
        Assert.True(attacked.State.RoundComplete);
        Assert.Empty(attacked.State.ModifiersThisTurn);
        Assert.Equal(1, attacked.State.EffectiveRngOf("rogue"));
        Assert.Equal(3, attacked.State.EffectiveAtkOf("rogue"));
        Assert.Equal(new AbilityUses(2, 1), Uses(attacked.State));
        Assert.Equal(2, restored.ModifiersThisTurn.Count);
        var next = Start(Restore(attacked.State));
        Assert.Equal(new AbilityUses(2, 1), Uses(next.State));
        Assert.Empty(next.State.BonusActionsUsedThisActivation);
    }

    [Theory]
    [InlineData(1, false)] // Same legal target with fewer attack dice is no improvement.
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void KnifeRelevanceComparesLegalTargetsDespiteLowerAttackDice(int targetX, bool relevant)
    {
        var moved = Moved(State(targetX));
        Assert.Equal(relevant, Ability(moved).Relevant);
        var original = JsonSerializer.Serialize(moved.State);
        var refreshed = GameEngine.RefreshChoices(Restore(moved.State), new Random(), false);
        Assert.Equal(relevant, Ability(refreshed).Relevant);
        Assert.Equal(original, JsonSerializer.Serialize(moved.State));
        var used = Choose(moved.State, Ability(moved).Key);
        Assert.Equal(new AbilityUses(2, 1), Uses(used.State)); // Irrelevance does not restrict legality.
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void RngGainOnlyCountsTargetsLegalAfterAtkPenaltyAlsoApplies(int baseAtk, bool relevant)
    {
        var moved = Moved(State(3, UnitType.Rogue() with { Atk = baseAtk }));
        Assert.DoesNotContain(moved.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        Assert.Equal(relevant, Ability(moved).Relevant);
        var used = Choose(moved.State, Ability(moved).Key);
        var snapshot = used.ResolutionSteps[0].StateAfter;
        Assert.Equal(3, snapshot.EffectiveRngOf("rogue"));
        Assert.Equal(baseAtk - 1, snapshot.EffectiveAtkOf("rogue"));
        Assert.Equal(relevant ? NormalAttackEvaluation.Possible : NormalAttackEvaluation.NotPossible,
            AttackRules.EvaluateFrom(snapshot, "rogue", new(0, 0), "enemy"));
    }

    [Theory]
    [InlineData("Throwing Knife", "Dash")]
    [InlineData("Dash", "Throwing Knife")]
    public void DifferentAbilitiesCanBothBeUsedOnceInEitherOrder(string first, string second)
    {
        var started = Start(State());
        var used = Choose(started.State, Ability(started, first).Key);
        Assert.Equal(new AbilityUses(2, 1), Uses(used.State, first));
        Assert.Equal(new AbilityUses(2, 2), Uses(used.State, second));
        Assert.Equal(first, Assert.Single(used.State.BonusActionsUsedThisActivation));
        Assert.Empty(started.State.BonusActionsUsedThisActivation);
        Assert.Throws<ArgumentException>(() => Choose(Restore(used.State), $"bonus-action:{first}"));
        var both = Choose(Restore(used.State), Ability(used, second).Key);
        Assert.Equal(new AbilityUses(2, 1), Uses(both.State, first));
        Assert.Equal(new AbilityUses(2, 1), Uses(both.State, second));
        Assert.True(both.State.BonusActionsUsedThisActivation.SetEquals([first, second]));
        Assert.DoesNotContain(both.NextInput!.Candidates, c => c.BonusAction is not null);
        Assert.Throws<ArgumentException>(() => Choose(both.State, $"bonus-action:{first}"));
        Assert.Throws<ArgumentException>(() => Choose(both.State, $"bonus-action:{second}"));
        Assert.Equal(6, both.State.EffectiveMovOf("rogue"));
        Assert.Equal(3, both.State.EffectiveRngOf("rogue"));
        Assert.Equal(2, both.State.EffectiveAtkOf("rogue"));
        Assert.Equal(3, both.State.ModifiersThisTurn.Count);
        Assert.Equal(first, Assert.Single(used.State.BonusActionsUsedThisActivation));

        var ended = Choose(Choose(both.State, "stay").State, "end-turn");
        Assert.Empty(ended.State.BonusActionsUsedThisActivation);
        Assert.Empty(ended.State.ModifiersThisTurn);
        var next = Start(Restore(ended.State));
        Assert.Empty(next.State.BonusActionsUsedThisActivation);
        Assert.Equal(new AbilityUses(2, 1), Uses(next.State, first));
        Assert.Equal(new AbilityUses(2, 1), Uses(next.State, second));
        Assert.NotNull(Ability(next, first));
        Assert.NotNull(Ability(next, second));
        var nextBoth = Choose(Choose(next.State, Ability(next, first).Key).State, $"bonus-action:{second}");
        Assert.Equal(new AbilityUses(2, 0), Uses(nextBoth.State, first));
        Assert.Equal(new AbilityUses(2, 0), Uses(nextBoth.State, second));
    }

    [Fact]
    public void ZeroKnifeUsesDoesNotRemoveDashOrRestoreKnifeUses()
    {
        var state = State();
        state.Units[0] = state.Units[0] with
        {
            BonusActionUses = state.Units[0].BonusActionUses.SetItem("Throwing Knife", new(2, 0))
        };
        var started = Start(state);
        Assert.Equal(new AbilityUses(2, 0), Uses(started.State));
        Assert.DoesNotContain(started.NextInput!.Candidates, c => c.BonusAction?.Name == "Throwing Knife");
        Assert.NotNull(Ability(started, "Dash"));
        Assert.Throws<ArgumentException>(() => Choose(started.State, "bonus-action:Throwing Knife"));
    }

    [Theory]
    [InlineData("atk-positive-range-negative", 3, false)]
    [InlineData("atk-positive-range-negative", 1, true)]
    [InlineData("atk-negative-only", 1, false)]
    [InlineData("range-negative-only", 1, false)]
    [InlineData("atk-and-range-positive", 1, true)] // ATK succeeds with no new RNG target.
    [InlineData("atk-and-range-positive", 3, true)]
    [InlineData("movement-and-range-positive", 7, true)] // MOV succeeds with no Attack.
    [InlineData("movement-cancelled", 7, false)]
    [InlineData("attack-cancelled", 1, false)]
    [InlineData("attack-net-negative", 1, false)]
    public void RelevanceComposesQuestionsAgainstTheCompletePackage(string package, int targetX, bool relevant)
    {
        ImmutableArray<ModifierThisTurn> modifiers = package switch
        {
            "atk-positive-range-negative" => [new(Stat.Atk, 2), new(Stat.Rng, -2)],
            "atk-negative-only" => [new(Stat.Atk, -1)],
            "range-negative-only" => [new(Stat.Rng, -1)],
            "atk-and-range-positive" => [new(Stat.Atk, 2), new(Stat.Rng, 2)],
            "movement-and-range-positive" => [new(Stat.Mov, 2), new(Stat.Rng, 2)],
            "movement-cancelled" => [new(Stat.Mov, 2), new(Stat.Mov, -2)],
            "attack-cancelled" => [new(Stat.Atk, 2), new(Stat.Atk, -2)],
            "attack-net-negative" => [new(Stat.Atk, 2), new(Stat.Atk, -3)],
            _ => throw new InvalidOperationException()
        };
        var type = UnitType.Rogue() with
        {
            Rng = package is "atk-positive-range-negative" or "range-negative-only" ? 3 : 1,
            BonusActions = [new("Combined", 2, modifiers)]
        };
        var state = State(targetX, type);
        var beforeMove = package.StartsWith("movement");
        var result = beforeMove ? Start(state) : Moved(state);
        Assert.Equal(relevant, Ability(result, "Combined").Relevant);
        Assert.Empty(result.State.ModifiersThisTurn);
        // Modifier order does not change relevance.
        state.Types[0] = type with { BonusActions = [new("Combined", 2, modifiers.Reverse().ToImmutableArray())] };
        var reversed = beforeMove ? Start(state) : Moved(state);
        Assert.Equal(relevant, Ability(reversed, "Combined").Relevant);
        var used = Choose(result.State, Ability(result, "Combined").Key);
        Assert.Equal(modifiers, used.ResolutionSteps[0].StateAfter.ModifiersThisTurn);
    }

    [Theory]
    [InlineData("before-move")]
    [InlineData("after-attack")]
    [InlineData("out-of-range")]
    [InlineData("no-normal-attack")]
    public void PositiveAttackEffectNeedsAnAuthoritativeAttackOpportunity(string restriction)
    {
        var type = UnitType.Rogue() with
        {
            BonusActions = [new("Attack Boost", 2, [new(Stat.Atk, 2)])],
            Actions = restriction == "no-normal-attack" ? UnitAction.None : UnitAction.NormalAttack
        };
        var state = State(restriction == "out-of-range" ? 4 : 1, type);
        var result = restriction == "before-move" ? Start(state) : Moved(state);
        if (restriction == "after-attack") result = Choose(result.State, "attack:enemy");

        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        Assert.False(Ability(result, "Attack Boost").Relevant);
        var used = Choose(result.State, Ability(result, "Attack Boost").Key);
        Assert.Equal(5, used.ResolutionSteps[0].StateAfter.EffectiveAtkOf("rogue"));
    }

    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    public void RelevanceFollowsTheSameOpportunitiesAsDecisionGeneration(
        bool moveDone, bool actionDone, bool dashRelevant, bool attackRelevant)
    {
        var type = UnitType.Rogue() with
        {
            BonusActions = [
                new("Dash", 2, [new(Stat.Mov, 2)]),
                new("Attack Boost", 2, [new(Stat.Atk, 2)])]
        };
        var state = Start(State(1, type)).State;
        state.MoveDone = moveDone;
        state.ActionDone = actionDone;
        var result = GameEngine.RefreshChoices(state, new Random(), false);
        var opportunities = GameEngine.GameplayCandidates(result.State, result.State.Units[0]).ToList();

        Assert.Equal(opportunities.Select(c => (c.Key, c.Destination, c.Action, c.TargetId)),
            result.NextInput!.Candidates
                .Where(c => c.Kind == ActivationChoiceKind.Move || c.Action == UnitAction.NormalAttack)
                .Select(c => (c.Key, c.Destination, c.Action, c.TargetId)));
        Assert.Equal(dashRelevant, Ability(result, "Dash").Relevant);
        Assert.Equal(attackRelevant, Ability(result, "Attack Boost").Relevant);
        Assert.Equal(attackRelevant, opportunities.Any(c => c.Action == UnitAction.NormalAttack));
    }

    [Fact]
    public void AtkAndRangeQuestionsBothSeeJointlyEnabledAttackAtZeroBaseAtk()
    {
        var type = UnitType.Rogue() with { Atk = 0, BonusActions = [new("Combined", 2, [new(Stat.Rng, 2), new(Stat.Atk, 1)])] };
        var moved = Moved(State(3, type));
        Assert.True(Ability(moved, "Combined").Relevant);
        var used = Choose(moved.State, Ability(moved, "Combined").Key);
        Assert.Contains(used.NextInput!.Candidates, c => c.TargetId == "enemy");
    }

    [Fact]
    public void KnifeIsIrrelevantAfterActionAndCanStillBeSelected()
    {
        var moved = Moved(State(1));
        var attacked = Choose(moved.State, "attack:enemy");
        Assert.True(attacked.State.ActionDone);
        Assert.False(Ability(attacked).Relevant);
        var used = Choose(attacked.State, Ability(attacked).Key);
        Assert.Equal(new AbilityUses(2, 1), Uses(used.State));
        Assert.NotNull(Ability(used, "Dash"));
        used = Choose(used.State, "end-turn");
        Assert.Empty(used.State.ModifiersThisTurn);
    }

    [Fact]
    public void BothModifiersClearBeforeNextRoguesActivationWithoutTransferringUses()
    {
        var state = State();
        state.Units.Add(UnitType.Rogue().CreateUnit("ally", "blue"));
        state.Physical.Figures.Add(new("ally", new(0, 2)));
        var started = Choose(Start(state).State, "rogue");
        var used = Choose(started.State, Ability(started).Key);
        Assert.Equal(1, used.State.EffectiveRngOf("ally"));
        Assert.Equal(3, used.State.EffectiveAtkOf("ally"));
        var beforeEnd = Choose(used.State, "stay");
        var ended = Choose(Restore(beforeEnd.State), "end-turn");
        Assert.Equal("ally", ended.State.CurrentUnitId);
        Assert.Empty(ended.State.BonusActionsUsedThisActivation);
        Assert.NotNull(Ability(ended));
        Assert.Empty(ended.State.ModifiersThisTurn);
        Assert.Equal(1, ended.State.EffectiveRngOf("rogue"));
        Assert.Equal(3, ended.State.EffectiveAtkOf("rogue"));
        Assert.Equal(1, ended.State.EffectiveRngOf("ally"));
        Assert.Equal(3, ended.State.EffectiveAtkOf("ally"));
        Assert.Equal(new AbilityUses(2, 2), ended.State.Units.Single(u => u.Id == "ally").BonusActionUses["Throwing Knife"]);
        Assert.Equal(new AbilityUses(2, 1), Uses(ended.State));
        Assert.Equal(2, beforeEnd.State.ModifiersThisTurn.Count);
    }

    [Fact]
    public void UnusedAbilitiesKeepIndependentRelevanceAsActivationProgresses()
    {
        var started = Start(State());
        Assert.True(Ability(started, "Dash").Relevant);
        Assert.False(Ability(started).Relevant);
        var dashed = Choose(started.State, Ability(started, "Dash").Key);
        Assert.False(Ability(dashed).Relevant);
        var moved = Choose(dashed.State, "stay");
        Assert.True(Ability(moved).Relevant);
        var both = Choose(moved.State, Ability(moved).Key);
        Assert.Contains(both.NextInput!.Candidates, c => c.TargetId == "enemy");

        var knifeFirst = Choose(started.State, Ability(started).Key);
        Assert.True(Ability(knifeFirst, "Dash").Relevant);
        var knifeMoved = Choose(knifeFirst.State, "stay");
        Assert.False(Ability(knifeMoved, "Dash").Relevant);
        var automatic = Choose(knifeMoved.State, "attack:enemy", auto: true);
        Assert.True(automatic.State.RoundComplete);
        Assert.Equal(new AbilityUses(2, 2), Uses(automatic.State, "Dash"));
    }

    [Fact]
    public void PerAbilityLimitUsesContentIdentityOnAnUnrelatedUnitType()
    {
        var type = UnitType.Hero("custom-type", 4, 1, 3, 2, 4) with
        {
            BonusActions = [new("Stride", 2, [new(Stat.Mov, 2)]),
                new("Reach", 2, [new(Stat.Rng, 2), new(Stat.Atk, -1)])]
        };
        var started = Start(State(rogue: type));
        var stride = Choose(started.State, Ability(started, "Stride").Key);
        Assert.Throws<ArgumentException>(() => Choose(stride.State, "bonus-action:Stride"));
        var both = Choose(stride.State, Ability(stride, "Reach").Key);
        Assert.True(both.State.BonusActionsUsedThisActivation.SetEquals(["Stride", "Reach"]));
        Assert.Equal(6, both.State.EffectiveMovOf("rogue"));
        Assert.Equal(3, both.State.EffectiveRngOf("rogue"));
        Assert.Equal(2, both.State.EffectiveAtkOf("rogue"));
    }

    [Theory]
    [InlineData("wall")]
    [InlineData("tree")]
    [InlineData("no-normal-attack")]
    public void RangeRelevanceReusesAuthoritativeAttackRestrictions(string restriction)
    {
        var state = State();
        if (restriction == "wall")
            state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.Wall));
        if (restriction == "tree")
            state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Tree));
        if (restriction == "no-normal-attack")
            state.Types[0] = state.Types[0] with { Actions = UnitAction.None };
        var moved = Moved(state);
        Assert.False(Ability(moved).Relevant);
        var used = Choose(moved.State, Ability(moved).Key);
        Assert.Equal(NormalAttackEvaluation.NotPossible,
            AttackRules.EvaluateFrom(used.ResolutionSteps[0].StateAfter, "rogue", new(0, 0), "enemy"));
    }

    [Fact]
    public void RelevanceAutoChoiceCanEndTurnWithoutSpendingEitherIrrelevantRogueAbility()
    {
        var moved = Moved(State(4));
        Assert.False(Ability(moved).Relevant);
        Assert.False(Ability(moved, "Dash").Relevant);
        var ended = GameEngine.RefreshChoices(moved.State, new Random());
        Assert.True(ended.State.RoundComplete);
        Assert.Equal(new AbilityUses(2, 2), Uses(ended.State));
        Assert.Equal(new AbilityUses(2, 2), Uses(ended.State, "Dash"));
        var relevant = Moved(State(3));
        var retained = GameEngine.RefreshChoices(relevant.State, new Random());
        Assert.NotNull(retained.NextInput);
        Assert.True(Ability(retained).Relevant); // Relevant Knife and End Turn both require a decision.
    }
}
