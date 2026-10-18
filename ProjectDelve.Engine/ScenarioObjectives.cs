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
        CaptureObjectiveStartUnits(state);
    }

    internal static void CaptureObjectiveStartUnits(GameState state)
    {
        foreach (var side in state.Objectives.SelectMany(o => o.Selectors)
                     .Where(s => s.Kind == UnitSelectorKind.SideAtStart).Select(s => s.SideId!).Distinct())
            if (!state.ObjectiveStartUnitIds.ContainsKey(side))
                state.ObjectiveStartUnitIds.Add(side, [.. state.Units.Where(u => u.SideId == side).Select(u => u.Id)]);
    }

    private static bool SelectedObjectiveAchieved(GameState state, SideObjective objective)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selector in objective.Selectors)
        {
            IEnumerable<string> selected = selector.Kind switch
            {
                UnitSelectorKind.Unit => [selector.UnitId!],
                UnitSelectorKind.SideAtStart => state.ObjectiveStartUnitIds[selector.SideId!],
                UnitSelectorKind.Group => state.Units.Where(u => u.TypeId == selector.UnitTypeId && u.SideId == selector.SideId &&
                    (objective.Objective == ScenarioObjective.DefeatUnits ||
                     u.CurrentHp > 0 && !state.DefeatedUnitIds.Contains(u.Id) && state.Physical.Figures.Any(f => f.Id == u.Id))).Select(u => u.Id),
                _ => throw new InvalidOperationException("Unsupported Unit selector.")
            };
            ids.UnionWith(selected);
        }
        if (ids.Count == 0) return false;
        bool Qualifies(string id)
        {
            if (objective.Objective == ScenarioObjective.DefeatUnits) return state.DefeatedUnitIds.Contains(id);
            var unit = state.Units.SingleOrDefault(u => u.Id == id);
            if (unit is null || unit.CurrentHp <= 0 || state.DefeatedUnitIds.Contains(id)) return false;
            var figures = state.Physical.Figures.Where(f => f.Id == id).ToArray();
            if (figures.Length == 0) return false;
            var footprint = state.Types.Single(t => t.Id == unit.TypeId).Footprint;
            var area = objective.Area!;
            return figures.SelectMany(f => FootprintGeometry.OccupiedCells(footprint, f.Position))
                .All(cell => cell.X >= area.X && cell.Y >= area.Y &&
                             (long)cell.X < (long)area.X + area.W && (long)cell.Y < (long)area.Y + area.H);
        }
        return objective.Count == 0 ? ids.All(Qualifies) : ids.Count(Qualifies) >= objective.Count;
    }

    private static void EvaluateObjectives(GameState state, ResolutionEvents events)
    {
        if (state.IsCompleted) return;
        var achieved = state.Objectives.Where(objective => objective.Objective switch
        {
            ScenarioObjective.NoObjective => false,
            ScenarioObjective.EliminateEnemies => !state.Units.Any(unit => unit.CurrentHp > 0 &&
                state.AreHostile(objective.SideId, unit.SideId) && state.Physical.Figures.Any(figure => figure.Id == unit.Id)),
            ScenarioObjective.ReachArea or ScenarioObjective.DefeatUnits => SelectedObjectiveAchieved(state, objective),
            _ => throw new InvalidOperationException("Unsupported Scenario Objective.")
        }).Select(objective => objective.SideId).ToImmutableArray();
        if (achieved.IsEmpty) return;
        state.Completion = new(achieved);
        state.Pending = null;
        events.Add(new RulesEvent("GameCompleted") { Completion = state.Completion, Round = state.Round });
    }
}
