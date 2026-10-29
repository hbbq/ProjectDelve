using Microsoft.AspNetCore.Http.Features;

namespace ProjectDelve.Web;

public static class LibraryCoverApi
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/library/covers", async (HttpContext context, LibraryCoverStore covers) =>
        {
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = LibraryCoverStore.MaxBytes;
            try
            {
                if (context.Request.ContentLength > LibraryCoverStore.MaxBytes)
                    return TooLarge(context);
                using var buffer = new MemoryStream();
                var block = new byte[64 * 1024];
                while (true)
                {
                    var read = await context.Request.Body.ReadAsync(block, context.RequestAborted);
                    if (read == 0) break;
                    if (buffer.Length + read > LibraryCoverStore.MaxBytes)
                        return TooLarge(context);
                    buffer.Write(block, 0, read);
                }
                return Results.Ok(covers.Import(buffer.ToArray(), context.Request.ContentType));
            }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: 400); }
            catch (BadHttpRequestException error) { return error.StatusCode == 413 ? TooLarge(context) : Results.Problem(error.Message, statusCode: error.StatusCode); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogError(error, "Cover import failed");
                return Results.Problem("The cover could not be saved. Check library storage permissions and available disk space.", statusCode: 503);
            }
        });
        app.MapGet("/api/library/covers/{name}", (string name, HttpContext context, LibraryCoverStore covers) =>
        {
            if (!LibraryCoverStore.IsCoverName(name)) return Results.NotFound();
            try
            {
                var cover = covers.Read(name);
                if (cover is null) return Results.NotFound();
                context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(cover.Bytes, cover.ContentType, enableRangeProcessing: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogError(error, "Cover read failed");
                return Results.Problem("The stored cover is unavailable.", statusCode: 503);
            }
        });
    }

    private static IResult TooLarge(HttpContext context)
    {
        // Do not ask Kestrel to drain an already-rejected request for reuse: draining
        // would hit its body limit and can reset a chunked error response mid-flight.
        if (context.Request.Protocol.StartsWith("HTTP/1.", StringComparison.Ordinal))
            context.Response.Headers.Connection = "close";
        return Results.Text("{\"title\":\"Cover upload too large\",\"status\":413,\"detail\":\"Cover uploads must be at most 5 MB.\"}",
            "application/problem+json", statusCode: 413);
    }
}
