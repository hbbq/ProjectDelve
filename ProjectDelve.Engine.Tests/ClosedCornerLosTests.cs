using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ClosedCornerLosTests
{
    private static readonly Cell Source = new(3, 3), Target = new(4, 4), NorthEast = new(4, 3), SouthWest = new(3, 4);

    private static Board Corner(string geometry)
    {
        var board = new Board(9, 9, []);
        void Terrain(Cell cell, TerrainKind kind = TerrainKind.SolidRock) => board.Terrain.Add(new(cell, kind));
        void Machine(Cell cell, string id) => board.Machines.Add(new(id, cell, MachineTypes.DiamondPress));
        void North(EdgeKind kind = EdgeKind.Wall) => board.Edges.Add(new(Source, NorthEast, kind));
        void West(EdgeKind kind = EdgeKind.Wall) => board.Edges.Add(new(Source, SouthWest, kind));
        switch (geometry)
        {
            case "open": break;
            case "tree": Terrain(NorthEast, TerrainKind.Tree); break;
            case "rock": Terrain(NorthEast); break;
            case "wall-end": North(); break;
            case "two-rocks": Terrain(NorthEast); Terrain(SouthWest); break;
            case "two-trees": Terrain(NorthEast, TerrainKind.Tree); Terrain(SouthWest, TerrainKind.Tree); break;
            case "rock-opposite-wall": Terrain(NorthEast); West(); break;
            case "rock-same-wall": Terrain(NorthEast); North(); break;
            case "two-opposite-walls": North(); West(); break;
            case "two-same-walls": North(); board.Edges.Add(new(NorthEast, Target, EdgeKind.Wall)); break;
            case "open-door": Terrain(NorthEast); West(EdgeKind.OpenDoor); break;
            case "closed-door": Terrain(NorthEast); West(EdgeKind.ClosedDoor); break;
            case "window": Terrain(NorthEast); West(EdgeKind.WallWithWindow); break;
            case "machine": Machine(NorthEast, "a"); break;
            case "two-machines": Machine(NorthEast, "a"); Machine(SouthWest, "b"); break;
            case "machine-tree": Machine(NorthEast, "a"); Terrain(SouthWest, TerrainKind.FruitTree); break;
            case "machine-wall": Machine(NorthEast, "a"); West(); break;
            case "machine-same-wall": Machine(NorthEast, "a"); North(); break;
            case "water": Terrain(NorthEast, TerrainKind.Water); Terrain(SouthWest, TerrainKind.Water); break;
            case "tables": Terrain(NorthEast, TerrainKind.StoneFloorWithTable); Terrain(SouthWest, TerrainKind.StoneFloorWithTable); break;
            default: throw new ArgumentException(geometry);
        }
        return board;
    }

    [Theory]
    [InlineData("open", true, true)]
    [InlineData("tree", true, true)]
    [InlineData("rock", true, true)]
    [InlineData("wall-end", true, true)]
    [InlineData("two-rocks", false, false)]
    [InlineData("two-trees", false, false)]
    [InlineData("rock-opposite-wall", false, true)]
    [InlineData("rock-same-wall", true, true)]
    [InlineData("two-opposite-walls", false, true)]
    [InlineData("two-same-walls", true, true)]
    [InlineData("open-door", true, true)]
    [InlineData("closed-door", false, true)]
    [InlineData("window", true, true)]
    [InlineData("machine", true, true)]
    [InlineData("two-machines", false, false)]
    [InlineData("machine-tree", false, false)]
    [InlineData("machine-wall", false, true)]
    [InlineData("machine-same-wall", true, true)]
    [InlineData("water", true, true)]
    [InlineData("tables", true, true)]
    public void PassageClosureUsesLosPropertiesWithRotationsReflectionsReversalAndSkewRays(string geometry, bool normal, bool phase)
    {
        var original = Corner(geometry);
        for (var rotation = 0; rotation < 4; rotation++)
        foreach (var mirror in new[] { false, true })
        {
            Cell Transform(Cell c)
            {
                var x = c.X - 4; var y = c.Y - 4;
                if (mirror) x = -x;
                for (var r = 0; r < rotation; r++) (x, y) = (-y, x);
                return new(x + 4, y + 4);
            }
            var board = original with {
                Terrain = original.Terrain.Select(t => t with { Position = Transform(t.Position) }).ToList(),
                Machines = original.Machines.Select(m => m with { Position = Transform(m.Position) }).ToList(),
                Edges = original.Edges.Select(e => e with { A = Transform(e.A), B = Transform(e.B) }).ToList() };
            foreach (var (start, end) in new (Cell, Cell)[] { (Source, Target), (new(2, 3), new(5, 4)), (new(1, 1), new(6, 6)) })
            foreach (var reverse in new[] { false, true })
            {
                var from = Transform(reverse ? end : start); var to = Transform(reverse ? start : end);
                Assert.Equal(normal, AttackRules.HasGeometricLineOfSight(board, from, to));
                Assert.Equal(phase, AttackRules.HasGeometricLineOfSight(board, from, to, phaseVision: true));
            }
        }
    }

    [Fact]
    public void EveryCrossedCornerIsCheckedAndOpenPassagesCanAlternateSides()
    {
        var board = new Board(8, 8, []) { Terrain = [new(new(2, 1), TerrainKind.Tree), new(new(2, 3), TerrainKind.SolidRock)] };
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(5, 5)));
        board.Terrain.Add(new(new(3, 2), TerrainKind.SolidRock));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(5, 5)));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(5, 5), new(1, 1)));
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(2, 2)));
    }

    [Fact]
    public void ExplicitTerrainAndMachineExemptionsAlsoApplyToFlankClosure()
    {
        var board = Corner("two-trees");
        Assert.True(AttackRules.HasGeometricLineOfSight(board, Source, Target, ignoredTerrainCell: NorthEast));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, Source, Target, ignoredTerrainCell: Source));
        board = Corner("two-machines");
        Assert.True(AttackRules.HasGeometricLineOfSight(board, Source, Target, interactionMachineId: "a"));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, Source, Target, interactionMachineId: "unrelated"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DiscoveringBlockingDestinationDoesNotExemptInterveningCornersOrSeeBeyondIt(bool machine)
    {
        var board = Corner("rock");
        if (machine) board.Machines.Add(new("destination", Target, MachineTypes.DiamondPress));
        else board.Terrain.Add(new(Target, TerrainKind.Tree));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, Source, Target));
        Assert.True(AttackRules.HasGeometricLineOfSight(board, Source, Target, ignoredTerrainCell: Target, discoverDestination: true));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, Source, new(5, 5), ignoredTerrainCell: new(5, 5), discoverDestination: true));
        board.Edges.Add(new(Source, SouthWest, EdgeKind.Wall));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, Source, Target, ignoredTerrainCell: Target, discoverDestination: true));
        Assert.True(AttackRules.HasGeometricLineOfSight(board, Source, Target, ignoredTerrainCell: Target, discoverDestination: true, phaseVision: true));
    }

    [Fact]
    public void FiguresDoNotCloseFlankPassagesButHostileInteriorsStillBlock()
    {
        var type = new UnitType("unit", 3, 8, 2, 1, 5);
        var state = new GameState { Types = [type],
            Units = [type.CreateUnit("source", "blue"), type.CreateUnit("target", "red"), type.CreateUnit("ne", "red"), type.CreateUnit("sw", "red")],
            Physical = new(new Board(8, 8, []), [new("source", Source), new("target", Target), new("ne", NorthEast), new("sw", SouthWest)]) };
        Assert.True(AttackRules.HasUnitLineOfSight(state, "source", "target"));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "source", Source, new(5, 3)));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "source", "ne"));
    }
}
