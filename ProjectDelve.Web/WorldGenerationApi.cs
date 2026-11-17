using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

// Authoring only. This endpoint never replaces or advances a running game.
public static class WorldGenerationApi
{
    public static void Map(WebApplication app)
    {
        var limits = app.Services.GetRequiredService<AuthoringLimits>();
        app.MapGet("/api/world-generation/catalog", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { maxBoardSize = limits.MaxBoardSize, presets = WorldGenerationPresets.All });
        });
        app.MapPost("/api/world-generation", (GenerationRequest request, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var recipe = WorldGenerationRecipe.Parse(request.Code);
                // Reject oversized requests before sampling/materializing. The detached sampler
                // and generator remain usable for larger experiments without changing host policy.
                limits.CheckDimensions(recipe.Window.Width, recipe.Window.Height);
                var canonical = app.Services.GetRequiredService<CanonicalContent>().Bundle;
                var workspace = app.Services.GetRequiredService<ContentWorkspace>().Snapshot().Definitions;
                var provider = ContentComposition.ComposeScopes(canonical, workspace).Definitions;
                var result = WorldGenerator.Generate(recipe, provider);
                var definition = WorkspaceScenarioExport.Capture(result.Definition, workspace, canonical);
                var catalog = ScenarioContentReferences.ScenarioCatalog(provider, definition).Definitions;
                var json = ScenarioDefinitionJson.ToJson(definition);
                return Results.Ok(new
                {
                    result.Code, heroCapacity = recipe.HeroCapacity,
                    playReady = PlayReadyScenarioSetup.Validate(definition, provider),
                    title = definition.Units.Count > 0 ? "Generated populated world" : "Generated world terrain",
                    definition = JsonSerializer.Deserialize<JsonElement>(json),
                    terrainDefinitions = catalog.Terrain,
                    unitDefinitions = catalog.Units.Where(type => definition.UnitTypeIds.Contains(type.Id))
                        .Select(type => new { type.Id, displayName = type.DisplayName ?? type.Id, cellSpan = type.Footprint == Footprint.TwoByTwo ? 2 : 1 }),
                    transport = ScenarioDefinitionTransport.Encode(definition),
                    fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
                    result.Diagnostics, result.VisualSeed, result.Window, result.Preview, result.Metrics
                });
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
            { return Results.Problem(error.Message, statusCode: 400); }
            catch (PlayReadyGenerationException error)
            {
                app.Logger.LogError(error, "WLD1 generation failed: {FailureKind}, seed {Seed}, stage {Stage}, revision {Revision}; configuration {Configuration}; diagnostics {Diagnostics}.",
                    error.FailureKind, error.Seed, error.Stage, error.BoardRevision, error.ConfigurationJson, error.DiagnosticsJson);
                return Results.Problem(error.Message, statusCode: 500, title: "WLD1 generation failure",
                    extensions: new Dictionary<string, object?> { ["failureKind"] = error.FailureKind.ToString(), ["seed"] = error.Seed.ToString("X16"),
                        ["stage"] = error.Stage, ["boardRevision"] = error.BoardRevision, ["configuration"] = JsonSerializer.Deserialize<JsonElement>(error.ConfigurationJson),
                        ["diagnostics"] = JsonSerializer.Deserialize<JsonElement>(error.DiagnosticsJson) });
            }
        });
        // Prepare a detached playtest artifact; game replacement uses the existing
        // revisioned Designer/Client import flow, never this authoring endpoint.
        app.MapPost("/api/world-generation/quick-play", (JsonElement input, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var canonical = app.Services.GetRequiredService<CanonicalContent>().Bundle;
                var original = ScenarioDefinitionJson.Materialize(input.GetRawText(), limits.CheckScenario, canonical);
                var selected = context.Request.Query["heroTypeIds"].SelectMany(value => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)).ToArray();
                var temporary = WorldQuickPlay.PrepareValidatedWithAccessibility(original, canonical, selected.Length == 0 ? null : selected);
                return Results.Ok(new { transport = ScenarioDefinitionTransport.Encode(temporary.Definition), accessibility = temporary.Accessibility });
            }
            catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or JsonException)
            { return Results.Problem(error.Message, statusCode: 400); }
        });
    }
}
