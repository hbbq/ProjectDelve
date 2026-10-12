using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public static class PlaytestHost
{
    public static WebApplication Build(string[] args, IRandomProvider? random = null)
        => Build(args, random, null);

    internal static WebApplication Build(string[] args, IRandomProvider? random, GameState? initialState,
        Func<ExternalDecisionContext, CancellationToken, Task<string?>>? external = null, int replayCapacity = 256,
        Func<ExternalDecisionContext, CancellationToken, Task<ExternalDecisionSelection>>? externalSelection = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        var randomProvider = random ?? new SystemRandomProvider();
        builder.Services.AddSingleton<PlaytestGame>(_ => new PlaytestGame(randomProvider,
            initialState ?? PlaytestScenarios.Create(PlaytestScenarios.DefaultId), external, replayCapacity, externalSelection));
        var app = builder.Build();
        app.Lifetime.ApplicationStopping.Register(app.Services.GetRequiredService<PlaytestGame>().Dispose);
        app.UseDefaultFiles();
        app.UseStaticFiles();
        DesignerApi.Map(app);
        app.MapGet("/api/game", (PlaytestGame game, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return game.Snapshot();
        });
        app.MapPost("/api/game/scenario", (ScenarioRequest request, PlaytestGame game) =>
            Mutate(() => game.StartScenario(request.ExpectedRevision, request.ScenarioId)));
        app.MapPost("/api/game/scenario/import", (ScenarioImportRequest request, PlaytestGame game) =>
            Mutate(() => game.StartTransportScenario(request.ExpectedRevision, request.Transport)));
        app.MapPost("/api/game/restart", (RoundRequest request, PlaytestGame game) =>
            Mutate(() => game.Restart(request.ExpectedRevision)));
        app.MapPost("/api/game/round", (RoundRequest request, PlaytestGame game) =>
            Mutate(() => game.StartRound(request.ExpectedRevision)));
        app.MapPost("/api/game/decision", (DecisionSubmission request, PlaytestGame game) =>
            Mutate(() => game.Decide(request.ExpectedRevision, request.CandidateKey, request.Commentary)));
        app.MapPost("/api/game/preferences", (PreferenceRequest request, PlaytestGame game) =>
            Mutate(() => game.SetRelevanceAutoChoice(request.ExpectedRevision, request.AutoChooseSingleRelevantChoice)));
        app.MapPost("/api/game/commands", (GameCommand request, PlaytestGame game) =>
        {
            try { return Results.Accepted(value: game.Accept(request)); }
            catch (PlaytestRequestException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
        });
        app.MapGet("/api/game/updates", Stream);
        return app;
    }

    private static async Task Stream(PlaytestGame game, HttpContext context,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> options)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        using var subscription = game.Subscribe(context.Request.Headers["Last-Event-ID"].FirstOrDefault());
        var cancellation = context.RequestAborted;
        try
        {
            await context.Response.WriteAsync(": connected\n\n", cancellation);
            await context.Response.Body.FlushAsync(cancellation);
            while (!cancellation.IsCancellationRequested)
            {
                var ready = subscription.Reader.WaitToReadAsync(cancellation).AsTask();
                while (!ready.IsCompleted)
                {
                    using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    var timer = Task.Delay(TimeSpan.FromSeconds(15), heartbeat.Token);
                    if (await Task.WhenAny(ready, timer) == ready) { await heartbeat.CancelAsync(); break; }
                    await context.Response.WriteAsync(": heartbeat\n\n", cancellation);
                    await context.Response.Body.FlushAsync(cancellation);
                }
                if (!await ready) break;
                while (subscription.Reader.TryRead(out var update))
                {
                    var json = JsonSerializer.Serialize(update, options.Value.SerializerOptions);
                    await context.Response.WriteAsync($"id: {update.SessionId}:{update.Sequence}\nevent: update\ndata: {json}\n\n", cancellation);
                    await context.Response.Body.FlushAsync(cancellation);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (IOException) when (cancellation.IsCancellationRequested) { }
    }

    private static IResult Mutate(Func<GameResponse> action)
    {
        try { return Results.Ok(action()); }
        catch (PlaytestRequestException error)
        {
            return Results.Problem(error.Message, statusCode: error.StatusCode);
        }
    }
}

public sealed record ScenarioRequest
{
    public required long ExpectedRevision { get; init; }
    public required string ScenarioId { get; init; }
}

public sealed record RoundRequest
{
    public required long ExpectedRevision { get; init; }
}

public sealed record ScenarioImportRequest
{
    public required long ExpectedRevision { get; init; }
    public required string Transport { get; init; }
}

public sealed record DecisionSubmission
{
    public required long ExpectedRevision { get; init; }
    public required string? CandidateKey { get; init; }
    public string? Commentary { get; init; }
}

public sealed record PreferenceRequest
{
    public required long ExpectedRevision { get; init; }
    public required bool AutoChooseSingleRelevantChoice { get; init; }
}

public sealed record GameResponse(long Revision, EngineResult Result, bool AutoChooseSingleRelevantChoice = true, string ScenarioId = PlaytestScenarios.DefaultId)
{
    public IReadOnlyList<DecisionCommentary> Commentaries { get; init; } = [];
    public IReadOnlyList<ScenarioDescription> Scenarios => PlaytestScenarios.Catalog;
    public BrowserPresentation Presentation => BrowserProjection.Create(Result);
}
