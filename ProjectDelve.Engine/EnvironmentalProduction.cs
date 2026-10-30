using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// One concrete Round Start operation, not an arbitrary timing/effect language.
public enum ProductionPlacement { SurroundingCellD8 }
public enum ProductionSourceLos { RespectTerrain, IgnoreProducingCell }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DirectionalResourceProduction), "directionalResourceProduction")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record EnvironmentalProduction;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DirectionalResourceProduction(
    [property: JsonRequired] string ResourceTypeId,
    [property: JsonRequired] int Attempts,
    ProductionPlacement Placement = ProductionPlacement.SurroundingCellD8,
    ProductionSourceLos SourceLos = ProductionSourceLos.RespectTerrain) : EnvironmentalProduction;

// Historical event/accounting names only. This never selects a production operation.
public enum LegacyTerrainProductionIdentity { LavaCoal, FruitTree }

public static class EnvironmentalProductionContent
{
    public static void Validate(EnvironmentalProduction? production)
    {
        if (production is null) return;
        if (production is not DirectionalResourceProduction directional || directional.Attempts is < 1 or > 100 ||
            directional.Placement != ProductionPlacement.SurroundingCellD8 || !Enum.IsDefined(directional.SourceLos))
            throw new ArgumentException("Directional production requires 1–100 attempts and supported placement and source LOS policies.");
        _ = ResourceTypes.Find(directional.ResourceTypeId);
    }

    public static string? Describe(EnvironmentalProduction? production) => production switch
    {
        null => null,
        DirectionalResourceProduction p => $"At Round Start, make {p.Attempts} d8 {(p.Attempts == 1 ? "attempt" : "attempts")} to place one {ResourceTypes.Find(p.ResourceTypeId).Name} on the selected surrounding Cell (1 north, then clockwise). " +
            "Resolve attempts in order; failed placement does not reroll. " +
            (p.SourceLos == ProductionSourceLos.IgnoreProducingCell ? "Ignore only the producing Cell's LOS blocking." : "Respect the producing Cell's LOS blocking."),
        _ => throw new ArgumentException("Unsupported environmental production mechanic.")
    };

    internal static (string RollEvent, string ActionId) EventIdentity(TerrainDefinition terrain) => terrain.LegacyProductionIdentity switch
    {
        LegacyTerrainProductionIdentity.LavaCoal => ("LavaCoalRolled", "lava-coal"),
        LegacyTerrainProductionIdentity.FruitTree => ("FruitTreeRolled", "fruit-tree"),
        _ => ("TerrainProductionRolled", "terrain-production")
    };
}
