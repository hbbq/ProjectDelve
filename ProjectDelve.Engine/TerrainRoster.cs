using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static class TerrainRoster
{
    public static ImmutableArray<TerrainAssemblyDefinition> Assemblies { get; } =
    [
        new("tree", "Tree", "grass") { Components = [new("object", "tree")] },
        new("stone-floor-with-table", "Stone Floor with Table", "stone-floor") { Components = [new("furniture", "table")] }
    ];

    public static ImmutableArray<TerrainDefinition> All { get; } = Create();

    private static ImmutableArray<TerrainDefinition> Create()
    {
        var grass = Define("grass", "Grass", true, false, TerrainKind.Grass);
        var floor = Define("stone-floor", "Stone Floor", true, false, TerrainKind.StoneFloor);
        var tree = TerrainAssemblyContent.Compose(Assemblies[0], grass, FixedComponentCatalog.All) with
            { LegacyKind = TerrainKind.Tree, Presentation = new("Tree") };
        var table = TerrainAssemblyContent.Compose(Assemblies[1], floor, FixedComponentCatalog.All) with
            { LegacyKind = TerrainKind.StoneFloorWithTable, Presentation = new("StoneFloorWithTable") };
        return
        [
            grass,
            tree,
            Define("water", "Water", false, false, TerrainKind.Water),
            floor,
            table,
            Define(TerrainCompatibility.OrdinaryLavaId, "Lava", false, false, TerrainKind.Lava) with { AllowsResourceMarkers = false },
            Define(TerrainCompatibility.CoalLavaId, "Coal Lava", false, false, TerrainKind.Lava) with
            {
                AllowsResourceMarkers = false,
                Production = new DirectionalResourceProduction("coal", 2, SourceLos: ProductionSourceLos.IgnoreProducingCell),
                LegacyProductionIdentity = LegacyTerrainProductionIdentity.LavaCoal
            },
            Define("rail", "Rail", true, false, TerrainKind.Rail),
            Define("gravel", "Gravel", true, false, TerrainKind.Gravel),
            Define("asphalt", "Asphalt", true, false, TerrainKind.Asphalt) with { Categories = [TerrainCategories.RacingGround] },
            Define("fruit-tree", "Fruit Tree", false, true, TerrainKind.FruitTree) with
            {
                Production = new DirectionalResourceProduction("fruit", 2, SourceLos: ProductionSourceLos.IgnoreProducingCell),
                LegacyProductionIdentity = LegacyTerrainProductionIdentity.FruitTree
            },
            Define("solid-rock", "Solid Rock", false, true, TerrainKind.SolidRock)
        ];
    }

    private static TerrainDefinition Define(string id, string name, bool passable, bool blocksLos, TerrainKind legacy) =>
        new(id, name, passable, blocksLos) { LegacyKind = legacy, Presentation = new(legacy.ToString()) };
}
