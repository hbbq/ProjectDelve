using ProjectDelve.Engine;
using System.Text.Json;

namespace ProjectDelve.Web;

// This is host orchestration, not gameplay: the engine computes and validates every choice.
public sealed class PlaytestGame
{
    private readonly object gate = new();
    private readonly DefaultAutomatedProvider automated = new();
    private readonly IRandomProvider random;
    private GameState state;
    private string scenarioId = PlaytestScenarios.DefaultId;
    private ScenarioDefinition scenarioDefinition = PlaytestScenarios.Definition(PlaytestScenarios.DefaultId);
    private long revision;
    private bool autoChooseSingleRelevantChoice = true;

    public PlaytestGame(IRandomProvider random) : this(random, PlaytestScenarios.Create(PlaytestScenarios.DefaultId)) { }

    // Test seam for focused rule fixtures.
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
            return StartDefinition(PlaytestScenarios.Definition(id), id);
        }
    }

    public GameResponse StartTransportScenario(long expectedRevision, string transport)
    {
        lock (gate)
        {
            CheckRevision(expectedRevision);
            ScenarioDefinition definition;
            try { definition = ScenarioDefinitionTransport.Decode(transport, DesignerApi.CheckHostLimits); }
            catch (DesignerBoardLimitException error)
            {
                throw new PlaytestRequestException(400, error.Message);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or JsonException)
            {
                throw new PlaytestRequestException(400, "Could not load scenario. Paste a valid DELVE1 scenario string.");
            }
            return StartDefinition(definition, "imported");
        }
    }

    // Both sources enter the same round-zero creation boundary before replacing the game.
    private GameResponse StartDefinition(ScenarioDefinition definition, string id)
    {
        var initialState = GameEngine.CreateGame(definition);
        state = initialState;
        scenarioDefinition = definition;
        scenarioId = id;
        revision++;
        return Snapshot();
    }

    public GameResponse Restart(long expectedRevision)
    {
        lock (gate)
        {
            CheckRevision(expectedRevision);
            return StartDefinition(scenarioDefinition, scenarioId);
        }
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
