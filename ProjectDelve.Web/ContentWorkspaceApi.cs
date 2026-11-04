using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public static class ContentWorkspaceApi
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/content-workspace") || context.Request.Path == "/api/content/loot-management")
            {
                if (context.Request.ContentLength > 8_000_000) { context.Response.StatusCode = 413; return; }
                var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 8_000_000;
            }
            await next(context);
        });
        var api = app.MapGroup("/api/content-workspace");
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (ContentCompositionException error) { return Results.Json(new { error = "Content validation failed.", diagnostics = error.Diagnostics }, Json, statusCode: 400); }
            catch (LibraryException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
            catch (BadHttpRequestException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException)
            { return Results.Problem(error.Message, statusCode: 400); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogError(error, "Workspace storage operation failed");
                return Results.Problem("Workspace could not be read or saved. Check storage and retry; published content is retained.", statusCode: 503);
            }
        });
        api.MapGet("/", (ContentWorkspace workspace) => Results.Json(workspace.Snapshot(), Json));
        api.MapGet("/catalog", (ContentWorkspace workspace, CanonicalContent canonical) =>
        {
            var snapshot = workspace.Snapshot();
            var browser = ContentBrowserCatalog.Canonical(ContentComposition.ComposeScopes(canonical.Bundle, snapshot.Definitions).Definitions);
            return Results.Json(ContentBrowserCatalog.WorkspaceOrigins(browser, snapshot.Definitions, canonical.Bundle)
                with { Context = new("workspace", "Workspace") }, Json);
        });
        api.MapPut("/", async (HttpRequest request, ContentWorkspace workspace) =>
        {
            var input = await Read<SaveContentWorkspace>(request);
            return Results.Json(workspace.Save(input.Definitions, input.ExpectedToken), Json);
        });
        api.MapDelete("/", async (HttpRequest request, ContentWorkspace workspace) =>
            Results.Json(workspace.Clear((await Read<ClearContentWorkspace>(request)).ExpectedToken), Json));
        api.MapPost("/promotion/preview", async (HttpRequest request, WorkspacePromotionService promotion) =>
            Results.Json(promotion.Preview(await Read<WorkspacePromotionRequest>(request)), Json));
        api.MapPost("/promotion/publish", async (HttpRequest request, WorkspacePromotionService promotion) =>
            Results.Json(promotion.Publish(await Read<ConfirmWorkspacePromotion>(request)), Json));
        api.MapPost("/loot/preview", async (HttpRequest request, LootManagementService loot) =>
            Results.Json(loot.Preview(await Read<LootBatchRequest>(request)), Json));
        api.MapPost("/loot/create", async (HttpRequest request, LootManagementService loot) =>
            Results.Json(loot.Create(await Read<ConfirmLootBatch>(request)), Json));
        app.MapPost("/api/content/loot-management", async (HttpRequest request, LootManagementService loot) =>
        {
            try { return Results.Json(loot.Inspect((await Read<LootManagementRequest>(request)).Context), Json); }
            catch (ContentCompositionException error) { return Results.Json(new { error = "Content validation failed.", diagnostics = error.Diagnostics }, Json, statusCode: 400); }
            catch (LibraryException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
            catch (Exception error) when (error is JsonException or ArgumentException) { return Results.Problem(error.Message, statusCode: 400); }
        });
    }
    private static async Task<T> Read<T>(HttpRequest request)
    {
        using var document = await JsonDocument.ParseAsync(request.Body);
        void Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                HashSet<string> fields = new(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!fields.Add(property.Name)) throw new JsonException($"Duplicate field '{property.Name}'.");
                    Unique(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var row in element.EnumerateArray()) Unique(row);
        }
        Unique(document.RootElement);
        return document.RootElement.Deserialize<T>(Json) ?? throw new JsonException("A Workspace request is required.");
    }
}
