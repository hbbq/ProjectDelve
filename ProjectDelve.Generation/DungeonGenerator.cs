using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record DungeonRoom(int X, int Y, int Width, int Height)
{
    public Cell Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(Cell c) => c.X >= X && c.X < X + Width && c.Y >= Y && c.Y < Y + Height;
    public IEnumerable<Cell> Cells()
    {
        for (var y = Y; y < Y + Height; y++)
        for (var x = X; x < X + Width; x++) yield return new(x, y);
    }
}
public sealed record RoomConnection(int A, int B);
public sealed record DungeonCorridor(int Width, IReadOnlyList<Cell> Spine, IReadOnlyList<Cell> Cells);
public sealed record GenerationDiagnostics(IReadOnlyList<DungeonRoom> Rooms, IReadOnlyList<RoomConnection> Connections,
    int StartRoom, int DestinationRoom, int DestinationDistance, int OccupiedRooms, int Monsters, int Attempts)
{
    public IReadOnlyList<DungeonCorridor> Corridors { get; init; } = [];
    public string Profile { get; init; } = "HVM";
    public string? SitePlan { get; init; }
    public IReadOnlyList<string> SiteNames { get; init; } = [];
    public int TreasureCaches { get; init; }
    public int LootMultiplier { get; init; } = 1;
    public int AlternateRouteDistance { get; init; }
}
public sealed record GeneratedDungeon(GenerationCode Code, ScenarioDefinition Definition, string Fingerprint, GenerationDiagnostics Diagnostics);
public sealed record MonsterThreat(string UnitTypeId, int Threat);

public static class HeroesVsMonsters
{
    public const string Heroes = "side-1", Monsters = "side-2";
    public static IReadOnlyList<string> HeroTypes { get; } = Array.AsReadOnly(new[]
    { UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Wizard, UnitTypeIds.Cleric });
    public static IReadOnlyList<MonsterThreat> Pool { get; } = Array.AsReadOnly(new MonsterThreat[]
    {
        new(UnitTypeIds.Goblin, 1), new(UnitTypeIds.Grunt, 2), new(UnitTypeIds.Zombie, 2),
        new(UnitTypeIds.BombImp, 3), new(UnitTypeIds.SkeletonArcher, 3), new(UnitTypeIds.Ghost, 3),
        new(UnitTypeIds.Troll, 4), new(UnitTypeIds.Shaman, 4), new(UnitTypeIds.DisplacerDemon, 5)
    });
    // Select a band first: symmetric integer bell, truncated at Threat 1 and 5.
    public static int ThreatWeight(int difficulty, int threat) => new[] { 16, 8, 3, 1, 1 }[Math.Abs(difficulty - threat)];
}

public static class DungeonGenerator
{
    public const int MaxAttempts = 12;
    public static GeneratedDungeon Generate(string code) => Generate(GenerationCode.Parse(code));
    public static GeneratedDungeon Generate(GenerationCode configuration)
    {
        var code = configuration.Complete();
        if (code.Profile == "OS") return OvergrownStrongholdGenerator.Generate(code);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var layout = new Layout(code, attempt);
            if (!layout.Build()) continue;
            var result = layout.Populate();
            // Independent flood verification reads the final board/edges, not the connection graph.
            VerifyConnectivity(result.Definition);
            _ = GameEngine.CreateGame(result.Definition);
            return result;
        }
        throw new ArgumentException($"DNG1 could not route the requested dungeon after {MaxAttempts} deterministic attempts. Change dimensions, rooms or seed. No parameters were changed.");
    }

    public static IReadOnlyDictionary<Cell, int> VerifyConnectivity(ScenarioDefinition scenario)
    {
        var board = scenario.Board;
        var catalog = TerrainCatalog.Resolve(scenario.TerrainTypes, scenario.TerrainAssemblies, scenario.FixedComponentTypes);
        var defaultTerrain = TerrainCatalog.Find(catalog, TerrainCompatibility.ResolveTypeId(board.DefaultTerrain, board.DefaultTerrainTypeId));
        var terrain = board.Cells.ToDictionary(c => c.Position, c => TerrainCatalog.Find(catalog,
            TerrainCompatibility.ResolveTypeId(c.Terrain, c.TerrainTypeId, c.CoalGeneration)));
        bool Passable(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < board.Width && c.Y < board.Height &&
            terrain.GetValueOrDefault(c, defaultTerrain).Passable;
        var edges = board.Edges.ToDictionary(e => Key(e.Position,
            new(e.Position.X + (e.Direction == EdgeDirection.Right ? 1 : 0), e.Position.Y + (e.Direction == EdgeDirection.Down ? 1 : 0))), e => e.Kind);
        var start = scenario.Units.First(u => u.SideId == HeroesVsMonsters.Heroes).Anchor;
        if (!Passable(start)) throw new InvalidOperationException("Generated start is impassable.");
        Dictionary<Cell, int> distances = new() { [start] = 0 };
        Queue<Cell> queue = new(); queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        foreach (var next in Neighbors(cell))
        {
            var edge = edges.GetValueOrDefault(Key(cell, next), EdgeKind.None);
            if (!Passable(next) || distances.ContainsKey(next) || !(edge.Passable() || edge == EdgeKind.ClosedDoor)) continue;
            distances.Add(next, distances[cell] + 1); queue.Enqueue(next);
        }
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
            if (Passable(new(x, y)) && !distances.ContainsKey(new(x, y))) throw new InvalidOperationException("Generated floor is disconnected.");
        foreach (var e in edges)
            if (e.Value == EdgeKind.ClosedDoor && (!Passable(e.Key.Item1) || !Passable(e.Key.Item2)))
                throw new InvalidOperationException("Generated Door has an impassable endpoint.");
        return distances;
    }

    private static (Cell, Cell) Key(Cell a, Cell b) => a.Y < b.Y || a.Y == b.Y && a.X < b.X ? (a, b) : (b, a);
    private static IEnumerable<Cell> Neighbors(Cell c)
    { yield return new(c.X, c.Y - 1); yield return new(c.X + 1, c.Y); yield return new(c.X, c.Y + 1); yield return new(c.X - 1, c.Y); }

    private sealed record Region(int X, int Y, int W, int H)
    {
        public int Capacity => W / 6 * (H / 6);
        public bool Contains(Cell c) => c.X >= X && c.Y >= Y && c.X < X + W && c.Y < Y + H;
    }
    private sealed class Layout(GenerationCode code, int attempt)
    {
        private readonly Pcg32 geometry = Pcg32.Stream(code.Seed!.Value, "rooms", attempt);
        private readonly Pcg32 routing = Pcg32.Stream(code.Seed!.Value, "connections", attempt);
        private readonly List<DungeonRoom> rooms = [];
        private readonly List<RoomConnection> connections = [];
        private readonly List<DungeonCorridor> corridors = [];
        private readonly List<(List<Cell> Sources, HashSet<Cell> Targets)> joins = [];
        private readonly Dictionary<Cell, int> floors = [];
        private readonly HashSet<(Cell, Cell)> passages = [];
        private readonly HashSet<(Cell, Cell)> portals = [];
        private readonly Region whole = new(1, 1, code.Width - 2, code.Height - 2);
        private bool failed;

        public bool Build()
        {
            Partition(whole, code.Rooms);
            if (failed) return false;
            // Place every room before routing. Width may extend across a BSP separator
            // into free rock, while every unrelated room is already a protected obstacle.
            foreach (var join in joins)
            {
                var sourceOwners = join.Sources.Select(c => floors[c]).ToHashSet();
                var targetOwners = join.Targets.Select(c => floors[c]).ToHashSet();
                var sources = floors.Where(p => sourceOwners.Contains(p.Value)).Select(p => p.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
                var targets = floors.Where(p => targetOwners.Contains(p.Value)).Select(p => p.Key).ToHashSet();
                if (!Connect(sources, targets, whole)) return false;
            }
            // Connectivity is a level: up to C additional distinct room connections, where possible.
            // No request to change dimensions or room count is ever silently approximated.
            var pairs = (from a in Enumerable.Range(0, rooms.Count)
                         from b in Enumerable.Range(a + 1, rooms.Count - a - 1)
                         where !connections.Contains(new(a, b)) select new RoomConnection(a, b)).ToList();
            routing.Shuffle(pairs);
            var added = 0;
            foreach (var pair in pairs)
            {
                if (added == code.Connectivity) break;
                if (Connect(rooms[pair.A].Cells().ToList(), rooms[pair.B].Cells().ToHashSet(), whole, preferWide: true)) added++;
            }
            // Prefer useful wide extra routes. Tight configurations may still need
            // a narrow alternative to honor their requested connection count.
            foreach (var pair in pairs.Where(p => !connections.Contains(p)))
            {
                if (added == code.Connectivity) break;
                if (Connect(rooms[pair.A].Cells().ToList(), rooms[pair.B].Cells().ToHashSet(), whole)) added++;
            }
            return added == Math.Min(code.Connectivity, pairs.Count);
        }

        private List<Cell> Partition(Region r, int count)
        {
            if (count == 1)
            {
                int RoomSize(int extent)
                {
                    var maximum = code.Excavation == 1 ? Math.Min(9, extent - 2) : extent - 2;
                    var minimum = code.Excavation == 1 ? 3 : Math.Max(3, (maximum * (code.Excavation == 2 ? 65 : 85) + 99) / 100);
                    return minimum + geometry.Next(maximum - minimum + 1);
                }
                var w = RoomSize(r.W);
                var h = RoomSize(r.H);
                var room = new DungeonRoom(r.X + 1 + geometry.Next(r.W - w - 1), r.Y + 1 + geometry.Next(r.H - h - 1), w, h);
                var cells = room.Cells().ToList(); var owner = rooms.Count; rooms.Add(room);
                foreach (var c in cells) floors.Add(c, owner);
                foreach (var c in cells)
                foreach (var n in Neighbors(c)) if (room.Contains(n)) passages.Add(Key(c, n));
                return cells;
            }
            List<(Region A, Region B, int CountA)> options = [];
            foreach (var vertical in new[] { true, false })
            for (var offset = 6; offset <= (vertical ? r.W : r.H) - 6; offset++)
            {
                var a = vertical ? new Region(r.X, r.Y, offset, r.H) : new(r.X, r.Y, r.W, offset);
                var b = vertical ? new Region(r.X + offset, r.Y, r.W - offset, r.H) : new(r.X, r.Y + offset, r.W, r.H - offset);
                for (var n = Math.Max(1, count - b.Capacity); n <= Math.Min(count - 1, a.Capacity); n++) options.Add((a, b, n));
            }
            if (options.Count == 0) { failed = true; return []; }
            var split = options[geometry.Next(options.Count)];
            var left = Partition(split.A, split.CountA); var right = Partition(split.B, count - split.CountA);
            if (failed) return [];
            joins.Add((left, right.ToHashSet()));
            return left.Concat(right).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        }

        // Multi-source route through unexcavated rock, favoring room for two-Cell width.
        // Existing floors are endpoints only.
        // Seal incidental floor contacts with ordinary Walls, so close parallel passages stay separate.
        private bool Connect(List<Cell> sources, HashSet<Cell> targets, Region bounds, bool preferWide = false)
        {
            routing.Shuffle(sources);
            Dictionary<Cell, Cell?> previous = []; Dictionary<Cell, int> costs = [];
            PriorityQueue<Cell, (int Cost, int Order)> queue = new(); var order = 0;
            foreach (var source in sources) { previous[source] = null; costs[source] = 0; queue.Enqueue(source, (0, order++)); }
            var turn = routing.Next(4);
            Cell? end = null;
            while (queue.TryDequeue(out var c, out var priority))
            {
                if (priority.Cost != costs[c]) continue;
                if (targets.Contains(c)) { end = c; break; }
                var neighbors = Neighbors(c).ToArray();
                for (var i = 0; i < 4; i++)
                {
                    var n = neighbors[(i + turn) % 4];
                    if (!bounds.Contains(n)) continue;
                    if (floors.ContainsKey(n) && !targets.Contains(n)) continue;
                    var dx = n.X - c.X; var dy = n.Y - c.Y;
                    bool Free(Cell side) => bounds.Contains(side) && !floors.ContainsKey(side);
                    var wide = Free(new(n.X - dy, n.Y + dx)) || Free(new(n.X + dy, n.Y - dx));
                    var cost = costs[c] + (targets.Contains(n) || wide ? 1 : 9);
                    if (costs.TryGetValue(n, out var oldCost) && oldCost <= cost) continue;
                    costs[n] = cost;
                    previous[n] = c;
                    queue.Enqueue(n, (cost, order++));
                }
            }
            if (end is null) return false;
            List<Cell> path = []; for (Cell? c = end; c is not null; c = previous[c]) path.Add(c);
            path.Reverse();
            if (preferWide && path.Count > 6)
            {
                var narrow = 0;
                for (var i = 1; i < path.Count - 1; i++)
                {
                    var dx = path[i].X - path[i - 1].X; var dy = path[i].Y - path[i - 1].Y;
                    bool Blocked(Cell side) => !bounds.Contains(side) || floors.ContainsKey(side);
                    if (Blocked(new(path[i].X - dy, path[i].Y + dx)) && Blocked(new(path[i].X + dy, path[i].Y - dx))) narrow++;
                }
                if (narrow * 100 > (path.Count - 2) * 15) return false;
            }
            var ownerA = floors[path[0]]; var ownerB = floors[path[^1]];
            if (ownerA == ownerB) return false;
            connections.Add(new(Math.Min(ownerA, ownerB), Math.Max(ownerA, ownerB)));
            var widthDraw = routing.Next(10);
            // Narrow draws are short chokepoints, never long single-file hallways.
            var width = widthDraw == 0 && path.Count <= 6 ? 1 : widthDraw < 8 ? 2 : 3;
            var side = routing.Next(2) == 0 ? -1 : 1;
            var excavated = path.Where(c => !floors.ContainsKey(c)).ToHashSet();
            // Widen the routed spine structurally. Existing floors remain endpoints only;
            // lateral contacts stay sealed, including beside single-Cell Door portals.
            for (var i = 1; i < path.Count - 1; i++)
            {
                var dx = path[i].X - path[i - 1].X;
                var dy = path[i].Y - path[i - 1].Y;
                var remaining = width - 1;
                foreach (var direction in new[] { side, -side })
                for (var lane = 1; lane < width && remaining > 0; lane++)
                {
                    var cell = new Cell(path[i].X - dy * direction * lane, path[i].Y + dx * direction * lane);
                    if (!bounds.Contains(cell) || floors.ContainsKey(cell)) break;
                    excavated.Add(cell);
                    remaining--;
                }
            }
            foreach (var cell in excavated) floors.Add(cell, ownerA);
            foreach (var cell in excavated)
            foreach (var neighbor in Neighbors(cell))
                if (excavated.Contains(neighbor)) passages.Add(Key(cell, neighbor));
            corridors.Add(new(width, path.ToArray(), excavated.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray()));
            for (var i = 0; i < path.Count; i++)
            {
                floors.TryAdd(path[i], ownerA);
                if (i > 0) passages.Add(Key(path[i - 1], path[i]));
            }
            // Doors only at actual room entrances, never across rock or a cramped corner.
            foreach (var edge in new[] { Key(path[0], path[1]), Key(path[^2], path[^1]) })
                if (rooms.Any(r => r.Contains(edge.Item1) != r.Contains(edge.Item2))) portals.Add(edge);
            return true;
        }

        public GeneratedDungeon Populate()
        {
            var doorRandom = Pcg32.Stream(code.Seed!.Value, "doors", attempt);
            List<EdgeDefinition> edges = [];
            var clearance = new HashSet<Cell>();
            foreach (var c in floors.Keys.OrderBy(c => c.Y).ThenBy(c => c.X))
            foreach (var n in new[] { new Cell(c.X + 1, c.Y), new Cell(c.X, c.Y + 1) })
            {
                if (!floors.ContainsKey(n)) continue;
                var key = Key(c, n);
                var kind = !passages.Contains(key) ? EdgeKind.Wall :
                    portals.Contains(key) && doorRandom.Next(4) < code.Doors ? EdgeKind.ClosedDoor : EdgeKind.None;
                if (portals.Contains(key)) { clearance.Add(c); clearance.Add(n); }
                if (kind != EdgeKind.None) edges.Add(new(c, n.X != c.X ? EdgeDirection.Right : EdgeDirection.Down, kind));
            }
            var board = new BoardDefinition { Width = code.Width, Height = code.Height, DefaultTerrain = TerrainKind.SolidRock,
                Cells = floors.Keys.OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => new CellDefinition(c, TerrainKind.StoneFloor)).ToList(), Edges = edges };
            var place = Pcg32.Stream(code.Seed!.Value, "placement", attempt);
            var startCandidates = Enumerable.Range(0, rooms.Count).Where(i => rooms[i].Cells().Count(c => !clearance.Contains(c)) >= 4).ToArray();
            if (startCandidates.Length == 0) throw new InvalidOperationException("No room can hold four Heroes clear of portals.");
            var start = startCandidates[place.Next(startCandidates.Length)];
            var starts = rooms[start].Cells().Where(c => !clearance.Contains(c)).ToList();
            place.Shuffle(starts);
            // Every room has at least 3 × 3 cells; four interior/clear portal positions are required.
            if (starts.Count < 4) throw new InvalidOperationException("Starting room has insufficient clear placement capacity.");
            var units = HeroesVsMonsters.HeroTypes.Select((id, i) => new UnitPlacement(id, HeroesVsMonsters.Heroes, starts[i], UnitId: $"hero-{i + 1}")).ToList();
            var shell = new ScenarioDefinition { Board = board, Units = units, UnitTypeIds = [.. HeroesVsMonsters.HeroTypes], Agency = [] };
            var distances = VerifyConnectivity(shell);
            var destination = Enumerable.Range(0, rooms.Count).Where(i => i != start)
                .OrderByDescending(i => distances[rooms[i].Center]).ThenBy(i => i).First();
            var encounters = Pcg32.Stream(code.Seed!.Value, "encounters", attempt);
            var composition = Pcg32.Stream(code.Seed!.Value, "composition", attempt);
            var occupied = 0; var shaman = false;
            var candidates = Enumerable.Range(0, rooms.Count).Where(i => i != start).ToList(); encounters.Shuffle(candidates);
            var occupiedCount = Math.Max(1, ((rooms.Count - 1) * (20 + code.Density * 20) + 50) / 100);
            foreach (var index in candidates.Take(occupiedCount))
            {
                var positions = rooms[index].Cells().Where(c => !clearance.Contains(c)).ToList(); place.Shuffle(positions);
                var count = Math.Min(positions.Count, 1 + encounters.Next(code.Density + 1));
                if (count == 0) continue;
                occupied++;
                for (var i = 0; i < count; i++)
                {
                    var weights = Enumerable.Range(1, 5).Select(t => HeroesVsMonsters.ThreatWeight(code.Difficulty, t)).ToArray();
                    var draw = composition.Next(weights.Sum()); var band = 0;
                    while (draw >= weights[band]) draw -= weights[band++];
                    var pool = HeroesVsMonsters.Pool.Where(m => m.Threat == band + 1 && (!shaman || m.UnitTypeId != UnitTypeIds.Shaman)).ToArray();
                    var monster = pool[composition.Next(pool.Length)].UnitTypeId;
                    shaman |= monster == UnitTypeIds.Shaman;
                    units.Add(new(monster, HeroesVsMonsters.Monsters, positions[i], UnitId: $"monster-{units.Count - 3}"));
                }
            }
            var types = units.Select(u => u.UnitTypeId).Distinct(StringComparer.Ordinal).ToList();
            // Shaman's ordinary runtime summon content must be declared even without initial Goblins.
            if (shaman && !types.Contains(UnitTypeIds.Goblin)) types.Add(UnitTypeIds.Goblin);
            var agency = units.Select(u => new AgencyAssignment(u.UnitTypeId, u.SideId,
                u.SideId == HeroesVsMonsters.Heroes ? ControllerKind.Human : ControllerKind.Automated)).Distinct().ToList();
            if (shaman && !agency.Any(a => a.UnitTypeId == UnitTypeIds.Goblin)) agency.Add(new(UnitTypeIds.Goblin, HeroesVsMonsters.Monsters, ControllerKind.Automated));
            var required = code.RequiredHeroes;
            var destinationRoom = rooms[destination];
            var definition = shell with { UnitTypeIds = types, Agency = agency, Loot = new() { Preset = code.LootPreset }, FogOfWarSides = code.FogOfWar ? [HeroesVsMonsters.Heroes] : null,
                Objectives = required == 0 ? [new(HeroesVsMonsters.Heroes), new(HeroesVsMonsters.Monsters)] :
                [new(HeroesVsMonsters.Heroes, ScenarioObjective.ReachArea) { Count = required == 4 ? 0 : required,
                    Selectors = [UnitSelector.SideAtStart(HeroesVsMonsters.Heroes)], Area = new(destinationRoom.X, destinationRoom.Y, destinationRoom.Width, destinationRoom.Height) },
                 new(HeroesVsMonsters.Monsters, ScenarioObjective.DefeatUnits) { Count = 5 - required, Selectors = [UnitSelector.SideAtStart(HeroesVsMonsters.Heroes)] }] };
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ScenarioDefinitionJson.ToJson(definition))));
            return new(code, definition, hash[..16], new(rooms.ToArray(), connections.ToArray(), start, destination,
                distances[destinationRoom.Center], occupied, units.Count - 4, attempt + 1) { Corridors = corridors.ToArray() });
        }
    }
}
