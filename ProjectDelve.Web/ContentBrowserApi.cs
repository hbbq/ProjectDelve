using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Inspection accepts authored input only. Neither endpoint reads a PlaytestGame.
public static class ContentBrowserApi
{
    public static void Map(WebApplication app)
    {
        var limits = app.Services.GetRequiredService<AuthoringLimits>();
        ContentBundle Canonical() => app.Services.GetRequiredService<CanonicalContent>().Bundle;
        app.MapGet("/api/content/catalog", () => ContentBrowserCatalog.Canonical(Canonical()));
        app.MapPost("/api/content/inspect", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                var document = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
                var canonical = Canonical();
                var workspace = app.Services.GetRequiredService<ContentWorkspace>().Snapshot().Definitions;
                var effective = ContentComposition.ComposeScopes(canonical, workspace).Definitions;
                var definition = ScenarioDefinitionJson.FromJson(document, limits.CheckScenario, effective);
                return Results.Ok(ContentBrowserCatalog.WorkspaceOrigins(ContentBrowserCatalog.Scenario(definition, effective), workspace, canonical));
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
