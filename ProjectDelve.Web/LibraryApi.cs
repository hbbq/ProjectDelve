using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public static class LibraryApi
{
    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/library") && !context.Request.Path.StartsWithSegments("/api/library/covers"))
            {
                var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 2_100_000;
            }
            await next(context);
        });
        var api = app.MapGroup("/api/library");
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (LibraryException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
            catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or JsonException)
            { return Results.Problem(error.Message, statusCode: 400); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogError(error, "Library storage operation failed");
                return Results.Problem("The library could not be read or saved. Check the server's storage permissions and available disk space, then retry.", statusCode: 503);
            }
        });
        api.MapGet("/", (ScenarioLibrary library) => library.Catalog());
        api.MapGet("/scenarios/{id}", (string id, ScenarioLibrary library) => library.Scenario(id));
        api.MapGet("/campaigns/{id}", (string id, ScenarioLibrary library) => library.Campaign(id));
        api.MapGet("/scenarios/{id}/transport", (string id, ScenarioLibrary library) => new
        {
            transport = ScenarioDefinitionTransport.Encode(ScenarioDefinitionJson.FromJson(library.Scenario(id).Definition.GetRawText(), DesignerApi.CheckHostLimits))
        });
        api.MapPost("/scenarios", (SaveLibraryScenario request, ScenarioLibrary library) => Results.Ok(library.SaveScenario(null, request)));
        api.MapPut("/scenarios/{id}", (string id, SaveLibraryScenario request, ScenarioLibrary library) => Results.Ok(library.SaveScenario(id, request)));
        api.MapDelete("/scenarios/{id}", (string id, long? revision, string? changeToken, ScenarioLibrary library) =>
        { library.DeleteScenario(id, revision, changeToken); return Results.NoContent(); });
        api.MapPost("/scenarios/{id}/restore", (string id, RestoreLibraryItem request, ScenarioLibrary library) => library.RestoreScenario(id, request));
        api.MapPost("/campaigns", (SaveLibraryCampaign request, ScenarioLibrary library) => Results.Ok(library.SaveCampaign(null, request)));
        api.MapPut("/campaigns/{id}", (string id, SaveLibraryCampaign request, ScenarioLibrary library) => Results.Ok(library.SaveCampaign(id, request)));
        api.MapDelete("/campaigns/{id}", (string id, long? revision, string? changeToken, ScenarioLibrary library) =>
        { library.DeleteCampaign(id, revision, changeToken); return Results.NoContent(); });
        api.MapPost("/campaigns/{id}/restore", (string id, RestoreLibraryItem request, ScenarioLibrary library) => library.RestoreCampaign(id, request));
    }
}
