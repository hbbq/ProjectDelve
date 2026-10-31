using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Inspection accepts authored input only. Neither endpoint reads a PlaytestGame.
public static class ContentBrowserApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/content/catalog", () => ContentBrowserCatalog.Canonical());
        app.MapPost("/api/content/inspect", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                var document = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
                var definition = ScenarioDefinitionJson.FromJson(document, DesignerApi.CheckHostLimits);
                return Results.Ok(ContentBrowserCatalog.Scenario(definition));
            }
            catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or JsonException)
            {
                var path = ScenarioValidationContext.PathOf(error) ?? (error is JsonException json ? json.Path : null);
                if (error is DesignerBoardLimitException) path = "board";
                return Results.Json(new ContentBrowserFailure(new("scenario", "Scenario context"),
                    [new(error.Message, path, error is JsonException ? "json" : "validation")]), statusCode: 400);
            }
        });
    }
}
