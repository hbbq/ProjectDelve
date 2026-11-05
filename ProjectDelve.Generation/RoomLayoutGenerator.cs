using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record RoomLayoutRequest(string Key, CellBounds Interior, int RoomCount = 2, bool Furniture = true,
    IReadOnlyList<SitePortal>? ExteriorPortals = null, string FurnitureTypeId = "stone-floor-with-table", bool ReplaceExistingEdges = false);

public static class RoomLayoutGenerator
{
    public static PlacementResult<RoomLayoutPlan> Plan(RoomLayoutRequest request, GenerationScope scope)
    {
        var id = scope.ObjectId(request.Key); var start = scope.Budget.Attempts;
        Rejected<RoomLayoutPlan> Fail(PlacementFailureCode code, string reason) => new(new(id, "Rooms", code, reason, scope.Budget.Attempts - start));
        if (!SiteGeometry.Fits(request.Interior, scope.Draft.AllowedArea) || request.RoomCount is < 1 or > 3 ||
            request.Interior.Width < 2 || request.Interior.Height < 2 || (long)request.Interior.Width * request.Interior.Height < request.RoomCount * 6)
            return Fail(PlacementFailureCode.InvalidRequest, "Rooms need a valid interior and at least six Cells per room.");
        if (request.Furniture && !scope.Content.Terrain.Any(t => t.Id == request.FurnitureTypeId && !t.Passable))
            return Fail(PlacementFailureCode.InvalidContent, "Furniture must reference a known obstructing Terrain Type.");
        var cells = SiteGeometry.Cells(request.Interior, scope.Draft.Width).ToArray();
        var interiorSet = cells.ToHashSet();
        foreach (var portal in request.ExteriorPortals ?? [])
        {
            if (portal.InsideIndex < 0 || portal.OutsideIndex < 0 || portal.InsideIndex >= scope.Draft.Width * scope.Draft.Height ||
                portal.OutsideIndex >= scope.Draft.Width * scope.Draft.Height || !interiorSet.Contains(portal.InsideIndex) || interiorSet.Contains(portal.OutsideIndex))
                return Fail(PlacementFailureCode.EntranceConstraint, "Exterior portal must connect this interior to an outside Cell.");
            EdgeKey actual;
            try { actual = EdgeKey.Between(portal.InsideIndex, portal.OutsideIndex, scope.Draft.Width); }
            catch (ArgumentException) { return Fail(PlacementFailureCode.EntranceConstraint, "Exterior portal endpoints must border the same Edge."); }
            if (actual != portal.Edge || scope.Draft.EdgeAt(actual) != portal.Kind || !SiteGeometry.Crossable(scope.Draft, portal.InsideIndex, portal.OutsideIndex))
                return Fail(PlacementFailureCode.EntranceConstraint, "Exterior portal does not describe a real potential passage.");
        }
        if (!scope.Budget.Spend(cells.Length)) return Fail(PlacementFailureCode.BudgetExhausted, "Room inspection budget exhausted.");
        if (cells.Any(i => !scope.Draft.CanWrite(i) || !scope.Draft.Passable(i)))
            return Fail(PlacementFailureCode.NoFootprint, "Room interior must be prepared, passable and writable.");
        var priorEdges = cells.SelectMany(i => scope.Draft.Neighbors(i).Where(j => j > i && interiorSet.Contains(j))
            .Select(j => EdgeKey.Between(i, j, scope.Draft.Width))).Where(k => scope.Draft.EdgeAt(k) != EdgeKind.None).ToArray();
        if (priorEdges.Length > 0 && !request.ReplaceExistingEdges)
            return Fail(PlacementFailureCode.ReservationConflict, "Existing interior Edges require explicit replacement permission.");
        var call = scope.Child(request.Key, scope.Draft);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Room attempt budget exhausted.");
            var random = call.Random("partitions", attempt); var doorsRandom = call.Random("doors", attempt);
            var furnishingRandom = call.Random("furnishing", attempt); List<CellBounds> rooms = [request.Interior];
            while (rooms.Count < request.RoomCount)
            {
                var splits = new List<(int Index, CellBounds A, CellBounds B)>();
                for (var n = 0; n < rooms.Count; n++)
                {
                    var r = rooms[n];
                    for (var x = 2; x <= r.Width - 2; x++)
                        if (x * r.Height >= 6 && (r.Width - x) * r.Height >= 6)
                            splits.Add((n, new(r.X, r.Y, x, r.Height), new(r.X + x, r.Y, r.Width - x, r.Height)));
                    for (var y = 2; y <= r.Height - 2; y++)
                        if (y * r.Width >= 6 && (r.Height - y) * r.Width >= 6)
                            splits.Add((n, new(r.X, r.Y, r.Width, y), new(r.X, r.Y + y, r.Width, r.Height - y)));
                }
                if (splits.Count == 0) break;
                var split = splits[random.Next(splits.Count)]; rooms[split.Index] = split.A; rooms.Add(split.B);
            }
            if (rooms.Count != request.RoomCount) continue;
            // IDs use stable logical slots, independent of accepted candidates elsewhere.
            rooms = rooms.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
            var draft = scope.Draft.Fork(request.Interior, scope.Draft.OwnerId);
            var before = draft.BuildPatch();
            foreach (var edge in priorEdges) draft.ReplaceEdge(edge, EdgeKind.None);
            var labels = rooms.SelectMany((r, n) => SiteGeometry.Cells(r, draft.Width).Select(i => (Index: i, Room: n))).ToDictionary(p => p.Index, p => p.Room);
            var boundaries = new Dictionary<(int, int), List<EdgeKey>>();
            var conflict = false;
            foreach (var i in cells) foreach (var j in draft.Neighbors(i).Where(j => j > i && labels.ContainsKey(j)))
            {
                if (labels[i] == labels[j]) continue;
                var pair = (Math.Min(labels[i], labels[j]), Math.Max(labels[i], labels[j]));
                if (!boundaries.TryGetValue(pair, out var edges)) boundaries[pair] = edges = [];
                var key = EdgeKey.Between(i, j, draft.Width); edges.Add(key);
                try
                {
                    if (request.ReplaceExistingEdges) draft.ReplaceEdge(key, EdgeKind.Wall);
                    else draft.SetEdge(key, EdgeKind.Wall);
                }
                catch (ArgumentException) { conflict = true; }
            }
            if (conflict) continue;
            List<ConstructionOperation> operations = [new(id, scope.Path, "Room layout", id, request.Interior, [], []),
                SiteGeometry.Operation(draft, before, id + "/partitions", id, "Partition Walls", id, request.Interior)];
            before = draft.BuildPatch();
            var portals = new List<SitePortal>();
            foreach (var (pair, edges) in boundaries.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2))
            {
                var edge = edges[doorsRandom.Next(edges.Count)]; draft.ReplaceEdge(edge, EdgeKind.ClosedDoor);
                portals.Add(new(id + $"/door-{pair.Item1}-{pair.Item2}", scope.Draft.OwnerId, edge.Index,
                    edge.Index + (edge.Direction == EdgeDirection.Right ? 1 : draft.Width), edge, EdgeKind.ClosedDoor));
            }
            operations.Add(SiteGeometry.Operation(draft, before, id + "/doors", id, "Connecting Doors", id, request.Interior));
            var allPortals = (request.ExteriorPortals ?? []).Concat(portals).ToArray();
            before = draft.BuildPatch();
            if (request.Furniture)
            {
                var endpoints = allPortals.SelectMany(p => new[] { p.InsideIndex, p.OutsideIndex }).ToHashSet();
                for (var n = 0; n < rooms.Count; n++)
                {
                    var roomCells = SiteGeometry.Cells(rooms[n], draft.Width).ToArray();
                    var candidates = roomCells.Where(i => !endpoints.Contains(i)).ToList(); furnishingRandom.Shuffle(candidates);
                    foreach (var index in candidates.Take(8))
                    {
                        var furnishing = draft.Fork(rooms[n], draft.OwnerId); furnishing.ReplaceTerrain(index, request.FurnitureTypeId);
                        var usable = roomCells.Where(furnishing.Passable).ToArray();
                        var reached = SiteGeometry.Flood(furnishing, usable[0], roomCells);
                        if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Furniture traversal budget exhausted.");
                        if (reached.Count != usable.Length) continue;
                        if (!draft.Accept(furnishing.BuildPatch(), out var error)) return Fail(PlacementFailureCode.ValidationFailed, error!);
                        break;
                    }
                }
            }
            operations.Add(SiteGeometry.Operation(draft, before, id + "/furniture", id, "Room furniture", id, request.Interior));
            var usableAll = cells.Where(draft.Passable).ToArray(); var connected = SiteGeometry.Flood(draft, usableAll[0], cells);
            if (connected is null) return Fail(PlacementFailureCode.BudgetExhausted, "Room traversal budget exhausted.");
            if (connected.Count != usableAll.Length) continue;
            var info = rooms.Select((r, n) =>
            {
                var roomId = id + $"/room-{n}"; var interior = SiteGeometry.Cells(r, draft.Width).ToArray(); var set = interior.ToHashSet();
                var usable = interior.Where(draft.Passable).ToArray();
                return new RoomInfo(roomId, "Room", GeneratedArea.From(roomId + "/interior", interior, draft.Width),
                    GeneratedArea.From(roomId + "/usable", usable, draft.Width),
                    allPortals.Where(p => set.Contains(p.InsideIndex) || set.Contains(p.OutsideIndex)).Select(p => p.Id).ToArray(), SiteGeometry.TwoByTwo(draft, usable));
            }).ToArray();
            return new Planned<RoomLayoutPlan>(new(draft.BuildPatch(), info, portals, operations));
        }
        return Fail(PlacementFailureCode.NoFootprint, "The requested room partition could not fit.");
    }
}
