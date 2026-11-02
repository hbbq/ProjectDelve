using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public static class ContentLibraryApi
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private sealed record ArchiveRequest(long ExpectedRevision);

    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/content-library"))
            {
                if (context.Request.ContentLength > 8_000_000)
                {
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.StatusCode = 413;
                    await context.Response.WriteAsJsonAsync(new { error = "Content Library request is too large." });
                    return;
                }
                var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 8_000_000;
            }
            await next(context);
        });
        var api = app.MapGroup("/api/content-library");
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (ContentCompositionException error)
            { return Results.Json(new { error = "Content validation failed.", diagnostics = error.Diagnostics }, Json, statusCode: 400); }
            catch (LibraryException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
            catch (BadHttpRequestException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
            catch (JsonException error) { return Results.Problem(error.Message, statusCode: 400); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or OverflowException)
            { return Results.Problem(error.Message, statusCode: 400); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogError(error, "Content Library storage operation failed");
                return Results.Problem("The Content Library could not be read or saved. Check server storage permissions and disk space, then retry.", statusCode: 503);
            }
        });
        api.MapGet("/", (ContentLibrary library, bool? includeArchived) => library.Catalog(includeArchived ?? false));
        api.MapGet("/providers", (CanonicalContent canonical) => canonical.Packages.Select(p => new { p.Id, title = p.Title ?? p.Id }));
        api.MapPost("/promotion/preview", async (HttpRequest request, ContentPromotionService promotion) => promotion.Preview(await Read<PromotionRequest>(request)));
        api.MapPost("/promotion/publish", async (HttpRequest request, ContentPromotionService promotion) => promotion.Publish(await Read<PublishPromotion>(request)));
        api.MapPost("/scenario-reference/preview", async (HttpRequest request, ScenarioReferenceService references) =>
            references.Preview(await Read<PreviewScenarioReference>(request)));
        api.MapPost("/authoring/context", async (HttpRequest request, ContentLibrary library, CanonicalContent canonical) =>
            ContentBrowserCatalog.Published(ContentLibraryAuthoring.Catalog(library.Inspect(await Read<SaveContentLibraryPackage>(request))), canonical.Bundle));
        api.MapPost("/authoring/validate", async (HttpRequest request, ContentLibrary library) =>
            ContentLibraryAuthoring.Validate(library, await Read<ValidateLibraryContent>(request)));
        api.MapGet("/packages/{id}/context", (ContentLibrary library, CanonicalContent canonical, string id, long? revision) => ContentBrowserCatalog.Published(ContentLibraryAuthoring.Catalog(library.Inspect(id, revision)), canonical.Bundle));
        api.MapGet("/packages/{id}", (ContentLibrary library, string id, long? revision) => library.Read(id, revision));
        api.MapGet("/packages/{id}/revisions", (ContentLibrary library, string id) => library.History(id));
        api.MapPost("/packages", async (HttpRequest request, ContentLibrary library) =>
        {
            var input = await Read<SaveContentLibraryPackage>(request);
            if (input.ExpectedRevision != 0) throw new ArgumentException("Creating requires expectedRevision 0.");
            return library.Create(input.Package, input.Bindings);
        });
        api.MapPut("/packages/{id}", async (HttpRequest request, ContentLibrary library, string id) => library.Save(id, await Read<SaveContentLibraryPackage>(request)));
        api.MapPost("/transactions", async (HttpRequest request, ContentLibrary library) =>
            library.SaveBatch(await Read<ImmutableArray<SaveContentLibraryPackage>>(request)));
        api.MapPost("/packages/{id}/archive", async (HttpRequest request, ContentLibrary library, string id) =>
        {
            library.Archive(id, (await Read<ArchiveRequest>(request)).ExpectedRevision);
            return Results.NoContent();
        });
        api.MapGet("/packages/{id}/export", (ContentLibrary library, string id, long? revision) =>
            Results.Text(ContentLibraryJson.ToJson(library.Export(id, revision)), "application/json"));
        api.MapGet("/packages/{id}/export/package", (ContentLibrary library, string id, long? revision) =>
            Results.Text(ContentPackageJson.ToJson(library.ExportPackage(id, revision)), "application/json"));
        api.MapPost("/import/package", async (HttpRequest request, ContentLibrary library) =>
            library.Import(ContentPackageJson.FromJson(await Text(request), "import")));
        api.MapPost("/import", async (HttpRequest request, ContentLibrary library) =>
            library.Import(ContentLibraryJson.FromJson(await Text(request))));
    }

    private static async Task<string> Text(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var text = await reader.ReadToEndAsync();
        if (text.Length > 8_000_000) throw new ArgumentException("Content Library request is too large.");
        return text;
    }
    private static async Task<T> Read<T>(HttpRequest request) => JsonSerializer.Deserialize<T>(await Text(request), Json)
        ?? throw new JsonException("A Content Library request is required.");
}
