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
            return game.BrowserResponse(game.Snapshot());
        });
        app.MapPost("/api/game/scenario", (ScenarioRequest request, PlaytestGame game) =>
            Mutate(() => game.BrowserResponse(game.SetupScenario(request.ExpectedRevision, request.ScenarioId, request.Controllers, request.ControllerAssignments))));
        app.MapPost("/api/game/scenario/import", (ScenarioImportRequest request, PlaytestGame game) =>
            Mutate(() => game.BrowserResponse(game.SetupImport(request.ExpectedRevision, request.Transport, request.Controllers, request.ControllerAssignments))));
        app.MapPost("/api/game/restart", (RoundRequest request, PlaytestGame game) =>
            Mutate(() => game.BrowserResponse(game.SetupRestart(request.ExpectedRevision, request.Controllers, request.ControllerAssignments))));
        app.MapPost("/api/game/round", (RoundRequest request, PlaytestGame game) =>
            Mutate(() => game.BrowserResponse(game.StartRound(request.ExpectedRevision))));
        app.MapPost("/api/game/decision", (DecisionSubmission request, PlaytestGame game) =>
            Mutate(() => game.BrowserResponse(game.Decide(request.ExpectedRevision, request.CandidateKey, request.Commentary))));
        app.MapPost("/api/game/preferences", (PreferenceRequest request, PlaytestGame game) =>
            Mutate(() => game.BrowserResponse(game.SetRelevanceAutoChoice(request.ExpectedRevision, request.AutoChooseSingleRelevantChoice))));
        app.MapPost("/api/game/commands", (GameCommand request, PlaytestGame game) =>
        {
            try { return Results.Accepted(value: game.Accept(request)); }
            catch (PlaytestRequestException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
        });
        app.MapGet("/api/game/updates", Stream);
        var controller = app.MapGroup("/api/controllers/gambit");
        controller.AddEndpointFilter(async (context, next) =>
        {
            var authorization = context.HttpContext.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || !MatchesSecret(authorization[7..], "PROJECTDELVE_GAMBIT_TOKEN")) return Results.Unauthorized();
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.RequestServices.GetRequiredService<PlaytestGame>().ControllerContact();
            try { return await next(context); }
            catch (PlaytestRequestException error) { return Results.Problem(error.Message, statusCode: error.StatusCode); }
        });
        controller.MapGet("/status", (PlaytestGame game) => game.ControllerState());
        controller.MapGet("/request", (PlaytestGame game) => Results.Json(game.ControllerRequest(), GambitJson.Options));
        controller.MapPost("/admit", (GambitBinding binding, PlaytestGame game) => game.AdmitController(binding));
        controller.MapPost("/selection", (GambitSubmission submission, PlaytestGame game) => game.SubmitController(submission));
        controller.MapGet("/receipts/{id}", (string id, PlaytestGame game) => game.Receipt(id));
        controller.MapPost("/error", (GambitError error, PlaytestGame game) => game.ReportControllerError(error));
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
                    var safeUpdate = game.BrowserUpdate(update);
                    var json = JsonSerializer.Serialize(safeUpdate, options.Value.SerializerOptions);
                    await context.Response.WriteAsync($"id: {update.SessionId}:{update.Sequence}\nevent: update\ndata: {json}\n\n", cancellation);
                    await context.Response.Body.FlushAsync(cancellation);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (IOException) when (cancellation.IsCancellationRequested) { }
    }

    private static bool MatchesSecret(string? supplied, string variable)
    {
        var expected = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied)) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes(expected));
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
    public Dictionary<string,string>? Controllers { get; init; }
    public List<ControllerSelection>? ControllerAssignments { get; init; }
    public required long ExpectedRevision { get; init; }
    public required string ScenarioId { get; init; }
}

public sealed record RoundRequest
{
    public Dictionary<string,string>? Controllers { get; init; }
    public List<ControllerSelection>? ControllerAssignments { get; init; }
    public required long ExpectedRevision { get; init; }
}

public sealed record ScenarioImportRequest
{
    public Dictionary<string,string>? Controllers { get; init; }
    public List<ControllerSelection>? ControllerAssignments { get; init; }
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
    // Presentation metadata, persisted with detached responses, independent of session/RNG.
    public string? VisualSeed { get; init; }
    public IReadOnlyList<DecisionCommentary> Commentaries { get; init; } = [];
    public IReadOnlyList<ScenarioDescription> Scenarios => PlaytestScenarios.Catalog;
    [JsonIgnore] public BrowserPresentation? PresentationOverride { get; init; }
    public BrowserPresentation Presentation => PresentationOverride ?? BrowserProjection.Create(Result);
    public IReadOnlyDictionary<string,string> Controllers { get; init; } = new Dictionary<string,string>();
    public IReadOnlyList<ControllerSelection> ControllerAssignments { get; init; } = [];
    public ControllerStatus? ControllerStatus { get; init; }
    public string? SessionId { get; init; }
}
