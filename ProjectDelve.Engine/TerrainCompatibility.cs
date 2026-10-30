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
        return typeId ?? TerrainCatalog.Legacy(legacy).Id;
    }

    // Version 1 runtime snapshots predate typed production. This upgrades only
    // entries already present, including catalogs reduced by Fog of War.
    internal static ImmutableArray<TerrainDefinition> UpgradeRuntimeCatalog(ImmutableArray<TerrainDefinition> definitions)
    {
        var upgraded = ImmutableArray.CreateBuilder<TerrainDefinition>();
        foreach (var definition in definitions)
        {
            if (definition.Id == LegacyLavaId)
            {
                if (string.IsNullOrWhiteSpace(definition.Name) || definition.LegacyKind != TerrainKind.Lava || definition.Passable || definition.BlocksLos ||
                    definition.AllowsResourceMarkers || definition.Categories.IsDefault || !definition.Categories.IsEmpty ||
                    definition.Production is not null || definition.LegacyProductionIdentity is not null)
                    throw new ArgumentException("Invalid legacy Lava definition in runtime snapshot.");
                foreach (var id in new[] { OrdinaryLavaId, CoalLavaId })
                {
                    var canonical = TerrainCatalog.Find(TerrainCatalog.All, id);
                    upgraded.Add(canonical with { Name = definition.Name == "Lava" ? canonical.Name : definition.Name,
                        Presentation = definition.Presentation });
                }
            }
            else if (definition.Id == "fruit-tree" && definition.Production is null && definition.LegacyProductionIdentity is null)
            {
                var canonical = TerrainCatalog.Legacy(TerrainKind.FruitTree);
                upgraded.Add(definition with { Production = canonical.Production, LegacyProductionIdentity = canonical.LegacyProductionIdentity });
            }
            else upgraded.Add(definition);
        }
        var result = upgraded.ToImmutable();
        TerrainCatalog.Validate(result);
        return result;
    }
}
