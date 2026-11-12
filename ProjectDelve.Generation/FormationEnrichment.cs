using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

// Concrete geometry shared by the four authoring transformations. All indices are window-local.
internal sealed class EnrichmentTerrain(WorldGenerationContext context, ContentBundle content)
{
    internal WorldGenerationContext Context => context;
    internal int Width => context.Window.Width;
    internal int Count => context.Samples.Length;
    private readonly Dictionary<string, TerrainDefinition> definitions = content.Terrain.ToDictionary(t => t.Id, StringComparer.Ordinal);
    internal bool Passable(int i) => definitions[context.Samples[i].TerrainTypeId].Passable;
    internal bool Water(int i) => definitions[context.Samples[i].TerrainTypeId].Categories.Contains(TerrainCategories.Water);
    internal bool OriginalWater(int i) => definitions[context.OriginalSamples[i].TerrainTypeId].Categories.Contains(TerrainCategories.Water);
    internal IEnumerable<int> Neighbors(int i)
    {
        if (i >= Width) yield return i - Width;
        if (i % Width > 0) yield return i - 1;
        if (i % Width + 1 < Width) yield return i + 1;
        if (i + Width < Count) yield return i + Width;
    }
    internal int Distance(int a, int b) => Math.Abs(a % Width - b % Width) + Math.Abs(a / Width - b / Width);
    internal CellBounds Bounds(IEnumerable<int> indices)
    {
        var cells = indices.ToArray(); var x = cells.Min(i => i % Width); var y = cells.Min(i => i / Width);
        return new(x, y, cells.Max(i => i % Width) - x + 1, cells.Max(i => i / Width) - y + 1);
    }
    internal bool InBounds(int i, CellBounds b) => i % Width >= b.X && i % Width < b.X + b.Width && i / Width >= b.Y && i / Width < b.Y + b.Height;
    internal List<int>? Route(int from, int to, Func<int, bool> allowed, Func<int, int> cost)
    {
        if (!allowed(from) || !allowed(to)) return null;
        context.RoutingSearches++;
        var costs = Enumerable.Repeat(int.MaxValue, Count).ToArray(); var previous = Enumerable.Repeat(-1, Count).ToArray();
        PriorityQueue<int, (int Cost, int Index)> queue = new(); costs[from] = 0; queue.Enqueue(from, (0, from));
        while (queue.TryDequeue(out var i, out var priority))
        {
            if (priority.Cost != costs[i]) continue;
            context.RoutingVisitedCells++;
            if (i == to)
            {
                List<int> path = [to]; while (path[^1] != from) path.Add(previous[path[^1]]);
                path.Reverse(); return path;
            }
            foreach (var j in Neighbors(i))
            {
                if (!allowed(j)) continue;
                var next = costs[i] + cost(j);
                if (next >= costs[j]) continue;
                costs[j] = next; previous[j] = i; queue.Enqueue(j, (next, j));
            }
        }
        return null;
    }
    internal List<int>? Meander(int from, int to, Func<int, bool> allowed, Func<int, int> cost, Pcg32 random)
    {
        var dx = to % Width - from % Width; var dy = to / Width - from / Width;
        if (Distance(from, to) >= 8 && Math.Max(Math.Abs(dx), Math.Abs(dy)) >= 2 * Math.Min(Math.Abs(dx), Math.Abs(dy)))
        {
            var offset = (random.Next(2) == 0 ? -1 : 1) * (2 + random.Next(3));
            var x = (from % Width + to % Width) / 2 + (Math.Abs(dy) > Math.Abs(dx) ? offset : 0);
            var y = (from / Width + to / Width) / 2 + (Math.Abs(dx) >= Math.Abs(dy) ? offset : 0);
            if (x >= 0 && x < Width && y >= 0 && y < context.Window.Height)
            {
                var bend = y * Width + x;
                if (allowed(bend))
                {
                    var a = Route(from, bend, allowed, cost); var b = a is null ? null : Route(bend, to, allowed, cost);
                    if (a is not null && b is not null) return [.. a, .. b.Skip(1)];
                }
            }
        }
        return Route(from, to, allowed, cost);
    }
    internal HashSet<int> Blob(int center, double radius, Pcg32 random, Func<int, bool> allowed)
    {
        var phase = random.Next(628) / 100.0; var aspect = .8 + random.Next(50) / 100.0;
        var rx = radius * aspect; var ry = radius / aspect; var extent = (int)Math.Ceiling(radius * 1.7);
        HashSet<int> mask = [];
        for (var y = Math.Max(0, center / Width - extent); y <= Math.Min(context.Window.Height - 1, center / Width + extent); y++)
        for (var x = Math.Max(0, center % Width - extent); x <= Math.Min(Width - 1, center % Width + extent); x++)
        {
            var i = y * Width + x; var dx = x - center % Width; var dy = y - center / Width;
            var angle = Math.Atan2(dy, dx);
            if (allowed(i) && dx * dx / (rx * rx) + dy * dy / (ry * ry) <= 1 + .27 * Math.Sin(angle * 3 + phase) + .13 * Math.Sin(angle * 5 - phase)) mask.Add(i);
        }
        // Rasterization can leave tips disconnected. Retain only the center's cardinal component.
        HashSet<int> connected = []; Queue<int> queue = new();
        if (mask.Contains(center)) { connected.Add(center); queue.Enqueue(center); }
        while (queue.TryDequeue(out var i)) foreach (var j in Neighbors(i)) if (mask.Contains(j) && connected.Add(j)) queue.Enqueue(j);
        return connected;
    }
    internal int ChangedOriginal(int formation) => Enumerable.Range(0, Count).Count(i =>
        context.OriginalAnalysis!.FormationLabels[i] == formation && context.Samples[i].TerrainTypeId != context.OriginalSamples[i].TerrainTypeId);
    internal void Commit(IEnumerable<int> cells, string terrain)
    {
        foreach (var i in cells) context.Samples[i] = context.Samples[i] with { TerrainTypeId = terrain };
    }
    internal int AddEntrance(string feature, int inside, int outside)
    {
        if (Distance(inside, outside) != 1 || !Passable(inside) || !Passable(outside)) throw new InvalidOperationException("Enrichment entrance is not traversable.");
        var index = context.Entrances.Count;
        context.Entrances.Add(new(feature + ":entry:" + index, feature, inside, outside, 1)); return index;
    }
    internal int WindingCost(int i, double phase, int baseCost = 10) => baseCost + (int)(9 * (1 + Math.Sin(i % Width * .43 + phase) * Math.Cos(i / Width * .37 - phase)));
    internal int ComponentCount()
    {
        var visited = new bool[Count]; Queue<int> queue = new(); var count = 0;
        for (var start = 0; start < Count; start++)
        {
            if (visited[start] || !Passable(start)) continue;
            count++; visited[start] = true; queue.Enqueue(start);
            while (queue.TryDequeue(out var i)) foreach (var j in Neighbors(i))
                if (!visited[j] && Passable(j)) { visited[j] = true; queue.Enqueue(j); }
        }
        return count;
    }
}

internal static class ForestEnricher
{
    internal static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var terrain = new EnrichmentTerrain(context, content); var original = context.OriginalAnalysis!;
        var intensity = ((EnrichmentStepParameters)step.Parameters).Intensity;
        var seed = WorldStepSeed.Derive(context.World.Seed, step.Id, step.Kind);
        for (var f = 0; f < original.Formations.Count; f++)
        {
            var formation = original.Formations[f];
            if (formation.Kind != NaturalFormationKind.Forest || formation.CoreArea < 64 || formation.MaxDepth < 2) continue;
            var random = WorldStepSeed.Stream(seed, formation.Id + ":clearings");
            var core = Enumerable.Range(0, terrain.Count).Where(i => original.FormationLabels[i] == f && context.OriginalSamples[i].TerrainTypeId == "tree").ToList();
            var remaining = formation.CoreArea * (3 + intensity * 5) / 100 - terrain.ChangedOriginal(f);
            if (remaining < 10) continue;
            var entrances = core.SelectMany(i => terrain.Neighbors(i).Where(j =>
                context.OriginalSamples[j].TerrainTypeId is "grass" or "dirt" && terrain.Passable(j)).Select(j => (Inside: i, Outside: j))).ToList();
            if (entrances.Count == 0) continue;
            random.Shuffle(core); var chosen = new List<int>();
            var centers = core.Where(i => original.Depth[i] >= 2 && context.Samples[i].TerrainTypeId == "tree")
                .OrderByDescending(i => Math.Min(5, original.Depth[i])).ToList();
            var existing = original.Opportunities.Where(o => o.Kind == "clearing" && o.UsableArea >= 4 && o.UsableArea <= 80 &&
                terrain.InBounds(o.AnchorIndex, formation.Bounds) && original.Formations[original.FormationLabels[o.AnchorIndex]].AdjacentFormations.Contains(f))
                .Select(o => o.AnchorIndex).ToList();
            centers.InsertRange(0, existing);
            var limit = Math.Min(6, 1 + formation.CoreArea / 500); var attempts = 0;
            foreach (var center in centers)
            {
                if (chosen.Count >= limit || attempts++ >= 24 || chosen.Any(c => terrain.Distance(c, center) < 9)) continue;
                var featureId = step.Id + ":" + formation.Id + ":clearing:" + center;
                bool Tree(int i) => original.FormationLabels[i] == f && context.Samples[i].TerrainTypeId == "tree";
                var clearing = Tree(center) ? terrain.Blob(center, 1.8 + random.Next(80) / 100.0, random, Tree) : new HashSet<int>();
                if (Tree(center) && clearing.Count < 5) continue;
                bool Allowed(int i) => terrain.InBounds(i, formation.Bounds) && (original.FormationLabels[i] == f ||
                    context.OriginalSamples[i].TerrainTypeId is "grass" or "dirt") &&
                    (context.Samples[i].TerrainTypeId is "tree" or "grass" or "dirt");
                var phase = random.Next(628) / 100.0;
                var entry = entrances.OrderBy(e => terrain.Distance(center, e.Inside)).ThenBy(e => e.Inside).First();
                var path = terrain.Meander(center, entry.Inside, Allowed, i => terrain.WindingCost(i, phase, terrain.Passable(i) ? 3 : 10), random);
                if (path is null) continue;
                var edits = clearing.Concat(path).Where(Tree).ToHashSet();
                if (edits.Count == 0 || edits.Count > remaining) continue;
                var selectedEntries = new List<(int Inside, int Outside)> { entry };
                // A second separated entrance can create a fork or a loop through the exterior.
                if (random.Next(3) == 0)
                {
                    var alternatives = entrances.Where(e => terrain.Distance(e.Inside, entry.Inside) >= 8)
                        .OrderBy(e => terrain.Distance(center, e.Inside)).ThenBy(e => e.Inside).Take(4);
                    foreach (var alternate in alternatives)
                    {
                        var branch = terrain.Meander(center, alternate.Inside, Allowed, i => terrain.WindingCost(i, phase + 1.7, edits.Contains(i) || terrain.Passable(i) ? 3 : 10), random);
                        if (branch is null) continue;
                        var combined = edits.Concat(branch.Where(Tree)).ToHashSet();
                        if (combined.Count > remaining) continue;
                        edits = combined; selectedEntries.Add(alternate); break;
                    }
                }
                terrain.Commit(edits, "dirt");
                // Clearings are grassy spaces, visually distinct from their exposed-soil trails.
                terrain.Commit(clearing, "grass");
                var entryIndices = selectedEntries.Select(e => terrain.AddEntrance(featureId, e.Inside, e.Outside)).ToArray();
                var existingSpace = original.Opportunities.FirstOrDefault(o => o.Kind == "clearing" && o.AnchorIndex == center);
                var usable = clearing.Count > 0 ? clearing : new HashSet<int> { center };
                context.Features.Add(new(featureId, step.Id, "forest-clearing", formation.Id, terrain.Bounds(edits), edits.Count, entryIndices));
                context.FeatureOpportunities.Add(new(featureId + ":space", "clearing", center, existingSpace?.Bounds ?? terrain.Bounds(usable), existingSpace?.UsableArea ?? usable.Count,
                    formation.Id, featureId, -1, false));
                remaining -= edits.Count; chosen.Add(center);
            }
        }
    }
}
