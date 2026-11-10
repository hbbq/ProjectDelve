using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

// Authoring diagnostics only. Runtime Alert remains exclusively an Engine rule.
public static class PopulationTopology
{
    public static IReadOnlyList<PopulationAlertComponent> Components(IEnumerable<UnitPlacement> source, IEnumerable<UnitType> types)
    {
        var footprints = types.ToDictionary(t => t.Id, t => t.Footprint, StringComparer.Ordinal);
        var units = source.OrderBy(u => u.Anchor.Y).ThenBy(u => u.Anchor.X).ThenBy(u => u.UnitId, StringComparer.Ordinal).ToArray();
        var cells = units.Select(u => FootprintGeometry.OccupiedCells(footprints[u.UnitTypeId], u.Anchor)).ToArray();
        var at = new Dictionary<(string Side, Cell Cell), List<int>>();
        for (var i = 0; i < units.Length; i++) foreach (var cell in cells[i])
        {
            if (!at.TryGetValue((units[i].SideId, cell), out var occupants)) at.Add((units[i].SideId, cell), occupants = []);
            occupants.Add(i);
        }
        HashSet<int> seen = []; List<PopulationAlertComponent> result = [];
        for (var start = 0; start < units.Length; start++)
        {
            if (!seen.Add(start)) continue;
            List<string> ids = []; Queue<int> queue = new(); queue.Enqueue(start);
            while (queue.TryDequeue(out var i))
            {
                ids.Add(units[i].UnitId!);
                foreach (var cell in cells[i])
                    for (var dx = -DormantRules.AlertDistance; dx <= DormantRules.AlertDistance; dx++)
                        for (var dy = -DormantRules.AlertDistance + Math.Abs(dx); dy <= DormantRules.AlertDistance - Math.Abs(dx); dy++)
                            if (at.TryGetValue((units[i].SideId, new(cell.X + dx, cell.Y + dy)), out var neighbors))
                                foreach (var neighbor in neighbors) if (seen.Add(neighbor)) queue.Enqueue(neighbor);
            }
            result.Add(new(result.Count, ids));
        }
        return result;
    }
}
