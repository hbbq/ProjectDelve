using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TerrainAssemblyTests
{
    private static TerrainAssemblyDefinition Assembly(string id = "unfamiliar-furniture-82", string surface = "grass", string component = "table") =>
        new(id, "A Completely Unfamiliar Assembly", surface) { Components = [new("furniture", component)] };

    private static TerrainDefinition Find(ImmutableArray<TerrainDefinition> catalog, string id = "unfamiliar-furniture-82") =>
        TerrainCatalog.Find(catalog, id);

    [Fact]
    public void CanonicalCampfireUsesGrassAndReusableComponentWithoutBlockingLos()
    {
        var campfire = TerrainCatalog.Find(TerrainCatalog.Resolve(null), "campfire");
        Assert.Equal("Campfire", campfire.Name);
        Assert.Null(campfire.LegacyKind);
        Assert.Null(campfire.Production);
        Assert.Null(campfire.LegacyProductionIdentity);
        Assert.Equal("grass", campfire.Assembly!.Surface.Id);
        var component = Assert.Single(campfire.Assembly.Components).Definition;
        Assert.Equal(Assert.Single(FixedComponentCatalog.All, c => c.Id == "campfire"), component);
        Assert.Equal("Campfire", component.Presentation.Recipe);
        Assert.Equal(FixedComponentLayer.Object, component.Presentation.Layer);
        Assert.True(component.BlocksMovement);
        Assert.False(component.BlocksLos);
        var json = JsonSerializer.Serialize(campfire);
        var restored = JsonSerializer.Deserialize<TerrainDefinition>(json)!;
        TerrainCatalog.Validate([restored]);
        Assert.Equal(json, JsonSerializer.Serialize(restored));

        var state = new GameState
        {
            Physical = new(new Board(5, 1, []) { Terrain = [new(new(2, 0)) { TypeId = "campfire" }] },
                [new("actor", new(0, 0)), new("target", new(4, 0))]),
            Types = [new("observer", 4, 4, 1, 0, 1)],
            Units = [new("actor", "observer", "blue", 1), new("target", "observer", "red", 1)]
        };
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.Contains(new Cell(1, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(2, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(3, 0), paths.Keys);
        Assert.False(SpatialRules.Fits(state.Physical.Board, Footprint.OneByOne, new(2, 0)));
        Assert.True(AttackRules.HasGeometricLineOfSight(state.Physical.Board, new(0, 0), new(4, 0)));
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateFrom(state, "actor", new(0, 0), "target"));
    }

    [Fact]
    public void SameTableResolvesOnDifferentSurfacesWithoutNewRules()
    {
        var types = TerrainCatalog.Resolve(null, [Assembly(), Assembly("other-98", "stone-floor")]);
        var grass = Find(types);
        var stone = Find(types, "other-98");
        Assert.False(grass.Passable); Assert.False(grass.BlocksLos);
        Assert.False(stone.Passable); Assert.False(stone.BlocksLos);
        Assert.Equal("grass", grass.Assembly!.Surface.Id);
        Assert.Equal("stone-floor", stone.Assembly!.Surface.Id);
        Assert.Equal(Assert.Single(grass.Assembly.Components).Definition, Assert.Single(stone.Assembly.Components).Definition);
        Assert.Equal("Table", Assert.Single(grass.Assembly.Components).Definition.Presentation.Recipe);
        Assert.DoesNotContain(TerrainCatalog.All, d => d.Id == grass.Id);
        Assert.Null(grass.LegacyKind);
    }

    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, false, true)]
    public void ComponentObstructionsComposeIndependently(bool blocksMovement, bool blocksLos, bool passable, bool opaque)
    {
        var component = new FixedComponentDefinition("strange-feature", "Strange Feature", blocksMovement, blocksLos)
            { Presentation = new("missing-visual-recipe") };
        var terrain = Find(TerrainCatalog.Resolve(null, [Assembly(component: component.Id)], [component]));
        Assert.Equal(passable, terrain.Passable);
        Assert.Equal(opaque, terrain.BlocksLos);
        Assert.Equal("missing-visual-recipe", Assert.Single(terrain.Assembly!.Components).Definition.Presentation.Recipe);
    }

    [Fact]
    public void ComponentsCannotRemoveSurfaceObstructionsAndEmptyAssemblyRetainsItsSurface()
    {
        var surface = new TerrainDefinition("opaque-floor", "Opaque Floor", false, true);
        var cosmetic = new FixedComponentDefinition("decoration", "Decoration", false, false)
            { Presentation = new("mark", FixedComponentLayer.Decal) };
        var types = TerrainCatalog.Resolve([surface], [Assembly(surface: surface.Id, component: cosmetic.Id),
            new("empty-assembly", "Empty Assembly", surface.Id)], [cosmetic]);
        Assert.False(Find(types).Passable); Assert.True(Find(types).BlocksLos);
        Assert.False(Find(types, "empty-assembly").Passable); Assert.True(Find(types, "empty-assembly").BlocksLos);
        Assert.Empty(Find(types, "empty-assembly").Assembly!.Components);
    }

    [Fact]
    public void SurfaceCategoriesResourcePolicyAndProductionSurviveComposition()
    {
        var mark = new FixedComponentDefinition("crest", "Crest", false, false)
            { Presentation = new("missing-mark", FixedComponentLayer.Decal) };
        var catalog = TerrainCatalog.Resolve(null, [Assembly("decorated-road", "asphalt", mark.Id),
            Assembly("decorated-coal", "coal-lava", mark.Id)], [mark]);
        Assert.True(Find(catalog, "decorated-road").Passable);
        Assert.Equal(new[] { TerrainCategories.RacingGround }, Find(catalog, "decorated-road").Categories);
        var lava = Find(catalog, "decorated-coal");
        Assert.False(lava.Passable); Assert.False(lava.AllowsResourceMarkers);
        Assert.Equal(TerrainCatalog.Find(TerrainCatalog.All, "coal-lava").Production, lava.Production);
        Assert.Null(lava.LegacyProductionIdentity);
        Assert.Equal("coal-lava", lava.Assembly!.Surface.Id);
    }

    [Fact]
    public void ResolvedAssembliesUseExistingPhaseAndMultiCellPlacementBoundaries()
    {
        var definition = Assembly();
        var state = new GameState
        {
            Physical = new(new Board(7, 3, []) { TerrainTypes = TerrainCatalog.Resolve(null, [definition]),
                Terrain = [new(new(2, 0)) { TypeId = definition.Id }] }, [new("actor", new(0, 0))]),
            Types = [new("large-unit", 5, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo,
                LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase())] }],
            Units = [new("actor", "large-unit", "blue", 1)]
        };
        var board = state.Physical.Board;
        Assert.False(SpatialRules.Fits(board, Footprint.TwoByTwo, new(1, 0)));
        Assert.True(SpatialRules.Fits(board, Footprint.TwoByTwo, new(3, 0)));
        var paths = MovementRules.FindPaths(state, "actor", new(0, 0));
        Assert.DoesNotContain(new Cell(1, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(2, 0), paths.Keys);
        Assert.Contains(new Cell(3, 0), paths.Keys);
        Assert.True(AttackRules.HasGeometricLineOfSight(board, new(0, 0), new(6, 0)));
    }

    [Fact]
    public void CanonicalTreeAndTableKeepIdsRulesRecipesAndJsonEquivalence()
    {
        var table = TerrainCatalog.Legacy(TerrainKind.StoneFloorWithTable);
        var tree = TerrainCatalog.Legacy(TerrainKind.Tree);
        Assert.Equal("stone-floor-with-table", table.Id);
        Assert.Equal("StoneFloorWithTable", table.Presentation.Recipe);
        Assert.Equal("stone-floor", table.Assembly!.Surface.Id);
        Assert.False(table.Passable); Assert.False(table.BlocksLos);
        Assert.Equal("tree", tree.Id); Assert.Equal("Tree", tree.Presentation.Recipe);
        Assert.Equal("grass", tree.Assembly!.Surface.Id);
        Assert.False(tree.Passable); Assert.True(tree.BlocksLos);
        var json = JsonSerializer.Serialize(TerrainCatalog.All);
        var loaded = JsonSerializer.Deserialize<ImmutableArray<TerrainDefinition>>(json);
        TerrainCatalog.Validate(loaded);
        Assert.Equal(json, JsonSerializer.Serialize(loaded));
    }

    [Fact]
    public void VersionTwoAtomicTreeAndTableSavesRemainValid()
    {
        var board = new Board(3, 1, []) { Terrain = [new(new(0, 0), TerrainKind.Tree), new(new(1, 0), TerrainKind.StoneFloorWithTable)] };
        var saved = JsonSerializer.SerializeToNode(board)!;
        foreach (var definition in saved["TerrainTypes"]!.AsArray()) definition!.AsObject().Remove("Assembly");
        var restored = JsonSerializer.Deserialize<Board>(saved.ToJsonString())!;
        TerrainCatalog.Validate(restored.TerrainTypes);
        Assert.Equal(2, restored.TerrainContentVersion);
        Assert.Null(restored.TerrainDefinitionAt(new(0, 0)).Assembly);
        Assert.False(restored.TerrainDefinitionAt(new(0, 0)).Passable);
        Assert.True(restored.TerrainDefinitionAt(new(0, 0)).BlocksLos);
        Assert.False(restored.TerrainDefinitionAt(new(1, 0)).BlocksLos);
    }

    [Fact]
    public void OneObjectAndSeveralFlatComponentsHaveDistinctStableSlotsAndOrientations()
    {
        var decal = new FixedComponentDefinition("seal", "Seal", false, false) { Presentation = new("unfamiliar-seal", FixedComponentLayer.Decal) };
        var source = Assembly() with { Components = [new("furniture", "table", 3), new("seal-a", decal.Id, 1), new("seal-b", decal.Id)] };
        var result = Find(TerrainCatalog.Resolve(null, [source], [decal]));
        Assert.Equal(new[] { "furniture", "seal-a", "seal-b" }, result.Assembly!.Components.Select(c => c.Slot));
        Assert.Equal(new int?[] { 3, 1, null }, result.Assembly.Components.Select(c => c.QuarterTurns));
        Assert.Equal(1, result.Assembly.Components.Count(c => c.Definition.Presentation.Layer == FixedComponentLayer.Object));
    }

    [Theory]
    [InlineData("absent-surface")]
    [InlineData("rail")]
    [InlineData("water")]
    [InlineData("fruit-tree")]
    [InlineData("solid-rock")]
    [InlineData("tree")]
    [InlineData("stone-floor-with-table")]
    [InlineData("lava")]
    public void UnsupportedOrMissingSurfaceIsRejected(string surface) =>
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve(null, [Assembly(surface: surface)]));

    [Theory]
    [InlineData("missing-component")]
    [InlineData("duplicate-slot")]
    [InlineData("second-object")]
    [InlineData("invalid-turn")]
    [InlineData("blank-slot")]
    [InlineData("too-many")]
    [InlineData("duplicate-terrain")]
    [InlineData("nested")]
    [InlineData("cycle")]
    public void InvalidAssembliesAreRejected(string error)
    {
        var source = Assembly();
        TerrainAssemblyDefinition[] sources = [source];
        switch (error)
        {
            case "missing-component": sources = [Assembly(component: "no-such-component")]; break;
            case "duplicate-slot": sources = [source with { Components = [new("same", "table"), new("same", "tree")] }]; break;
            case "second-object": sources = [source with { Components = [new("one", "table"), new("two", "tree")] }]; break;
            case "invalid-turn": sources = [source with { Components = [new("furniture", "table", 4)] }]; break;
            case "blank-slot": sources = [source with { Components = [new(" ", "table")] }]; break;
            case "too-many": sources = [source with { Components = [.. Enumerable.Range(0, 5).Select(i => new FixedComponentPlacement($"slot-{i}", "table"))] }]; break;
            case "duplicate-terrain": sources = [source with { Id = "grass" }]; break;
            case "nested": sources = [source, Assembly("nested", source.Id)]; break;
            case "cycle": sources = [Assembly("a", "b"), Assembly("b", "a")]; break;
        }
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve(null, sources));
    }

    [Fact]
    public void InvalidComponentsCannotOverrideCanonicalRulesOrDeclareUnknownProperties()
    {
        Assert.Throws<ArgumentException>(() => FixedComponentCatalog.Resolve([new("table", "Counterfeit", false, false)]));
        Assert.Throws<ArgumentException>(() => FixedComponentCatalog.Resolve([new("invalid", "Invalid", false, false) { Presentation = new("generic", (FixedComponentLayer)99) }]));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FixedComponentDefinition>("""{"Id":"a","Name":"A","BlocksMovement":true}"""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FixedComponentDefinition>("""{"Id":"a","Name":"A","BlocksMovement":true,"BlocksLos":false,"HitPoints":1}"""));
    }

    [Fact]
    public void RuntimeSaveCannotContradictResolvedGameplayOrReuseIdsForDifferentDefinitions()
    {
        var component = new FixedComponentDefinition("local-piece", "Local Piece", true, false);
        var catalog = TerrainCatalog.Resolve(null, [Assembly(component: component.Id), Assembly("other", component: component.Id)], [component]);
        var terrain = Find(catalog);
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Validate(catalog.Replace(terrain, terrain with { Passable = true })));
        var changedComponent = terrain with { Assembly = terrain.Assembly! with
            { Components = [new("furniture", component with { Name = "Inconsistent Name" })] } };
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Validate(catalog.Replace(terrain, changedComponent)));
        var changedSurface = terrain with { Assembly = terrain.Assembly! with
            { Surface = terrain.Assembly.Surface with { Name = "Inconsistent Surface" } } };
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Validate(catalog.Replace(terrain, changedSurface)));
        TerrainCatalog.Validate([terrain]); // Observed content can embed a surface omitted as a separate entry.
    }
}
