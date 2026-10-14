using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SelectedObjectivesTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static SideObjective Reach(int count = 0, params UnitSelector[] selectors) => new("Blue", ScenarioObjective.ReachArea)
    { Count = count, Selectors = selectors.Length == 0 ? [UnitSelector.SideAtStart("Blue")] : [.. selectors], Area = new(3, 1, 2, 2) };
    private static SideObjective Defeat(int count = 0, params UnitSelector[] selectors) => new("Red", ScenarioObjective.DefeatUnits)
    { Count = count, Selectors = selectors.Length == 0 ? [UnitSelector.SideAtStart("Blue")] : [.. selectors] };
    private static GameState Active(SideObjective objective)
    {
        var type = new UnitType("fighter", 4, 1, 1, 0, 1);
        var state = new GameState
        {
            Physical = new(new Board(6, 4, []), [new("actor", new(0, 0)), new("enemy", new(5, 3))]),
            Types = [type], Units = [type.CreateUnit("actor", "Blue"), type.CreateUnit("enemy", "Red")],
            Controllers = [new(new(type.Id, "Blue"), ControllerKind.Human), new(new(type.Id, "Red"), ControllerKind.Human)],
            Objectives = [objective], Round = 1, ActiveToken = new(type.Id, "Blue"), CurrentUnitId = "actor",
            MoveDone = true, ActionDone = true, Pending = new(DecisionKind.Activation, type.Id, "actor", [], false)
        };
        // Give the other Side an unsatisfied concrete objective rather than default elimination.
        var other = objective.SideId == "Blue" ? "Red" : "Blue";
        state.Objectives.Add(new(other, ScenarioObjective.ReachArea)
            { Count = 1, Selectors = [UnitSelector.ForUnit("enemy")], Area = new(0, 0, 1, 1) });
        GameEngine.CaptureObjectiveStartUnits(state);
        return state;
    }
    private static EngineResult End(GameState state) => GameEngine.Advance(state, new Choice("end-turn"), new Dice(), false);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static void Position(GameState state, string id, Cell cell) =>
        state.Physical.Figures[state.Physical.Figures.FindIndex(f => f.Id == id)] = new(id, cell);

    [Theory]
    [InlineData(Footprint.OneByOne, 3, 1, true)]
    [InlineData(Footprint.OneByOne, 2, 1, false)]
    [InlineData(Footprint.TwoByTwo, 3, 1, true)]
    [InlineData(Footprint.TwoByTwo, 4, 1, false)]
    [InlineData(Footprint.TwoByTwo, 3, 2, false)]
    public void ReachRequiresWholeFootprint(Footprint footprint, int x, int y, bool succeeds)
    {
        var state = Active(Reach()); state.Types[0] = state.Types[0] with { Footprint = footprint };
        Position(state, "actor", new(x, y));
        Assert.Equal(succeeds, End(state).State.IsCompleted);
    }

    [Fact]
    public void EveryFigureMustFitAndDefeatedUnitNeverQualifies()
    {
        var state = Active(Reach()); Position(state, "actor", new(3, 1));
        state.Physical.Figures.Add(new("actor", new(2, 2)));
        Assert.False(End(state).State.IsCompleted);
        state.Physical.Figures[^1] = new("actor", new(4, 2));
        Assert.True(End(state).State.IsCompleted);
        state.DefeatedUnitIds.Add("actor");
        Assert.False(End(state).State.IsCompleted);
    }

    [Fact]
    public void ReachUsesSimultaneousOccupancyNotPastVisits()
    {
        var state = Active(Reach()); state.PlaceUnit("fighter", "second", "Blue", new(1, 0));
        state.ObjectiveStartUnitIds["Blue"] = ["actor", "second"];
        Position(state, "actor", new(3, 1));
        Assert.False(End(state).State.IsCompleted);
        Position(state, "actor", new(0, 0)); Position(state, "second", new(4, 2));
        Assert.False(End(state).State.IsCompleted);
        Position(state, "actor", new(3, 1));
        Assert.True(End(state).State.IsCompleted);
        Position(state, "actor", new(0, 0));
        Assert.False(End(state).State.IsCompleted);
    }

    [Fact]
    public void MidActivationReachWaitsUntilCompletedActivation()
    {
        var state = Active(Reach()); state.MoveDone = false; state.ActionDone = false;
        state.Types[0] = state.Types[0] with { BonusActions = [new("Focus", null, [])] };
        var moved = GameEngine.Advance(state, new Choice("3,1"), new Dice(), false);
        Assert.Equal(new Cell(3, 1), moved.State.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.Null(moved.State.Completion);
        Assert.All(moved.ResolutionSteps, s => Assert.Null(s.StateAfter.Completion));
        Assert.True(End(moved.State).State.IsCompleted);
    }

    [Fact]
    public void FrozenSideIgnoresSpawnAndRetainsDefeatedInitialIdsAfterSaveAndCopy()
    {
        var state = Active(Reach()); state.PlaceUnit("fighter", "spawn", "Blue", new(1, 0));
        Position(state, "actor", new(3, 1));
        var result = End(Restore(state)); Assert.True(result.State.IsCompleted);
        Assert.Equal(new[] { "actor" }, result.State.ObjectiveStartUnitIds["Blue"]);
        result.State.ObjectiveStartUnitIds.Clear(); Assert.Single(state.ObjectiveStartUnitIds);
        GameEngine.ResolveDefeat(state, "actor");
        Assert.False(End(Restore(state)).State.IsCompleted);
        Assert.Contains("actor", state.ObjectiveStartUnitIds["Blue"]);
    }

    [Fact]
    public void DynamicReachIncludesSpawnsButExcludesDefeatedGroupMembers()
    {
        var state = Active(Reach(0, UnitSelector.Group("fighter", "Blue")));
        GameEngine.ResolveDefeat(state, "actor");
        Assert.False(End(state).State.IsCompleted); // empty does not achieve All
        state.PlaceUnit("fighter", "spawn", "Blue", new(3, 1));
        Assert.True(End(state).State.IsCompleted);
        state.PlaceUnit("fighter", "outside", "Blue", new(1, 0));
        Assert.False(End(state).State.IsCompleted);
    }

    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, false)]
    public void SelectorsFormIdentityUnion(int count, bool succeeds)
    {
        var state = Active(Reach(count, UnitSelector.ForUnit("actor"), UnitSelector.SideAtStart("Blue"), UnitSelector.Group("fighter", "Blue")));
        Position(state, "actor", new(3, 1)); Assert.Equal(succeeds, End(state).State.IsCompleted);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void EmptySelectionCannotAchieveEitherObjective(int count)
    {
        foreach (var objective in new[] { Reach(count, UnitSelector.Group("missing", "Blue")), Defeat(count, UnitSelector.Group("missing", "Blue")) })
            Assert.False(End(Active(objective)).State.IsCompleted);
    }

    [Fact]
    public void DefeatRequiresActualUnreplacedOutcomeAndPreservesDetachedLedger()
    {
        var state = Active(Defeat(1)); state.Physical.Figures.RemoveAll(f => f.Id == "actor");
        state.Units[0] = state.Units[0] with { CurrentHp = 0 };
        Assert.False(End(state).State.IsCompleted);
        state = Active(Defeat(1)); state.Types[0] = state.Types[0] with { Undying = new() };
        Assert.Equal("PostureChanged", GameEngine.ResolveDefeat(state, "actor").Kind);
        Assert.Empty(state.DefeatedUnitIds); Assert.False(End(state).State.IsCompleted);
        Assert.Equal("UnitDefeated", GameEngine.ResolveDefeat(state, "actor").Kind);
        var result = End(Restore(state)); Assert.Equal(new[] { "Red" }, result.State.Completion!.AchievedSideIds);
        result.State.DefeatedUnitIds.Clear(); Assert.Contains("actor", state.DefeatedUnitIds);
    }

    [Fact]
    public void DynamicDefeatRetainsDefeatedMembersAndIncludesLaterSpawns()
    {
        var state = Active(Defeat(0, UnitSelector.Group("fighter", "Blue"), UnitSelector.SideAtStart("Blue")));
        GameEngine.ResolveDefeat(state, "actor"); state.PlaceUnit("fighter", "spawn", "Blue", new(1, 0));
        Assert.False(End(state).State.IsCompleted);
        GameEngine.ResolveDefeat(state, "spawn"); Assert.True(End(Restore(state)).State.IsCompleted);
        state.Objectives[0] = Defeat(2, UnitSelector.SideAtStart("Blue"));
        Assert.False(End(state).State.IsCompleted);
    }

    [Fact]
    public void RoundStartDefeatWaitsForAllWorldCards()
    {
        var state = Active(Defeat(1)); state.Round = 0; state.Pending = null;
        state.WorldEffects = new(2, 1); state.WorldDeck = new() { DrawPile = [new("kill", WorldEffect.Loneliness), new("later", WorldEffect.Earthquake)] };
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(new[] { "Red" }, result.State.Completion!.AchievedSideIds);
        Assert.Equal(2, result.State.WorldDeck!.DiscardPile.Count);
        Assert.All(result.ResolutionSteps.Take(result.ResolutionSteps.Count - 1), s => Assert.Null(s.StateAfter.Completion));
    }

    private static ScenarioDefinition Definition(SideObjective objective) => new()
    {
        Board = new() { Width = 6, Height = 4 }, UnitTypeIds = [UnitTypeIds.Grunt, UnitTypeIds.Goblin],
        Units = [new(UnitTypeIds.Grunt, "Blue", new(0, 0), UnitId: "actor"), new(UnitTypeIds.Grunt, "Red", new(5, 3), UnitId: "enemy")],
        Agency = [new(UnitTypeIds.Grunt, "Blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "Red", ControllerKind.Automated), new(UnitTypeIds.Goblin, "Blue", ControllerKind.Human)],
        Objectives = [objective]
    };

    [Fact]
    public void ObjectiveExportRoundTripsAndFreezesCanonicalSetupIds()
    {
        var definition = Definition(Reach(0, UnitSelector.SideAtStart("Blue"), UnitSelector.ForUnit("actor"), UnitSelector.Group(UnitTypeIds.Goblin, "Blue")));
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var state = GameEngine.CreateGame(restored);
        Assert.Equal(definition.Objectives![0].Selectors.ToArray(), state.Objectives[0].Selectors.ToArray());
        Assert.Equal(new[] { "actor" }, state.ObjectiveStartUnitIds["Blue"]);
        Assert.Equal(new ObjectiveArea(3, 1, 2, 2), state.Objectives[0].Area);
    }

    [Fact]
    public void InvalidReferencesCountsSelectorsAndRectanglesFailSetup()
    {
        var invalid = new[]
        {
            Reach() with { Count = -1 }, Reach() with { Selectors = [] }, Reach() with { Area = null },
            Reach() with { Area = new(5, 1, 2, 2) }, Reach() with { Area = new(0, 0, 0, 1) },
            Reach(1, UnitSelector.ForUnit("typo")), Reach(1, UnitSelector.SideAtStart("typo")),
            Reach(1, UnitSelector.Group(UnitTypeIds.Goblin, "Red")),
            Reach(1, new UnitSelector(UnitSelectorKind.Unit, UnitId: "actor", SideId: "Blue")),
            Defeat() with { Area = new(0, 0, 1, 1) }
        };
        Assert.All(invalid, objective => Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Definition(objective))));
    }
    [Fact]
    public void DeclaredEmptyDynamicGroupIsValidButCannotSucceed()
    {
        foreach (var objective in new[] { Reach(0, UnitSelector.Group(UnitTypeIds.Goblin, "Blue")), Defeat(0, UnitSelector.Group(UnitTypeIds.Goblin, "Blue")) })
        {
            var state = GameEngine.CreateGame(Definition(objective));
            state.WorldEffects = new(1, 1);
            state.WorldDeck = new() { DrawPile = [new("calm", WorldEffect.Calm)] };
            Assert.False(GameEngine.StartRound(state, new Dice(), false).State.IsCompleted);
        }
    }

    [Fact]
    public void DefeatSelectorOverlapCountsOnceAndAllRequiresEveryInitialUnit()
    {
        var state = Active(Defeat(2, UnitSelector.ForUnit("actor"), UnitSelector.SideAtStart("Blue"), UnitSelector.Group("fighter", "Blue")));
        GameEngine.ResolveDefeat(state, "actor");
        Assert.False(End(state).State.IsCompleted);
        state.Objectives[0] = Defeat(0); // frozen selection includes the original actor only
        Assert.True(End(state).State.IsCompleted);
        state = Active(Defeat(0)); state.PlaceUnit("fighter", "second", "Blue", new(1, 0));
        state.ObjectiveStartUnitIds["Blue"] = ["actor", "second"];
        GameEngine.ResolveDefeat(state, "actor"); Assert.False(End(state).State.IsCompleted);
        GameEngine.ResolveDefeat(state, "second"); Assert.True(End(state).State.IsCompleted);
    }

    [Fact]
    public void DifferentObjectiveKindsCanCompleteSimultaneouslyAtSameBoundary()
    {
        var state = Active(Reach()); Position(state, "actor", new(3, 1));
        state.Objectives[1] = Defeat(1, UnitSelector.ForUnit("enemy"));
        GameEngine.ResolveDefeat(state, "enemy");
        var result = End(state);
        Assert.Equal(new[] { "Blue", "Red" }, result.State.Completion!.AchievedSideIds);
        Assert.Single(result.Events.Where(e => e.Kind == "GameCompleted"));
    }

    [Fact]
    public void RoundStartReachWaitsForLaterCardsAndAllowsLyingFigures()
    {
        var state = Active(Reach()); Position(state, "actor", new(3, 1)); state.Round = 0; state.Pending = null;
        state.WorldEffects = new(2, 1);
        state.WorldDeck = new() { DrawPile = [new("calm", WorldEffect.Calm), new("later", WorldEffect.Earthquake)] };
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(new[] { "Blue" }, result.State.Completion!.AchievedSideIds);
        Assert.All(result.State.Physical.Figures, f => Assert.Equal(Posture.Lying, f.Posture));
        Assert.Equal(2, result.State.WorldDeck!.DiscardPile.Count + result.State.WorldDeck.ActiveContinuous.Count);
        Assert.All(result.ResolutionSteps.Take(result.ResolutionSteps.Count - 1), s => Assert.Null(s.StateAfter.Completion));
    }
    [Fact]
    public void CompactObjectiveJsonOmitsIrrelevantNullableFieldsAndRoundTripsSelectors()
    {
        var objective = Defeat(1, UnitSelector.ForUnit("actor"), UnitSelector.SideAtStart("Blue"), UnitSelector.Group("fighter", "Blue"));
        var json = JsonSerializer.Serialize(objective);
        Assert.DoesNotContain(":null", json);
        var restored = JsonSerializer.Deserialize<SideObjective>(json)!;
        Assert.Equal(objective.SideId, restored.SideId);
        Assert.Equal(objective.Objective, restored.Objective);
        Assert.Equal(objective.Count, restored.Count);
        Assert.Equal(objective.Selectors.ToArray(), restored.Selectors.ToArray());
        Assert.Null(restored.Area);
    }
}
