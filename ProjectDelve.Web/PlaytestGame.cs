using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// This is host orchestration, not gameplay: the engine computes and validates every choice.
public sealed class PlaytestGame(IRandomProvider random)
{
    private readonly object gate = new();
    private readonly DefaultMonsterProvider monsters = new();
    private GameState state = ExploratoryScenario.Create();
    private long revision;
    private bool autoChooseSingleRelevantChoice = true;

    public GameResponse Snapshot()
    {
        lock (gate) return new(revision, new(state, [], state.Pending), autoChooseSingleRelevantChoice);
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
            if (state.Pending is null || state.Pending.TypeId is not ("barbarian-type" or "rogue-type" or "cleric-type" or "wizard-type"))
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
        while (result.NextInput?.TypeId is "grunt-type" or "zombie-type" or "skeleton-archer-type" or "goblin-type" or "troll-type")
        {
            result = GameEngine.Advance(result.State, monsters, random, autoChooseSingleRelevantChoice);
            steps.AddRange(result.ResolutionSteps.Select(step => step with { EventIndex = step.EventIndex + events.Count }));
            events.AddRange(result.Events);
        }
        state = result.State;
        revision++;
        return new(revision, result with { Events = events, ResolutionSteps = steps }, autoChooseSingleRelevantChoice);
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
