using ProjectDelve.Engine;

namespace ProjectDelve.Web.Tests;

// Legacy tests focus on completed rules operations. Author all test groups as human
// and explicitly submit every intervening dice continuation through the normal API.
internal static class TestGame
{
    internal static void Author(GameState state)
    {
        // Test fixtures explicitly select their supporting definitions.
        if (state.ResourceDefinitions.Count == 0) state.ResourceDefinitions = [.. ResourceTypes.All];
        if (state.EquipmentTypes.Count == 0) state.EquipmentTypes = [.. EquipmentCatalog.All];
        if (state.ItemTypes.Count == 0) state.ItemTypes = [.. ItemCatalog.All];
        if (state.WorldCardDefinitions.Count == 0) state.WorldCardDefinitions = [.. WorldCardCatalog.All];
        if (state.LootDeck is {} loot && loot.DrawPile.Concat(loot.DiscardPile).Any() && state.LootCardDefinitions.Count == 0)
            state.LootCardDefinitions = [.. LootCardCatalog.All];
        var available = CanonicalUnitTypes.All.Where(t => !state.Types.Any(existing => existing.Id == t.Id)).Concat(state.Types).ToArray();
        var graph = new ContentDependencyGraph(available.Select(t => new ContentDefinition(t)), state.ResourceDefinitions);
        var required = graph.Closure(state.Types.Select(t => new ContentIdentity(ContentFamily.Unit, t.Id)))
            .Where(d => d.Family == ContentFamily.Unit).Select(d => d.TypeId).ToHashSet();
        state.Types.AddRange(available.Where(t => required.Contains(t.Id) && !state.Types.Any(existing => existing.Id == t.Id)));
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
    internal static List<RulesEvent> OperationEvents(EngineResult result) => result.Events.Where(IsOperation).ToList();
    internal static List<ResolutionStep> OperationSteps(EngineResult result) => result.ResolutionSteps
        .Where(s => IsOperation(result.Events[s.EventIndex]))
        .Select((s, index) => s with { EventIndex = index }).ToList();
    private sealed class Roll : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => "roll-dice";
    }
}
