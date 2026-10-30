using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static class TerrainRoster
{
    public static ImmutableArray<TerrainDefinition> All { get; } =
    [
        Define("grass", "Grass", true, false, TerrainKind.Grass),
        Define("tree", "Tree", false, true, TerrainKind.Tree),
        Define("water", "Water", false, false, TerrainKind.Water),
        Define("stone-floor", "Stone Floor", true, false, TerrainKind.StoneFloor),
        Define("stone-floor-with-table", "Stone Floor with Table", false, false, TerrainKind.StoneFloorWithTable),
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

    private static TerrainDefinition Define(string id, string name, bool passable, bool blocksLos, TerrainKind legacy) =>
        new(id, name, passable, blocksLos) { LegacyKind = legacy, Presentation = new(legacy.ToString()) };
}
