using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PatrolDistanceEquivalenceTests
{
    private static IEnumerable<Cell> Cells(Board board) =>
        Enumerable.Range(0, board.Height).SelectMany(y => Enumerable.Range(0, board.Width).Select(x => new Cell(x, y)));

    private static GameState World(UnitType actor, int width = 6, int height = 4) => new()
    {
        Physical = new(new Board(width, height, []), [new("actor", new(0, 0))]),
        Types = [actor], Units = [actor.CreateUnit("actor", "blue")],
        Controllers = [new(new(actor.Id, "blue"), ControllerKind.Automated)],
        Objectives = [new("blue")], ResourceDefinitions = [.. ResourceTypes.All],
        WorldCardDefinitions = [.. WorldCardCatalog.All]
    };

    // Freeze the previous query's full forward flood and final minimum reduction.
    // Do not use either the optimized search or ApproachRules.Distances as oracle.
    private static int? FullFloodOracle(Board board, Cell from, ObjectiveArea area,
        MovementTraversal traversal, Footprint footprint)
    {
        if (!MovementRules.Inside(board, from)) throw new ArgumentException();
        var distances = new Dictionary<Cell, int> { [from] = 0 };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in MovementRules.Neighbors(current))
            {
                if (distances.ContainsKey(next) || !MovementRules.CanStep(board, footprint, current, next, traversal)) continue;
                distances[next] = distances[current] + 1;
                queue.Enqueue(next);
            }
        }
        return distances.Where(p => FootprintGeometry.OccupiedCells(footprint, p.Key)
                .Any(c => RouteRules.Contains(area, c)))
            .Select(p => (int?)p.Value).Min();
    }

    private static void CompareAllOriginsAndAreas(GameState state)
    {
        var before = JsonSerializer.Serialize(state);
        var board = state.Physical.Board;
        var type = state.Types.Single(t => t.Id == state.Units.Single(u => u.Id == "actor").TypeId);
        var origins = Cells(board).Where(c => SpatialRules.CanPlaceUnit(state, type, c, "actor")).ToArray();
        Assert.NotEmpty(origins);
        var areas = Cells(board).Select(c => new ObjectiveArea(c.X, c.Y, 1, 1))
            .Concat([new(1, 1, 3, 2), new(0, 0, board.Width, board.Height),
                new(-1, -1, 2, 2), new(board.Width - 1, 1, 3, 2),
                new(board.Width + 1, board.Height + 1, 2, 2), new(2, 2, 0, 0)]).ToArray();
        var queries = new GameplayQueries(state);
        foreach (var origin in origins)
        foreach (var doors in new[] { false, true })
        foreach (var area in areas)
        {
            var expected = FullFloodOracle(board, origin, area, MovementTraversal.For(state, "actor", doors), type.Footprint);
            Assert.Equal(expected, queries.RouteDistanceToArea("actor", origin, area, doors));
        }
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("phase")]
    [InlineData("amphibious")]
    [InlineData("asphalt")]
    [InlineData("category")]
    [InlineData("fog")]
    [InlineData("large")]
    [InlineData("large-phase")]
    [InlineData("large-amphibious")]
    [InlineData("large-fog")]
    [InlineData("lying-phase")]
    [InlineData("disconnected")]
    public void EveryLegalOriginAndAreaMatchesFrozenFullFlood(string variant)
    {
        var actor = new UnitType("patroller", 1, 1, 1, 0, 1);
        if (variant.Contains("phase")) actor = actor.WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        if (variant.Contains("amphibious")) actor = actor with { Amphibious = true };
        if (variant.StartsWith("large")) actor = actor with { Footprint = Footprint.TwoByTwo };
        if (variant == "asphalt") actor = actor with { TerrainRestrictedMovement = new(TerrainKind.Asphalt) };
        if (variant == "category") actor = actor with { TerrainRestriction = new([TerrainCategories.RacingGround]) };
        var state = World(actor);
        var board = state.Physical.Board;
        board.Terrain.AddRange([new(new(2, 0), TerrainKind.Water), new(new(2, 1), TerrainKind.Lava),
            new(new(4, 2), TerrainKind.SolidRock)]);
        board.Edges.AddRange([new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor),
            new(new(1, 2), new(2, 2), EdgeKind.Wall), new(new(3, 1), new(4, 1), EdgeKind.OpenDoor),
            new(new(3, 2), new(3, 3), EdgeKind.ClosedDoor)]);
        if (variant is "asphalt" or "category")
            board.Terrain.AddRange(Cells(board).Where(c => c.Y == 3 || c.X == 0).Select(c => new TerrainTile(c, TerrainKind.Asphalt)));
        if (variant.Contains("fog"))
        {
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = Cells(board).Where(c => c.X != 3 || c.Y == 0).ToHashSet();
        }
        if (variant == "lying-phase") state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        if (variant == "disconnected")
            board.Edges.AddRange(Enumerable.Range(0, board.Height).Select(y => new Edge(new(4, y), new(5, y), EdgeKind.Wall)));
        CompareAllOriginsAndAreas(state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialOverlapAndDepartureRetainDirectedOriginException(bool unknown)
    {
        var actor = new UnitType("patroller", 1, 1, 1, 0, 1)
            { TerrainRestrictedMovement = unknown ? null : new(TerrainKind.Asphalt) };
        var state = World(actor, 5, 1);
        state.Physical.Board.Terrain.AddRange(Enumerable.Range(1, 4).Select(x => new TerrainTile(new(x, 0), TerrainKind.Asphalt)));
        if (unknown)
        {
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = Enumerable.Range(1, 4).Select(x => new Cell(x, 0)).ToHashSet();
        }
        var queries = new GameplayQueries(state);
        Assert.Equal(0, queries.RouteDistanceToArea("actor", new(0, 0), new(0, 0, 1, 1)));
        Assert.Equal(4, queries.RouteDistanceToArea("actor", new(0, 0), new(4, 0, 1, 1)));
        Assert.Null(queries.RouteDistanceToArea("actor", new(4, 0), new(0, 0, 1, 1)));
        CompareAllOriginsAndAreas(state);
    }

    [Fact]
    public void PartialLargeFootprintOverlapUsesClosestOccupiedCell()
    {
        var state = World(new("patroller", 0, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo });
        var queries = new GameplayQueries(state);
        Assert.Equal(0, queries.RouteDistanceToArea("actor", new(0, 0), new(1, 1, 1, 1)));
        Assert.Equal(3, queries.RouteDistanceToArea("actor", new(0, 0), new(4, 1, 1, 1)));
        Assert.Equal(6, queries.RouteDistanceToArea("actor", new(0, 0), new(5, 3, 1, 1)));
    }

    [Fact]
    public void PlanningIgnoresCongestionMarkersMovementPatternAndAllowance()
    {
        var actor = new UnitType("patroller", 0, 0, 0, 0, 1) { MovementPattern = MovementPattern.Adjacent };
        var state = World(actor, 7, 1);
        state.Units.AddRange([actor.CreateUnit("friend", "blue"), actor.CreateUnit("hostile", "red")]);
        state.Physical.Figures.AddRange([new("friend", new(1, 0)), new("hostile", new(2, 0))]);
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(3, 0)));
        Assert.Equal(6, new GameplayQueries(state).RouteDistanceToArea("actor", new(0, 0), new(6, 0, 1, 1)));
        Assert.Single(MovementRules.FindPaths(state, "actor", new(0, 0), 0));
        CompareAllOriginsAndAreas(state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImpassableAreaIsNotExceptionalGoalAndClosedDoorsAreExplicit(bool phase)
    {
        var actor = new UnitType("patroller", 1, 0, 0, 0, 1);
        if (phase) actor = actor.WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        var state = World(actor, 5, 1);
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor));
        state.Physical.Board.Terrain.Add(new(new(4, 0), TerrainKind.Lava));
        var queries = new GameplayQueries(state);
        Assert.Equal(phase ? 2 : (int?)null, queries.RouteDistanceToArea("actor", new(0, 0), new(2, 0, 1, 1)));
        Assert.Equal(2, queries.RouteDistanceToArea("actor", new(0, 0), new(2, 0, 1, 1), true));
        Assert.Equal(phase ? 4 : (int?)null, queries.RouteDistanceToArea("actor", new(0, 0), new(4, 0, 1, 1), true));
    }

    [Fact]
    public void LargeFootprintInternalEdgesAndWholeKnownFootprintAreRequired()
    {
        var state = World(new("patroller", 1, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo }, 5, 2);
        state.Physical.Board.Edges.Add(new(new(2, 0), new(2, 1), EdgeKind.ClosedDoor));
        var area = new ObjectiveArea(4, 0, 1, 1);
        Assert.Null(new GameplayQueries(state).RouteDistanceToArea("actor", new(0, 0), area));
        Assert.Equal(3, new GameplayQueries(state).RouteDistanceToArea("actor", new(0, 0), area, true));
        state.FogOfWarSides = ["blue"];
        state.ExploredCells["blue"] = Cells(state.Physical.Board).Where(c => c != new Cell(2, 1)).ToHashSet();
        Assert.Null(new GameplayQueries(state).RouteDistanceToArea("actor", new(0, 0), area, true));
    }

    [Theory]
    [InlineData(Footprint.OneByOne)]
    [InlineData(Footprint.TwoByTwo)]
    public void SeededDirectedGraphsMatchFullFloodIncludingInitiallyRestrictedAnchors(Footprint footprint)
    {
        var random = new Random(821903);
        for (var sample = 0; sample < 12; sample++)
        {
            var board = new Board(5, 4, []);
            foreach (var cell in Cells(board))
            {
                var terrain = random.Next(6);
                if (terrain < 4) board.Terrain.Add(new(cell, new[]
                    { TerrainKind.Asphalt, TerrainKind.Water, TerrainKind.Lava, TerrainKind.SolidRock }[terrain]));
                foreach (var neighbor in MovementRules.Neighbors(cell).Where(c => MovementRules.Inside(board, c) &&
                             (c.X > cell.X || c.Y > cell.Y)))
                    if (random.Next(4) == 0) board.Edges.Add(new(cell, neighbor,
                        random.Next(2) == 0 ? EdgeKind.Wall : EdgeKind.ClosedDoor));
            }
            var traversal = new MovementTraversal(IgnoreTerrainAndEdges: sample % 4 == 0,
                ClosedDoorsTraversable: sample % 2 == 0,
                RequiredTerrain: sample % 5 == 0 ? TerrainKind.Asphalt : null,
                KnownCells: sample % 3 == 0 ? Cells(board).Where(_ => random.Next(3) != 0).ToHashSet() : null,
                Capabilities: sample % 2 != 0 ? [MovementKinds.Land, MovementKinds.Water] : [MovementKinds.Land]);
            var areas = Cells(board).Select(c => new ObjectiveArea(c.X, c.Y, 1, 1))
                .Concat([new(1, 1, 3, 2), new(-2, -2, 1, 1)]);
            foreach (var origin in Cells(board))
            foreach (var area in areas)
                Assert.Equal(FullFloodOracle(board, origin, area, traversal, footprint),
                    ApproachRules.DistanceToArea(board, origin, area, traversal, footprint));
        }
    }

    [Fact]
    public void OutOfBoardOriginsKeepTheExistingEndpointFailure()
    {
        var board = new Board(3, 2, []);
        Assert.Throws<ArgumentException>(() => ApproachRules.DistanceToArea(board, new(-1, 0), new(-1, 0, 1, 1)));
        Assert.Throws<ArgumentException>(() => ApproachRules.DistanceToArea(board, new(3, 0), new(0, 0, 1, 1)));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void ExactFallbackAndWarmedFieldsKeepOriginExceptionsAndPartialOverlap(bool large, bool unknown, bool restricted)
    {
        var actor = new UnitType("patroller", 1, 0, 0, 0, 1)
        {
            Footprint = large ? Footprint.TwoByTwo : Footprint.OneByOne,
            TerrainRestrictedMovement = restricted ? new(TerrainKind.StoneFloor) : null
        };
        var state = World(actor, 40, 16);
        var board = state.Physical.Board;
        var area = new ObjectiveArea(39, 0, 1, 1);
        if (restricted) board.Terrain.Add(new(new(39, 0), TerrainKind.Grass));
        if (unknown)
        {
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = Cells(board).Where(c => c != new Cell(39, 0)).ToHashSet();
        }
        var traversal = MovementTraversal.For(state, "actor");
        Assert.False(ApproachRules.TryDistanceToArea(board, new(0, 0), area, out _, traversal,
            actor.Footprint, expansionBudget: 256));
        var before = JsonSerializer.Serialize(state);
        var queries = new GameplayQueries(state);
        Cell[] origins = [new(0, 0), new(1, 1), new(20, 10), new(37, 0), new(38, 0), new(0, 0)];
        // The first query forces fallback; subsequent calls exercise the shared
        // reverse field, including an unknown/restricted origin overlapping area.
        foreach (var origin in origins)
            Assert.Equal(FullFloodOracle(board, origin, area, traversal, actor.Footprint),
                queries.RouteDistanceToArea("actor", origin, area));
        Assert.Equal(large ? 0 : unknown || restricted ? (int?)null : 1,
            queries.RouteDistanceToArea("actor", new(38, 0), area));
        if (!large) Assert.Equal(0, queries.RouteDistanceToArea("actor", new(39, 0), area));
        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.Throws<ArgumentException>(() => queries.RouteDistanceToArea("actor", new(40, 0), area));
    }

    [Fact]
    public void FallbackFieldsSeparateDoorPolicyAndAreaAndRemainDetached()
    {
        var state = World(new("patroller", 1, 0, 0, 0, 1), 40, 16);
        var board = state.Physical.Board;
        board.Edges.AddRange(Enumerable.Range(0, board.Height).Select(y => new Edge(new(19, y), new(20, y), EdgeKind.ClosedDoor)));
        var area = new ObjectiveArea(39, 0, 1, 1);
        var queries = new GameplayQueries(state);
        Assert.Null(queries.RouteDistanceToArea("actor", new(0, 0), area));
        Assert.Equal(39, queries.RouteDistanceToArea("actor", new(0, 0), area, true));
        Assert.Equal(29, queries.RouteDistanceToArea("actor", new(10, 0), area, true));
        Assert.Null(queries.RouteDistanceToArea("actor", new(10, 0), area));
        Assert.Equal(9, queries.RouteDistanceToArea("actor", new(10, 0), new(1, 0, 1, 1)));
        board.Edges.Clear();
        Assert.Null(queries.RouteDistanceToArea("actor", new(0, 0), area));
        Assert.Equal(39, new GameplayQueries(state).RouteDistanceToArea("actor", new(0, 0), area));
    }

    [Fact]
    public void SearchBudgetExhaustionNeverMeansUnreachableOrCapsDistance()
    {
        var board = new Board(600, 1, []);
        var area = new ObjectiveArea(599, 0, 1, 1);
        Assert.False(ApproachRules.TryDistanceToArea(board, new(0, 0), area, out var unfinished,
            expansionBudget: 256));
        Assert.Null(unfinished);
        Assert.Equal(599, ApproachRules.DistanceToArea(board, new(0, 0), area));
        var state = World(new("patroller", 1, 0, 0, 0, 1), 600, 1);
        Assert.Equal(599, new GameplayQueries(state).RouteDistanceToArea("actor", new(0, 0), area));
    }
}
