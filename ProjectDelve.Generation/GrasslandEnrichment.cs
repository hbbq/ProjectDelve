using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal static class GrasslandEnricher
{
    internal static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content)
    {
        var terrain = new EnrichmentTerrain(context, content); var source = context.OriginalAnalysis!;
        var current = context.CurrentAnalysis ?? source;
        var intensity = ((EnrichmentStepParameters)step.Parameters).Intensity;
        var seed = WorldStepSeed.Derive(context.World.Seed, step.Id, step.Kind);
        HashSet<int> protectedCells = [.. current.Chokepoints];
        foreach (var entrance in context.Entrances)
            foreach (var i in terrain.Neighbors(entrance.InsideIndex).Concat(terrain.Neighbors(entrance.OutsideIndex)).Append(entrance.InsideIndex).Append(entrance.OutsideIndex)) protectedCells.Add(i);
        foreach (var opportunity in source.Opportunities.Where(o => o.Kind is "clearing" or "island").Concat(context.FeatureOpportunities))
        {
            protectedCells.Add(opportunity.AnchorIndex);
            if (opportunity.OtherBankIndex is { } otherBank) protectedCells.Add(otherBank);
            if (opportunity.Kind is not ("clearing" or "cave-chamber" or "island")) continue;
            for (var y = opportunity.Bounds.Y; y < opportunity.Bounds.Y + opportunity.Bounds.Height; y++)
            for (var x = opportunity.Bounds.X; x < opportunity.Bounds.X + opportunity.Bounds.Width; x++) protectedCells.Add(y * terrain.Width + x);
        }
        var componentCount = terrain.ComponentCount();
        for (var f = 0; f < source.Formations.Count; f++)
        {
            var formation = source.Formations[f];
            if (formation.Kind != NaturalFormationKind.OpenLand || formation.CoreArea < 150) continue;
            var random = WorldStepSeed.Stream(seed, formation.Id + ":patches");
            var core = Enumerable.Range(0, terrain.Count).Where(i => source.FormationLabels[i] == f).ToList();
            var remaining = formation.CoreArea * (2 + intensity * 2) / 100 - terrain.ChangedOriginal(f);
            var obstacleRemaining = formation.CoreArea * (intensity + 1) / 200 - core.Count(i => !terrain.Passable(i));
            random.Shuffle(core); List<int> centers = []; var attempts = 0;
            foreach (var center in core)
            {
                if (centers.Count >= Math.Min(24, 1 + formation.CoreArea / 140) || attempts++ >= 96 || remaining < 3) break;
                if (current.Clearance[center] < 2 || protectedCells.Contains(center) || centers.Any(c => terrain.Distance(c, center) < 8)) continue;
                var draw = random.Next(10); var id = draw < 3 ? "tree" : draw < 5 ? "solid-rock" : "dirt";
                var blocked = id is "tree" or "solid-rock";
                if (blocked && obstacleRemaining < 3) continue;
                bool Ground(int i) => source.FormationLabels[i] == f && !protectedCells.Contains(i) &&
                    context.Samples[i].TerrainTypeId is "grass" or "dirt" && context.Samples[i] == context.OriginalSamples[i];
                var patch = terrain.Blob(center, blocked ? 1.2 + random.Next(60) / 100.0 : 2 + random.Next(100) / 100.0, random, Ground);
                patch.RemoveWhere(i => context.Samples[i].TerrainTypeId == id);
                if (patch.Count < 3 || patch.Count > remaining || blocked && patch.Count > obstacleRemaining) continue;
                // Cluster additions may screen terrain, but must not sever an existing ordinary route.
                var before = patch.ToDictionary(i => i, i => context.Samples[i]); terrain.Commit(patch, id);
                if (blocked)
                {
                    var after = terrain.ComponentCount();
                    if (after > componentCount) { foreach (var pair in before) context.Samples[pair.Key] = pair.Value; continue; }
                    componentCount = after; obstacleRemaining -= patch.Count;
                }
                var kind = id == "tree" ? "tree-pocket" : id == "solid-rock" ? "rock-outcrop" : "dirt-patch";
                var featureId = step.Id + ":" + formation.Id + ":" + kind + ":" + center;
                context.Features.Add(new(featureId, step.Id, kind, formation.Id, terrain.Bounds(patch), patch.Count, []));
                var anchor = blocked ? patch.SelectMany(terrain.Neighbors).Where(terrain.Passable).Order().FirstOrDefault(-1) : center;
                if (anchor >= 0) context.FeatureOpportunities.Add(new(featureId + ":landmark", "natural-landmark", anchor,
                    terrain.Bounds(patch.Append(anchor)), blocked ? 1 : patch.Count, formation.Id, featureId, -1, false));
                centers.Add(center); remaining -= patch.Count;
            }
        }
    }
}
