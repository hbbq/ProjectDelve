using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static class TerrainRoster
{
    public static ImmutableArray<TerrainAssemblyDefinition> Assemblies { get; } =
    [
        new("tree", "Tree", "grass") { Components = [new("object", "tree")], DestroyedTerrainTypeId = "grass" },
        new("stone-floor-with-table", "Stone Floor with Table", "stone-floor") { Components = [new("furniture", "table")], DestroyedTerrainTypeId = "stone-floor" },
        new("campfire", "Campfire", "grass") { Components = [new("object", "campfire")], DestroyedTerrainTypeId = "grass" }
    ];

    public static ImmutableArray<TerrainDefinition> All { get; } = Create();

    private static ImmutableArray<TerrainDefinition> Create()
    {
        var grass = Define("grass", "Grass", [MovementKinds.Land], false, TerrainKind.Grass, [TerrainCategories.Land, TerrainCategories.Vegetation]);
        var floor = Define("stone-floor", "Stone Floor", [MovementKinds.Land], false, TerrainKind.StoneFloor, [TerrainCategories.Land, TerrainCategories.Rock, TerrainCategories.Constructed]);
        var tree = TerrainAssemblyContent.Compose(Assemblies[0], grass, FixedComponentCatalog.All) with
            { LegacyKind = TerrainKind.Tree, Presentation = new("Tree") };
        var table = TerrainAssemblyContent.Compose(Assemblies[1], floor, FixedComponentCatalog.All) with
            { LegacyKind = TerrainKind.StoneFloorWithTable, Presentation = new("StoneFloorWithTable") };
        var campfire = TerrainAssemblyContent.Compose(Assemblies[2], grass, FixedComponentCatalog.All);
        return
        [
            grass,
            new("dirt", "Dirt", [MovementKinds.Land], false) { Categories = [TerrainCategories.Land], Presentation = new("Dirt") },
            tree,
            Define("water", "Water", [MovementKinds.Water], false, TerrainKind.Water, [TerrainCategories.Water]),
            new("wet-grass", "Wet Grass", [MovementKinds.Land, MovementKinds.Water], false) {
                Categories = [TerrainCategories.Land, TerrainCategories.Water, TerrainCategories.Vegetation],
                BlocksVision = false, AllowsResourceMarkers = true, Presentation = new("WetGrass") },
            new("rocky-water", "Rocky Water", [MovementKinds.Land, MovementKinds.Water], false) {
                Categories = [TerrainCategories.Land, TerrainCategories.Water, TerrainCategories.Rock],
                BlocksVision = false, AllowsResourceMarkers = true, Presentation = new("RockyWater") },
            new("stepping-stones", "Stepping Stones", [MovementKinds.Land, MovementKinds.Water], false) {
                Categories = [TerrainCategories.Land, TerrainCategories.Water, TerrainCategories.Constructed],
                BlocksVision = false, AllowsResourceMarkers = true, DestroyedTerrainTypeId = "water", Presentation = new("SteppingStones") },
            floor,
            table,
            Define(TerrainCompatibility.OrdinaryLavaId, "Lava", [], false, TerrainKind.Lava, []) with { AllowsResourceMarkers = false },
            Define(TerrainCompatibility.CoalLavaId, "Coal Lava", [], false, TerrainKind.Lava, []) with
            {
                AllowsResourceMarkers = false,
                Production = new DirectionalResourceProduction("coal", 2, SourceLos: ProductionSourceLos.IgnoreProducingCell),
                LegacyProductionIdentity = LegacyTerrainProductionIdentity.LavaCoal
            },
            Define("rail", "Rail", [MovementKinds.Land], false, TerrainKind.Rail, [TerrainCategories.Land, TerrainCategories.Rock, TerrainCategories.Constructed]),
            Define("gravel", "Gravel", [MovementKinds.Land], false, TerrainKind.Gravel, [TerrainCategories.Land, TerrainCategories.Rock]),
            Define("asphalt", "Asphalt", [MovementKinds.Land], false, TerrainKind.Asphalt, [TerrainCategories.Land, TerrainCategories.Constructed, TerrainCategories.RacingGround]),
            Define("fruit-tree", "Fruit Tree", [], true, TerrainKind.FruitTree, [TerrainCategories.Land, TerrainCategories.Vegetation]) with
            {
                BlocksVision = false,
                DestroyedTerrainTypeId = "grass",
                Production = new DirectionalResourceProduction("fruit", 2, SourceLos: ProductionSourceLos.IgnoreProducingCell),
                LegacyProductionIdentity = LegacyTerrainProductionIdentity.FruitTree
            },
            Define("solid-rock", "Solid Rock", [], true, TerrainKind.SolidRock, [TerrainCategories.Rock]) with { DestroyedTerrainTypeId = "gravel" },
            campfire
        ];
    }

    private static TerrainDefinition Define(string id, string name, ImmutableArray<string> permissions, bool blocksLos, TerrainKind legacy, ImmutableArray<string> categories) =>
        new(id, name, permissions, blocksLos) { Categories = categories, LegacyKind = legacy, Presentation = new(legacy.ToString()) };
}
