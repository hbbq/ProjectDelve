namespace ProjectDelve.Generation;

// Conservative early preference, not construction permission or a promised placement.
// Keep narrow masses' existing capacity; larger masses retain at least two buffered rooms.
internal sealed class RockOpportunityGuard
{
    private readonly EnrichmentTerrain terrain;
    private readonly List<(int[] Cells, int Capacity)> opportunities = [];
    internal RockOpportunityGuard(EnrichmentTerrain terrain, IReadOnlyList<int> core)
    {
        this.terrain = terrain;
        var remaining = core.Where(i => terrain.Context.Samples[i].TerrainTypeId == "solid-rock").ToHashSet();
        while (remaining.Count > 0)
        {
            var mass = Flood(remaining.Min(), remaining);
            var capacity = Capacity(mass);
            if (capacity >= 2) opportunities.Add((mass.Order().ToArray(), capacity <= 4 ? capacity : 2));
        }
    }
    internal bool Allows(IReadOnlySet<int> edits)
    {
        foreach (var original in opportunities)
        {
            if (!original.Cells.Any(edits.Contains)) continue;
            var remaining = original.Cells.Where(i => !edits.Contains(i) && terrain.Context.Samples[i].TerrainTypeId == "solid-rock").ToHashSet();
            var found = false;
            while (remaining.Count > 0)
                if (Capacity(Flood(remaining.Min(), remaining)) >= original.Capacity) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
    private HashSet<int> Flood(int start, HashSet<int> remaining)
    {
        HashSet<int> mass = [start]; Queue<int> queue = new([start]); remaining.Remove(start);
        while (queue.TryDequeue(out var i)) foreach (var j in terrain.Neighbors(i))
            if (remaining.Remove(j)) { mass.Add(j); queue.Enqueue(j); }
        return mass;
    }
    private int Capacity(HashSet<int> mass)
    {
        List<int> centers = [];
        foreach (var i in mass.Order())
        {
            var x = i % terrain.Width; var y = i / terrain.Width;
            if (x < 2 || y < 2 || x + 2 >= terrain.Width || y + 2 >= terrain.Context.Window.Height) continue;
            if (centers.Any(c => Math.Abs(x - c % terrain.Width) <= 3 && Math.Abs(y - c / terrain.Width) <= 3)) continue;
            var fits = true;
            for (var dy = -2; dy <= 2 && fits; dy++) for (var dx = -2; dx <= 2; dx++)
                if (!mass.Contains(i + dy * terrain.Width + dx)) { fits = false; break; }
            if (!fits) continue;
            centers.Add(i);
            if (centers.Count == 5) return 5;
        }
        return centers.Count;
    }
}
