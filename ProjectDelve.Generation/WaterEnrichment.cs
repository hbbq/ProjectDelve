using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal static class WaterEnricher
{
    internal static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var terrain = new EnrichmentTerrain(context, content); var source = context.OriginalAnalysis!;
        var seed = WorldStepSeed.Derive(context.World.Seed, step.Id, step.Kind);
        var intensity = ((EnrichmentStepParameters)step.Parameters).Intensity;
        for (var f = 0; f < source.Formations.Count; f++)
        {
            var formation = source.Formations[f];
            if (formation.Kind != NaturalFormationKind.WaterBody || formation.CoreArea < 30) continue;
            var random = WorldStepSeed.Stream(seed, formation.Id + ":islands");
            var core = Enumerable.Range(0, terrain.Count).Where(i => source.FormationLabels[i] == f).ToList();
            var remaining = formation.CoreArea * (1 + 2 * intensity) / 100 - terrain.ChangedOriginal(f);
            bool Water(int i) => source.FormationLabels[i] == f && context.Samples[i].TerrainTypeId == "water";
            bool Interior(int i) => Water(i) && source.Depth[i] >= 2 && terrain.Neighbors(i).All(Water);
            if (formation.CoreArea >= 100 && formation.MaxDepth >= 3 && remaining >= 6)
            {
                random.Shuffle(core); List<int> centers = [];
                var candidates = core.Where(i => source.Depth[i] >= 3).OrderByDescending(i => Math.Min(source.Depth[i], 6)).ToList();
                var attempts = 0; var limit = Math.Min(5, 1 + formation.CoreArea / 500);
                foreach (var center in candidates)
                {
                    if (centers.Count >= limit || attempts++ >= 32) break;
                    if (!Interior(center) || centers.Any(c => terrain.Distance(c, center) < 8)) continue;
                    var island = terrain.Blob(center, 1.4 + random.Next(80) / 100.0, random, Interior);
                    if (island.Count < 6 || island.Count > remaining) continue;
                    AddIsland(island, center, "island"); centers.Add(center);
                    // A short, loose chain shares an orientation rather than repeating equally spaced circles.
                    if (random.Next(3) == 0 && remaining >= 6)
                    {
                        var dx = random.Next(2) == 0 ? -1 : 1; var dy = random.Next(3) - 1;
                        var x = center % terrain.Width + dx * (5 + random.Next(3)); var y = center / terrain.Width + dy * (3 + random.Next(3));
                        if (x >= 0 && x < terrain.Width && y >= 0 && y < context.Window.Height)
                        {
                            var next = y * terrain.Width + x;
                            if (source.Depth[next] >= 3 && Interior(next))
                            {
                                var companion = terrain.Blob(next, 1.5, random, Interior);
                                if (companion.Count >= 6 && companion.Count <= remaining) { AddIsland(companion, next, "island-chain"); centers.Add(next); }
                            }
                        }
                    }
                }
                void AddIsland(HashSet<int> island, int center, string kind)
                {
                    terrain.Commit(island, "grass"); remaining -= island.Count;
                    var id = step.Id + ":" + formation.Id + ":island:" + center;
                    context.Features.Add(new(id, step.Id, kind, formation.Id, terrain.Bounds(island), island.Count, []));
                    context.FeatureOpportunities.Add(new(id + ":space", "island", center, terrain.Bounds(island), island.Count, formation.Id, id, -1, true));
                }
            }
            var shores = core.Where(Water).SelectMany(i => terrain.Neighbors(i).Where(terrain.Passable)).Distinct().Order().ToList();
            random.Shuffle(shores); List<int> selected = [];
            foreach (var shore in shores)
            {
                if (selected.Count >= 4) break;
                if (selected.Any(i => terrain.Distance(i, shore) < 10)) continue;
                selected.Add(shore);
                var id = step.Id + ":" + formation.Id + ":shore:" + shore;
                context.FeatureOpportunities.Add(new(id, "shore", shore, terrain.Bounds([shore]), 1, formation.Id, null, -1, false));
            }
            // Candidate only: no Water is made traversable, and these are not claimed entrances.
            var crossings = 0; List<int> banks = [];
            foreach (var from in shores.Order())
            {
                if (crossings >= 4) break;
                foreach (var direction in new[] { 1, terrain.Width })
                {
                    var current = from; var span = 0;
                    while (span <= 8)
                    {
                        var next = current + direction;
                        if (next >= terrain.Count || direction == 1 && next / terrain.Width != from / terrain.Width) break;
                        if (Water(next)) { span++; current = next; continue; }
                        if (span >= 1 && span <= 8 && terrain.Passable(next) && banks.All(i => terrain.Distance(i, from) >= 8))
                        {
                            var id = step.Id + ":" + formation.Id + ":crossing:" + from + ":" + next;
                            context.FeatureOpportunities.Add(new(id, "bridge-site", from, terrain.Bounds([from, next]), 0, formation.Id, null, -1, true)
                                { OtherBankIndex = next, WaterSpan = span });
                            banks.Add(from); crossings++;
                        }
                        break;
                    }
                }
            }
        }
    }
}
