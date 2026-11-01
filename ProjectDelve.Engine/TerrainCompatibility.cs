using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Explicit import boundary for the former per-Cell Lava switch. New content never
// uses the alias or the switch to choose a mechanic.
public static class TerrainCompatibility
{
    public const string LegacyLavaId = "lava";
    public const string OrdinaryLavaId = "ordinary-lava";
    public const string CoalLavaId = "coal-lava";

    public static bool IsLegacyLavaReference(TerrainKind legacy, string? typeId) =>
        typeId == LegacyLavaId || typeId is null && legacy == TerrainKind.Lava;

    public static string ResolveTypeId(TerrainKind legacy, string? typeId, bool? coalGeneration = null)
    {
        if (IsLegacyLavaReference(legacy, typeId)) return coalGeneration == false ? OrdinaryLavaId : CoalLavaId;
        if (coalGeneration is not null)
            throw new ArgumentException("coalGeneration is supported only on legacy Lava references; new Terrain Types declare production.");
        return typeId ?? legacy switch
        {
            TerrainKind.Grass => "grass", TerrainKind.Tree => "tree", TerrainKind.Water => "water",
            TerrainKind.StoneFloor => "stone-floor", TerrainKind.StoneFloorWithTable => "stone-floor-with-table",
            TerrainKind.Rail => "rail", TerrainKind.Gravel => "gravel", TerrainKind.Asphalt => "asphalt",
            TerrainKind.FruitTree => "fruit-tree", TerrainKind.SolidRock => "solid-rock",
            _ => throw new ArgumentException("Unknown legacy Terrain kind.")
        };
    }

}
