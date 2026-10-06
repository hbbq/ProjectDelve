using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// This is host orchestration, not gameplay: the engine computes and validates every choice.
public sealed class PlaytestGame
{
    private readonly object gate = new();
    private readonly DefaultAutomatedProvider automated = new();
    private readonly IRandomProvider random;
    private GameState state;
    private string scenarioId = PlaytestScenarios.DefaultId;
    private long revision;
    private bool autoChooseSingleRelevantChoice = true;

    public PlaytestGame(IRandomProvider random) : this(random, PlaytestScenarios.Create(PlaytestScenarios.DefaultId)) { }

    // Test seam for focused rule fixtures; production setups always come from the catalog.
    internal PlaytestGame(IRandomProvider random, GameState initialState)
    {
        this.random = random;
        state = initialState;
    }

    public GameResponse StartScenario(long expectedRevision, string id)
    {
        lock (gate)
        {
            CheckRevision(expectedRevision);
            if (!PlaytestScenarios.Catalog.Any(s => s.Id == id))
                throw new PlaytestRequestException(400, "Choose an available scenario.");
            state = PlaytestScenarios.Create(id);
            scenarioId = id;
            revision++;
            return Snapshot();
        }
    }

    public GameResponse Restart(long expectedRevision)
    {
        lock (gate) return StartScenario(expectedRevision, scenarioId);
    }

    public GameResponse Snapshot()
    {
        lock (gate) return new(revision, new(state, [], state.Pending), autoChooseSingleRelevantChoice, scenarioId);
    }

    public GameResponse StartRound(long expectedRevision)
    {
        lock (gate)
        {
            CheckRevision(expectedRevision);
            if (state.Round != 0 && !state.RoundComplete)
                throw new PlaytestRequestException(409, "The current round is still active.");
            return Commit(GameEngine.StartRound(state, random, autoChooseSingleRelevantChoice));
        }
    }

    public GameResponse Decide(long expectedRevision, string? key)
    {
        lock (gate)
        {
            CheckRevision(expectedRevision);
            if (state.Pending is null || state.ControllerFor(state.Pending) != ControllerKind.Human)
                throw new PlaytestRequestException(409, "No player decision is pending.");
            EngineResult result;
            try { result = GameEngine.Advance(state, new SubmittedDecisionProvider(key), random, autoChooseSingleRelevantChoice); }
            catch (ArgumentException error) when (error.ParamName == "decisions")
            {
                throw new PlaytestRequestException(400, "Choose one of the supplied candidates, or none when allowed.");
            }
            return Commit(result);
        }
    }

    public GameResponse SetRelevanceAutoChoice(long expectedRevision, bool enabled)
    {
        lock (gate)
        {
            CheckRevision(expectedRevision);
            var result = GameEngine.RefreshChoices(state, random, enabled);
            autoChooseSingleRelevantChoice = enabled;
            return Commit(result);
        }
    }

    private void CheckRevision(long expectedRevision)
    {
        if (expectedRevision != revision)
            throw new PlaytestRequestException(409, "The game changed. Refresh before choosing again.");
    }

    private GameResponse Commit(EngineResult result)
    {
        var events = new List<RulesEvent>(result.Events);
        var steps = new List<ResolutionStep>(result.ResolutionSteps);
        while (result.NextInput is { } pending && result.State.ControllerFor(pending) == ControllerKind.Automated)
        {
            result = GameEngine.Advance(result.State, automated, random, autoChooseSingleRelevantChoice);
            steps.AddRange(result.ResolutionSteps.Select(step => step with { EventIndex = step.EventIndex + events.Count }));
            events.AddRange(result.Events);
        }
        state = result.State;
        revision++;
        return new(revision, result with { Events = events, ResolutionSteps = steps }, autoChooseSingleRelevantChoice, scenarioId);
    }

    private sealed class SubmittedDecisionProvider(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
}

public sealed class PlaytestRequestException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
