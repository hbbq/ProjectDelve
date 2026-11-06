using ProjectDelve.Engine;
using System.Diagnostics;

namespace ProjectDelve.Generation;

/// <summary>Bounded, deterministic cardinal corridor planning. All changes remain in the returned private patch.</summary>
public static class RouteGenerator
{
    public static PlacementResult<RoutePlan> Plan(RouteRequest request, GenerationScope scope)
    {
        var searchClock = Stopwatch.StartNew();
        string id;
        try { id = scope.ObjectId(request.Key); }
        catch (ArgumentException e) { return new Rejected<RoutePlan>(new(request.Key, "Route", PlacementFailureCode.InvalidRequest, e.Message, 0)); }
        var attemptsBefore = scope.Budget.Attempts;
        Rejected<RoutePlan> Fail(PlacementFailureCode code, string reason) => new(new(id, "Route", code, reason, scope.Budget.Attempts - attemptsBefore));
        var r = request.Requirements ?? RouteRequirements.For(request.ProfileKind);
        var prefs = request.Preferences ?? RoutePreferences.For(request.ProfileKind);
        var policy = r.Preparation ?? RouteRequirements.For(request.ProfileKind).Preparation!;
        if (!Enum.IsDefined(request.ProfileKind) || !Enum.IsDefined(request.Purpose) || !SiteGeometry.Fits(request.Area, scope.Draft.AllowedArea) ||
            r.Width is < 1 or > 3 || r.RequiredFootprint is < 1 or > 2 || r.Width < r.RequiredFootprint || r.MaxLength < 1 ||
            r.MaxAttempts is < 1 or > 3 || r.MaxExpandedStates < 1 || r.MaxWaterCells < 0 || r.MaxWaterRun < 0 ||
            prefs.TurnPenalty is < 0 or > 10000 || prefs.VariationWeight is < 0 or > 10000 ||
            request.Start.Cells.Count == 0 || request.Destination.Cells.Count == 0)
            return Fail(PlacementFailureCode.InvalidRequest, "Route requires valid geometry, endpoints and bounded search allowances.");
        try
        {
            policy.Validate();
            if (request.Window is { } window)
            {
                window.Validate();
                if (window.Width != scope.Board.Width || window.Height != scope.Board.Height)
                    return Fail(PlacementFailureCode.InvalidRequest, "Route world window must match the planning board dimensions.");
            }
        }
        catch (ArgumentException e) { return Fail(PlacementFailureCode.InvalidRequest, e.Message); }
        var wantedSurface = r.SurfaceTypeId ?? (request.ProfileKind is RouteProfileKind.ForestTrail or RouteProfileKind.MinorPath ? "dirt" :
            request.ProfileKind == RouteProfileKind.Road ? "gravel" : "stone-floor");
        var surfaceCandidates = r.SurfaceTypeId is null ? new[] { wantedSurface, "stone-floor", "dirt", "grass" }.Distinct() : [wantedSurface];
        var surface = surfaceCandidates.Select(id => scope.Content.Terrain.FirstOrDefault(t => t.Id == id && t.Passable &&
            t.Assembly is null && t.Production is null && t.LegacyKind != TerrainKind.Rail)).FirstOrDefault(t => t is not null);
        if (surface is null) return Fail(PlacementFailureCode.InvalidContent, "Requested route surface must be available passable ground without fixed components, production or directional Rail requirements.");
        var floor = surface.Id;
        var n = checked(scope.Board.Width * scope.Board.Height); var width = scope.Board.Width;
        if (request.Start.Cells.Concat(request.Destination.Cells).Any(i => i < 0 || i >= n))
            return Fail(PlacementFailureCode.InvalidRequest, "Route endpoints must be on the board.");
        var hardAvoid = (r.AvoidCells ?? []).ToHashSet(); var softAvoid = (prefs.AvoidCells ?? []).ToHashSet();
        var preferred = (prefs.PreferredCells ?? []).ToHashSet();
        var totalExpanded = 0; var queuePeak = 0;
        PlacementFailureCode lastCode = PlacementFailureCode.NoFootprint; var lastReason = "No permitted corridor joins the endpoint sets.";
        for (var attempt = 0; attempt < r.MaxAttempts; attempt++)
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Shared route attempt budget exhausted.");
            var draft = scope.Draft.Fork(request.Area, id);
            try { draft.EnableRoutePlanning(request.AccessGrants); }
            catch (ArgumentException e) { return Fail(PlacementFailureCode.InvalidRequest, e.Message); }
            var terrain = new string[n]; var passable = new bool[n];
            var definitions = draft.Content.Terrain.ToDictionary(t => t.Id, StringComparer.Ordinal);
            if (!scope.Budget.Spend(n)) return Fail(PlacementFailureCode.BudgetExhausted, "Shared route indexing budget exhausted.");
            for (var i = 0; i < n; i++) { terrain[i] = draft.TerrainAt(i); passable[i] = definitions[terrain[i]].Passable; }
            IEnumerable<int> Square(int anchor)
            {
                for (var y = 0; y < r.Width; y++) for (var x = 0; x < r.Width; x++) yield return anchor + y * width + x;
            }
            bool CellAllowed(int i)
            {
                if (hardAvoid.Contains(i) || !draft.CanRouteThrough(i)) return false;
                if (passable[i]) return true;
                if (!draft.CanWrite(i)) return false;
                return terrain[i] switch
                {
                    "tree" or "fruit-tree" => policy.ClearTrees,
                    "solid-rock" => policy.ExcavateRock,
                    "water" => policy.FillWater && r.MaxWaterCells > 0 && r.MaxWaterRun > 0,
                    _ => false
                };
            }
            bool EdgeAllowed(EdgeKey key, bool internalEdge = false)
            {
                var kind = draft.EdgeAt(key);
                if (kind.Passable()) return true;
                if (!internalEdge && kind == EdgeKind.ClosedDoor && r.ClosedDoorsTraversable) return true;
                if (!policy.ReplaceExistingEdges) return false;
                var end = key.Index + (key.Direction == EdgeDirection.Right ? 1 : width);
                foreach (var i in new[] { key.Index, end })
                {
                    if (!draft.CanRouteThrough(i)) return false;
                    if (draft.IsReserved(i, draft.OwnerId) && !(request.AccessGrants ?? []).Any(g =>
                        !draft.IsReserved(i, g.ExactOwnerId) && g.TraverseCells.Contains(i) && g.WritableEdges.Contains(key))) return false;
                }
                return true;
            }
            var anchorAllowed = new bool[n]; var anchorCost = new int[n]; var anchorReuse = new bool[n];
            for (var a = 0; a < n; a++)
            {
                if (!SiteGeometry.Fits(new(a % width, a / width, r.Width, r.Width), request.Area)) continue;
                var okay = true; var cost = 0; var reuse = true;
                foreach (var i in Square(a))
                {
                    if (!CellAllowed(i)) { okay = false; break; }
                    reuse &= draft.IsRouteCorridor(i);
                    cost += terrain[i] is "tree" or "fruit-tree" ? (request.ProfileKind == RouteProfileKind.ForestTrail ? 9 : 4) :
                        terrain[i] == "solid-rock" ? 16 : terrain[i] == "water" ? 45 : 0;
                    if (request.ProfileKind == RouteProfileKind.RockPassage && draft.Neighbors(i).Any(j => terrain[j] == "water")) cost += 12;
                    var x = i % width - a % width; var y = i / width - a / width;
                    if (x + 1 < r.Width && !EdgeAllowed(new(i, EdgeDirection.Right), true) ||
                        y + 1 < r.Width && !EdgeAllowed(new(i, EdgeDirection.Down), true)) { okay = false; break; }
                }
                anchorAllowed[a] = okay; anchorCost[a] = cost; anchorReuse[a] = reuse;
            }
            var starts = request.Start.Cells.Where(i => anchorAllowed[i]).Distinct().Order().ToArray();
            var goals = request.Destination.Cells.Where(i => anchorAllowed[i]).ToHashSet();
            if (starts.Length == 0 || goals.Count == 0) { lastCode = PlacementFailureCode.NoFootprint; lastReason = "An endpoint has no permitted full-width placement."; continue; }
            var spatialCost = RouteCostField.Build(request, scope, terrain, anchorReuse, attempt);
            // An obstacle-free multi-source distance field is an admissible Manhattan heuristic.
            var heuristic = Enumerable.Repeat(int.MaxValue, n).ToArray(); Queue<int> distanceQueue = new();
            foreach (var goal in goals.Order()) { heuristic[goal] = 0; distanceQueue.Enqueue(goal); }
            while (distanceQueue.TryDequeue(out var a)) foreach (var b in draft.Neighbors(a))
                if (heuristic[b] == int.MaxValue) { heuristic[b] = heuristic[a] + 1; distanceQueue.Enqueue(b); }
            var random = RouteSeed.Stream(scope.RootSeed, id, "search", attempt);
            var phaseX = random.Next(628) / 100.0; var phaseY = random.Next(628) / 100.0;
            // Spatial variation is independent of search heading and expansion order.
            for (var b = 0; b < n; b++)
            {
                var wx = b % width + (request.Window?.OffsetX ?? 0); var wy = b / width + (request.Window?.OffsetY ?? 0);
                spatialCost[b] += (int)Math.Round(prefs.VariationWeight * (1 +
                    Math.Sin(wx * .16 + wy * .09 + phaseX) * Math.Cos(wx * .08 - wy * .13 + phaseY)));
            }
            var costs = Enumerable.Repeat(long.MaxValue, checked(n * 5)).ToArray(); var previous = Enumerable.Repeat(-1, n * 5).ToArray();
            PriorityQueue<(int State, long Cost), (long Estimate, long Cost, int State)> queue = new();
            foreach (var a in starts) { costs[a * 5 + 4] = 0; queue.Enqueue((a * 5 + 4, 0), (heuristic[a] * 8L, 0, a * 5 + 4)); }
            var expanded = 0; var found = -1;
            while (queue.TryDequeue(out var entry, out _))
            {
                if (entry.Cost != costs[entry.State]) continue;
                if (expanded >= r.MaxExpandedStates || !scope.Budget.Spend(1)) return Fail(PlacementFailureCode.BudgetExhausted, "Route expanded-state or shared work allowance exhausted.");
                expanded++; totalExpanded++; var a = entry.State / 5; var heading = entry.State % 5;
                if (goals.Contains(a)) { found = entry.State; break; }
                foreach (var b in draft.Neighbors(a))
                {
                    if (!anchorAllowed[b] || !EdgeAllowed(EdgeKey.Between(a, b, width))) continue;
                    var direction = b / width < a / width ? 0 : b / width > a / width ? 2 : b % width > a % width ? 1 : 3;
                    var reuse = anchorReuse[b];
                    int Direction(CardinalSide side) => side switch { CardinalSide.North => 0, CardinalSide.East => 1, CardinalSide.South => 2, _ => 3 };
                    var departure = request.Start.Side is { } startSide ? (Direction(startSide) + (request.Start.Kind == RouteEndpointKind.Boundary ? 2 : 0)) % 4 : -1;
                    var arrival = request.Destination.Side is { } endSide ? (Direction(endSide) + (request.Destination.Kind == RouteEndpointKind.Site ? 2 : 0)) % 4 : -1;
                    var orientation = (heading == 4 && departure >= 0 && direction != departure ? 24 : 0) +
                        (goals.Contains(b) && arrival >= 0 && direction != arrival ? 24 : 0);
                    var step = (reuse ? 8L : 12L) + anchorCost[b] + spatialCost[b] + orientation + (heading != 4 && heading != direction ? prefs.TurnPenalty : 0) +
                        (softAvoid.Contains(b) ? 30 : 0) + (preferred.Count > 0 && !preferred.Contains(b) ? 3 : 0);
                    var next = b * 5 + direction; var cost = entry.Cost + step;
                    if (cost >= costs[next]) continue;
                    costs[next] = cost; previous[next] = entry.State;
                    queue.Enqueue((next, cost), (cost + heuristic[b] * 8L, cost, next));
                }
                queuePeak = Math.Max(queuePeak, queue.Count);
            }
            if (found < 0) continue;
            List<int> path = []; for (var s = found; s >= 0; s = previous[s]) path.Add(s / 5); path.Reverse();
            if (path.Count > r.MaxLength) { lastCode = PlacementFailureCode.PreparationLimit; lastReason = "Route length exceeds its hard allowance."; continue; }
            var corridor = path.SelectMany(Square).ToHashSet();
            var water = corridor.Where(i => terrain[i] == "water").ToHashSet();
            var run = 0; var longest = 0;
            foreach (var a in path) { run = Square(a).Any(water.Contains) ? run + 1 : 0; longest = Math.Max(longest, run); }
            if (water.Count > r.MaxWaterCells || longest > r.MaxWaterRun)
            { lastCode = PlacementFailureCode.PreparationLimit; lastReason = "The complete corridor exceeds its water crossing allowance."; continue; }
            var before = draft.BuildPatch();
            searchClock.Stop(); var materializationClock = Stopwatch.StartNew();
            try
            {
                foreach (var i in corridor.Order())
                    if (!passable[i] || draft.CanWrite(i) && terrain[i] != floor &&
                        definitions[terrain[i]] is { Assembly: null, Production: null, LegacyKind: not TerrainKind.Rail })
                        draft.ReplaceTerrain(i, floor);
                HashSet<EdgeKey> requiredEdges = [];
                foreach (var a in path)
                    foreach (var i in Square(a))
                    {
                        if (i % width - a % width + 1 < r.Width) requiredEdges.Add(new(i, EdgeDirection.Right));
                        if (i / width - a / width + 1 < r.Width) requiredEdges.Add(new(i, EdgeDirection.Down));
                    }
                for (var k = 1; k < path.Count; k++) requiredEdges.Add(EdgeKey.Between(path[k - 1], path[k], width));
                foreach (var edge in requiredEdges.OrderBy(e => e.Index).ThenBy(e => e.Direction))
                    if (!draft.EdgeAt(edge).Passable() && !(r.Width == 1 && r.ClosedDoorsTraversable && draft.EdgeAt(edge) == EdgeKind.ClosedDoor))
                        draft.ReplaceEdge(edge, EdgeKind.None);
                // Verify actual, complete square placements and every cardinal transition after preparation.
                if (corridor.Any(i => !draft.Passable(i)) || requiredEdges.Any(e => !draft.EdgeAt(e).Passable() &&
                    !(r.Width == 1 && r.ClosedDoorsTraversable && draft.EdgeAt(e) == EdgeKind.ClosedDoor)))
                    return Fail(PlacementFailureCode.ValidationFailed, "Prepared corridor failed its final footprint check.");
                var patch = draft.BuildPatch(); var summary = SiteGeometry.Summary(patch);
                if (summary.ChangedTerrain > policy.MaxChangedCells || summary.Cost > policy.MaxCost)
                { lastCode = PlacementFailureCode.PreparationLimit; lastReason = "Complete corridor exceeds its preparation allowance."; searchClock.Start(); continue; }
                draft.ReserveRoute(corridor, id);
                var ox = request.Window?.OffsetX ?? 0; var oy = request.Window?.OffsetY ?? 0;
                var runs = new List<RouteRun>(); var turns = 0;
                for (var k = 1; k < path.Count;)
                {
                    var from = path[k - 1]; var delta = path[k] - from; var count = 1;
                    while (k + count < path.Count && path[k + count] - path[k + count - 1] == delta) count++;
                    var next = path[k];
                    var side = next / width > from / width ? CardinalSide.South : next / width < from / width ? CardinalSide.North :
                        next % width > from % width ? CardinalSide.East : CardinalSide.West;
                    runs.Add(new(from % width + ox, from / width + oy, side, count)); k += count;
                }
                turns = Math.Max(0, runs.Count - 1);
                RoutePort Port(RouteEndpoint endpoint, int anchor) => new(endpoint.Id, anchor, anchor % width + ox, anchor / width + oy,
                    endpoint.Kind, endpoint.Side, endpoint.SiteId);
                List<RouteTunnelEntrance> mouths = [];
                for (var k = 1; k < path.Count; k++)
                    if ((terrain[path[k - 1]] == "solid-rock") != (terrain[path[k]] == "solid-rock") &&
                        draft.EdgeAt(EdgeKey.Between(path[k - 1], path[k], width)).Passable())
                        mouths.Add(new(path[k], path[k] % width + ox, path[k] / width + oy, EdgeKey.Between(path[k - 1], path[k], width)));
                List<RouteCrossing> crossings = [];
                while (water.Count > 0)
                {
                    var seed = water.Min(); HashSet<int> group = [seed]; water.Remove(seed); Queue<int> flood = new(); flood.Enqueue(seed);
                    while (flood.TryDequeue(out var a)) foreach (var b in draft.Neighbors(a)) if (water.Remove(b)) { group.Add(b); flood.Enqueue(b); }
                    crossings.Add(new(GeneratedArea.From(id + "/crossing-" + crossings.Count, group, width)));
                }
                var area = GeneratedArea.From(id + "/corridor", corridor, width);
                var info = new RouteInfo(id, request.Purpose, request.ProfileKind, area,
                    new(path[0] % width + ox, path[0] / width + oy, runs), Port(request.Start, path[0]), Port(request.Destination, path[^1]),
                    r.RequiredFootprint, r.Width, summary, new(scope.Budget.Attempts - attemptsBefore, totalExpanded, queuePeak, costs[found],
                        path.Count, turns, corridor.Count(scope.Board.IsRouteCorridor)), crossings, mouths,
                    id + "/materialization-" + scope.Draft.Revision, scope.Draft.Revision)
                {
                    RequiresOpeningDoors = requiredEdges.Any(e => draft.EdgeAt(e) == EdgeKind.ClosedDoor), SurfaceTypeId = floor,
                    RealizedSurfaceTypeIds = corridor.Select(draft.TerrainAt).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                };
                var result = new RoutePlan(draft.BuildPatch(), info,
                    [new(id, scope.Path, "Route", id, area.Bounds, [], []),
                        SiteGeometry.Operation(draft, before, id + "/prepare", id, "Route corridor", id, area.Bounds)])
                    { SearchMilliseconds = searchClock.Elapsed.TotalMilliseconds, MaterializationMilliseconds = materializationClock.Elapsed.TotalMilliseconds };
                return new Planned<RoutePlan>(result);
            }
            catch (ArgumentException e) { lastCode = PlacementFailureCode.ReservationConflict; lastReason = e.Message; }
            searchClock.Start();
        }
        return Fail(lastCode, lastReason);
    }
}
