using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record ExplorationCandidate(RouteRequest Request, IReadOnlySet<int> SelectedCut, bool ConnectsRegion);
internal static class ExplorationRoutes
{
    internal static IEnumerable<ExplorationCandidate> Candidates(WorldGenerationContext context, GenerationScope scope, RouteSettings settings)
    {
        var board = scope.Draft; var w = board.Width; var area = board.AllowedArea;
        var network = Enumerable.Range(0, w * board.Height).Where(board.IsRouteCorridor).ToHashSet();
        if (network.Count == 0) yield break;
        var chokepoints = context.CurrentAnalysis!.Chokepoints.ToHashSet();
        var opportunities = (context.CurrentAnalysis!.Opportunities.Concat(context.FeatureOpportunities))
            .Where(o => o.UsableArea >= 12 && o.WalkableComponent >= 0 && !network.Contains(o.AnchorIndex) &&
                network.All(i => context.CurrentAnalysis.WalkableLabels[i] != o.WalkableComponent))
            .OrderByDescending(o => o.UsableArea).ThenBy(o => o.Id, StringComparer.Ordinal).DistinctBy(o => o.WalkableComponent).Take(3).ToArray();
        var all = context.Routes.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        RouteSeed.Stream(context.World.Seed, scope.Path, "alternatives").Shuffle(all);
        foreach (var route in all.Take(6))
        {
            var path = route.Geometry.LocalIndices(w, context.Window).ToArray(); if (path.Length < 16) continue;
            var start = path[path.Length / 5]; var end = path[path.Length * 4 / 5];
            var midpoint = path.Skip(path.Length / 5 + 2).Take(Math.Max(0, path.Length * 3 / 5 - 4))
                .Where(chokepoints.Contains).DefaultIfEmpty(path[path.Length / 2]).First();
            var oldDistance = Distance(board, start, end, network);
            var direct = Math.Abs(start % w - end % w) + Math.Abs(start / w - end / w);
            var important = chokepoints.Contains(midpoint);
            if (oldDistance < 10 || oldDistance < direct * 1.25 && !important) continue;
            if (important)
            {
                var k = Array.IndexOf(path, midpoint);
                start = path[Math.Max(0, k - 8)]; end = path[Math.Min(path.Length - 1, k + 8)];
            }
            var cut = route.Corridor.Indices(w).Where(i => Math.Abs(i % w - midpoint % w) + Math.Abs(i / w - midpoint / w) <= route.RealizedWidth + 1)
                .Where(i => i != start && i != end).ToHashSet();
            var profile = context.OriginalSamples[midpoint].TerrainTypeId == "solid-rock" ? RouteProfileKind.RockPassage : RouteProfileKind.MinorPath;
            yield return new(new("bypass-" + route.Id.Replace('/', '-')[^Math.Min(48, route.Id.Length)..], area,
                new(route.Id + "/bypass-start", [start], RouteEndpointKind.Network), new(route.Id + "/bypass-end", [end], RouteEndpointKind.Network),
                profile, RoutePurpose.ExplorationLink, context.Window,
                RouteRequirements.For(profile, settings.Intensity, settings.WaterCrossings) with { AvoidCells = cut, MaxAttempts = 1 },
                RoutePreferences.For(profile)), cut, false);
        }
        // Chords between different routes expose useful shortcuts even when each route is direct.
        // The candidate set is small and deterministic; a straight isolated road still gets no loop.
        var anchors = all.Take(6).SelectMany(r =>
        {
            var cells = r.Geometry.LocalIndices(w, context.Window).ToArray();
            return new[] { cells[cells.Length / 4], cells[cells.Length * 3 / 4] };
        }).Distinct().Order().ToArray();
        var chords = new List<(int Start, int End, int Saving)>();
        for (var a = 0; a < anchors.Length; a++) for (var b = a + 1; b < anchors.Length; b++)
        {
            var direct = Math.Abs(anchors[a] % w - anchors[b] % w) + Math.Abs(anchors[a] / w - anchors[b] / w);
            if (direct < 12) continue;
            if (!scope.Budget.Spend(network.Count)) yield break;
            var distance = Distance(board, anchors[a], anchors[b], network);
            if (distance > direct * 1.5) chords.Add((anchors[a], anchors[b], distance - direct));
        }
        foreach (var chord in chords.OrderByDescending(c => c.Saving).ThenBy(c => c.Start).ThenBy(c => c.End).Take(3))
            yield return new(new($"chord-{chord.Start}-{chord.End}", area,
                new(scope.Path + "/chord-start", [chord.Start], RouteEndpointKind.Network),
                new(scope.Path + "/chord-end", [chord.End], RouteEndpointKind.Network), RouteProfileKind.MinorPath,
                RoutePurpose.ExplorationLink, context.Window,
                RouteRequirements.For(RouteProfileKind.MinorPath, settings.Intensity, settings.WaterCrossings) with { MaxAttempts = 1 }), new HashSet<int>(), false);
        for (var i = 0; i < opportunities.Length; i++)
        {
            var o = opportunities[i]; var key = "region-" + i;
            yield return new(new(key, area, new(scope.Path + "/network", network, RouteEndpointKind.Network),
                new(o.Id, [o.AnchorIndex]), RouteProfileKind.RockPassage, RoutePurpose.ExplorationLink, context.Window,
                RouteRequirements.For(RouteProfileKind.RockPassage, settings.Intensity, settings.WaterCrossings) with { MaxAttempts = 1 }), new HashSet<int>(), true);
        }
    }

    internal static int Distance(BoardDraft board, int start, int end, IReadOnlySet<int> allowed, IReadOnlySet<int>? excluded = null, PlanningState? proposed = null)
    {
        if (!allowed.Contains(start) || !allowed.Contains(end) || excluded?.Contains(start) == true || excluded?.Contains(end) == true) return -1;
        var distance = new Dictionary<int, int> { [start] = 0 }; var queue = new Queue<int>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var i))
        {
            if (i == end) return distance[i];
            foreach (var j in board.Neighbors(i))
            {
                if (!allowed.Contains(j) || excluded?.Contains(j) == true || distance.ContainsKey(j)) continue;
                var edge = EdgeKey.Between(i, j, board.Width);
                if (!(proposed?.EdgeAt(edge) ?? board.EdgeAt(edge)).Passable()) continue;
                distance[j] = distance[i] + 1; queue.Enqueue(j);
            }
        }
        return -1;
    }

    internal static AlternativeRouteQuality? Quality(BoardDraft before, RoutePlan plan, ExplorationCandidate candidate)
    {
        var oldMask = Enumerable.Range(0, before.Width * before.Height).Where(before.IsRouteCorridor).ToHashSet();
        var addition = plan.Route.Corridor.Indices(before.Width).ToHashSet();
        var fresh = addition.Count(i => !oldMask.Contains(i)); var shared = addition.Count - fresh;
        if (fresh < 6) return null;
        var start = plan.Route.Start.AnchorIndex; var end = plan.Route.End.AnchorIndex;
        var oldDistance = Distance(before, start, end, oldMask);
        var union = oldMask.Concat(addition).ToHashSet();
        var newDistance = Distance(before, start, end, union, proposed: plan.Patch.Result);
        if (newDistance < 0) return null;
        var walkable = Enumerable.Range(0, before.Width * before.Height).Where(before.Passable).ToHashSet();
        var bypasses = candidate.SelectedCut.Count > 0 && oldDistance >= 0 && Distance(before, start, end, oldMask, candidate.SelectedCut) < 0 &&
            Distance(before, start, end, walkable, candidate.SelectedCut) < 0;
        var connectsRegion = candidate.ConnectsRegion && Distance(before, start, end, walkable) < 0;
        var direct = Math.Abs(start % before.Width - end % before.Width) + Math.Abs(start / before.Width - end / before.Width);
        if (!connectsRegion && (plan.Route.Stats.Length > direct * 1.8 + 4 ||
            !(oldDistance >= 0 && oldDistance - newDistance >= Math.Max(3, oldDistance / 10) || bypasses))) return null;
        return new(plan.Route.Id, oldDistance, newDistance, fresh, shared, oldDistance >= 0, bypasses, false)
        { ConnectsRegion = connectsRegion };
    }
}
