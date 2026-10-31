using ProjectDelve.Engine;

namespace ProjectDelve.Engine.Tests;

// Gameplay tests focus on completed rules operations. Author all test groups as human
// and explicitly submit every intervening dice continuation through the normal API.
internal static class TestGame
{
    internal static GameState WithIsolatedOpponent(GameState state, string sideId = "red")
    {
        var board = state.Physical.Board;
        var expanded = board with { Height = board.Height + 2, Edges = [.. board.Edges] };
        for (var x = 0; x < board.Width; x++)
            expanded.Edges.Add(new(new(x, board.Height - 1), new(x, board.Height), EdgeKind.Wall));
        state.Physical = state.Physical with { Board = expanded };
        state.Types.Add(new("isolated-type", 0, 0, 0, 0, 1));
        state.Units.Add(new("isolated", "isolated-type", sideId, 1));
        state.Physical.Figures.Add(new("isolated", new(0, board.Height + 1)));
        Author(state);
        return state;
    }
    internal static void Author(GameState state)
    {
        foreach (var group in state.Units.Select(u => new ActivationToken(u.TypeId, u.SideId)).Distinct())
            if (!state.Controllers.Any(c => c.Token == group)) state.Controllers.Add(new(group, ControllerKind.Human));
    }
    internal static EngineResult StartRound(GameState state, IRandomProvider random, bool autoChooseSingleRelevantChoice = true)
    {
        state = System.Text.Json.JsonSerializer.Deserialize<GameState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        Author(state);
        return GameEngine.StartRound(state, random, autoChooseSingleRelevantChoice);
    }
    internal static EngineResult Advance(GameState state, IDecisionProvider provider, IRandomProvider random, bool autoChooseSingleRelevantChoice = true)
    {
        state = System.Text.Json.JsonSerializer.Deserialize<GameState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        Author(state);
        var result = GameEngine.Advance(state, provider, random, autoChooseSingleRelevantChoice);
        var events = new List<RulesEvent>(result.Events);
        var steps = new List<ResolutionStep>(result.ResolutionSteps);
        while (result.NextInput?.Kind == DecisionKind.RollDice)
        {
            result = GameEngine.Advance(result.State, new Roll(), random, autoChooseSingleRelevantChoice);
            steps.AddRange(result.ResolutionSteps.Select(s => s with { EventIndex = s.EventIndex + events.Count }));
            events.AddRange(result.Events);
        }
        return result with { Events = events, ResolutionSteps = steps };
    }
    // Additional occurrences do not change the semantics of these existing operation assertions.
    private static bool IsOperation(RulesEvent e) => e.Kind is not ("RoundStarted" or "ActivationStarted" or "ActivationCompleted" or "ActionUsed" or "AttackStarted" or "DiceRolled");
    // A simple Bonus Action's sole operation event is now the generic ActionUsed.
    // Effects with their own outcome events retain those outcomes for these assertions.
    private static bool IsOperation(EngineResult result, RulesEvent e) => IsOperation(e) ||
        !result.Events.Any(IsOperation) && e.Kind == "ActionUsed" && e.Category == ActivationChoiceKind.BonusAction;
    internal static List<RulesEvent> OperationEvents(EngineResult result) => result.Events.Where(e => IsOperation(result, e)).ToList();
    internal static List<ResolutionStep> OperationSteps(EngineResult result) => result.ResolutionSteps
        .Where(s => IsOperation(result, result.Events[s.EventIndex]))
        .Select((s, index) => s with { EventIndex = index }).ToList();
    private sealed class Roll : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => "roll-dice";
    }
}
