using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ConsolidatedMechanicTests
{
    private static LocalAbilityDefinition Ability(AbilityMechanic mechanic, AbilityTiming timing = AbilityTiming.Action,
        params AbilityCost[] costs) => new("authored", "Authored", timing, mechanic) { Costs = [.. costs], Usage = new(1) };

    private static GameState Ready(LocalAbilityDefinition ability)
    {
        var actor = new UnitType("actor-type", 2, 1, 0, 0, 4, Actions: UnitAction.None)
        {
            Unique = true, LocalAbilities = [ability, new("reserve", "Reserve", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))],
            ResourceStorage = new([new(ResourceTypes.Coal, 3)])
        };
        var state = new GameState
        {
            Physical = new(new Board(6, 6, []), [new("actor", new(2, 2)), new("enemy", new(3, 2)),
                new("neutral", new(1, 2)), new("friend", new(2, 1))]),
            Types = [actor, new("enemy-type", 0, 0, 0, 0, 4) { Unique = true },
                new("neutral-type", 0, 0, 0, 0, 4) { Unique = true },
                new("friend-type", 0, 0, 0, 0, 4) { Unique = true }, new("spawn-type", 1, 0, 0, 0, 1)],
            Units = [actor.CreateUnit("actor", "blue"), new("enemy", "enemy-type", "red", 3),
                new("neutral", "neutral-type", "neutral", 3), new("friend", "friend-type", "blue", 3)],
            Controllers = [new(new("actor-type", "blue"), ControllerKind.Human)],
            NeutralSides = ["neutral"], Round = 1, ActiveToken = new("actor-type", "blue"), CurrentUnitId = "actor",
            MoveDone = true, StoredResources = [new("blue", actor.Id, ResourceTypes.Coal, 3)],
            Pending = new(DecisionKind.Activation, actor.Id, "actor", [], false)
        };
        return Refresh(state);
    }

    private static GameState Refresh(GameState state) => GameEngine.RefreshChoices(state, new Dice(), false).State;
    private static Candidate Authored(GameState state) => state.Pending!.Candidates.First(candidate => candidate.Ability?.Key == "authored");
    private static EngineResult Use(GameState state, string? key = null) =>
        GameEngine.Advance(state, new Choice(key ?? Authored(state).Key), new Dice(), false);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Theory]
    [InlineData(RelationshipSelector.Friendlies, "friend")]
    [InlineData(RelationshipSelector.Enemies, "enemy")]
    [InlineData(RelationshipSelector.All, "friend,neutral,enemy")]
    public void PluralPostureBindsDistinctRecipientsInBoardOrderAndPaysBeforeEffects(RelationshipSelector selector, string expected)
    {
        var state = Ready(Ability(new AdjacentUnitsPostureChangeMechanic(selector, Posture.Lying), AbilityTiming.Action,
            new LayDownSelf(), new SpendResource("coal", 3)));
        Assert.Equal(expected.Split(','), Authored(state).TargetIds);
        var result = Use(Restore(state));
        Assert.Equal(new[] { "actor" }.Concat(expected.Split(',')),
            result.Events.Where(e => e.Kind == "PostureChanged").Select(e => e.UnitId));
        Assert.Equal(0, result.State.StoredQuantity("blue", "actor-type", "coal"));
        Assert.Single(result.Events.Where(e => e.Kind == "ResourceSpent"));
        Assert.True(result.Events.FindIndex(e => e.Kind == "ResourceSpent") <
            result.Events.FindIndex(e => e.Kind == "PostureChanged" && e.UnitId != "actor"));
        Assert.All(expected.Split(','), id => Assert.False(result.State.IsUpright(id)));
        Assert.Equal(new AbilityUses(1, 0), result.State.AbilityUsesOf("actor", Authored(state).Ability!));
    }

    [Fact]
    public void AdjacentRecipientsRespectLosAliveStateAndCountLargeFiguresOnce()
    {
        var state = Ready(Ability(new AdjacentUnitsPostureChangeMechanic(RelationshipSelector.All, Posture.Lying)));
        state.Types[1] = state.Types[1] with { Footprint = Footprint.TwoByTwo };
        state.Physical.Board.Edges.Add(new(new(2, 2), new(2, 1), EdgeKind.Wall));
        state.Units[2] = state.Units[2] with { CurrentHp = 0 };
        Assert.Equal(new[] { "enemy" }, Authored(Refresh(state)).TargetIds);
    }

    [Fact]
    public void EmptyPluralPostureIsLegalAndPaysWithoutImplicitSourcePostureChange()
    {
        var state = Ready(Ability(new AdjacentUnitsPostureChangeMechanic(RelationshipSelector.Enemies, Posture.Lying),
            AbilityTiming.Action, new SpendResource("coal", 2)));
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying };
        state = Refresh(state);
        Assert.Empty(Authored(state).TargetIds);
        Assert.False(Authored(state).Relevant);
        var result = Use(state);
        Assert.Equal(1, result.State.StoredQuantity("blue", "actor-type", "coal"));
        Assert.True(result.State.IsUpright("actor"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "PostureChanged");
    }

    [Theory]
    [InlineData(RelationshipSelector.Friendlies, "friend")]
    [InlineData(RelationshipSelector.Enemies, "enemy")]
    [InlineData(RelationshipSelector.All, "friend,neutral,enemy")]
    public void SelectedHealingUsesSelectorExcludesSelfAndCapsRestoration(RelationshipSelector selector, string expected)
    {
        var state = Ready(Ability(new AdjacentUnitHealMechanic(selector, 9), AbilityTiming.Action, new LayDownSelf()));
        Assert.Equal(expected.Split(','), state.Pending!.Candidates.Where(c => c.Ability?.Key == "authored").Select(c => c.TargetId));
        var targetId = Authored(state).TargetId;
        var result = Use(state);
        Assert.Equal(4, result.State.Units.Single(unit => unit.Id == targetId).CurrentHp);
        Assert.False(result.State.IsUpright("actor"));
        state.Units = state.Units.Select(unit => unit with { CurrentHp = 4 }).ToList();
        Assert.DoesNotContain(Refresh(state).Pending!.Candidates, c => c.Ability?.Key == "authored");
    }

    [Theory]
    [InlineData(Posture.Upright)]
    [InlineData(Posture.Lying)]
    public void SelectedPostureOnlyOffersDifferentPostureAndResolvesAfterLayDownPayment(Posture newPosture)
    {
        var state = Ready(Ability(new AdjacentUnitPostureChangeMechanic(RelationshipSelector.All, newPosture),
            AbilityTiming.Action, new LayDownSelf()));
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying };
        state = Refresh(state);
        var expected = newPosture == Posture.Upright ? new[] { "enemy" } : new[] { "friend", "neutral" };
        Assert.Equal(expected, state.Pending!.Candidates.Where(c => c.Ability?.Key == "authored").Select(c => c.TargetId));
        var target = Authored(state).TargetId;
        var result = Use(state);
        Assert.Equal(newPosture, result.State.Physical.Figures.Single(figure => figure.Id == target).Posture);
        Assert.False(result.State.IsUpright("actor"));
    }

    [Theory]
    [InlineData(RelationshipSelector.Friendlies, "friend")]
    [InlineData(RelationshipSelector.Enemies, "enemy")]
    [InlineData(RelationshipSelector.All, "friend,neutral,enemy")]
    public void AuraUsesSelectorAndCurrentSourcePosture(RelationshipSelector selector, string expected)
    {
        var passive = new LocalAbilityDefinition("aura", "Aura", AbilityTiming.Passive,
            new AdjacentUnitsStatModifiersMechanic(selector, [new(Stat.Def, 2), new(Stat.Def, 1)]));
        var state = Ready(passive);
        foreach (var unit in state.Units)
            Assert.Equal(expected.Split(',').Contains(unit.Id) ? 3 : 0, state.EffectiveDefOf(unit.Id));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.All(state.Units, unit => Assert.Equal(0, state.EffectiveDefOf(unit.Id)));
    }

    [Fact]
    public void SummonUsesAuthoredTypeLimitAndBoundPlacementAfterPayment()
    {
        var state = Ready(Ability(new AdjacentUnitSummonMechanic("spawn-type", Posture.Upright, 1), AbilityTiming.Action,
            new SpendResource("coal", 3), new LayDownSelf()));
        state.PlaceUnit("spawn-type", "opponent-spawn", "red", new(5, 5));
        state = Refresh(state);
        var candidate = Authored(state);
        var result = Use(Restore(state), candidate.Key);
        var created = Assert.Single(result.Events.Where(e => e.Kind == "UnitCreated"));
        Assert.Equal("spawn-type", created.TypeId);
        Assert.Equal(candidate.Destination, created.Cell);
        Assert.Equal(Posture.Upright, created.Posture);
        Assert.Equal(0, result.State.StoredQuantity("blue", "actor-type", "coal"));
        Assert.Single(result.Events.Where(e => e.Kind == "ResourceSpent"));
        Assert.False(result.State.IsUpright("actor"));
        state.PlaceUnit("spawn-type", "own-spawn", "blue", new(5, 4));
        Assert.DoesNotContain(Refresh(state).Pending!.Candidates, c => c.Ability?.Key == "authored");
    }

    [Fact]
    public void BananaAndExtraMovesAreResourceAgnosticAndConsumeOnlyDeclaredCosts()
    {
        var banana = Ready(Ability(new PlaceBananaPeel(), AbilityTiming.BonusAction,
            new SpendResource("coal", 3), new LayDownSelf()));
        var placed = Use(banana);
        Assert.Single(placed.State.CellMarkers);
        Assert.Single(placed.Events.Where(e => e.Kind == "ResourceSpent"));
        Assert.Equal(0, placed.State.StoredQuantity("blue", "actor-type", "coal"));
        var moves = Ready(Ability(new SelfExtraMovesMechanic(2), AbilityTiming.BonusAction));
        var boosted = Use(moves);
        Assert.Equal(2, boosted.State.MoveOpportunityModifierThisActivation);
        Assert.Contains(boosted.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
        Assert.DoesNotContain(boosted.Events, e => e.Kind == "ResourceSpent");
        moves.ActionDone = true;
        Assert.DoesNotContain(Refresh(moves).Pending!.Candidates, c => c.Ability?.Key == "authored");
    }

    [Fact]
    public void TargetAttackUsesFixedStatsAfterPaymentAndDoesNotPayAgainOnResume()
    {
        var state = Ready(Ability(new TargetUnitAttackMechanic(4, 3), AbilityTiming.Action,
            new SpendResource("coal", 3), new LayDownSelf()));
        var started = Use(state);
        Assert.False(started.State.IsUpright("actor"));
        Assert.Equal(0, started.State.StoredQuantity("blue", "actor-type", "coal"));
        Assert.Equal(DecisionKind.RollDice, started.NextInput!.Kind);
        Assert.Equal(UnitAction.TargetUnitAttack, started.State.AttackInProgress!.SemanticAction);
        Assert.Equal(4, started.State.AttackInProgress.RangeOverride);
        Assert.Equal(3, started.State.AttackInProgress.AttackDice);
        var resumed = Use(Restore(started.State), "roll-dice");
        Assert.DoesNotContain(resumed.Events, e => e.Kind == "ResourceSpent");
        Assert.Equal(JsonSerializer.Serialize(Use(started.State, "roll-dice")), JsonSerializer.Serialize(resumed));
        Assert.Contains(resumed.Events, e => e.Kind == "AttackTargetResolved");
    }

    [Fact]
    public void LargeIndependentMoveGrantsCannotWrapTheMovementBudget()
    {
        var state = Ready(Ability(new SelfExtraMovesMechanic(int.MaxValue), AbilityTiming.BonusAction));
        state.MoveOpportunityModifierThisActivation = int.MaxValue - 1;
        Assert.Equal(int.MaxValue, state.EffectiveMovesPerActivation);
        var result = Use(Refresh(state));
        Assert.Equal(int.MaxValue, result.State.MoveOpportunityModifierThisActivation);
        Assert.Equal(int.MaxValue, result.State.EffectiveMovesPerActivation);
        Assert.Contains(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
    }

    private sealed class Choice(string key) : IDecisionProvider
    { public string? Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }
}
