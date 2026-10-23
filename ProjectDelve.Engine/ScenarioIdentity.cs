using System.Globalization;

namespace ProjectDelve.Engine;

// Initial identity resolution shared by setup and authoring projections.
public static class ScenarioIdentity
{
    public static IReadOnlyList<string> UnitIds(IReadOnlyList<UnitPlacement> placements, IEnumerable<string> machineIds)
    {
        var reserved = placements.Where(u => u.UnitId is not null).Select(u => u.UnitId!).ToHashSet(StringComparer.Ordinal);
        var machines = machineIds.ToHashSet(StringComparer.Ordinal);
        List<string> ids = [];
        for (var index = 0; index < placements.Count; index++)
        {
            var id = placements[index].UnitId;
            if (id is null)
            {
                var number = index + 1;
                do id = "scenario-unit-" + (number++).ToString(CultureInfo.InvariantCulture);
                while (reserved.Contains(id) || ids.Contains(id) || machines.Contains(id));
            }
            ids.Add(id);
        }
        return ids;
    }
}
