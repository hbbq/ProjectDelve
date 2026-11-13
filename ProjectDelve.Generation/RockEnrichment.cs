using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal static class RockEnricher
{
    internal static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var terrain = new EnrichmentTerrain(context, content); var source = context.OriginalAnalysis!;
        var intensity = ((EnrichmentStepParameters)step.Parameters).Intensity;
        var seed = WorldStepSeed.Derive(context.World.Seed, step.Id, step.Kind);
        // Shared indexed land geometry; Rock keeps its own excavation and water policy.
        var geometry = new ForestGeometry(terrain);
        var cores = new List<int>[source.Formations.Count]; var changed = new int[cores.Length];
        for (var i = 0; i < terrain.Count; i++)
        {
            var f = source.FormationLabels[i];
            if (context.Samples[i].TerrainTypeId != context.OriginalSamples[i].TerrainTypeId) changed[f]++;
            if (context.OriginalSamples[i].TerrainTypeId == "solid-rock") (cores[f] ??= []).Add(i);
        }
        var dry = Enumerable.Range(0, terrain.Count).Select(i => !terrain.Neighbors(i).Any(terrain.OriginalWater)).ToArray();
        var depth = new int[terrain.Count]; var excluded = new int[terrain.Count];
        for (var f = 0; f < cores.Length && geometry.WorkRemaining > 0; f++)
        {
            var formation = source.Formations[f];
            if (formation.Kind != NaturalFormationKind.RockMass || formation.CoreArea < 100 || formation.MaxDepth < 3 || cores[f] is not { } core) continue;
            var remaining = formation.CoreArea * (4 + 3 * intensity) / 100 - changed[f];
            if (remaining < 7) continue;
            bool Core(int i) => source.FormationLabels[i] == f && context.OriginalSamples[i].TerrainTypeId == "solid-rock";
            bool Rock(int i) => Core(i) && context.Samples[i].TerrainTypeId == "solid-rock";
            // Land-passable mixed Water cannot override the explicit water-adjacency exclusion.
            var entries = core.Where(i => dry[i] && context.Samples[i].TerrainTypeId is "solid-rock" or "stone-floor").SelectMany(i => terrain.Neighbors(i)
                .Where(j => !Core(j) && terrain.Passable(j)).Select(j => (Inside: i, Outside: j)))
                .OrderBy(e => e.Inside).ThenBy(e => e.Outside).ToArray();
            if (entries.Length == 0) continue;
            var guard = new RockOpportunityGuard(terrain, core);
            var random = WorldStepSeed.Stream(seed, formation.Id + ":chambers");
            var centers = core.ToList(); random.Shuffle(centers);
            void Exclude(int center, int radius)
            {
                var cx = center % terrain.Width; var cy = center / terrain.Width;
                for (var y = Math.Max(0, cy - radius); y <= Math.Min(context.Window.Height - 1, cy + radius); y++)
                {
                    var dx = radius - Math.Abs(y - cy);
                    for (var x = Math.Max(0, cx - dx); x <= Math.Min(terrain.Width - 1, cx + dx); x++) excluded[y * terrain.Width + x] = f + 1;
                }
            }
            foreach (var o in context.FeatureOpportunities.Where(o => o.FormationId == formation.Id && o.FeatureId is not null)) Exclude(o.AnchorIndex, 7);
            void RefreshDepth()
            {
                Queue<int> queue = new();
                foreach (var i in core) depth[i] = 0;
                foreach (var i in core) if (Rock(i) && (geometry.Neighbors(i).Length < 4 || terrain.Neighbors(i).Any(j => !Rock(j))))
                { depth[i] = 1; queue.Enqueue(i); }
                while (queue.TryDequeue(out var i)) foreach (var j in geometry.Neighbors(i))
                    if (Rock(j) && depth[j] == 0) { depth[j] = depth[i] + 1; queue.Enqueue(j); }
            }
            RefreshDepth();
            var limit = Math.Min(48, 2 + formation.CoreArea / 300); var accepted = 0;
            var attempts = Math.Min(192, 16 + limit * 4);
            for (var attempt = 0; attempt < attempts && accepted < limit && remaining >= 7 && geometry.WorkRemaining > 0; attempt++)
            {
                var center = -1; var best = int.MinValue;
                foreach (var candidate in centers)
                {
                    if (excluded[candidate] == f + 1 || !Rock(candidate) || !dry[candidate] || depth[candidate] < 3) continue;
                    var score = 4 * depth[candidate] + source.Depth[candidate];
                    if (score > best) { best = score; center = candidate; }
                }
                if (center < 0) break;
                Exclude(center, 2);
                var substantial = Math.Min(64, entries.Max(e => geometry.Area(e.Outside)));
                var targets = entries.Where(e => geometry.Area(e.Outside) >= substantial)
                    .GroupBy(e => e.Inside).ToDictionary(g => g.Key, g => g.First());
                // Any earlier accessible tunnel can be reused, not merely the previous chamber.
                var goals = core.Where(i => terrain.Passable(i) && geometry.Area(i) >= substantial).Concat(targets.Keys).ToHashSet();
                bool Interior(int i) => Core(i) && dry[i] && (context.Samples[i].TerrainTypeId is "solid-rock" or "stone-floor") && terrain.Neighbors(i).All(Core);
                bool Allowed(int i) => Interior(i) || targets.ContainsKey(i);
                bool RoomCell(int i) => Rock(i) && depth[i] >= 2 && dry[i] && terrain.Neighbors(i).All(Rock);
                var room = terrain.Blob(center, 1.9 + random.Next(75) / 100.0, random, RoomCell);
                if (room.Count < 7) continue;
                var phase = random.Next(628) / 100.0;
                var tunnel = geometry.Route(center, goals.Contains, Allowed,
                    i => terrain.WindingCost(i, phase, terrain.Passable(i) || room.Contains(i) ? 2 : 10));
                if (tunnel is null) continue;
                var edits = room.Concat(tunnel).Where(Rock).ToHashSet();
                if (edits.Count > remaining) continue;
                List<int>? Shaped(int target, Func<int, bool> reuse, Func<int, bool> allowed)
                {
                    var dx = target % terrain.Width - center % terrain.Width; var dy = target / terrain.Width - center / terrain.Width;
                    var length = Math.Sqrt(dx * dx + dy * dy); var amplitude = Math.Min(3, length * .12);
                    return geometry.Route(center, i => i == target, allowed, i =>
                    {
                        var x = i % terrain.Width - center % terrain.Width; var y = i / terrain.Width - center / terrain.Width;
                        var t = length == 0 ? 0 : Math.Clamp((x * dx + y * dy) / (length * length), 0, 1);
                        var offset = amplitude * Math.Sin(Math.PI * t) * Math.Sin(2 * Math.PI * t + phase);
                        var perpendicular = length == 0 ? 0 : (x * -dy + y * dx) / length;
                        return terrain.WindingCost(i, phase, reuse(i) ? 2 : 10) + (int)Math.Round(Math.Min(40, Math.Abs(perpendicular - offset) * 4));
                    });
                }
                if (tunnel.Count >= 10)
                {
                    var shaped = Shaped(tunnel[^1], i => terrain.Passable(i) || room.Contains(i), i => i == tunnel[^1] || Interior(i));
                    if (shaped is not null && shaped.Count <= tunnel.Count * 1.3 + 4)
                    {
                        var proposal = room.Concat(shaped).Where(Rock).ToHashSet();
                        if (proposal.Count <= remaining) { tunnel = shaped; edits = proposal; }
                    }
                }
                if (!guard.Allows(edits)) continue;
                var selectedEntries = new List<(int Inside, int Outside)>();
                var usefulPass = false;
                if (targets.TryGetValue(tunnel[^1], out var entry)) selectedEntries.Add(entry);
                var touched = edits.SelectMany(terrain.Neighbors).Select(geometry.Root).Where(r => r >= 0).ToHashSet();
                if (random.Next(2) == 0)
                {
                    var alternatives = entries.Where(e => !edits.Contains(e.Inside) && selectedEntries.All(a => terrain.Distance(a.Inside, e.Inside) >= 10))
                        .OrderBy(e => touched.Contains(geometry.Root(e.Outside))).ThenByDescending(e => Math.Min(64, geometry.Area(e.Outside)))
                        .ThenBy(e => terrain.Distance(center, e.Inside)).ThenBy(e => e.Inside).Take(4);
                    foreach (var alternate in alternatives)
                    {
                        var branch = Shaped(alternate.Inside, i => edits.Contains(i) || terrain.Passable(i), i => i == alternate.Inside || Interior(i));
                        if (branch is null) continue;
                        var combined = edits.Concat(branch.Where(Rock)).ToHashSet();
                        if (combined.Count <= edits.Count || combined.Count > remaining) continue;
                        if (touched.Contains(geometry.Root(alternate.Outside)) && geometry.HasShortPath(center, alternate.Outside, branch.Count + 8, edits)) continue;
                        if (!guard.Allows(combined)) continue;
                        edits = combined; selectedEntries.Add(alternate); usefulPass = true; break;
                    }
                }
                terrain.Commit(edits, "stone-floor");
                foreach (var i in edits.Order()) geometry.Add(i);
                var id = step.Id + ":" + formation.Id + ":chamber:" + center;
                var entranceIndices = selectedEntries.Select(e => terrain.AddEntrance(id, e.Inside, e.Outside)).ToArray();
                context.Features.Add(new(id, step.Id, usefulPass ? "cave-with-mountain-pass" : "cave-chamber",
                    formation.Id, terrain.Bounds(edits), edits.Count, entranceIndices));
                context.FeatureOpportunities.Add(new(id + ":room", "cave-chamber", center, terrain.Bounds(room), room.Count, formation.Id, id, -1, false));
                remaining -= edits.Count; accepted++; Exclude(center, 7); RefreshDepth();
            }
        }
    }
}
