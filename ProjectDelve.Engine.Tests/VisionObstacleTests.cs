using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class VisionObstacleTests
{
    private static GameState Corridor(params TerrainTile[] terrain)
    {
        var type = new UnitType("test-observer", 7, 7, 1, 0, 1);
        var state = new GameState
        {
            Physical = new(new Board(9, 1, []) { Terrain = [.. terrain] },
                [new("observer", new(0, 0)), new("target", new(5, 0))]),
            Types = [type], Units = [type.CreateUnit("observer", "blue"), type.CreateUnit("target", "red")],
            FogOfWarSides = ["blue"]
        };
        FogOfWar.Refresh(state);
        return state;
    }

    [Theory]
    [InlineData(TerrainKind.Tree, 1)]
    [InlineData(TerrainKind.Tree, 3)]
    [InlineData(TerrainKind.FruitTree, 1)]
    [InlineData(TerrainKind.FruitTree, 3)]
    public void TreesRevealUnitsAndChoicesRemainBoundByAttackAndMovementRules(TerrainKind terrain, int count)
    {
        var state = Corridor([.. Enumerable.Range(1, count).Select(x => new TerrainTile(new(x, 0), terrain))]);
        Assert.True(FogOfWar.IsUnitObservable(state, "blue", "target"));
        Assert.True(FogOfWar.IsExplored(state, "blue", new(5, 0)));
        Assert.False(FogOfWar.IsExplored(state, "blue", new(6, 0)));
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateFrom(state, "observer", new(0, 0), "target"));
        Assert.DoesNotContain(new Cell(4, 0), MovementRules.FindPaths(state, "observer", new(0, 0)).Keys);
        Assert.DoesNotContain(new Cell(1, 0), MovementRules.FindPaths(state, "observer", new(0, 0)).Keys);
        Assert.Contains(FogOfWar.Observe(state, "blue").Units, u => u.Id == "target");
        Assert.NotNull(FogOfWar.ObserveEvent(state, "blue", new("PostureChanged", UnitId: "target")));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.WallWithWindow, true)]
    public void EdgesRetainTheirVisionAndAttackProperties(EdgeKind kind, bool clear)
    {
        var state = Corridor();
        state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), kind));
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.Equal(clear, FogOfWar.IsUnitObservable(state, "blue", "target"));
        Assert.Equal(clear, AttackRules.HasUnitLineOfSight(state, "observer", "target"));
    }

    [Fact]
    public void PhaseSeesThroughSolidTerrainFixedComponentsMachinesAndEdgesButCannotAttackOrStopInsideThem()
    {
        var component = new FixedComponentDefinition("test-opaque-object", "Opaque Object", true, true);
        var assembly = new TerrainAssemblyDefinition("test-obstacle", "Test Obstacle", "grass")
            { Components = [new("obstacle", component.Id)] };
        var state = Corridor(new TerrainTile(new(1, 0), TerrainKind.SolidRock));
        state.Physical = state.Physical with { Board = state.Physical.Board with
            { TerrainTypes = TerrainCatalog.Resolve(null, [assembly], [component]) } };
        state.Physical.Board.Terrain.Add(new(new(2, 0)) { TypeId = assembly.Id });
        state.Physical.Board.Machines.Add(new("machine", new(3, 0), MachineTypes.DiamondPress));
        state.Physical.Board.Edges.AddRange([new(new(0, 0), new(1, 0), EdgeKind.Wall),
            new(new(3, 0), new(4, 0), EdgeKind.ClosedDoor)]);
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.False(FogOfWar.IsUnitObservable(state, "blue", "target"));
        state.Types[0] = state.Types[0].WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        FogOfWar.Refresh(state);
        Assert.True(FogOfWar.IsUnitObservable(state, "blue", "target"));
        Assert.False(FogOfWar.IsExplored(state, "blue", new(6, 0)));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "observer", "target"));
        var paths = MovementRules.FindPaths(state, "observer", new(0, 0));
        Assert.Contains(new Cell(4, 0), paths.Keys);
        Assert.All(new Cell[] { new(1, 0), new(2, 0), new(3, 0) }, c => Assert.DoesNotContain(c, paths.Keys));
        Assert.Equal([new Cell(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0)], paths[new(4, 0)]);
    }

    [Fact]
    public void SharedTreeDiscoveryAndPermanentHistorySurviveCopyAndSerialization()
    {
        var state = Corridor(new TerrainTile(new(1, 0), TerrainKind.Tree));
        state.PlaceUnit("test-observer", "ally", "blue", new(8, 0));
        Assert.Contains(FogOfWar.Observe(state, "blue").Units, u => u.Id == "target");
        var json = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<GameState>(json)!;
        TerrainCatalog.Validate(restored.Physical.Board.TerrainTypes);
        FogOfWar.Refresh(restored);
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        restored.Physical.Board.Terrain[0] = new(new(1, 0), TerrainKind.SolidRock);
        restored.Physical.Figures.RemoveAll(f => f.Id == "ally");
        FogOfWar.Refresh(restored);
        Assert.True(FogOfWar.IsUnitObservable(restored, "blue", "target"));
        restored.ExploredCells.Clear(); FogOfWar.Refresh(restored);
        Assert.False(FogOfWar.IsUnitObservable(restored, "blue", "target"));
        Assert.True(FogOfWar.IsUnitObservable(state, "blue", "target"));
        Assert.Null(FogOfWar.ObserveEvent(restored, "blue", new("PostureChanged", UnitId: "target")));
    }

    [Fact]
    public void TypedVisionPropertiesComposeAndValidateWithoutUsingNamesOrRecipes()
    {
        var tree = TerrainCatalog.Legacy(TerrainKind.Tree);
        var fruitTree = TerrainCatalog.Legacy(TerrainKind.FruitTree);
        Assert.False(tree.BlocksVision); Assert.False(fruitTree.BlocksVision);
        Assert.True(tree.BlocksLos); Assert.True(fruitTree.BlocksLos);
        var opaque = new FixedComponentDefinition("opaque-decal", "Tree", false, true)
            { Presentation = new("Tree", FixedComponentLayer.Decal) };
        var assembly = new TerrainAssemblyDefinition("tree-and-obstacle", "Test Assembly", "grass")
            { Components = [new("tree", "tree"), new("opaque", opaque.Id)] };
        var definitions = TerrainCatalog.Resolve(null, [assembly], [opaque]);
        var composed = TerrainCatalog.Find(definitions, assembly.Id);
        Assert.True(composed.BlocksVision);
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Validate([composed with { BlocksVision = false }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Validate([tree with { BlocksVision = true }]));
        Assert.Throws<ArgumentException>(() => FixedComponentCatalog.Validate([
            FixedComponentCatalog.All.Single(c => c.Id == "tree") with { BlocksVision = true }]));
        var roundTrip = JsonSerializer.Deserialize<ImmutableArray<TerrainDefinition>>(JsonSerializer.Serialize(definitions));
        TerrainCatalog.Validate(roundTrip);
        Assert.True(TerrainCatalog.Find(roundTrip, assembly.Id).BlocksVision);
    }

    [Fact]
    public void ReplacingCapturedVisionPropertiesRefreshesSightWithoutChangingAttackBlocking()
    {
        var blocker = new TerrainDefinition("test-screen", "Screen", false, true) { BlocksVision = false };
        var state = Corridor();
        var board = state.Physical.Board;
        state.Physical = state.Physical with { Board = board with { TerrainTypes = TerrainCatalog.Resolve([blocker]) } };
        state.Physical.Board.Terrain.Add(new(new(1, 0)) { TypeId = blocker.Id });
        FogOfWar.Refresh(state);
        var transparentCache = state.VisionCache;
        Assert.True(FogOfWar.IsUnitObservable(state, "blue", "target"));
        state.Physical = state.Physical with { Board = state.Physical.Board with
            { TerrainTypes = TerrainCatalog.Resolve([blocker with { BlocksVision = true }]) } };
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.NotSame(transparentCache, state.VisionCache);
        Assert.False(FogOfWar.IsUnitObservable(state, "blue", "target"));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "observer", "target"));
    }

    [Fact]
    public void OmittedVisionPropertiesKeepExistingLocalBlockersOpaque()
    {
        var terrain = JsonSerializer.Deserialize<TerrainDefinition>(
            """{"Id":"test-legacy-terrain","Name":"Old Blocker","MovementPermissions":[],"BlocksLos":true}""")!;
        var component = JsonSerializer.Deserialize<FixedComponentDefinition>(
            """{"Id":"test-legacy-component","Name":"Old Component","BlocksMovement":true,"BlocksLos":true}""")!;
        Assert.True(terrain.BlocksVision); Assert.True(component.BlocksVision);
        var assembly = new TerrainAssemblyDefinition("old-composition", "Old Composition", "grass")
            { Components = [new("object", component.Id)] };
        Assert.True(TerrainCatalog.Find(TerrainCatalog.Resolve([terrain], [assembly], [component]), assembly.Id).BlocksVision);
    }

    [Fact]
    public void ScenarioTransportPreservesLocalVisionPolicyWithoutChangingAttackOrMovement()
    {
        var definition = Scenario.Define(Scenario.Map(6, 1),
            [Scenario.Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, Scenario.At(0, 0, unitId: "wizard")),
             Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Human, Scenario.At(4, 0, unitId: "target"))]) with
        {
            FogOfWarSides = ["blue"],
            TerrainTypes = [new("test-screen", "Screen", false, true) { BlocksVision = false }]
        };
        definition.Board.Cells.Add(new(new(1, 0), TerrainKind.StoneFloor) { TerrainTypeId = "test-screen" });
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var state = GameEngine.CreateGame(restored);
        Assert.False(state.Physical.Board.TerrainDefinitionAt(new(1, 0)).BlocksVision);
        Assert.True(FogOfWar.IsUnitObservable(state, "blue", "target"));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "wizard", "target"));
        Assert.DoesNotContain(new Cell(2, 0), MovementRules.FindPaths(state, "wizard", new(0, 0)).Keys);
    }
}
