using System.Text.Json.Serialization;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public static class PlaytestHost
{
    public static WebApplication Build(string[] args, IRandomProvider? random = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton(new PlaytestGame(random ?? new SystemRandomProvider()));
        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapGet("/api/game", (PlaytestGame game, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return game.Snapshot();
        });
        app.MapPost("/api/game/round", (RoundRequest request, PlaytestGame game) =>
            Mutate(() => game.StartRound(request.ExpectedRevision)));
        app.MapPost("/api/game/decision", (DecisionSubmission request, PlaytestGame game) =>
            Mutate(() => game.Decide(request.ExpectedRevision, request.CandidateKey)));
        app.MapPost("/api/game/preferences", (PreferenceRequest request, PlaytestGame game) =>
            Mutate(() => game.SetRelevanceAutoChoice(request.ExpectedRevision, request.AutoChooseSingleRelevantChoice)));
        return app;
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

public sealed record RoundRequest
{
    public required long ExpectedRevision { get; init; }
}

public sealed record DecisionSubmission
{
    public required long ExpectedRevision { get; init; }
    public required string? CandidateKey { get; init; }
}

public sealed record PreferenceRequest
{
    public required long ExpectedRevision { get; init; }
    public required bool AutoChooseSingleRelevantChoice { get; init; }
}

public sealed record GameResponse(long Revision, EngineResult Result, bool AutoChooseSingleRelevantChoice = true);
