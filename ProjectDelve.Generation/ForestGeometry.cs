namespace ProjectDelve.Generation;

// Decision-local index for the early, sample-only Forest pass. It is never a gameplay graph.
// Construction/Edges/Machines cannot precede this pass (WorldGenerationPlan.Validate).
internal sealed class ForestGeometry
{
    private readonly EnrichmentTerrain terrain;
    private readonly int[] parent, area, costs, previous, stamps, neighbors;
    private readonly byte[] neighborCounts;
    private readonly PriorityQueue<int, (int Cost, int Index)> frontier = new();
    private int stamp;
    internal int WorkRemaining { get; private set; } = 2_000_000;

    internal ForestGeometry(EnrichmentTerrain terrain)
    {
        this.terrain = terrain;
        parent = Enumerable.Repeat(-1, terrain.Count).ToArray();
        area = new int[terrain.Count]; costs = new int[terrain.Count];
        previous = new int[terrain.Count]; stamps = new int[terrain.Count];
        neighbors = new int[terrain.Count * 4]; neighborCounts = new byte[terrain.Count];
        for (var i = 0; i < terrain.Count; i++)
            foreach (var j in terrain.Neighbors(i)) neighbors[i * 4 + neighborCounts[i]++] = j;
        for (var i = 0; i < terrain.Count; i++) if (terrain.Passable(i)) Add(i);
    }

    internal ReadOnlySpan<int> Neighbors(int i) => neighbors.AsSpan(i * 4, neighborCounts[i]);

    internal int Root(int i)
    {
        if (parent[i] < 0) return -1;
        while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
        return i;
    }
    internal int Area(int i) => Root(i) is var root && root >= 0 ? area[root] : 0;
    internal void Add(int i)
    {
        if (parent[i] >= 0) return;
        parent[i] = i; area[i] = 1;
        foreach (var j in Neighbors(i))
        {
            var a = Root(i); var b = Root(j);
            if (b < 0 || a == b) continue;
            // Union by size; cell index settles equal-size ties deterministically.
            if (area[a] < area[b] || area[a] == area[b] && a > b) (a, b) = (b, a);
            parent[b] = a; area[a] += area[b];
        }
    }

    // One multi-target search, rather than a full search for each mouth. Scratch arrays are reused.
    internal List<int>? Route(int from, Func<int, bool> goal, Func<int, bool> allowed, Func<int, int> cost)
    {
        if (WorkRemaining == 0 || !allowed(from)) return null;
        terrain.Context.RoutingSearches++;
        frontier.Clear(); stamp++; stamps[from] = stamp; costs[from] = 0; previous[from] = -1;
        frontier.Enqueue(from, (0, from));
        while (WorkRemaining > 0 && frontier.TryDequeue(out var i, out var priority))
        {
            if (priority.Cost != costs[i]) continue;
            WorkRemaining--; terrain.Context.RoutingVisitedCells++;
            if (goal(i))
            {
                List<int> path = [i];
                while (previous[path[^1]] >= 0) path.Add(previous[path[^1]]);
                path.Reverse(); return path;
            }
            foreach (var j in Neighbors(i))
            {
                if (!allowed(j)) continue;
                var next = costs[i] + cost(j);
                if (stamps[j] == stamp && next >= costs[j]) continue;
                stamps[j] = stamp; costs[j] = next; previous[j] = i;
                frontier.Enqueue(j, (next, j));
            }
        }
        return null;
    }

    // Only answers whether an existing short path defeats a proposed shortcut. It does not
    // exhaust a large component just to measure its full diameter. Shares the stage work cap.
    internal bool HasShortPath(int from, int to, int maxDistance, IReadOnlySet<int> proposed)
    {
        if (WorkRemaining == 0) return true; // No unmeasured benefit on budget exhaustion.
        stamp++; Queue<int> queue = new([from]); stamps[from] = stamp; costs[from] = 0;
        while (queue.TryDequeue(out var i))
        {
            if (WorkRemaining-- <= 0) { WorkRemaining = 0; return true; }
            terrain.Context.RoutingVisitedCells++;
            if (i == to) return true;
            if (costs[i] == maxDistance) continue;
            foreach (var j in Neighbors(i))
                if (stamps[j] != stamp && (terrain.Passable(j) || proposed.Contains(j)))
                { stamps[j] = stamp; costs[j] = costs[i] + 1; queue.Enqueue(j); }
        }
        return false;
    }
}
