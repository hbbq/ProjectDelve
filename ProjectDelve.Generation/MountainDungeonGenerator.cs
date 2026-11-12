using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record MountainDungeonRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    IReadOnlySet<int>? ExcludedRockMassAnchors = null, MountainDungeonProfile? Profile = null,
    IReadOnlySet<int>? ExcludedRockCells = null);

/// <summary>Excavates ordinary, single-level scenario geometry inside remaining connected rock.</summary>
public static class MountainDungeonGenerator
{
    public static int Target(WorldWindow window, CivilizationSettings settings)
    {
        settings.Validate();
        var density = Math.Max((int)settings.Campsites, (int)settings.Settlements);
        var effective = (long)Math.Sqrt((long)window.Width * window.Height * 720);
        return !settings.Enabled || density == 0 ? 0 : Math.Min(4, (int)((effective * density + 2399) / 2400));
    }

    public static PlacementResult<SitePlan> Plan(MountainDungeonRequest request, GenerationScope scope)
    {
        var id = scope.ObjectId(request.Key); var start = scope.Budget.Attempts;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) => new(new(id, "MountainDungeon", code, reason, scope.Budget.Attempts - start));
        if (!SiteGeometry.Fits(request.Area, scope.Draft.AllowedArea) || request.Profile is { } p && !Enum.IsDefined(p))
            return Fail(PlacementFailureCode.InvalidRequest, "Mountain dungeon needs a valid area and profile.");
        try { request.Preparation.Validate(); }
        catch (ArgumentException e) { return Fail(PlacementFailureCode.InvalidRequest, e.Message); }
        if (!scope.Content.Terrain.Any(t => t.Id == "stone-floor" && t.Passable) ||
            !scope.Content.Terrain.Any(t => t.Id == "solid-rock" && !t.Passable))
            return Fail(PlacementFailureCode.InvalidContent, "Mountain dungeon requires Solid Rock and Stone Floor.");
        var source = scope.Draft;
        var cells = SiteGeometry.Cells(request.Area, source.Width).ToArray();
        // Charge every board scan before performing it, including component preparation.
        if (!scope.Budget.Spend(cells.Length * 12L)) return Fail(PlacementFailureCode.BudgetExhausted, "Rock/component survey exceeds shared work allowance.");
        var main = PlayableWorldComponent.Select(source);
        var remaining = cells.Where(i => source.TerrainAt(i) == "solid-rock").ToHashSet();
        var masses = new List<HashSet<int>>();
        foreach (var anchor in cells)
        {
            if (!remaining.Remove(anchor)) continue;
            var mass = new HashSet<int> { anchor }; var q = new Queue<int>(); q.Enqueue(anchor);
            while (q.TryDequeue(out var i)) foreach (var j in source.Neighbors(i)) if (remaining.Remove(j)) { mass.Add(j); q.Enqueue(j); }
            if (request.ExcludedRockMassAnchors?.Contains(anchor) != true && !mass.Any(i => request.ExcludedRockCells?.Contains(i) == true)) masses.Add(mass);
        }
        var call = scope.Child(request.Key, source);
        var lastCode = PlacementFailureCode.NoFootprint; var lastReason = "No connected rock mass has room for two separated 3x3 rooms and an intact shell.";
        foreach (var mass in masses.OrderByDescending(m => m.Count).ThenBy(m => m.Min()).Take(24))
        {
            // An eight-neighbor rock shell prevents diagonal cave/exterior breaches too.
            var core = mass.Where(i => Neighborhood(i, source.Width, source.Height).Count() == 8 &&
                Neighborhood(i, source.Width, source.Height).All(mass.Contains) && source.CanWrite(i)).ToHashSet();
            var centers = core.Where(i => Square(i, 3, 3, source.Width).All(core.Contains)).Order().ToArray();
            var packed = new HashSet<int>(); var roomCapacity = 0;
            foreach (var center in centers)
                if (!Square(center, 5, 5, source.Width).Any(packed.Contains)) { packed.UnionWith(Square(center, 3, 3, source.Width)); roomCapacity++; }
            if (roomCapacity < 2) continue;
            var entrances = mass.Where(i => source.CanWrite(i) && !core.Contains(i) && source.Neighbors(i).Any(core.Contains))
                .SelectMany(i => source.Neighbors(i).Where(main.Contains).Where(j => source.EdgeAt(EdgeKey.Between(i, j, source.Width)) == EdgeKind.None).Select(j => (Inside: i, Outside: j)))
                .Where(e => source.Neighbors(e.Inside).Where(j => !mass.Contains(j)).All(j => j == e.Outside))
                .OrderBy(e => e.Inside).ThenBy(e => e.Outside).ToArray();
            if (entrances.Length == 0) { lastCode = PlacementFailureCode.EntranceConstraint; lastReason = "Room-capable rock has no single protected entrance adjoining the main playable component."; continue; }
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Shared dungeon attempt allowance exhausted.");
                var random = call.Random("layout", mass.Min() * 5 + attempt);
                var features = call.Random("features", mass.Min() * 5 + attempt);
                if (!scope.Budget.Spend(centers.Length * 8L)) return Fail(PlacementFailureCode.BudgetExhausted, "Room candidate ranking exceeds shared work allowance.");
                var profile = request.Profile ?? (MountainDungeonProfile)random.Next(3);
                var entrance = entrances[random.Next(entrances.Length)];
                var draft = source.Fork(request.Area, id);
                var before = draft.BuildPatch();
                var floor = new HashSet<int> { entrance.Inside }; var corridors = new HashSet<int> { entrance.Inside };
                var roomCells = new HashSet<int>(); var rooms = new List<RoomInfo>(); var required = new HashSet<int> { entrance.Inside };
                var desired = profile == MountainDungeonProfile.ChamberComplex ? 3 + random.Next(3) : 2 + random.Next(4);
                var ranked = centers.Select(i => (Index: i, Tie: random.Next(1000000))).ToArray();
                for (var n = 0; n < desired; n++)
                {
                    var available = ranked.Where(c => !Square(c.Index, 5, 5, draft.Width).Any(roomCells.Contains));
                    // Tunnel chains, branching chambers and a long inward passage share one carver.
                    var previous = rooms.Count == 0 ? entrance.Inside : rooms[^1].Interior.Indices(draft.Width).ElementAt(rooms[^1].Interior.Area / 2);
                    var rankedRooms = available.OrderBy(c => n == 0 ? Distance(c.Index, entrance.Inside, draft.Width) :
                        profile == MountainDungeonProfile.DeepPassage ? -Distance(c.Index, entrance.Inside, draft.Width) :
                        profile == MountainDungeonProfile.TunnelNetwork ? Distance(c.Index, previous, draft.Width) : c.Tie / 50000).ThenBy(c => c.Tie).Take(72).ToArray();
                    var added = false;
                    var desiredWidth = profile == MountainDungeonProfile.TunnelNetwork ? 3 + random.Next(2) : 4 + random.Next(2);
                    var desiredHeight = 5 + random.Next(3);
                    // Try the authored-size family across several centers before shrinking.
                    // An entrance-adjacent shallow center must not force every room to 3x3.
                    (int Width, int Height)[] sizes = profile == MountainDungeonProfile.ChamberComplex
                        ? [(desiredWidth, desiredHeight), (4, 5), (3, 5)]
                        : [(desiredWidth, desiredHeight), (3, 5), (3, 4), (3, 3)];
                    if (random.Next(2) == 0) sizes = sizes.Select(s => (s.Height, s.Width)).ToArray();
                    foreach (var size in sizes.Distinct())
                    {
                        foreach (var candidate in rankedRooms)
                        {
                            var w = size.Width; var h = size.Height;
                            if (!scope.Budget.Spend(w * h)) return Fail(PlacementFailureCode.BudgetExhausted, "Room fitting exhausted shared work.");
                            var rectangle = Square(candidate.Index, w, h, draft.Width).ToArray();
                            if (!SiteGeometry.Fits(new(candidate.Index % draft.Width - w / 2, candidate.Index / draft.Width - h / 2, w, h), request.Area) ||
                                !rectangle.All(core.Contains)) continue;
                            if (rectangle.Any(i => roomCells.Contains(i) || Neighborhood(i, draft.Width, draft.Height).Any(roomCells.Contains))) continue;
                            // A clipped corner varies chamber silhouette without sacrificing its
                            // minimum 3x3 usable room or introducing diagonal-only connections.
                            if (w >= 4 && h >= 4 && rectangle.Length == w * h && random.Next(4) == 0)
                                rectangle = rectangle.Where(i => i != rectangle[0]).ToArray();
                            var startCell = n == 0 ? entrance.Inside : profile == MountainDungeonProfile.TunnelNetwork ? previous : floor.OrderBy(i => Distance(i, candidate.Index, draft.Width)).ThenBy(i => i).First();
                            var allowed = core.Append(entrance.Inside).ToHashSet();
                            var path = Path(draft, startCell, candidate.Index, allowed);
                            if (path is null) return Fail(PlacementFailureCode.BudgetExhausted, "Dungeon passage search exhausted shared work.");
                            if (path.Count == 0 || path.Count > 65) continue;
                            floor.UnionWith(rectangle); roomCells.UnionWith(rectangle); required.UnionWith(path);
                            CarvePath(path); var roomId = id + "/room-" + n;
                            var area = GeneratedArea.From(roomId, rectangle, draft.Width);
                            rooms.Add(new(roomId, n == desired - 1 ? "treasure-chamber" : "chamber", area, area, [], (area.Bounds.Width - 1) * (area.Bounds.Height - 1)));
                            added = true; break;
                        }
                        if (added) break;
                    }
                    if (!added) break;
                }
                if (rooms.Count < 2) continue;
                // Side spurs are deliberately blind ends. Occasional extra links make loops.
                for (var branch = 0; branch < 2; branch++)
                {
                    var origin = corridors.Order().ElementAt(random.Next(corridors.Count));
                    var direction = random.Next(4); var delta = direction == 0 ? 1 : direction == 1 ? -1 : direction == 2 ? draft.Width : -draft.Width;
                    var path = new List<int> { origin }; var cursor = origin;
                    for (var length = 0; length < 2 + random.Next(4); length++)
                    {
                        var next = cursor + delta;
                        if (!core.Contains(next) || !draft.Neighbors(cursor).Contains(next) || roomCells.Contains(next)) break;
                        path.Add(next); cursor = next;
                    }
                    CarvePath(path);
                }
                if (rooms.Count > 2 && random.Next(3) == 0)
                {
                    var a = rooms[0].Interior.Indices(draft.Width).First(); var b = rooms[^1].Interior.Indices(draft.Width).First();
                    var loop = Path(draft, a, b, core);
                    if (loop is null) return Fail(PlacementFailureCode.BudgetExhausted, "Dungeon loop search exhausted shared work.");
                    CarvePath(loop);
                }
                var shell = floor.SelectMany(i => Neighborhood(i, draft.Width, draft.Height)).Where(i => mass.Contains(i) && !floor.Contains(i)).ToHashSet();
                if (shell.Any(i => !draft.CanWrite(i))) { lastCode = PlacementFailureCode.ReservationConflict; lastReason = "Local rock shell overlaps protected construction."; continue; }
                var problem = SiteGeometry.Prepare(draft, floor, "stone-floor", request.Preparation);
                if (problem is not null) { lastCode = problem.Code; lastReason = problem.Reason; if (lastCode == PlacementFailureCode.BudgetExhausted) return Fail(lastCode, lastReason); continue; }
                // Required spanning access crosses each room's center; no lava may occupy it.
                required.Clear(); required.Add(entrance.Inside);
                foreach (var room in rooms)
                {
                    var center = room.Interior.Indices(draft.Width).ElementAt(room.Interior.Area / 2);
                    var path = Path(draft, entrance.Inside, center, floor);
                    if (path is null) return Fail(PlacementFailureCode.BudgetExhausted, "Required access planning exhausted shared work.");
                    required.UnionWith(path);
                }
                var lava = new HashSet<int>();
                if (draft.Content.Terrain.Any(t => t.Id == "ordinary-lava") && features.Next(4) == 0)
                {
                    var poolSize = 1 + features.Next(3);
                    for (var poolCell = 0; poolCell < poolSize; poolCell++)
                    {
                        var candidates = (lava.Count == 0 ? roomCells : lava.SelectMany(draft.Neighbors).Where(roomCells.Contains).ToHashSet())
                            .Where(i => !required.Contains(i) && !lava.Contains(i)).OrderByDescending(i => Distance(i, entrance.Inside, draft.Width)).ThenBy(i => i).Take(12);
                        var placed = false;
                        foreach (var i in candidates)
                        {
                            var safe = floor.Where(j => j != i && !lava.Contains(j)).ToHashSet();
                            var reached = Flood(draft, entrance.Inside, safe);
                            if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Lava safety search exhausted shared work.");
                            if (reached.Count != safe.Count || rooms.Any(r => SiteGeometry.TwoByTwo(draft, r.Interior.Indices(draft.Width).Where(j => j != i)) < 1)) continue;
                            draft.ReplaceTerrain(i, "ordinary-lava"); lava.Add(i); placed = true; break;
                        }
                        if (!placed) break;
                    }
                }
                var portalEdge = EdgeKey.Between(entrance.Inside, entrance.Outside, draft.Width);
                // A Door uses two owned cells; leave entrance open when its exterior is a road.
                var approach = new HashSet<int>();
                if (draft.CanWrite(entrance.Outside))
                {
                    approach.Add(entrance.Outside);
                    if (features.Next(3) == 0) draft.SetEdge(portalEdge, EdgeKind.ClosedDoor);
                }
                if (features.Next(3) == 0)
                {
                    var neck = corridors.Except(roomCells).Where(i => i != entrance.Inside && floor.Contains(i) &&
                        draft.Neighbors(i).Count(floor.Contains) == 2).Order().FirstOrDefault(-1);
                    if (neck >= 0)
                    {
                        var neighbor = draft.Neighbors(neck).Where(i => corridors.Contains(i) && !roomCells.Contains(i) && i != entrance.Inside).Order().FirstOrDefault(-1);
                        if (neighbor >= 0) draft.SetEdge(EdgeKey.Between(neck, neighbor, draft.Width), EdgeKind.ClosedDoor);
                    }
                }
                var usable = floor.Except(lava).ToHashSet(); var connected = Flood(draft, entrance.Inside, usable);
                if (connected is null) return Fail(PlacementFailureCode.BudgetExhausted, "Dungeon validation exhausted shared work.");
                if (!usable.SetEquals(connected)) { lastCode = PlacementFailureCode.DisconnectedSpace; lastReason = "Dungeon floors do not share physical access."; continue; }
                draft.Reserve(floor.Concat(shell).Concat(approach));
                var portal = new SitePortal(id + "/entrance", id, entrance.Inside, entrance.Outside, portalEdge, draft.EdgeAt(portalEdge));
                var outward = draft.Neighbors(entrance.Outside).Where(main.Contains).Where(i => i != entrance.Inside && !shell.Contains(i) &&
                    (draft.EdgeAt(EdgeKey.Between(entrance.Outside, i, draft.Width)).Passable() || draft.EdgeAt(EdgeKey.Between(entrance.Outside, i, draft.Width)) == EdgeKind.ClosedDoor)).Order().FirstOrDefault(entrance.Outside);
                var side = entrance.Outside / draft.Width < entrance.Inside / draft.Width ? CardinalSide.North : entrance.Outside / draft.Width > entrance.Inside / draft.Width ? CardinalSide.South : entrance.Outside < entrance.Inside ? CardinalSide.West : CardinalSide.East;
                var patch = draft.BuildPatch(); var summary = SiteGeometry.Summary(patch);
                rooms = rooms.Select(r => r with
                {
                    Usable = GeneratedArea.From(r.Id + "/usable", r.Interior.Indices(draft.Width).Where(usable.Contains), draft.Width),
                    TwoByTwoPlacements = SiteGeometry.TwoByTwo(draft, r.Interior.Indices(draft.Width).Where(usable.Contains))
                }).ToList();
                IReadOnlyList<GeneratedArea> treasure = features.Next(3) == 0 ? [] :
                    [GeneratedArea.From(id + "/treasure", rooms[^1].Usable.Indices(draft.Width).Where(i => !required.Contains(i)), draft.Width)];
                treasure = treasure.Where(area => area.Area > 0).ToArray();
                rooms = rooms.Select((r, n) => r with { Purpose = treasure.Count > 0 && n == rooms.Count - 1 ? "treasure-chamber" : "chamber" }).ToList();
                var metadata = new MountainDungeonInfo(profile, rooms, GeneratedArea.From(id + "/corridors", corridors.Except(roomCells), draft.Width),
                    GeneratedArea.From(id + "/shell", shell, draft.Width), GeneratedArea.From(id + "/required-access", required, draft.Width),
                    GeneratedArea.From(id + "/lava", lava, draft.Width),
                    treasure, mass.Count, mass.Min(), roomCapacity) { AccessRootIndex = main.Min() };
                var site = new SiteInfo(id, GeneratedSiteKind.MountainDungeon, GeneratedArea.From(id + "/footprint", floor.Concat(shell), draft.Width),
                    GeneratedArea.From(id + "/exterior", approach, draft.Width), [], [portal], new(usable.Count, connected.Count, true, false, SiteGeometry.TwoByTwo(draft, usable)),
                    [SiteTag.Constructed], [], [], null, summary)
                {
                    MountainDungeon = metadata,
                    AccessPorts = [new(id + "/outward-access", id, entrance.Outside, outward, side, GeneratedArea.From(id + "/approach", approach, draft.Width), 1, new(id, approach, [], []))],
                    PopulationIntent = new(profile.ToString(), rooms.Select((r, n) => new SitePopulationArea(r.Id + "/population", treasure.Count > 0 && n == rooms.Count - 1 ? "treasure" : "chamber",
                        GeneratedArea.From(r.Id + "/population", r.Interior.Indices(draft.Width).Where(usable.Contains), draft.Width), r.Interior.Indices(draft.Width).First(usable.Contains), r.Id)).ToArray(), [], 2, Math.Min(16, rooms.Count * 3), CampAlertTopology.Separated)
                };
                return new Planned<SitePlan>(new(patch, site, [new(id, scope.Path, "Mountain Dungeon", id, site.Footprint.Bounds, [], []),
                    SiteGeometry.Operation(draft, before, id + "/excavate", id, "Rooms, passages and rock shell", id, site.Footprint.Bounds)]));

                void CarvePath(IReadOnlyList<int> path)
                {
                    floor.UnionWith(path); corridors.UnionWith(path);
                    for (var n = 0; n < path.Count; n++)
                    {
                        // Widen to a parallel lane wherever the intact shell permits it.
                        var i = path[n]; var other = n + 1 < path.Count ? path[n + 1] : n > 0 ? path[n - 1] : i;
                        var horizontal = Math.Abs(other - i) == 1; var offset = horizontal ? draft.Width : 1;
                        foreach (var j in new[] { i + offset, i - offset })
                            if (draft.Neighbors(i).Contains(j) && core.Contains(j)) { floor.Add(j); corridors.Add(j); break; }
                    }
                }
            }
        }
        return Fail(lastCode, lastReason);
    }

    private static int Distance(int a, int b, int width) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
    private static IEnumerable<int> Square(int center, int width, int height, int stride)
    {
        for (var y = center / stride - height / 2; y < center / stride - height / 2 + height; y++)
        for (var x = center % stride - width / 2; x < center % stride - width / 2 + width; x++) yield return y * stride + x;
    }
    private static IEnumerable<int> Neighborhood(int i, int width, int height)
    {
        for (var y = Math.Max(0, i / width - 1); y <= Math.Min(height - 1, i / width + 1); y++)
        for (var x = Math.Max(0, i % width - 1); x <= Math.Min(width - 1, i % width + 1); x++) if (y * width + x != i) yield return y * width + x;
    }
    private static List<int>? Path(BoardDraft draft, int start, int goal, HashSet<int> allowed)
    {
        var parents = new Dictionary<int, int> { [start] = -1 }; var q = new Queue<int>(); q.Enqueue(start);
        while (q.TryDequeue(out var i))
        {
            if (!draft.Budget.Spend(1)) return null;
            if (i == goal) { var path = new List<int>(); for (var c = i; c >= 0; c = parents[c]) path.Add(c); path.Reverse(); return path; }
            foreach (var j in draft.Neighbors(i)) if (allowed.Contains(j) && draft.EdgeAt(EdgeKey.Between(i, j, draft.Width)) == EdgeKind.None && parents.TryAdd(j, i)) q.Enqueue(j);
        }
        return [];
    }
    private static HashSet<int>? Flood(BoardDraft draft, int start, HashSet<int> allowed)
    {
        var seen = new HashSet<int> { start }; var q = new Queue<int>(); q.Enqueue(start);
        while (q.TryDequeue(out var i))
        {
            if (!draft.Budget.Spend(1)) return null;
            foreach (var j in draft.Neighbors(i)) if (allowed.Contains(j) && (draft.EdgeAt(EdgeKey.Between(i, j, draft.Width)).Passable() || draft.EdgeAt(EdgeKey.Between(i, j, draft.Width)) == EdgeKind.ClosedDoor) && seen.Add(j)) q.Enqueue(j);
        }
        return seen;
    }
}
