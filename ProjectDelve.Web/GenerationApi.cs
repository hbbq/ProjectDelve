using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

public sealed record GenerationRequest(string Code);
public static class GenerationApi
{
    public static void Map(WebApplication app)
    {
        ContentBundle Canonical() => app.Services.GetRequiredService<CanonicalContent>().Bundle;
        app.MapPost("/api/generation", (GenerationRequest request, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var result = DungeonGenerator.Generate(request.Code);
                var effectiveUnits = UnitContent.Resolve(result.Definition.UnitTypes, Canonical().Units);
                return Results.Ok(new { code = result.Code.ToString(), prefix = result.Code.Prefix,
                    terrainDefinitions = TerrainCatalog.Resolve(result.Definition.TerrainTypes,
                        result.Definition.TerrainAssemblies, result.Definition.FixedComponentTypes, Canonical()),
                    unitMetadata = result.Definition.UnitTypeIds.Select(id => UnitContent.Find(id, effectiveUnits)!).Select(type => new
                    { unitTypeId = type.Id, footprintOffsets = FootprintGeometry.OccupiedCells(type.Footprint, new(0, 0)) }),
                    definition = JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(result.Definition)),
                    transport = ScenarioDefinitionTransport.Encode(result.Definition), result.Fingerprint, result.Diagnostics });
            }
            catch (Exception error) when (error is ArgumentException or FormatException)
            { return Results.Problem(error.Message, statusCode: 400); }
        });
    }
}
