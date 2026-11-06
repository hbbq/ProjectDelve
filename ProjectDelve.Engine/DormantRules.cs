namespace ProjectDelve.Engine;

/// <summary>Individual physical markers; proximity uses neither movement nor visibility.</summary>
public static class DormantRules
{
    public const int DefaultWakeDistance = 8;

    internal static IReadOnlyList<string> WakingUnits(GameState state)
    {
        if (!state.Units.Any(u => u.Dormant && u.CurrentHp > 0)) return [];
        var figures = state.Physical.Figures.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var footprints = state.Types.ToDictionary(t => t.Id, t => t.Footprint, StringComparer.Ordinal);
        var living = state.Units.Where(u => u.CurrentHp > 0 && figures.ContainsKey(u.Id))
            .Select(u => (Unit: u, Cells: FootprintGeometry.OccupiedCells(footprints[u.TypeId], figures[u.Id].Position))).ToArray();
        var hostiles = living.Where(u => u.Unit.Dormant).Select(u => u.Unit.SideId).Distinct(StringComparer.Ordinal)
            .ToDictionary(side => side, side => living.Where(other => state.AreHostile(side, other.Unit.SideId)).ToArray(), StringComparer.Ordinal);
        return living.Where(sleeper => sleeper.Unit.Dormant && hostiles[sleeper.Unit.SideId].Any(other =>
                SpatialRules.ManhattanDistance(sleeper.Cells, other.Cells) <= sleeper.Unit.WakeDistance))
            .OrderBy(sleeper => figures[sleeper.Unit.Id].Position.Y)
            .ThenBy(sleeper => figures[sleeper.Unit.Id].Position.X)
            .ThenBy(sleeper => sleeper.Unit.Id, StringComparer.Ordinal)
            .Select(sleeper => sleeper.Unit.Id).ToArray();
    }
}

public static partial class GameEngine
{
    private static void ResolveDormantWaking(GameState state, ResolutionEvents events)
    {
        // Determine every recipient from the same boundary state before removing markers.
        foreach (var id in DormantRules.WakingUnits(state)) WakeUnit(state, id, events, "proximity");
    }

    private static void WakeUnit(GameState state, string id, ResolutionEvents events, string reason)
    {
        var index = state.Units.FindIndex(u => u.Id == id);
        if (index < 0 || !state.Units[index].Dormant || state.Units[index].CurrentHp <= 0) return;
        state.Units[index] = state.Units[index] with { Dormant = false };
        events.Add(new RulesEvent("UnitWoken", id) { ActionId = reason });
    }
}
