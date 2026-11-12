using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal static class RockEnricher
{
    internal static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var terrain = new EnrichmentTerrain(context, content); var source = context.OriginalAnalysis!;
        var intensity = ((EnrichmentStepParameters)step.Parameters).Intensity;
        var seed = WorldStepSeed.Derive(context.World.Seed, step.Id, step.Kind);
        for (var f = 0; f < source.Formations.Count; f++)
        {
            var formation = source.Formations[f];
            if (formation.Kind != NaturalFormationKind.RockMass || formation.CoreArea < 100 || formation.MaxDepth < 3) continue;
            var random = WorldStepSeed.Stream(seed, formation.Id + ":chambers");
            var remaining = formation.CoreArea * (4 + 3 * intensity) / 100 - terrain.ChangedOriginal(f);
            if (remaining < 12) continue;
            bool Core(int i) => source.FormationLabels[i] == f && context.OriginalSamples[i].TerrainTypeId == "solid-rock";
            bool Dry(int i) => !terrain.Neighbors(i).Any(terrain.OriginalWater);
            var core = Enumerable.Range(0, terrain.Count).Where(Core).ToList();
            var entries = core.Where(i => Dry(i) && context.Samples[i].TerrainTypeId == "solid-rock")
                .SelectMany(i => terrain.Neighbors(i).Where(j => context.OriginalSamples[j].TerrainTypeId is "grass" or "dirt" && terrain.Passable(j))
                .Select(j => (Inside: i, Outside: j))).ToList();
            if (entries.Count == 0) continue;
            random.Shuffle(core);
            var centers = core.Where(i => source.Depth[i] >= 3).OrderByDescending(i => Math.Min(6, source.Depth[i])).ToList();
            List<int> chambers = []; var attempts = 0;
            foreach (var center in centers)
            {
                if (chambers.Count >= Math.Min(5, 1 + formation.CoreArea / 650) || attempts++ >= 28) break;
                if (chambers.Any(c => terrain.Distance(c, center) < 8) || context.Samples[center].TerrainTypeId != "solid-rock") continue;
                bool RoomCell(int i) => Core(i) && source.Depth[i] >= 2 && Dry(i) && context.Samples[i].TerrainTypeId == "solid-rock" &&
                    terrain.Neighbors(i).All(j => context.Samples[j].TerrainTypeId == "solid-rock");
                var room = terrain.Blob(center, 1.9 + random.Next(75) / 100.0, random, RoomCell);
                if (room.Count < 7) continue;
                var entry = entries.Where(e => context.Samples[e.Inside].TerrainTypeId == "solid-rock")
                    .OrderBy(e => terrain.Distance(center, e.Inside)).ThenBy(e => e.Inside).FirstOrDefault((Inside: -1, Outside: -1));
                if (entry.Inside < 0) break;
                bool Allowed(int i, int endpoint) => Core(i) && Dry(i) &&
                    (context.Samples[i].TerrainTypeId is "solid-rock" or "stone-floor") &&
                    (i == endpoint || terrain.Neighbors(i).All(j => Core(j) || context.OriginalSamples[j].TerrainTypeId == "solid-rock"));
                var phase = random.Next(628) / 100.0;
                var target = chambers.Count == 0 ? entry.Inside : chambers[^1];
                var tunnel = terrain.Meander(center, target, i => Allowed(i, target),
                    i => terrain.WindingCost(i, phase, context.Samples[i].TerrainTypeId == "stone-floor" ? 2 : 10), random);
                if (tunnel is null) continue;
                var edits = room.Concat(tunnel).Where(i => context.Samples[i].TerrainTypeId == "solid-rock").ToHashSet();
                if (edits.Count > remaining) continue;
                var selectedEntries = new List<(int Inside, int Outside)>();
                if (chambers.Count == 0) selectedEntries.Add(entry);
                // Occasionally a separate mouth makes a genuine pass through the massif.
                if (chambers.Count == 0 && random.Next(3) == 0 || chambers.Count > 0 && random.Next(2) == 0)
                {
                    foreach (var alternate in entries.Where(e => selectedEntries.All(a => terrain.Distance(a.Inside, e.Inside) >= 10))
                        .OrderBy(e => terrain.Distance(center, e.Inside)).ThenBy(e => e.Inside).Take(5))
                    {
                        var branch = terrain.Meander(center, alternate.Inside, i => Allowed(i, alternate.Inside),
                            i => terrain.WindingCost(i, phase + 2, edits.Contains(i) || terrain.Passable(i) ? 2 : 10), random);
                        if (branch is null) continue;
                        var combined = edits.Concat(branch.Where(i => context.Samples[i].TerrainTypeId == "solid-rock")).ToHashSet();
                        if (combined.Count > remaining) continue;
                        edits = combined; selectedEntries.Add(alternate); break;
                    }
                }
                terrain.Commit(edits, "stone-floor");
                var id = step.Id + ":" + formation.Id + ":chamber:" + center;
                var entranceIndices = selectedEntries.Select(e => terrain.AddEntrance(id, e.Inside, e.Outside)).ToArray();
                context.Features.Add(new(id, step.Id, selectedEntries.Count > 1 ? "cave-with-mountain-pass" : "cave-chamber",
                    formation.Id, terrain.Bounds(edits), edits.Count, entranceIndices));
                context.FeatureOpportunities.Add(new(id + ":room", "cave-chamber", center, terrain.Bounds(room), room.Count, formation.Id, id, -1, false));
                remaining -= edits.Count; chambers.Add(center);
            }
        }
    }
}
