using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal static class ForestEnricher
{
    internal static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var terrain = new EnrichmentTerrain(context, content); var original = context.OriginalAnalysis!;
        var intensity = ((EnrichmentStepParameters)step.Parameters).Intensity;
        var seed = WorldStepSeed.Derive(context.World.Seed, step.Id, step.Kind);
        var geometry = new ForestGeometry(terrain);
        // Original membership and cumulative disturbance are indexed once, including repeated steps.
        var cores = new List<int>[original.Formations.Count]; var changed = new int[cores.Length];
        for (var i = 0; i < terrain.Count; i++)
        {
            var f = original.FormationLabels[i];
            if (context.Samples[i].TerrainTypeId != context.OriginalSamples[i].TerrainTypeId) changed[f]++;
            if (context.OriginalSamples[i].TerrainTypeId == "tree") (cores[f] ??= []).Add(i);
        }
        var depth = new int[terrain.Count]; var excluded = new int[terrain.Count];
        for (var f = 0; f < cores.Length && geometry.WorkRemaining > 0; f++)
        {
            var formation = original.Formations[f];
            if (formation.Kind != NaturalFormationKind.Forest || formation.CoreArea < 64 || formation.MaxDepth < 2 || cores[f] is not { } core) continue;
            var remaining = formation.CoreArea * (3 + intensity * 5) / 100 - changed[f];
            if (remaining < 5) continue;
            bool Tree(int i) => original.FormationLabels[i] == f && context.OriginalSamples[i].TerrainTypeId == "tree" && context.Samples[i].TerrainTypeId == "tree";
            bool Allowed(int i) => terrain.InBounds(i, formation.Bounds) && (Tree(i) || terrain.Passable(i));
            List<int>? Trail(int from, int to, double phase, Func<int, bool> reuse)
            {
                var dx = to % terrain.Width - from % terrain.Width; var dy = to / terrain.Width - from / terrain.Width;
                var length = Math.Sqrt(dx * dx + dy * dy);
                var amplitude = length > 8 ? Math.Min(4, length * .12) : 0;
                return geometry.Route(from, i => i == to, Allowed, i =>
                {
                    var guide = 0;
                    if (amplitude > 0)
                    {
                        var x = i % terrain.Width - from % terrain.Width; var y = i / terrain.Width - from / terrain.Width;
                        var t = Math.Clamp((x * dx + y * dy) / (length * length), 0, 1);
                        var offset = amplitude * Math.Sin(Math.PI * t) * Math.Sin(2 * Math.PI * t + phase);
                        var perpendicular = (x * -dy + y * dx) / length;
                        guide = (int)Math.Round(Math.Min(48, Math.Abs(perpendicular - offset) * 4));
                    }
                    return terrain.WindingCost(i, phase, reuse(i) ? 3 : 10) + guide;
                });
            }
            // Current ordinary land permissions include Wet Grass and Rocky Water. Only original
            // Tree Cells may be written; passable approaches (including earlier trails) are reused.
            var entrances = core.SelectMany(i => terrain.Neighbors(i).Where(j => terrain.Passable(j) &&
                context.OriginalSamples[j].TerrainTypeId != "tree").Select(j => (Inside: i, Outside: j)))
                .OrderBy(e => e.Inside).ThenBy(e => e.Outside).ToArray();
            if (entrances.Length == 0) continue;
            var random = WorldStepSeed.Stream(seed, formation.Id + ":clearings");
            var centers = core.ToList(); random.Shuffle(centers);
            var existing = original.Opportunities.Where(o => o.Kind == "clearing" && o.UsableArea is >= 4 and <= 80 &&
                terrain.InBounds(o.AnchorIndex, formation.Bounds) && original.Formations[original.FormationLabels[o.AnchorIndex]].AdjacentFormations.Contains(f))
                .DistinctBy(o => o.AnchorIndex).ToDictionary(o => o.AnchorIndex);
            centers.InsertRange(0, existing.Keys.Order());
            void ExcludeNear(int center, int distance)
            {
                var cx = center % terrain.Width; var cy = center / terrain.Width;
                for (var y = Math.Max(0, cy - distance); y <= Math.Min(context.Window.Height - 1, cy + distance); y++)
                {
                    var dx = distance - Math.Abs(y - cy);
                    for (var x = Math.Max(0, cx - dx); x <= Math.Min(terrain.Width - 1, cx + dx); x++)
                        excluded[y * terrain.Width + x] = f + 1;
                }
            }
            foreach (var o in context.FeatureOpportunities.Where(o => o.FormationId == formation.Id && o.FeatureId is not null))
                ExcludeNear(o.AnchorIndex, 7);
            var limit = Math.Min(64, 2 + formation.CoreArea / 250);
            var attemptLimit = Math.Min(256, 16 + limit * 4); var accepted = 0;

            void RefreshDepth()
            {
                Queue<int> queue = new();
                foreach (var i in core) depth[i] = 0;
                foreach (var i in core)
                {
                    if (!Tree(i)) continue;
                    var boundary = geometry.Neighbors(i).Length < 4;
                    foreach (var j in geometry.Neighbors(i)) if (!Tree(j)) { boundary = true; break; }
                    if (boundary)
                    { depth[i] = 1; queue.Enqueue(i); }
                }
                while (queue.TryDequeue(out var i)) foreach (var j in geometry.Neighbors(i))
                    if (Tree(j) && depth[j] == 0) { depth[j] = depth[i] + 1; queue.Enqueue(j); }
            }
            RefreshDepth();
            for (var attempt = 0; attempt < attemptLimit && accepted < limit && remaining >= 5 && geometry.WorkRemaining > 0; attempt++)
            {
                // Any substantial approach region qualifies, not just the original largest one.
                // In very sparse crops, use the largest available approach as a relative fallback.
                var substantial = Math.Min(64, entrances.Max(e => geometry.Area(e.Outside)));
                var center = -1; var best = int.MinValue;
                foreach (var candidate in centers)
                {
                    if (excluded[candidate] == f + 1) continue;
                    int score;
                    if (existing.TryGetValue(candidate, out var space))
                    {
                        if (!entrances.Any(e => geometry.Root(e.Outside) != geometry.Root(candidate) && geometry.Area(e.Outside) >= substantial)) continue;
                        score = 24 + Math.Min(40, space.UsableArea);
                    }
                    else
                    {
                        if (!Tree(candidate) || depth[candidate] < 2) continue;
                        // No original-depth plateau. Remaining depth steers subsequent features
                        // away from already opened space, with seeded order breaking equal scores.
                        score = depth[candidate] * 4 + original.Depth[candidate];
                    }
                    if (score > best) { best = score; center = candidate; }
                }
                if (center < 0) break;
                ExcludeNear(center, 3);
                var ownRoot = geometry.Root(center);
                var targets = entrances.Where(e => geometry.Area(e.Outside) >= substantial &&
                    (ownRoot < 0 || geometry.Root(e.Outside) != ownRoot))
                    .GroupBy(e => e.Inside).ToDictionary(g => g.Key, g => g.First());
                if (targets.Count == 0) continue;
                var clearing = Tree(center) ? terrain.Blob(center, 1.8 + random.Next(80) / 100.0, random, Tree) : [];
                if (Tree(center) && clearing.Count < 5) continue;
                var phase = random.Next(628) / 100.0;
                var path = geometry.Route(center, targets.ContainsKey, Allowed,
                    i => terrain.WindingCost(i, phase, terrain.Passable(i) || clearing.Contains(i) ? 3 : 10));
                if (path is null) continue;
                var entry = targets[path[^1]];
                var edits = clearing.Concat(path).Where(Tree).ToHashSet();
                if (edits.Count == 0 || edits.Count > remaining) continue;
                // Access chooses the destination first. A bounded soft guide then gives long
                // trails gradual bends, as in ForestTrail route planning. Retain the original
                // connection if shaping cannot fit its budget or would make an excessive detour.
                if (path.Count >= 10)
                {
                    var shaped = Trail(center, entry.Inside, phase, i => terrain.Passable(i) || clearing.Contains(i));
                    if (shaped is not null && shaped.Count <= path.Count * 1.3 + 4)
                    {
                        var shapedEdits = clearing.Concat(shaped).Where(Tree).ToHashSet();
                        if (shapedEdits.Count <= remaining) { path = shaped; edits = shapedEdits; }
                    }
                }
                var selectedEntries = new List<(int Inside, int Outside)> { entry };
                // Incidental contact is real connectivity too. Evaluate against the precommit graph.
                var touched = edits.SelectMany(terrain.Neighbors).Select(geometry.Root).Where(r => r >= 0).ToHashSet();
                if (ownRoot >= 0) touched.Add(ownRoot);
                touched.Add(geometry.Root(entry.Outside));
                if (random.Next(3) == 0)
                {
                    var alternatives = entrances.Where(e => terrain.Distance(e.Inside, entry.Inside) >= 8 &&
                        !edits.Contains(e.Inside)).OrderBy(e => touched.Contains(geometry.Root(e.Outside)))
                        .ThenBy(e => terrain.Distance(center, e.Inside)).ThenBy(e => e.Inside).ThenBy(e => e.Outside).Take(4);
                    foreach (var alternate in alternatives)
                    {
                        var branch = Trail(center, alternate.Inside, phase + 1.7, i => edits.Contains(i) || terrain.Passable(i));
                        if (branch is null) continue;
                        var combined = edits.Concat(branch.Where(Tree)).ToHashSet();
                        if (combined.Count <= edits.Count || combined.Count > remaining) continue;
                        // A second mouth in the same region must shorten an actual existing walk.
                        if (touched.Contains(geometry.Root(alternate.Outside)) &&
                            geometry.HasShortPath(center, alternate.Outside, branch.Count + 8, edits)) continue;
                        edits = combined; selectedEntries.Add(alternate); break;
                    }
                }
                var featureId = step.Id + ":" + formation.Id + ":clearing:" + center;
                terrain.Commit(edits, "dirt"); terrain.Commit(clearing, "grass");
                foreach (var i in edits.Order()) geometry.Add(i);
                var entryIndices = selectedEntries.Select(e => terrain.AddEntrance(featureId, e.Inside, e.Outside)).ToArray();
                existing.TryGetValue(center, out var existingSpace);
                var usable = clearing.Count > 0 ? clearing : new HashSet<int> { center };
                context.Features.Add(new(featureId, step.Id, "forest-clearing", formation.Id, terrain.Bounds(edits), edits.Count, entryIndices));
                context.FeatureOpportunities.Add(new(featureId + ":space", "clearing", center, existingSpace?.Bounds ?? terrain.Bounds(usable),
                    existingSpace?.UsableArea ?? usable.Count, formation.Id, featureId, -1, false));
                remaining -= edits.Count; ExcludeNear(center, 7); accepted++; RefreshDepth();
            }
        }
    }
}
