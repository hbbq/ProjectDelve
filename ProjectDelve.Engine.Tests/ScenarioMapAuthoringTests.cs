using ProjectDelve.Engine;
using Xunit;
using static ProjectDelve.Engine.Scenario;
using static ProjectDelve.Engine.EdgeDirection;

namespace ProjectDelve.Engine.Tests;

public sealed class ScenarioMapAuthoringTests
{
    public static TheoryData<EdgeKind> EdgeKinds => new(Enum.GetValues<EdgeKind>());

    [Theory, MemberData(nameof(EdgeKinds))]
    public void VerticalUsesSuppliedKindForEveryInclusiveSegment(EdgeKind kind)
    {
        Assert.Equal(new[] {
            new EdgeDefinition(new(2, 1), Right, kind),
            new EdgeDefinition(new(2, 2), Right, kind),
            new EdgeDefinition(new(2, 3), Right, kind)
        }, Vertical(2, 1, 3, kind));
    }

    [Theory, MemberData(nameof(EdgeKinds))]
    public void HorizontalUsesSuppliedKindForEveryInclusiveSegment(EdgeKind kind)
    {
        Assert.Equal(new[] {
            new EdgeDefinition(new(1, 2), Down, kind),
            new EdgeDefinition(new(2, 2), Down, kind),
            new EdgeDefinition(new(3, 2), Down, kind)
        }, Horizontal(2, 1, 3, kind));
    }

    [Theory, MemberData(nameof(EdgeKinds))]
    public void RoomBuildsFourCompleteSidesWithSuppliedKind(EdgeKind kind)
    {
        var expected = new[] {
            new EdgeDefinition(new(1, 3), Right, kind),
            new EdgeDefinition(new(1, 4), Right, kind),
            new EdgeDefinition(new(3, 3), Right, kind),
            new EdgeDefinition(new(3, 4), Right, kind),
            new EdgeDefinition(new(2, 2), Down, kind),
            new EdgeDefinition(new(3, 2), Down, kind),
            new EdgeDefinition(new(2, 4), Down, kind),
            new EdgeDefinition(new(3, 4), Down, kind)
        };
        Assert.Equal(expected, Room(2, 3, 3, 4, kind));
    }

    [Fact]
    public void RoomDefaultsToWall() => Assert.Equal(Room(2, 3, 3, 4, EdgeKind.Wall), Room(2, 3, 3, 4));

    [Fact]
    public void ExplicitRoomOverridesStayInFirstOccurrenceOrder()
    {
        var sides = Room(2, 2, 4, 4);
        var definition = Define(Map(8, 8, edges: [
            .. sides,
            Edge(1, 3, Right, EdgeKind.ClosedDoor),
            Edge(3, 4, Down, EdgeKind.OpenDoor)
        ]), []);
        Assert.Equal(sides.Length, definition.Board.Edges.Count);
        Assert.Equal(sides.Select(e => (e.Position, e.Direction)), definition.Board.Edges.Select(e => (e.Position, e.Direction)));
        Assert.Equal(EdgeKind.ClosedDoor, definition.Board.Edges.Single(e => e.Position == new Cell(1, 3) && e.Direction == Right).Kind);
        Assert.Equal(EdgeKind.OpenDoor, definition.Board.Edges.Single(e => e.Position == new Cell(3, 4) && e.Direction == Down).Kind);
        Assert.All(sides, e => Assert.Equal(EdgeKind.Wall, e.Kind)); // Inputs are not mutated.
        Assert.Equal(definition.Board.Edges.Count, definition.Board.Edges.Select(e => (e.Position, e.Direction)).Distinct().Count());
        var runtime = GameEngine.CreateGame(definition).Physical.Board;
        Assert.Equal(EdgeKind.ClosedDoor, runtime.EdgeBetween(new(1, 3), new(2, 3)));
        Assert.Equal(EdgeKind.OpenDoor, runtime.EdgeBetween(new(3, 4), new(3, 5)));
    }

    [Fact]
    public void VerticalAllowsMultipleCanonicalOverridesAndLatestEntryWins()
    {
        var board = Map(8, 8, edges: [
            .. Vertical(2, 1, 4, EdgeKind.Wall),
            // Concrete entries and helper entries share the same canonical identity.
            new EdgeDefinition(new(2, 2), Right, EdgeKind.ClosedDoor),
            Edge(2, 4, Right, EdgeKind.WallWithWindow),
            Edge(2, 2, Right, EdgeKind.OpenDoor),
            Edge(5, 5, Right, EdgeKind.None)
        ]);
        Assert.Equal(new[] { EdgeKind.Wall, EdgeKind.OpenDoor, EdgeKind.Wall, EdgeKind.WallWithWindow, EdgeKind.None }, board.Edges.Select(e => e.Kind));
        Assert.Equal(new Cell(2, 2), board.Edges[1].Position);
        Assert.Equal(Right, board.Edges[1].Direction);
        Assert.Equal(5, board.Edges.Select(e => (e.Position, e.Direction)).Distinct().Count());
    }

    [Fact]
    public void HorizontalAllowsMultipleDistinctOverrides()
    {
        var definition = Define(Map(8, 8, edges: [
            .. Horizontal(2, 1, 4, EdgeKind.OpenDoor),
            Edge(1, 2, Down, EdgeKind.Wall),
            Edge(3, 2, Down, EdgeKind.ClosedDoor)
        ]), []);
        Assert.Equal(new[] { EdgeKind.Wall, EdgeKind.OpenDoor, EdgeKind.ClosedDoor, EdgeKind.OpenDoor }, definition.Board.Edges.Select(e => e.Kind));
        Assert.Equal(4, GameEngine.CreateGame(definition).Physical.Board.Edges.Count);
    }

    [Fact]
    public void DirectDefinitionDuplicatesAreStillRejected()
    {
        var definition = Define(Map(8, 8), []) with { Board = new BoardDefinition {
            Width = 8, Height = 8, Edges = [
                new(new(1, 1), Right, EdgeKind.Wall),
                new(new(1, 1), Right, EdgeKind.OpenDoor)
            ]
        } };
        var error = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        Assert.Contains("Duplicate Edge Position + Direction", error.Message);
    }

    [Fact]
    public void OnlyRightAndDownAreRepresentableDirections() =>
        Assert.Equal(new[] { "Right", "Down" }, Enum.GetNames<EdgeDirection>());

    [Fact]
    public void OverridesDistinguishDirectionsAtTheSamePosition()
    {
        var definition = Define(Map(3, 3, edges: [
            Edge(1, 1, Right, EdgeKind.Wall),
            Edge(1, 1, Down, EdgeKind.WallWithWindow),
            Edge(1, 1, Right, EdgeKind.ClosedDoor)
        ]), []);
        Assert.Equal(new[] {
            new EdgeDefinition(new(1, 1), Right, EdgeKind.ClosedDoor),
            new EdgeDefinition(new(1, 1), Down, EdgeKind.WallWithWindow)
        }, definition.Board.Edges);
        var board = GameEngine.CreateGame(definition).Physical.Board;
        Assert.Equal(EdgeKind.ClosedDoor, board.EdgeBetween(new(1, 1), new(2, 1)));
        Assert.Equal(EdgeKind.WallWithWindow, board.EdgeBetween(new(1, 1), new(1, 2)));
    }

    [Theory]
    [InlineData(1, 3, 0, 0, EdgeDirection.Right)]
    [InlineData(3, 1, 0, 0, EdgeDirection.Down)]
    [InlineData(3, 3, 2, 1, EdgeDirection.Right)]
    [InlineData(3, 3, 1, 2, EdgeDirection.Down)]
    [InlineData(3, 3, -1, 0, EdgeDirection.Right)]
    [InlineData(3, 3, 0, -1, EdgeDirection.Down)]
    [InlineData(3, 3, 3, 0, EdgeDirection.Down)]
    [InlineData(3, 3, 0, 3, EdgeDirection.Right)]
    public void ExteriorOrOutOfBoundsEdgesAreRejected(int width, int height, int x, int y, EdgeDirection direction)
    {
        var definition = Define(Map(width, height, edges: [Edge(x, y, direction, EdgeKind.Wall)]), []);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
    }

    [Fact]
    public void InternalEdgesBesideBoundariesAreValid()
    {
        var definition = Define(Map(3, 3, edges: [
            Edge(2, 1, Down, EdgeKind.OpenDoor),
            Edge(1, 2, Right, EdgeKind.ClosedDoor)
        ]), []);
        Assert.Equal(new[] {
            new ProjectDelve.Engine.Edge(new(2, 1), new(2, 2), EdgeKind.OpenDoor),
            new ProjectDelve.Engine.Edge(new(1, 2), new(2, 2), EdgeKind.ClosedDoor)
        }, GameEngine.CreateGame(definition).Physical.Board.Edges);
    }

    [Fact]
    public void TilesExpandsToConcreteCellsInAuthoredOrder()
    {
        var cells = Tiles(TerrainKind.Tree, At(5, 4), At(3, 12));
        Assert.Equal(new[] {
            new CellDefinition(new(5, 4), TerrainKind.Tree),
            new CellDefinition(new(3, 12), TerrainKind.Tree)
        }, cells);
        var definition = Define(Map(15, 15, cells: [.. cells, Tile(6, 10, TerrainKind.StoneFloorWithTable)]), []);
        var board = GameEngine.CreateGame(definition).Physical.Board;
        Assert.Equal(TerrainKind.Tree, board.TerrainAt(new(5, 4)));
        Assert.Equal(TerrainKind.Tree, board.TerrainAt(new(3, 12)));
        Assert.Equal(TerrainKind.StoneFloorWithTable, board.TerrainAt(new(6, 10)));
    }

    [Theory]
    [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.Water)]
    public void TilesDoesNotResolveDuplicateOrConflictingCellOverrides(TerrainKind laterTerrain)
    {
        var definition = Define(Map(3, 3, cells: [
            .. Tiles(TerrainKind.Tree, At(1, 1)),
            .. Tiles(laterTerrain, At(1, 1))
        ]), []);
        Assert.Equal(2, definition.Board.Cells.Count);
        var error = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        Assert.Contains("Duplicate Cell override", error.Message);
    }
}
