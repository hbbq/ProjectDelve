using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class VisionCapabilityTests
{
    private static LocalAbilityDefinition Passive(string key, AbilityMechanic mechanic) => new(key, key, AbilityTiming.Passive, mechanic);
    private static ScenarioDefinition Scenario(UnitType? type = null) => new()
    {
        Board = new() { Width = 20, Height = 4 }, UnitTypeIds = ["scout"],
        UnitTypes = [type ?? new("scout", 4, 1, 1, 1, 1) { CanHaveEquipment = true }],
        Units = [new("scout", "blue", new(1, 1), UnitId: "a")],
        Agency = [new("scout", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], FogOfWarSides = ["blue"], Loot = LootSettings.Empty
    };
    private static bool Known(GameState state, int x, int y = 1) => FogOfWar.IsExplored(state, "blue", new(x, y));
    private static void ResetSight(GameState state) { state.ExploredCells.Clear(); FogOfWar.Refresh(state); }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Pick(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static GameState Ready(GameState state)
    {
        state.Round = 1; state.CurrentUnitId = "a"; state.ActiveToken = state.Units[0].Token;
        state.ResolvingTokenUnitIds = ["a"];
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    [Fact]
    public void RangeDefaultsToFiveAndTracksBaseEquipmentTemporaryAndPostureChanges()
    {
        var state = GameEngine.CreateGame(Scenario());
        Assert.Equal(5, state.EffectiveSightDistanceOf("a")); Assert.True(Known(state, 6)); Assert.False(Known(state, 7));
        state.Types[0] = state.Types[0] with { SightDistance = 2 };
        ResetSight(state); Assert.True(Known(state, 3)); Assert.False(Known(state, 4));
        state.Units[0] = state.Units[0] with { Inventory = [new("glass-1", "binoculars")] };
        ResetSight(state); Assert.Equal(5, state.EffectiveSightDistanceOf("a")); Assert.True(Known(state, 6));
        state.CurrentUnitId = "a"; state.ModifiersThisTurn.Add(new(Stat.SightDistance, 2));
        ResetSight(state); Assert.Equal(7, state.EffectiveSightDistanceOf("a")); Assert.True(Known(state, 8));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        ResetSight(state); Assert.Equal(4, state.EffectiveSightDistanceOf("a")); Assert.False(Known(state, 6));
        state.ModifiersThisTurn.Add(new(Stat.SightDistance, -100));
        ResetSight(state); Assert.Equal(0, state.EffectiveSightDistanceOf("a"));
        Assert.Equal(new Cell(1, 1), Assert.Single(state.ExploredCells["blue"]));
    }

    [Fact]
    public void SelfAndAdjacentSightModifiersShareStatsAndObservationAndRespectProviderPosture()
    {
        var type = Scenario().UnitTypes![0] with { SightDistance = 1, LocalAbilities = [Passive("self", new SelfModifiersMechanic([new(Stat.SightDistance, 1)]))] };
        var definition = Scenario(type);
        definition.UnitTypes!.Add(new("guide", 1, 0, 0, 0, 1)
        { LocalAbilities = [Passive("aura", new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.SightDistance, 2)]))] });
        definition.UnitTypeIds.Add("guide"); definition.Units.Add(new("guide", "blue", new(1, 2), UnitId: "guide"));
        definition.Agency.Add(new("guide", "blue", ControllerKind.Human));
        var state = GameEngine.CreateGame(definition);
        Assert.Equal(4, state.EffectiveSightDistanceOf("a"));
        Assert.Equal(4, FogOfWar.Observe(state, "blue").EffectiveSightDistanceOf("a"));
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying };
        ResetSight(state); Assert.Equal(2, state.EffectiveSightDistanceOf("a"));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        ResetSight(state); Assert.Equal(1, state.EffectiveSightDistanceOf("a"));
    }

    [Fact]
    public void ObservationPreservesSightFromHiddenAuraProvider()
    {
        var definition = Scenario(Scenario().UnitTypes![0] with { SightDistance = 0 });
        definition.UnitTypes!.Add(new("guide", 0, 0, 0, 0, 1)
        { LocalAbilities = [Passive("darkness", new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Enemies, [new(Stat.SightDistance, -1)]))] });
        definition.UnitTypeIds.Add("guide"); definition.Units.Add(new("guide", "red", new(2, 1), UnitId: "hidden"));
        definition.Agency.Add(new("guide", "red", ControllerKind.Human));
        definition = definition with { Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)] };
        var state = GameEngine.CreateGame(definition);
        // Restore positive base sight without refreshing exploration: observation
        // must preserve the complete world's modifier despite filtering the source.
        state.Types[0] = state.Types[0] with { SightDistance = 2 };
        var observation = FogOfWar.Observe(state, "blue");
        Assert.DoesNotContain(observation.Units, u => u.Id == "hidden");
        Assert.Equal(1, state.EffectiveSightDistanceOf("a"));
        Assert.Equal(1, observation.EffectiveSightDistanceOf("a"));
    }

    [Fact]
    public void SharedAreaUsesFootprintManhattanDistanceBlockingDestinationCornersAndMachineExclusion()
    {
        var board = new Board(10, 3, []);
        board.Machines.Add(new("source", new(1, 1), MachineTypes.DiamondPress));
        board.Machines.Add(new("blocker", new(3, 1), MachineTypes.DiamondPress));
        Assert.DoesNotContain(new Cell(2, 1), FogOfWar.VisionArea(board, [new(1, 1)], 5));
        var area = FogOfWar.VisionArea(board, [new(1, 1)], 5, excludedMachineId: "source");
        Assert.Contains(new Cell(3, 1), area); Assert.DoesNotContain(new Cell(4, 1), area);
        Assert.Contains(new Cell(6, 1), FogOfWar.VisionArea(board, [new(1, 1)], 5, phase: true));
        Assert.DoesNotContain(new Cell(7, 1), FogOfWar.VisionArea(board, [new(1, 1)], 5, phase: true));
        var origins = FootprintGeometry.OccupiedCells(Footprint.TwoByTwo, new(0, 0));
        Assert.Contains(new Cell(3, 1), FogOfWar.VisionArea(board, origins, 2, phase: true));
        board.Machines.Clear(); board.Terrain.Add(new(new(1, 0), TerrainKind.SolidRock));
        board.Terrain.Add(new(new(0, 1), TerrainKind.SolidRock));
        Assert.DoesNotContain(new Cell(1, 1), FogOfWar.VisionArea(board, [new(0, 0)], 2));
        board.Terrain.RemoveAt(1);
        Assert.Contains(new Cell(1, 1), FogOfWar.VisionArea(board, [new(0, 0)], 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevealUsesAuthoredDistancePhaseAndSourceAccountingAndPersistsAcrossSave(bool equipment)
    {
        var reveal = new LocalAbilityDefinition("survey", "Survey", AbilityTiming.BonusAction, new RevealDistance(8)) { Usage = new(1) };
        var definition = Scenario(Scenario().UnitTypes![0] with { SightDistance = 1, Footprint = Footprint.TwoByTwo,
            LocalAbilities = equipment ? [] : [reveal] });
        if (equipment)
        {
            definition = definition with { EquipmentTypes = [new("map", "Map") { LocalAbilities = [reveal] }] };
            definition.Units[0] = definition.Units[0] with { Inventory = [new("physical-map", "map"), new("sheet", "bedsheet")] };
        }
        else definition.UnitTypes![0] = definition.UnitTypes[0].WithLocal(Passive("phase", new Phase()));
        definition.Board.Edges.Add(new(new(3, 1), EdgeDirection.Right, EdgeKind.Wall));
        var state = Ready(GameEngine.CreateGame(definition));
        Assert.False(Known(state, 10));
        var candidate = Assert.Single(state.Pending!.Candidates.Where(c => c.Ability?.Definition.Mechanic is RevealDistance));
        Assert.Equal(equipment ? "physical-map" : "a", candidate.Ability!.SourceInstanceId);
        var outcome = GameEngine.Advance(state, new Pick(candidate.Key), new Dice(), false);
        var replay = GameEngine.Advance(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!, new Pick(candidate.Key), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(outcome), JsonSerializer.Serialize(replay));
        var result = outcome.State;
        var revealEvent = outcome.Events.FindIndex(e => e.Kind == "ActionUsed" && e.ActionId == candidate.Ability.ActionId);
        var snapshot = outcome.ResolutionSteps.Single(s => s.EventIndex == revealEvent).StateAfter;
        Assert.True(Known(snapshot, 10));
        Assert.Equal(0, snapshot.AbilityUsesOf("a", candidate.Ability)!.RemainingUses);
        Assert.True(Known(result, 10)); Assert.False(Known(result, 11));
        Assert.Equal(0, result.AbilityUsesOf("a", candidate.Ability)!.RemainingUses);
        Assert.DoesNotContain(result.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is RevealDistance);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result))!;
        GameEngine.ValidateScenario(restored); Assert.True(Known(restored, 10));
        Assert.Equal(equipment ? "physical-map" : "a", restored.AbilitiesOf("a").Single(a => a.Definition.Mechanic is RevealDistance).SourceInstanceId);
    }

    [Fact]
    public void RevealIsLegalWithoutNewCellsAndStillUsesItsOpportunity()
    {
        var definition = Scenario(Scenario().UnitTypes![0].WithLocal(new("survey", "Survey", AbilityTiming.BonusAction, new RevealDistance(0))));
        var state = Ready(GameEngine.CreateGame(definition));
        var known = state.ExploredCells["blue"].ToHashSet();
        var candidate = Assert.Single(state.Pending!.Candidates.Where(c => c.Ability?.Definition.Mechanic is RevealDistance));
        var result = GameEngine.Advance(state, new Pick(candidate.Key), new Dice(), false).State;
        Assert.True(known.SetEquals(result.ExploredCells["blue"]));
        Assert.DoesNotContain(result.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is RevealDistance);
    }

    [Theory]
    [InlineData(AbilityTiming.Action)]
    [InlineData(AbilityTiming.FreeAction)]
    public void RevealUsesOrdinaryGeometryAndTimingEvenWhenNoFogIsEnabled(AbilityTiming timing)
    {
        var definition = Scenario(Scenario().UnitTypes![0] with { SightDistance = 0,
            LocalAbilities = [new("survey", "Survey", timing, new RevealDistance(7)) { Usage = new(1) }] });
        definition.Board.Cells.Add(new(new(3, 1), TerrainKind.SolidRock));
        foreach (var fog in new[] { true, false })
        {
            var state = GameEngine.CreateGame(definition with { FogOfWarSides = fog ? ["blue"] : [] });
            state.MoveDone = true; state = Ready(state);
            var candidate = Assert.Single(state.Pending!.Candidates.Where(c => c.Ability?.Definition.Mechanic is RevealDistance));
            var outcome = GameEngine.Advance(state, new Pick(candidate.Key), new Dice(), false);
            var result = outcome.State;
            var used = outcome.Events.FindIndex(e => e.Kind == "ActionUsed" && e.ActionId == candidate.Ability!.ActionId);
            var snapshot = outcome.ResolutionSteps.Single(s => s.EventIndex == used).StateAfter;
            Assert.Equal(0, result.AbilityUsesOf("a", candidate.Ability!)!.RemainingUses);
            if (fog) { Assert.True(Known(result, 3)); Assert.False(Known(result, 4)); }
            else Assert.Empty(result.ExploredCells);
            Assert.Equal(timing == AbilityTiming.Action ? 1 : 0, snapshot.ActionsUsedThisActivation);
        }
    }

    [Fact]
    public void SweepUsesEffectiveSightAndExplorationQueriesUseItWithoutChangingHistory()
    {
        var state = GameEngine.CreateGame(Scenario(Scenario().UnitTypes![0] with { SightDistance = 1,
            LocalAbilities = [Passive("phase", new Phase())] }));
        state.CurrentUnitId = "a"; state.ModifiersThisTurn.Add(new(Stat.SightDistance, 2));
        FogOfWar.Sweep(state, "a", [new(1, 1), new(2, 1), new(3, 1)]);
        Assert.True(Known(state, 6)); Assert.False(Known(state, 7));
        var before = state.ExploredCells["blue"].ToHashSet();
        var queries = new GameplayQueries(state);
        Assert.Equal(4, queries.PotentialDiscoveryAlongPath("a", [new(4, 1)]));
        Assert.True(before.SetEquals(state.ExploredCells["blue"]));
    }

    [Fact]
    public void SweptVisionReevaluatesAurasWhileAdvisoryForecastUsesDecisionTimeRange()
    {
        var definition = Scenario(Scenario().UnitTypes![0] with { SightDistance = 0 });
        definition.UnitTypes!.Add(new("guide", 0, 0, 0, 0, 1) { SightDistance = 0,
            LocalAbilities = [Passive("aura", new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.SightDistance, 3)]))] });
        definition.UnitTypeIds.Add("guide"); definition.Units.Add(new("guide", "blue", new(3, 1), UnitId: "guide"));
        definition.Agency.Add(new("guide", "blue", ControllerKind.Human));
        var state = GameEngine.CreateGame(definition);
        Assert.Equal(0, state.EffectiveSightDistanceOf("a"));
        Assert.Equal(1, new GameplayQueries(state).PotentialDiscoveryAlongPath("a", [new(2, 1)]));
        FogOfWar.Sweep(state, "a", [new(1, 1), new(2, 1)]);
        Assert.True(Known(state, 5)); Assert.False(Known(state, 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistentCapabilitiesAllowLyingWaterOccupancyRelocationAndSaveButPhaseCannotStopInRock(bool native)
    {
        var definition = Scenario(Scenario().UnitTypes![0] with { LocalAbilities = native ? [Passive("swim", new Amphibious()), Passive("phase", new Phase())] : [] });
        definition = definition with { FogOfWarSides = [] };
        definition.Board.Cells.Add(new(new(1, 1), TerrainKind.Water));
        definition.Board.Cells.Add(new(new(2, 1), TerrainKind.SolidRock));
        definition.Units[0] = definition.Units[0] with { Posture = Posture.Lying,
            Inventory = native ? [] : [new("tube-1", "swim-tube"), new("sheet-1", "bedsheet"), new("glass-1", "binoculars")] };
        var state = GameEngine.CreateGame(definition);
        Assert.True(state.HasAmphibious("a")); Assert.True(state.HasPhase("a")); Assert.Equal(5, state.EffectiveSightDistanceOf("a"));
        state.FogOfWarSides = ["blue"]; ResetSight(state);
        Assert.True(Known(state, 3));
        state.FogOfWarSides.Clear();
        var paths = MovementRules.FindPaths(state, "a", new(1, 1), 3);
        Assert.Contains(new Cell(3, 1), paths.Keys); Assert.DoesNotContain(new Cell(2, 1), paths.Keys);
        Assert.NotEmpty(new GameplayQueries(state).MovementPathsFrom("a", new(1, 1)));
        state.PlaceUnit("scout", "b", "blue", new(0, 1));
        Assert.Equal(native, ExternalMovementRules.CanSwap(state, "b", "a"));
        if (!native)
        {
            state.Units[1] = state.Units[1] with { Inventory = [new("tube-2", "swim-tube")] };
            Assert.True(ExternalMovementRules.CanSwap(state, "b", "a"));
        }
        Assert.Contains(new Cell(1, 0), ExternalMovementRules.Destinations(state, "b", "a", 1));
        Assert.DoesNotContain(new Cell(2, 1), ExternalMovementRules.Destinations(state, "b", "a", 1));
        GameEngine.ValidateScenario(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!);
    }

    [Fact]
    public void ContentBoundaryRejectsNegativeRangesAndRoundTripsTypedMechanics()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario(Scenario().UnitTypes![0] with { SightDistance = -1 })));
        Assert.Throws<ArgumentException>(() => AbilityContent.Validate([new("bad", "Bad", AbilityTiming.Action, new RevealDistance(-1))], AbilitySourceKind.Unit));
        AbilityContent.Validate([Passive("water", new Amphibious())], AbilitySourceKind.Equipment);
        AbilityMechanic mechanic = new RevealDistance(7);
        Assert.Equal(mechanic, JsonSerializer.Deserialize<AbilityMechanic>(JsonSerializer.Serialize(mechanic)));
        var ability = new LocalAbilityDefinition("reveal", "Survey", AbilityTiming.Action, mechanic);
        Assert.Contains("7", ability.CardEntry("test").Description);
        Assert.Contains("Sight Distance +3", EquipmentCatalog.All.Single(e => e.Id == "binoculars").Description);
    }
}

