using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MixedWaterTerrainTests
{
    private static readonly string[] MixedIds = ["wet-grass", "rocky-water", "stepping-stones"];
    private static UnitType Walker(params string[] kinds) => new("mixed-water-walker", 8, 1, 1, 1, 5)
        { BaseMovementCapabilities = [.. kinds], CanHaveEquipment = true, Unique = true };
    private static ScenarioDefinition Fixture(UnitType type, params string[] columns) => new()
    {
        Board = new() { Width = columns.Length, Height = 2,
            Cells = columns.SelectMany((id, x) => Enumerable.Range(0, 2).Select(y =>
                new CellDefinition(new(x, y), TerrainKind.StoneFloor) { TerrainTypeId = id })).ToList() },
        UnitTypes = [type], UnitTypeIds = [type.Id],
        Units = [new(type.Id, "blue", new(0, 0), UnitId: "actor")],
        Agency = [new(type.Id, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };

    [Theory]
    [InlineData("wet-grass", "vegetation", "WetGrass", null)]
    [InlineData("rocky-water", "rock", "RockyWater", null)]
    [InlineData("stepping-stones", "constructed", "SteppingStones", "water")]
    public void CanonicalDeclarationsKeepCategoriesPermissionsAndPresentationIndependent(
        string id, string category, string recipe, string? destroyed)
    {
        var terrain = TerrainCatalog.All.Single(t => t.Id == id);
        Assert.Equal(new[] { "land", "water" }, terrain.MovementPermissions);
        Assert.Equal(new[] { "land", "water", category }, terrain.Categories);
        Assert.True(terrain.Passable);
        Assert.True(terrain.AllowsResourceMarkers);
        Assert.False(terrain.BlocksLos);
        Assert.False(terrain.BlocksVision);
        Assert.Null(terrain.Production);
        Assert.Null(terrain.Assembly);
        Assert.Null(terrain.LegacyKind);
        Assert.Equal(recipe, terrain.Presentation.Recipe);
        Assert.Equal(destroyed, terrain.DestroyedTerrainTypeId);
        Assert.False(MovementTraversal.OccupancyFor(Walker(category)).CanTraverse(terrain));
        Assert.True(new TerrainRestriction([category]).Accepts(terrain));
    }

    [Theory]
    [InlineData("land", false, false, false)]
    [InlineData("water", false, false, false)]
    [InlineData("land", true, false, true)]
    [InlineData("water", true, false, false)]
    [InlineData("land", false, true, false)]
    [InlineData("water", false, true, false)]
    public void CanonicalCrossingRouteUsesReceivingCapabilitiesAndPhaseEndpoints(
        string kind, bool amphibious, bool phase, bool both)
    {
        var type = Walker(kind) with { Amphibious = amphibious,
            LocalAbilities = phase ? [new("phase", "Phase", AbilityTiming.Passive, new Phase())] : [] };
        var state = GameEngine.CreateGame(Fixture(type,
            "wet-grass", "rocky-water", "stepping-stones", "water", "grass", "wet-grass"));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Contains(new Cell(1, 0), paths.Keys);
        Assert.Contains(new Cell(2, 0), paths.Keys);
        Assert.Equal(kind == "water" || both, paths.ContainsKey(new(3, 0)));
        Assert.Equal(both || phase && kind == "land", paths.ContainsKey(new(4, 0)));
        Assert.Equal(both || phase, paths.ContainsKey(new(5, 0)));
        Assert.Equal(2, MovementRules.Cost(paths[new(2, 0)]));
        Assert.Equal(kind == "water" || both, SpatialRules.CanPlaceUnit(state, "actor", new(3, 0)));
        Assert.Equal(kind == "land" || both, SpatialRules.CanPlaceUnit(state, "actor", new(4, 0)));
    }

    [Theory]
    [InlineData("land")]
    [InlineData("water")]
    public void TwoByTwoCrossingFootprintsRespectEveryInternalEdge(string kind)
    {
        var state = GameEngine.CreateGame(Fixture(Walker(kind) with { Footprint = Footprint.TwoByTwo },
            "wet-grass", "rocky-water", "stepping-stones"));
        Assert.True(SpatialRules.CanPlaceUnit(state, "actor", new(1, 0)));
        foreach (var edgeKind in Enum.GetValues<EdgeKind>())
        {
            state.Physical.Board.Edges.Clear();
            var edge = new Edge(new(1, 0), new(2, 0), edgeKind);
            Assert.True(EdgeReplacementRules.CanReplace(state, edge));
            EdgeReplacementRules.ReplaceEdges(state, [edge]);
            Assert.Equal(edgeKind is EdgeKind.None or EdgeKind.OpenDoor,
                SpatialRules.CanPlaceUnit(state, "actor", new(1, 0)));
        }
    }

    [Fact]
    public void EveryEdgeKindIsValidBetweenEveryCanonicalMixedWaterPair()
    {
        foreach (var first in MixedIds.Append("water"))
        foreach (var second in MixedIds.Append("grass"))
        foreach (var kind in Enum.GetValues<EdgeKind>())
        {
            var state = GameEngine.CreateGame(Fixture(Walker("land"), "grass", first, second));
            var edge = new Edge(new(1, 0), new(2, 0), kind);
            Assert.True(EdgeReplacementRules.CanReplace(state, edge));
            EdgeReplacementRules.ReplaceEdges(state, [edge]);
            Assert.Equal(kind, state.Physical.Board.EdgeBetween(edge.A, edge.B));
            GameEngine.ValidateScenario(state);
        }
    }

    private static GameState BreakState(AbilityMechanic mechanic)
    {
        var type = Walker("land") with { LocalAbilities = [new("break", "Break", AbilityTiming.Action, mechanic)] };
        var state = GameEngine.CreateGame(Fixture(type, "wet-grass", "stepping-stones", "rocky-water"));
        state.Round = 1; state.ActiveToken = state.Units[0].Token; state.CurrentUnitId = "actor";
        state.ResolvingTokenUnitIds = ["actor"]; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, type.Id, "actor", [], false);
        return state;
    }

    private static EngineResult Break(GameState state, Dice dice, Cell? target = null) => TestGame.Advance(state,
        new Choice(r => r.Candidates.Single(c => c.Ability?.Definition.Key == "break" &&
            (target is null || c.BreakTarget?.Cell == target)).Key), dice, false);

    [Theory]
    [InlineData("land", false)]
    [InlineData("phase", false)]
    [InlineData("water", true)]
    [InlineData("amphibious", true)]
    [InlineData("native", true)]
    [InlineData("equipment", true)]
    public void BreakingOccupiedSteppingStonesUsesPersistentCapabilitiesWhileLying(string profile, bool allowed)
    {
        var state = BreakState(new BreakObject(6));
        var occupant = Walker(profile == "water" ? "water" : "land") with
            { Id = "occupant", Amphibious = profile == "amphibious" };
        if (profile is "native" or "phase") occupant = occupant with { LocalAbilities = [new("grant", "Grant",
            AbilityTiming.Passive, profile == "phase" ? new Phase() : new MovementCapabilityGrant(["water"]))] };
        state.Types.Add(occupant);
        var unit = occupant.CreateUnit("occupant", "blue");
        if (profile == "equipment")
        {
            state.EquipmentTypes.Add(new("test-water-boots", "Water Boots") { LocalAbilities = [new("water", "Water",
                AbilityTiming.Passive, new MovementCapabilityGrant(["water"]))] });
            unit = unit with { Inventory = [new OwnedObject("physical-boots") { EquipmentTypeId = "test-water-boots" }] };
        }
        state.Units.Add(unit);
        state.Physical.Figures.Add(new("occupant", new(1, 0), Posture.Lying));
        Assert.Equal(allowed, BreakObjectRules.CanBreak(state, new(Cell: new(1, 0))));
        Assert.Equal(allowed, BreakObjectRules.Targets(state, [new(0, 0)]).Any(t => t.Cell == new Cell(1, 0)));
        if (!allowed) return;
        var result = Break(state, new(), new(1, 0));
        Assert.Equal("water", result.State.Physical.Board.TerrainDefinitionAt(new(1, 0)).Id);
        Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single(f => f.Id == "occupant").Posture);
        Assert.Equal(JsonSerializer.Serialize(unit), JsonSerializer.Serialize(result.State.Units.Single(u => u.Id == "occupant")));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        GameEngine.ValidateScenario(restored);
        Assert.Contains("water", restored.EffectiveMovementCapabilitiesOf("occupant"));
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("marker")]
    [InlineData("machine")]
    public void AreaPreflightPreservesGroundLayerInvariantsAndDoesNotRollForUnsafeCrossing(string layer)
    {
        var state = BreakState(new BreakAdjacentObjects(3));
        var target = new Cell(1, 0);
        if (layer == "resource") state.Resources.Add(new(ResourceTypes.Coal, target));
        if (layer == "marker") state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, target));
        if (layer == "machine") state.Physical.Board.Machines.Add(new("machine", target,
            MachineTypes.Beacon with { RequiredTerrainTypeId = "stepping-stones" }));
        var dice = new Dice(1);
        var result = Break(state, dice);
        var outcome = Assert.Single(Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved").BreakOutcomes);
        Assert.Equal(new Cell(1, 1), outcome.Target.Cell);
        Assert.Equal(1, dice.Count);
        Assert.Equal("stepping-stones", result.State.Physical.Board.TerrainDefinitionAt(target).Id);
        Assert.Equal(state.Resources, result.State.Resources);
        Assert.Equal(state.CellMarkers, result.State.CellMarkers);
        Assert.Equal(state.Physical.Board.Machines, result.State.Physical.Board.Machines);
        GameEngine.ValidateScenario(result.State);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void AreaBreakFreezesCanonicalCrossingsAndEdgesWithDeterministicReplay(int threshold)
    {
        string? expected = null;
        foreach (var reverse in new[] { false, true })
        {
            var state = BreakState(new BreakAdjacentObjects(threshold));
            state.Physical.Board.Edges.Add(new(new(1, 0), new(1, 1), EdgeKind.Wall));
            if (reverse) { state.Physical.Board.Terrain.Reverse(); state.Physical.Board.Edges[0] = new(new(1, 1), new(1, 0), EdgeKind.Wall); }
            var dice = new Dice(1, 6, 1);
            var result = Break(state, dice);
            var resolved = Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved");
            Assert.Equal(new BreakTarget[] { new(Cell: new(1, 0)), new(Cell: new(1, 1)),
                new(Edge: new(new(1, 0), new(1, 1), EdgeKind.Wall)) }, resolved.BreakOutcomes.Select(o => o.Target));
            Assert.Equal(threshold == 6 ? 0 : 3, dice.Count);
            Assert.Equal("water", result.State.Physical.Board.TerrainDefinitionAt(new(1, 0)).Id);
            Assert.Equal(threshold == 6 ? "water" : "stepping-stones", result.State.Physical.Board.TerrainDefinitionAt(new(1, 1)).Id);
            Assert.Equal("rocky-water", result.State.Physical.Board.TerrainDefinitionAt(new(2, 0)).Id);
            Assert.Equal("wet-grass", result.State.Physical.Board.TerrainDefinitionAt(new(0, 0)).Id);
            var replay = Break(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!, new(1, 6, 1));
            Assert.Equal(JsonSerializer.Serialize(result.Events), JsonSerializer.Serialize(replay.Events));
            Assert.Equal(JsonSerializer.Serialize(result.State), JsonSerializer.Serialize(replay.State));
            var serialized = JsonSerializer.Serialize(resolved.BreakOutcomes);
            if (expected is not null) Assert.Equal(expected, serialized);
            expected = serialized;
            GameEngine.ValidateScenario(result.State);
        }
    }

    private sealed class Choice(Func<DecisionRequest, string> select) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => select(request); }
    private sealed class Dice(params int[] rolls) : IRandomProvider
    {
        public int Count;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => rolls[Count++];
    }
}
