using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    // Materialize defaults for legacy/direct state constructors before resolution can defeat Units.
    private static void EnsureObjectives(GameState state)
    {
        foreach (var side in state.Units.Select(u => u.SideId).Concat(state.Controllers.Select(c => c.Token.SideId))
                     .Distinct(StringComparer.Ordinal).OrderBy(side => side, StringComparer.Ordinal))
            if (!state.Objectives.Any(o => o.SideId == side)) state.Objectives.Add(new(side));
    }

    private static void EvaluateObjectives(GameState state, ResolutionEvents events)
    {
        if (state.IsCompleted) return;
        var achieved = state.Objectives.Where(objective => objective.Objective switch
        {
            ScenarioObjective.EliminateEnemies => !state.Units.Any(unit => unit.CurrentHp > 0 &&
                unit.SideId != objective.SideId && state.Physical.Figures.Any(figure => figure.Id == unit.Id)),
            _ => throw new InvalidOperationException("Unsupported Scenario Objective.")
        }).Select(objective => objective.SideId).ToImmutableArray();
        if (achieved.IsEmpty) return;
        state.Completion = new(achieved);
        state.Pending = null;
        events.Add(new RulesEvent("GameCompleted") { Completion = state.Completion, Round = state.Round });
    }
}
