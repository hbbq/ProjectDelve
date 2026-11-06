namespace ProjectDelve.Engine;

/// <summary>Individual physical markers; proximity uses neither movement nor visibility.</summary>
public static class DormantRules
{
    public const int DefaultWakeDistance = 8;
    public const int AlertDistance = 3;

    internal static IReadOnlyList<string> WakingUnits(GameState state, IReadOnlyCollection<string>? attackTargets = null)
    {
        if (!state.Units.Any(u => u.Dormant && u.CurrentHp > 0)) return [];
        var figures = state.Physical.Figures.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var footprints = state.Types.ToDictionary(t => t.Id, t => t.Footprint, StringComparer.Ordinal);
        var living = state.Units.Where(u => u.CurrentHp > 0 && figures.ContainsKey(u.Id))
            .OrderBy(u => figures[u.Id].Position.Y).ThenBy(u => figures[u.Id].Position.X).ThenBy(u => u.Id, StringComparer.Ordinal)
            .Select(u => (Unit: u, Cells: FootprintGeometry.OccupiedCells(footprints[u.TypeId], figures[u.Id].Position))).ToArray();
        var hostiles = attackTargets is not null ? null : living.Select(u => u.Unit.SideId).Distinct(StringComparer.Ordinal)
            .ToDictionary(side => side, side => living.Where(other => state.AreHostile(side, other.Unit.SideId)).ToArray(), StringComparer.Ordinal);
        var reached = new HashSet<int>();
        var pending = new Queue<int>();
        for (var i = 0; i < living.Length; i++)
        {
            var source = living[i];
            if (attackTargets is not null ? attackTargets.Contains(source.Unit.Id) :
                hostiles![source.Unit.SideId].Any(other =>
                    SpatialRules.ManhattanDistance(source.Cells, other.Cells) <= source.Unit.WakeDistance))
            {
                reached.Add(i);
                pending.Enqueue(i);
            }
        }
        if (pending.Count == 0) return [];

        // Per-calculation lookup only: each relay visits the small raw Manhattan diamond
        // around its occupied Cells, rather than scanning all Units again for each link.
        var atCell = new Dictionary<(string Side, Cell Cell), List<int>>();
        for (var i = 0; i < living.Length; i++)
            foreach (var cell in living[i].Cells)
            {
                var key = (living[i].Unit.SideId, cell);
                if (!atCell.TryGetValue(key, out var occupants)) atCell[key] = occupants = [];
                occupants.Add(i);
            }
        while (pending.TryDequeue(out var index))
        {
            var relay = living[index];
            foreach (var cell in relay.Cells)
                for (var dx = -AlertDistance; dx <= AlertDistance; dx++)
                    for (var dy = -(AlertDistance - Math.Abs(dx)); dy <= AlertDistance - Math.Abs(dx); dy++)
                        if (atCell.TryGetValue((relay.Unit.SideId, new(cell.X + dx, cell.Y + dy)), out var neighbors))
                            foreach (var neighbor in neighbors)
                                if (reached.Add(neighbor)) pending.Enqueue(neighbor);
        }
        // Determine the whole network from one boundary state, then emit only marker
        // removals in spatial order. Neither sources nor propagation paths are stored.
        return reached.Order().Where(i => living[i].Unit.Dormant).Select(i => living[i].Unit.Id).ToArray();
    }
}

public static partial class GameEngine
{
    private static void ResolveDormantWaking(GameState state, ResolutionEvents events)
    {
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
