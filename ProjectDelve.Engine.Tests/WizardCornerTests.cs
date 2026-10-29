using ProjectDelve.Engine;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class WizardCornerTests(ITestOutputHelper output)
{
    [Fact]
    public void SuppliedDungeonClosedCornerConcealsBothCellsWithFreshExplorationAndRetainsLegitimateEarlierDiscovery()
    {
        var path = Path.Combine(FindRoot(), "ProjectDelve.Engine.Tests", "Fixtures", "wizard-corner.delve");
        var definition = ScenarioDefinitionTransport.Decode(File.ReadAllText(path).Trim());
        var state = GameEngine.CreateGame(definition);
        var targets = new Cell[] { new(11, 5), new(10, 4) };
        foreach (var target in targets)
        {
            output.WriteLine($"Initial {target}: {FogOfWar.IsExplored(state, "side-1", target)}");
            Assert.False(FogOfWar.IsExplored(state, "side-1", target));
        }
        var index = state.Physical.Figures.FindIndex(f => f.Id == "hero-3");
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = new(13, 7) };
        output.WriteLine($"Wizard RNG {state.EffectiveRngOf("hero-3")}");
        Assert.Equal(4, state.EffectiveRngOf("hero-3"));
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        foreach (var hero in state.Units.Where(u => u.SideId == "side-1"))
        foreach (var target in targets)
        {
            var from = state.Physical.Figures.Single(f => f.Id == hero.Id).Position;
            output.WriteLine($"{hero.Id} {from} -> {target}: LOS {AttackRules.HasGeometricLineOfSight(state.Physical.Board, from, target)}; distance {Math.Abs(from.X-target.X)+Math.Abs(from.Y-target.Y)}");
            Assert.False(AttackRules.HasGeometricLineOfSight(state.Physical.Board, from, target));
        }
        Assert.All(targets, t => Assert.False(FogOfWar.IsExplored(state, "side-1", t)));
        Assert.False(FogOfWar.IsUnitObservable(state, "side-1", "monster-2"));
        // A legal earlier position inside the room genuinely reveals both Cells.
        Relocate(state, "hero-3", new(11, 5)); FogOfWar.Refresh(state);
        Assert.All(targets, t => Assert.True(FogOfWar.IsExplored(state, "side-1", t)));
        Relocate(state, "hero-3", new(13, 7)); FogOfWar.Refresh(state);
        Assert.All(targets, t => Assert.True(FogOfWar.IsExplored(state, "side-1", t)));
    }

    private static ScenarioDefinition Corner(TerrainKind blocking = TerrainKind.SolidRock)
    {
        var floor = (from y in Enumerable.Range(3, 4) from x in Enumerable.Range(9, 3) select new Cell(x, y))
            .Concat([new(12, 6), new(13, 6), new(13, 7), new(13, 8), new(13, 9)]);
        return new ScenarioDefinition {
            Board = new() { Width = 16, Height = 11, DefaultTerrain = blocking,
                Cells = floor.Select(c => new CellDefinition(c, TerrainKind.StoneFloor)).ToList(),
                Edges = [new(new(11, 5), EdgeDirection.Down, EdgeKind.Wall)] },
            UnitTypeIds = [UnitTypeIds.Wizard, UnitTypeIds.Shaman, UnitTypeIds.Cleric, UnitTypeIds.Goblin],
            Units = [new(UnitTypeIds.Wizard, "heroes", new(13, 9), UnitId: "wizard"), new(UnitTypeIds.Shaman, "monsters", new(10, 4), UnitId: "shaman")],
            Agency = [new(UnitTypeIds.Wizard, "heroes", ControllerKind.Human), new(UnitTypeIds.Shaman, "monsters", ControllerKind.Automated),
                new(UnitTypeIds.Cleric, "heroes", ControllerKind.Human), new(UnitTypeIds.Goblin, "monsters", ControllerKind.Automated)], FogOfWarSides = ["heroes"] };
    }
    private static void Relocate(GameState state, string id, Cell cell)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = cell };
    }

    [Theory]
    [InlineData(TerrainKind.SolidRock, false)] [InlineData(TerrainKind.Tree, false)]
    [InlineData(TerrainKind.SolidRock, true)] [InlineData(TerrainKind.Tree, true)]
    public void MixedCornerClosureBlocksNormallyWhilePhaseIgnoresOnlyTheWall(TerrainKind blocking, bool phase)
    {
        var state = GameEngine.CreateGame(Corner(blocking));
        var index = state.Types.FindIndex(t => t.Id == UnitTypeIds.Wizard);
        if (phase) state.Types[index] = state.Types[index] with { Phase = new() };
        Relocate(state, "wizard", new(13, 7)); state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.Equal(phase, FogOfWar.IsExplored(state, "heroes", new(11, 5)));
        // Six Cells away: outside vision even with Phase and clear LOS.
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(10, 4)));
        foreach (var cell in new Cell[] { new(12, 6), new(12, 5), new(13, 6) })
            Assert.True(FogOfWar.IsExplored(state, "heroes", cell));
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(11, 4)));
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(11, 3)));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "wizard", "shaman"));
        Assert.False(AttackRules.HasGeometricLineOfSight(state.Physical.Board, new(13, 7), new(11, 3)));
    }

    [Theory]
    [InlineData(TerrainKind.SolidRock)] [InlineData(TerrainKind.Tree)]
    public void TwoDiagonalBlockingCellsCloseSightIncludingPhaseVision(TerrainKind blocking)
    {
        var state = GameEngine.CreateGame(Corner() with {
            Board = new() { Width = 6, Height = 6, Cells = [new(new(2, 1), blocking), new(new(1, 2), blocking)] },
            Units = [new(UnitTypeIds.Wizard, "heroes", new(1, 1), UnitId: "wizard"), new(UnitTypeIds.Shaman, "monsters", new(3, 3), UnitId: "shaman")] });
        var board = state.Physical.Board;
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(2, 2)));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(3, 3)));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(0, 1), new(3, 1)));
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(2, 2)));
        Assert.False(FogOfWar.IsUnitObservable(state, "heroes", "shaman"));
        board.Edges.AddRange([new(new(1, 1), new(2, 1), EdgeKind.Wall), new(new(1, 1), new(1, 2), EdgeKind.Wall)]);
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(2, 2)));
        Assert.False(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(2, 2), phaseVision: true));
        board.Terrain.Clear();
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(1, 1), new(2, 2), phaseVision: true));
    }

    [Fact]
    public void InitialSightSweepPermanentMemoryAndIndependentHeroSourcesRemainDistinct()
    {
        var state = GameEngine.CreateGame(Corner());
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(11, 5)));
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(10, 4)));
        // Marking a Cell explored must never turn it into another source of vision.
        state.ExploredCells["heroes"].Add(new(11, 5)); FogOfWar.Refresh(state);
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(10, 4)));
        FogOfWar.Sweep(state, "wizard", [new(13, 9), new(13, 8), new(13, 7)]);
        Assert.False(FogOfWar.IsExplored(state, "heroes", new(10, 4)));
        Assert.True(FogOfWar.IsExplored(state, "heroes", new(11, 5)));
        Assert.Equal(new(13, 9), state.Physical.Figures.Single(f => f.Id == "wizard").Position);
        FogOfWar.Refresh(state); Assert.False(FogOfWar.IsExplored(state, "heroes", new(10, 4)));
        state = GameEngine.CreateGame(Corner());
        state.PlaceUnit(UnitTypeIds.Cleric, "other-hero", "heroes", new(10, 6));
        Assert.True(FogOfWar.IsExplored(state, "heroes", new(10, 4)));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectDelve.sln"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
