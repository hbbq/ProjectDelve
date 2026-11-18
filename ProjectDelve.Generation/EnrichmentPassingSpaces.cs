namespace ProjectDelve.Generation;

// Runs only on trails and tunnels from an uncommitted early enrichment feature.
internal sealed class EnrichmentPassingSpaces(EnrichmentTerrain terrain, HashSet<int> edits,
    int allowance, Func<int, bool> canClear, Func<IReadOnlySet<int>, bool>? preservesRock = null)
{
    private const int LongRun = 8, MaxBays = 3;
    internal int Added { get; private set; }
    internal int Skipped { get; private set; }
    private bool Open(int i) => edits.Contains(i) || terrain.Passable(i);

    internal void Along(IReadOnlyList<int> path)
    {
        for (var start = 0; start < path.Count;)
        {
            if (!ThinDegreeTwo(path[start])) { start++; continue; }
            var end = start + 1;
            while (end < path.Count && ThinDegreeTwo(path[end])) end++;
            // Keep the mouths and several narrow Cells between bays. Try near the
            // middle of each section, with only a few alternatives for a Rock guard.
            while (end - start >= LongRun)
            {
                var section = Math.Min(12, end - start);
                var middle = start + section / 2;
                var installed = false;
                if (Added < MaxBays && edits.Count < allowance)
                    foreach (var i in new[] { middle, middle - 1, middle + 1 })
                    {
                        if (!TryBay(path, i)) continue;
                        start = i + 4;
                        installed = true;
                        break;
                    }
                if (!installed) { Skipped++; start += section; }
            }
            start = end;
        }
    }

    private bool ThinDegreeTwo(int cell) =>
        terrain.Neighbors(cell).Count(Open) == 2 && !Squares(cell).Any(square => square.All(Open));

    private bool TryBay(IReadOnlyList<int> path, int index)
    {
        var center = path[index];
        if (!ThinDegreeTwo(center)) return false;
        var previous = path[index - 1];
        var next = path[index + 1];
        // A three-Cell shoulder gives the middle Cell a bypass. At a bend, a
        // complete 2×2 can still give the two approaching figures a place to pass.
        var delta = center - previous;
        if (next - center == delta)
        {
            var horizontal = previous / terrain.Width == next / terrain.Width;
            foreach (var side in terrain.Neighbors(center).Where(i => i != previous && i != next))
            {
                var a = side - delta;
                var b = side + delta;
                if (!terrain.Neighbors(side).Contains(a) || !terrain.Neighbors(side).Contains(b)) continue;
                if (horizontal && (a / terrain.Width != side / terrain.Width || b / terrain.Width != side / terrain.Width)) continue;
                if (Add([a, side, b])) return true;
            }
        }
        foreach (var square in Squares(center))
            if ((square.Contains(previous) || square.Contains(next)) && Add(square)) return true;
        return false;
    }

    private bool Add(IEnumerable<int> footprint)
    {
        var changes = footprint.Where(i => !Open(i)).ToArray();
        if (changes.Length == 0 || changes.Length + edits.Count > allowance || !changes.All(canClear)) return false;
        edits.UnionWith(changes);
        if (preservesRock is not null && !preservesRock(edits))
        {
            edits.ExceptWith(changes);
            return false;
        }
        Added++;
        return true;
    }

    private IEnumerable<int[]> Squares(int cell)
    {
        var width = terrain.Width;
        var x = cell % width;
        var y = cell / width;
        for (var dy = -1; dy <= 0; dy++)
        for (var dx = -1; dx <= 0; dx++)
        {
            if (x + dx < 0 || x + dx + 1 >= width || y + dy < 0 || y + dy + 1 >= terrain.Context.Window.Height) continue;
            var top = cell + dy * width + dx;
            yield return [top, top + 1, top + width, top + width + 1];
        }
    }
}
