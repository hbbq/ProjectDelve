using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PlacementLookupTests
{
    private static GameState World() => new()
    {
        Physical = new(new Board(8, 7, []), []), Units = [],
        Types = [new("small", 1, 1, 1, 1, 1),
            new("large", 1, 1, 1, 1, 1) { Footprint = Footprint.TwoByTwo }]
    };

    private static void Add(GameState state, string id, string typeId, Cell anchor, Posture posture = Posture.Upright)
    {
        state.Units.Add(state.Types.Single(t => t.Id == typeId).CreateUnit(id, "blue"));
        state.Physical.Figures.Add(new(id, anchor, posture));
    }

    // Deliberately retain the pre-optimization implementation as an independent oracle.
    private static bool Legacy(GameState state, UnitType type, Cell anchor, string? excludedId = null) =>
        SpatialRules.Fits(state.Physical.Board, type.Footprint, anchor, MovementTraversal.OccupancyFor(type)) &&
        !state.Physical.Figures.Any(f => f.Id != excludedId && FootprintGeometry.OccupiedCells(state, f.Id)
            .Intersect(FootprintGeometry.OccupiedCells(type.Footprint, anchor)).Any());

    [Fact]
    public void LookupMatchesOrderedScanAcrossOverlapsFootprintsAndExcludedIdentities()
    {
        var state = World();
        Add(state, "large-a", "large", new(1, 1), Posture.Lying);
        Add(state, "large-b", "large", new(2, 2));
        Add(state, "small", "small", new(5, 4));
        // Physical figures, including malformed defeated figures, retain occupancy.
        state.Units[2] = state.Units[2] with { CurrentHp = 0 };
        var lookup = new PlacementLookup(state);
        foreach (var type in state.Types)
        foreach (var excluded in new string?[] { null, "large-a", "large-b", "small", "absent" })
        for (var y = -1; y <= 7; y++)
        for (var x = -1; x <= 8; x++)
            Assert.Equal(Legacy(state, type, new(x, y), excluded),
                SpatialRules.CanPlaceUnit(state, type, new(x, y), excluded, lookup));
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[1], new(1, 1), "large-a", lookup));
    }

    [Fact]
    public void TerrainMachinesInternalEdgesAndIntrinsicOccupancyRemainAuthoritative()
    {
        var state = World();
        var phase = state.Types[1].WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        var amphibious = phase with { Amphibious = true };
        state.Physical.Board.Terrain.Add(new(new(2, 2), TerrainKind.Water));
        var lookup = new PlacementLookup(state);
        Assert.False(SpatialRules.CanPlaceUnit(state, phase, new(1, 1), lookup: lookup));
        Assert.True(SpatialRules.CanPlaceUnit(state, amphibious, new(1, 1), lookup: lookup));
        state.Physical.Board.Machines.Add(new("press", new(2, 2), MachineTypes.DiamondPress));
        Assert.False(SpatialRules.CanPlaceUnit(state, amphibious, new(1, 1)));
        state.Physical.Board.Machines.Clear();
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.ClosedDoor));
        Assert.False(SpatialRules.CanPlaceUnit(state, amphibious, new(1, 1)));
        state.Physical.Board.Edges[0] = state.Physical.Board.Edges[0] with { Kind = EdgeKind.OpenDoor };
        Assert.True(SpatialRules.CanPlaceUnit(state, amphibious, new(1, 1)));
    }

    [Fact]
    public void FreshQueriesObserveDirectMovementCreationDefeatAndTypeChanges()
    {
        var state = World();
        Add(state, "actor", "small", new(1, 1));
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1)));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(3, 3) };
        Assert.True(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1)));
        Add(state, "other", "small", new(1, 1));
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1)));
        state.Units[1] = state.Units[1] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAt(1);
        Assert.True(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1)));
        state.Types[0] = state.Types[0] with { Footprint = Footprint.TwoByTwo };
        Assert.False(SpatialRules.CanPlaceUnit(state, Footprint.OneByOne, new(4, 4)));
        state.Units[0] = state.Units[0] with { TypeId = "large" };
        state.Types[1] = state.Types[1] with { Footprint = Footprint.OneByOne };
        Assert.True(SpatialRules.CanPlaceUnit(state, Footprint.OneByOne, new(4, 4)));
    }

    [Fact]
    public void CopiesAndNewContextsDoNotShareOccupancyOrBoardResults()
    {
        var state = World();
        Add(state, "actor", "small", new(1, 1));
        var first = new PlacementLookup(state);
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1), lookup: first));
        var copy = state.Copy();
        copy.Physical.Figures[0] = copy.Physical.Figures[0] with { Position = new(5, 5) };
        Assert.True(SpatialRules.CanPlaceUnit(copy, copy.Types[0], new(1, 1), lookup: new(copy)));
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1), lookup: first));
        state.Physical.Figures.Clear();
        Assert.True(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1), lookup: new(state)));
        state.Physical.Board.Terrain.Add(new(new(1, 1), TerrainKind.SolidRock));
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1)));
    }

    [Fact]
    public void EdgeLookupRequiresOneFigureSpanningBothCells()
    {
        var state = World();
        Add(state, "a", "small", new(1, 1));
        Add(state, "b", "small", new(2, 1));
        var wall = new Edge(new(1, 1), new(2, 1), EdgeKind.Wall);
        Assert.True(EdgeReplacementRules.CanReplace(state, wall));
        Add(state, "large", "large", new(1, 1), Posture.Lying);
        Assert.False(EdgeReplacementRules.CanReplace(state, wall));
        EdgeReplacementRules.ReplaceEdges(state, [wall, new(new(5, 5), new(6, 5), EdgeKind.Wall)]);
        Assert.Equal(EdgeKind.None, state.Physical.Board.EdgeBetween(wall.A, wall.B));
        Assert.Equal(EdgeKind.Wall, state.Physical.Board.EdgeBetween(new(5, 5), new(6, 5)));
    }

    [Fact]
    public void AttackBatchMatchesPerTargetValidationIncludingPassivesCopiesAndInvalidTerrain()
    {
        var state = World();
        state.Types[1] = (state.Types[1] with { Rng = 6 }).WithLocal(
            new("backstab", "Backstab", AbilityTiming.Passive, new Backstab()));
        Add(state, "actor", "large", new(0, 2));
        Add(state, "near", "small", new(2, 2));
        Add(state, "blocker", "small", new(3, 2));
        Add(state, "far", "large", new(5, 2));
        for (var i = 1; i < state.Units.Count; i++)
            state.Units[i] = state.Units[i] with { SideId = "red" };
        Add(state, "ally", "small", new(2, 3));

        static string[] LegacyTargets(GameState world, Cell from) => world.Units
            .Where(target => AttackRules.EvaluateFrom(world, "actor", from, target.Id) == UnitTargetEvaluation.Possible)
            .Select(target => $"attack:{target.Id}").ToArray();
        static string[] BatchTargets(GameState world)
        {
            world.CurrentUnitId = "actor";
            world.MoveDone = true;
            return GameEngine.GameplayCandidates(world, world.Units[0])
                .Where(c => c.Action == UnitAction.NormalAttack).Select(c => c.Key).ToArray();
        }

        Assert.Equal(LegacyTargets(state, new(0, 2)), BatchTargets(state));
        Assert.True(state.EffectiveAtkAgainst("actor", "near") > state.EffectiveAtkOf("actor"));
        var copy = state.Copy();
        copy.Physical.Figures[0] = copy.Physical.Figures[0] with { Position = new(0, 0) };
        Assert.Equal(LegacyTargets(state, new(0, 0)), BatchTargets(copy));
        copy.Physical.Board.Terrain.Add(new(new(2, 0), TerrainKind.Tree));
        Assert.Equal(LegacyTargets(copy, new(0, 0)), BatchTargets(copy));
        // Phase does not permit an actor's complete footprint to stand on blocking terrain.
        copy.Types[1] = copy.Types[1].WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        copy.Physical.Board.Terrain.Add(new(new(1, 1), TerrainKind.SolidRock));
        Assert.Throws<ArgumentException>(() => LegacyTargets(copy, new(0, 0)));
        Assert.Throws<ArgumentException>(() => BatchTargets(copy));
        Assert.Equal(LegacyTargets(state, new(0, 2)), BatchTargets(state));
    }

    [Fact]
    public void MalformedSummonStateRetainsWholeBoardFailureOutsideBlockedNeighborhood()
    {
        var state = World();
        state.Physical = new(new Board(10, 10, []), []);
        state.Types[0] = state.Types[0].WithLocal(new("summon", "Summon", AbilityTiming.Action,
            new AdjacentUnitSummonMechanic("small", Posture.Lying)));
        Add(state, "caller", "small", new(5, 5));
        state.Physical.Figures.Add(new("missing", new(9, 9)));
        for (var y = 4; y <= 6; y++)
        for (var x = 4; x <= 6; x++)
            if (x != 5 || y != 5) state.Physical.Board.Terrain.Add(new(new(x, y), TerrainKind.Tree));
        var ability = state.AbilitiesOf("caller").Single(a => a.Definition.Mechanic is AdjacentUnitSummonMechanic);
        // The old scan reaches free floor (0,0), then the dangling figure, before testing adjacency.
        Assert.Throws<InvalidOperationException>(() => Legacy(state, state.Types[0], new(0, 0)));
        Assert.Throws<InvalidOperationException>(() =>
            SummoningRules.Candidates(state, state.Units[0], ability).ToArray());
    }

    [Fact]
    public void MalformedReferencesPreserveShortCircuitAndExceptionBehavior()
    {
        var state = World();
        Add(state, "first", "small", new(1, 1));
        state.Physical.Figures.Add(new("missing", new(6, 6)));
        var lookup = new PlacementLookup(state);
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[0], new(-1, 0), lookup: lookup));
        Assert.False(SpatialRules.CanPlaceUnit(state, state.Types[0], new(1, 1), lookup: lookup));
        Assert.Throws<InvalidOperationException>(() => Legacy(state, state.Types[0], new(4, 4)));
        Assert.Throws<InvalidOperationException>(() => SpatialRules.CanPlaceUnit(state, state.Types[0], new(4, 4), lookup: lookup));
        // Duplicate figures retain Single's failure instead of merging separate bases.
        state.Physical.Figures[1] = new("first", new(2, 1));
        Assert.Throws<InvalidOperationException>(() =>
            EdgeReplacementRules.CanReplace(state, new(new(1, 1), new(2, 1), EdgeKind.Wall)));
        state.Units.Add(state.Units[0]);
        Assert.Throws<InvalidOperationException>(() => SpatialRules.CanPlaceUnit(state, state.Types[0], new(4, 4)));
    }
}
