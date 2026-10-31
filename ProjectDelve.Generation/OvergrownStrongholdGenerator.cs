using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>A setup policy: plan a mission graph, realize its sites and approaches, then populate roles.</summary>
public static class OvergrownStrongholdGenerator
{
    public const int MaxAttempts = 24;
    public static GeneratedDungeon Generate(GenerationCode configuration)
    {
        var code = configuration.Complete();
        if (code.Profile != "OS") throw new ArgumentException("Overgrown Stronghold requires profile OS.");
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var layout = new Layout(code, attempt);
            if (!layout.Build()) continue;
            var result = layout.Populate();
            if (result is null) continue;
            _ = GameEngine.CreateGame(result.Definition);
            return result;
        }
        throw new ArgumentException($"Overgrown Stronghold could not meet its route/placement quality checks after {MaxAttempts} deterministic attempts. Try a different seed or dimensions.");
    }

    private static Pcg32 Stream(ulong seed, string domain, int attempt)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"DNG2:OS:{seed:X16}:{domain}:{attempt}")));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(hash), BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(8)));
    }
    private enum SiteKind { Entry, Court, Hall, Ruin, Grove, Vault }
    private sealed record Site(string Name, DungeonRoom Room, SiteKind Kind);
    private sealed class Layout(GenerationCode code, int attempt)
    {
        private readonly Pcg32 plan = Stream(code.Seed!.Value, "mission", attempt);
        private readonly Pcg32 ground = Stream(code.Seed!.Value, "geography", attempt);
        private readonly Pcg32 encounters = Stream(code.Seed!.Value, "encounters", attempt);
        private readonly List<Site> sites = [];
        private readonly List<RoomConnection> links = [];
        private readonly List<DungeonCorridor> paths = [];
        private readonly Dictionary<Cell, TerrainKind> terrain = [];
        private readonly Dictionary<Cell, TerrainDefinition> terrainOverrides = [];
        private readonly Dictionary<(Cell, Cell), EdgeKind> edges = [];
        private readonly HashSet<Cell> protectedGround = [];
        private readonly HashSet<Cell> portals = [];
        private readonly List<UnitPlacement> units = [];
        private readonly List<LootMarker> loot = [];
        private string planName = "";
        private int alternateDistance;
        private int destinationDistance;
        private int cacheCount;
        private const int Goal = 2;
        private bool Inside(Cell c) => c.X > 0 && c.Y > 0 && c.X < code.Width - 1 && c.Y < code.Height - 1;
        private bool Passable(Cell c) => terrainOverrides.TryGetValue(c, out var definition)
            ? definition.Passable : terrain.TryGetValue(c, out var kind) && kind.Passable();
        private static (Cell, Cell) Key(Cell a, Cell b) => a.Y < b.Y || a.Y == b.Y && a.X < b.X ? (a, b) : (b, a);
        private static IEnumerable<Cell> Neighbors(Cell c)
        { yield return new(c.X, c.Y - 1); yield return new(c.X + 1, c.Y); yield return new(c.X, c.Y + 1); yield return new(c.X - 1, c.Y); }
        private static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        public bool Build()
        {
            PlanSites();
            // Reject overlapping buildings before realizing geography; no accidental shared walls.
            for (var a = 0; a < sites.Count; a++)
            for (var b = a + 1; b < sites.Count; b++)
                if (sites[a].Room.Cells().Any(sites[b].Room.Contains)) return false;

            var rockSeeds = Enumerable.Range(0, 3 + code.Terrain).Select(_ => new Cell(ground.Next(code.Width), ground.Next(code.Height))).ToArray();
            for (var y = 0; y < code.Height; y++)
            for (var x = 0; x < code.Width; x++)
            {
                var cell = new Cell(x, y);
                var rock = code.Terrain >= 2 && rockSeeds.Any(p => Distance(p, cell) < 3 + code.Terrain);
                terrain[cell] = !Inside(cell) || rock ? TerrainKind.SolidRock : TerrainKind.Tree;
            }
            foreach (var site in sites)
            {
                foreach (var c in site.Room.Cells())
                {
                    PaintDisk(c, code.Openness == 3 ? 2 : 1, TerrainKind.Grass);
                    protectedGround.Add(c);
                }
            }
            foreach (var connection in links) CarveApproach(connection);
            foreach (var site in sites) RealizeSite(site);
            AddLandmarks();
            AddCampfires();

            // Four Heroes stand together; no initial monster, loot or furnishing occupies this apron.
            var start = sites[0].Room.Center;
            var starts = new[] { start, new(start.X - 1, start.Y), new(start.X, start.Y - 1), new Cell(start.X - 1, start.Y - 1) };
            units.AddRange(HeroesVsMonsters.HeroTypes.Select((type, i) => new UnitPlacement(type, HeroesVsMonsters.Heroes, starts[i], UnitId: $"hero-{i + 1}")));
            var all = Flood(start);
            if (all.Count != terrain.Keys.Count(Passable) || !all.TryGetValue(sites[Goal].Room.Center, out destinationDistance)) return false;
            if (destinationDistance < Math.Min(code.Width, code.Height) / 3) return false;
            var direct = Path(start, sites[Goal].Room.Center);
            // Check actual emitted terrain AND edges. Wide roads alone do not excuse a mandatory one-Cell throat.
            foreach (var c in direct.Where(c => !sites[0].Room.Contains(c) && !sites[Goal].Room.Contains(c)))
                if (!Flood(start, p => p == c).ContainsKey(sites[Goal].Room.Center)) return false;
            var middle = direct[direct.Count / 2];
            var alternative = Flood(start, c => Distance(c, middle) <= 1 && !sites[0].Room.Contains(c) && !sites[Goal].Room.Contains(c));
            if (!alternative.TryGetValue(sites[Goal].Room.Center, out alternateDistance)) return false;
            if (alternateDistance > destinationDistance * 3) return false;
            return true;
        }

        private void PlanSites()
        {
            var family = plan.Next(3);
            var orientation = plan.Next(4);
            void Add(string name, int px, int py, SiteKind kind)
            {
                if ((orientation & 1) != 0) px = 100 - px;
                if ((orientation & 2) != 0) py = 100 - py;
                var w = Math.Clamp(4 + code.Width / 18 + plan.Next(2), 4, 8);
                var h = Math.Clamp(4 + code.Height / 20 + plan.Next(2), 4, 8);
                // Compact maps need room between sites; larger sites gain interior tactical space.
                if (Math.Min(code.Width, code.Height) < 24) { w = 4; h = 4; }
                var cx = 2 + px * (code.Width - 5) / 100;
                var cy = 2 + py * (code.Height - 5) / 100;
                var jitter = Math.Min(code.Width, code.Height) >= 26 ? 1 : 0;
                cx += plan.Next(2 * jitter + 1) - jitter; cy += plan.Next(2 * jitter + 1) - jitter;
                sites.Add(new(name, new(Math.Clamp(cx - w / 2, 2, code.Width - w - 2), Math.Clamp(cy - h / 2, 2, code.Height - h - 2), w, h), kind));
            }
            switch (family)
            {
                case 0:
                    planName = "Broken Bailey";
                    Add("Trailhead", 5, 48, SiteKind.Entry); Add("Broken gatecourt", 45, 48, SiteKind.Ruin);
                    Add("Warden's hall", 94, 47, SiteKind.Hall); Add("Overgrown orchard", 42, 3, SiteKind.Grove);
                    Add("Old storehouse", 63, 94, SiteKind.Hall); Add("Wayside crypt", 5, 94, SiteKind.Vault);
                    links.AddRange([new(0, 1), new(1, 2), new(0, 3), new(3, 2), new(1, 4), new(4, 2), new(0, 5)]);
                    break;
                case 1:
                    planName = "Twin Halls";
                    Add("Woodland landing", 4, 4, SiteKind.Entry); Add("North barracks", 61, 4, SiteKind.Hall);
                    Add("Shattered sanctuary", 94, 94, SiteKind.Ruin); Add("Hunter's grove", 4, 66, SiteKind.Grove);
                    Add("Crossroads court", 47, 51, SiteKind.Court); Add("Sealed treasury", 96, 44, SiteKind.Vault);
                    links.AddRange([new(0, 1), new(1, 2), new(0, 3), new(3, 2), new(1, 4), new(4, 3), new(4, 5)]);
                    break;
                default:
                    planName = "Crown Ring";
                    Add("Pilgrim's approach", 2, 48, SiteKind.Entry); Add("North watchtower", 42, 2, SiteKind.Ruin);
                    Add("Inner keep", 54, 49, SiteKind.Hall); Add("Abandoned camp", 39, 96, SiteKind.Grove);
                    Add("Eastern cloister", 96, 36, SiteKind.Court); Add("Buried vault", 96, 96, SiteKind.Vault);
                    links.AddRange([new(0, 1), new(1, 4), new(4, 3), new(3, 0), new(1, 2), new(3, 2), new(4, 5)]);
                    break;
            }
            // Long approaches need something to investigate or fight around. Insert concrete
            // roadside ruins into the mission graph before drawing terrain, rather than scattered foes.
            foreach (var link in links.ToArray())
            {
                var a = sites[link.A].Room.Center; var b = sites[link.B].Room.Center;
                if (Distance(a, b) < 23 || sites.Count >= 10) continue;
                var room = new DungeonRoom((a.X + b.X) / 2 - 2, (a.Y + b.Y) / 2 - 2, 4, 4);
                if (room.Cells().Any(c => !Inside(c) || sites.Any(s => s.Room.Cells().Any(p => Distance(c, p) < 2)))) continue;
                var index = sites.Count;
                sites.Add(new($"Roadside redoubt {index - 5}", room, SiteKind.Ruin));
                links.Remove(link); links.Add(new(link.A, index)); links.Add(new(index, link.B));
            }
        }

        private void PaintDisk(Cell center, int radius, TerrainKind kind)
        {
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var c = new Cell(center.X + dx, center.Y + dy);
                if (Inside(c) && dx * dx + dy * dy <= radius * radius + 1 &&
                    !(kind == TerrainKind.Grass && terrain.GetValueOrDefault(c) is TerrainKind.StoneFloor or TerrainKind.Gravel)) terrain[c] = kind;
            }
        }
        private void CarveApproach(RoomConnection connection)
        {
            var a = sites[connection.A].Room.Center; var b = sites[connection.B].Room.Center;
            // A short stone road is exposed; the longer routes meander through screening woodland.
            var direct = connection.A == 0 && connection.B == 1 || connection.A == 1 && connection.B == Goal;
            var bend = new Cell((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            if (!direct)
            {
                var offset = Math.Max(1, Math.Min(code.Width, code.Height) / 12);
                bend = Math.Abs(a.X - b.X) > Math.Abs(a.Y - b.Y)
                    ? bend with { Y = Math.Clamp(bend.Y + (ground.Next(2) == 0 ? -offset : offset), 2, code.Height - 3) }
                    : bend with { X = Math.Clamp(bend.X + (ground.Next(2) == 0 ? -offset : offset), 2, code.Width - 3) };
            }
            List<Cell> spine = [a];
            void Walk(Cell target)
            {
                while (spine[^1] != target)
                {
                    var c = spine[^1];
                    var horizontal = c.X != target.X && (c.Y == target.Y || ground.Next(2) == 0);
                    spine.Add(horizontal ? c with { X = c.X + Math.Sign(target.X - c.X) } : c with { Y = c.Y + Math.Sign(target.Y - c.Y) });
                }
            }
            Walk(bend); Walk(b);
            var radius = code.Openness == 3 ? 3 : direct ? code.Openness : 1;
            foreach (var c in spine)
            {
                PaintDisk(c, radius, TerrainKind.Grass);
                protectedGround.Add(c);
                foreach (var n in Neighbors(c).Where(Inside)) protectedGround.Add(n);
            }
            foreach (var c in spine) terrain[c] = direct ? TerrainKind.StoneFloor : TerrainKind.Gravel;
            paths.Add(new(2 * radius + 1, spine, spine.SelectMany(c => Neighbors(c).Append(c)).Where(Inside).Distinct().ToArray()));
        }

        private void RealizeSite(Site site)
        {
            var r = site.Room;
            if (site.Kind is SiteKind.Entry or SiteKind.Grove or SiteKind.Court)
            {
                foreach (var c in r.Cells()) terrain[c] = site.Kind == SiteKind.Court ? TerrainKind.Gravel : TerrainKind.Grass;
                return;
            }
            foreach (var c in r.Cells()) terrain[c] = site.Kind == SiteKind.Ruin && ground.Next(5) == 0 ? TerrainKind.Grass : TerrainKind.StoneFloor;
            var horizontal = ground.Next(2) == 0;
            foreach (var c in r.Cells())
            foreach (var n in Neighbors(c).Where(n => !r.Contains(n) && Inside(n)))
            {
                var key = Key(c, n);
                var cross = horizontal ? c.X == r.X || c.X == r.X + r.Width - 1 : c.Y == r.Y || c.Y == r.Y + r.Height - 1;
                var offset = horizontal ? c.Y - r.Center.Y : c.X - r.Center.X;
                var nearCenter = offset is 0 or -1;
                var aperture = cross && nearCenter;
                var kind = aperture ? EdgeKind.None : EdgeKind.Wall;
                // Each building has two broad opposing access points. The front includes a Door;
                // the breached rear gives an alternative and leaves large footprints maneuvering room.
                if (aperture)
                {
                    PaintDisk(n, 1, TerrainKind.Grass);
                    portals.Add(c); portals.Add(n);
                    if (site.Kind != SiteKind.Ruin && (horizontal ? c.X == r.X : c.Y == r.Y))
                        kind = EdgeKind.ClosedDoor;
                }
                else if (site.Kind == SiteKind.Ruin && ground.Next(4) == 0) kind = EdgeKind.None;
                else if (site.Kind == SiteKind.Hall && ground.Next(6) == 0) kind = EdgeKind.WallWithWindow;
                if (kind != EdgeKind.None) edges[key] = kind;
            }
            // Furnish a corner, preserving the complete central 2x2 footprint and every portal.
            var corner = new Cell(r.X + (ground.Next(2) == 0 ? 0 : r.Width - 1), r.Y + (ground.Next(2) == 0 ? 0 : r.Height - 1));
            if (!portals.Contains(corner) && Distance(corner, r.Center) > 1) terrain[corner] = TerrainKind.StoneFloorWithTable;
        }

        private void AddLandmarks()
        {
            // Groves and ruined masonry break up broad lawns between approaches. Keep their shapes
            // clustered so they create cover and flanks instead of a salt-and-pepper obstacle field.
            var groveCenters = terrain.Where(p => p.Value.Passable() && !protectedGround.Contains(p.Key) &&
                !sites.Any(s => s.Room.Contains(p.Key))).Select(p => p.Key).ToList();
            ground.Shuffle(groveCenters);
            var groveBudget = code.Width * code.Height / (code.Openness == 3 ? 160 : 90);
            foreach (var center in groveCenters)
            {
                if (groveBudget == 0) break;
                var patch = new[] { center, new(center.X + 1, center.Y), new(center.X, center.Y + 1), new Cell(center.X + 1, center.Y + 1) };
                if (patch.Any(c => !Passable(c) || protectedGround.Contains(c) || portals.Contains(c) || sites.Any(s => s.Room.Contains(c)))) continue;
                var old = patch.Select(c => terrain[c]).ToArray();
                var kind = code.Terrain >= 2 && ground.Next(3) == 0 ? TerrainKind.SolidRock : TerrainKind.Tree;
                foreach (var c in patch) terrain[c] = kind;
                if (Flood(sites[0].Room.Center).Count != terrain.Keys.Count(Passable))
                { for (var i = 0; i < patch.Length; i++) terrain[patch[i]] = old[i]; }
                else groveBudget--;
            }
            // Small clustered obstacles belong to the edge of a clearing, never the promised route.
            var candidates = terrain.Where(p => p.Value.Passable() && !protectedGround.Contains(p.Key) && !portals.Contains(p.Key) &&
                !sites.Any(s => s.Room.Contains(p.Key)) && Neighbors(p.Key).Any(n => !Passable(n))).Select(p => p.Key).ToList();
            ground.Shuffle(candidates);
            var budget = code.Width * code.Height / (code.Openness == 3 ? 100 : 65);
            foreach (var c in candidates.Take(budget))
            {
                var old = terrain[c];
                terrain[c] = code.Terrain == 3 && ground.Next(3) == 0 ? TerrainKind.Lava :
                    code.Terrain >= 2 && ground.Next(3) == 0 ? TerrainKind.SolidRock : TerrainKind.Tree;
                // Avoid decorative islands and sealed pockets. Connected floor is a quality invariant.
                if (Flood(sites[0].Room.Center).Count != terrain.Keys.Count(Passable)) terrain[c] = old;
            }
            // Meadow remains open around routes; tree-lined boundaries provide a readable board edge.
            if (code.Terrain == 0)
                foreach (var c in terrain.Keys.ToArray())
                    if (Inside(c) && terrain[c] == TerrainKind.Tree && Neighbors(c).Any(Passable) && ground.Next(3) == 0) terrain[c] = TerrainKind.Grass;
        }

        private void AddCampfires()
        {
            // A fire belongs to a clearing or ruined encampment, not to a random road Cell.
            // Keep the mission destination and its complete interior available to every Hero.
            var eligible = sites.Where((site, index) => index != Goal &&
                site.Kind is SiteKind.Grove or SiteKind.Court or SiteKind.Ruin).ToList();
            ground.Shuffle(eligible);
            var routes = paths.SelectMany(path => path.Cells).ToHashSet();
            var budget = Math.Clamp(1 + code.Width * code.Height / 600, 1, 4);
            var campfire = TerrainCatalog.Find(TerrainCatalog.All, "campfire");
            foreach (var site in eligible)
            {
                if (terrainOverrides.Count >= budget) break;
                var chance = (site.Kind == SiteKind.Grove ? 65 : 45) - (code.Openness == 3 ? 15 : 0);
                if (ground.Next(100) >= chance) continue;
                var room = site.Room;
                var candidates = room.Cells().Where(c => Passable(c) && !routes.Contains(c) &&
                    !portals.Any(p => Distance(c, p) <= 1) && Distance(c, sites[0].Room.Center) >= 6 &&
                    !(c.X >= room.Center.X - 1 && c.X <= room.Center.X && c.Y >= room.Center.Y - 1 && c.Y <= room.Center.Y) &&
                    terrainOverrides.Keys.All(other => Distance(c, other) >= 6) &&
                    Neighbors(c).All(n => Passable(n) && CanCross(c, n)) &&
                    Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3)
                        .Where(dx => dx != 0 || dy != 0).Select(dx => new Cell(c.X + dx, c.Y + dy))).Count(Passable) >= 6).ToList();
                ground.Shuffle(candidates);
                foreach (var cell in candidates.OrderBy(c => Distance(c, room.Center)))
                {
                    terrainOverrides[cell] = campfire;
                    // Even a spacious-looking clearing can hide a cut vertex. Never seal a pocket.
                    if (Flood(sites[0].Room.Center).Count == terrain.Keys.Count(Passable)) break;
                    terrainOverrides.Remove(cell);
                }
            }
        }

        private Dictionary<Cell, int> Flood(Cell start, Func<Cell, bool>? blocked = null)
        {
            Dictionary<Cell, int> distances = new() { [start] = 0 }; Queue<Cell> queue = new(); queue.Enqueue(start);
            while (queue.TryDequeue(out var c))
            foreach (var n in Neighbors(c))
                if (Passable(n) && !distances.ContainsKey(n) && blocked?.Invoke(n) != true && CanCross(c, n))
                { distances[n] = distances[c] + 1; queue.Enqueue(n); }
            return distances;
        }
        private bool CanCross(Cell a, Cell b) => !edges.TryGetValue(Key(a, b), out var edge) || edge is EdgeKind.OpenDoor or EdgeKind.ClosedDoor or EdgeKind.None;
        private List<Cell> Path(Cell start, Cell goal)
        {
            var distances = Flood(start); List<Cell> path = [goal];
            while (path[^1] != start)
                path.Add(Neighbors(path[^1]).First(c => distances.TryGetValue(c, out var distance) && distance == distances[path[^1]] - 1 && CanCross(c, path[^1])));
            path.Reverse(); return path;
        }

        public GeneratedDungeon? Populate()
        {
            var occupied = new HashSet<Cell>(units.Select(u => u.Anchor));
            var start = sites[0].Room.Center;
            var occupiedSites = new HashSet<int>();
            var distanceFromStart = Flood(start);
            bool Place(string typeId, int siteIndex, bool leader = false)
            {
                var type = UnitContent.Find(typeId)!;
                if (type.Unique && units.Any(u => u.UnitTypeId == typeId)) return false;
                var site = sites[siteIndex];
                var positions = site.Room.Cells().Where(c => Passable(c) && !portals.Contains(c) && distanceFromStart[c] >= 6).ToList();
                encounters.Shuffle(positions);
                // Prefer sentries toward the entrance, other defenders toward the room's heart.
                int SentryRank(Cell c) => portals.Where(p => site.Room.Contains(p)).Any(p =>
                    Neighbors(p).Any(n => !site.Room.Contains(n) && Passable(n) &&
                        !edges.ContainsKey(Key(p, n)) && c == new Cell(2 * p.X - n.X, 2 * p.Y - n.Y))) ? 0 : 1;
                positions = positions.OrderBy(c => typeId == UnitTypeIds.SkeletonArcher ? SentryRank(c) : 0)
                    .ThenBy(c => typeId == UnitTypeIds.SkeletonArcher ? Distance(c, start) : Distance(c, site.Room.Center)).ToList();
                foreach (var c in positions)
                {
                    var cells = FootprintGeometry.OccupiedCells(type.Footprint, c).ToArray();
                    if (cells.Any(p => !Passable(p) || occupied.Contains(p) || portals.Contains(p))) continue;
                    if (cells.Any(p => Neighbors(p).Any(n => cells.Contains(n) && !CanCross(p, n)))) continue;
                    if (type.Footprint == Footprint.TwoByTwo && !LargeFootprintHasRoom(c)) continue;
                    units.Add(new(typeId, HeroesVsMonsters.Monsters, c, UnitId: leader ? "stronghold-warden" : $"defender-{units.Count - 3}"));
                    foreach (var cell in cells) occupied.Add(cell);
                    occupiedSites.Add(siteIndex); return true;
                }
                return false;
            }
            // The warden anchors the stronghold. Unusual content is selected for a tactical role.
            var leader = code.Difficulty switch
            {
                1 => UnitTypeIds.Grunt,
                2 => encounters.Next(2) == 0 ? UnitTypeIds.Zombie : UnitTypeIds.Grunt,
                3 => encounters.Next(3) == 0 ? UnitTypeIds.GoblinKing : UnitTypeIds.Troll,
                4 => encounters.Next(3) == 0 ? UnitTypeIds.TowerTroll : UnitTypeIds.DisplacerDemon,
                _ => encounters.Next(3) == 0 ? UnitTypeIds.RedDragon : UnitTypeIds.DisplacerDemon
            };
            if (!Place(leader, Goal, true)) return null;
            Place(UnitTypeIds.SkeletonArcher, 1);
            if (code.Treasure > 0) Place(UnitTypeIds.SkeletonArcher, 5);
            foreach (var index in Enumerable.Range(6, sites.Count - 6)) Place(UnitTypeIds.SkeletonArcher, index);
            var enemyBudget = 2 + code.Density * 2 + code.Width * code.Height / 350;
            if (leader == UnitTypeIds.RedDragon) enemyBudget -= 2;
            var roster = code.Difficulty switch
            {
                1 => new[] { UnitTypeIds.Goblin, UnitTypeIds.Grunt, UnitTypeIds.Zombie },
                2 => new[] { UnitTypeIds.Goblin, UnitTypeIds.Grunt, UnitTypeIds.SkeletonArcher, UnitTypeIds.Zombie },
                3 => new[] { UnitTypeIds.Goblin, UnitTypeIds.SkeletonArcher, UnitTypeIds.BombImp, UnitTypeIds.Ghost },
                4 => new[] { UnitTypeIds.SkeletonArcher, UnitTypeIds.BombImp, UnitTypeIds.Ghost, UnitTypeIds.Troll },
                _ => new[] { UnitTypeIds.SkeletonArcher, UnitTypeIds.BombImp, UnitTypeIds.Troll, UnitTypeIds.Ghost }
            };
            // Alternate approaches have pressure, but never a random wall of enemies on the trailhead.
            var siteOrder = new[] { 3, 4, Goal, 1, 5 };
            for (var i = 0; units.Count - 4 < enemyBudget && i < enemyBudget * 3; i++)
                Place(roster[encounters.Next(roster.Length)], siteOrder[i % siteOrder.Length]);
            if (code.Difficulty >= 4 && code.Density >= 2 && encounters.Next(3) == 0) Place(UnitTypeIds.Shaman, Goal);

            // Treasure lives off the mission destination, with multiple unopened markers per cache.
            // Fixed presets can scale to provision these caches. Dynamic presets
            // retain their exact quantities and cap reservations below.
            var cacheSites = new[] { 5, 4, 3 };
            foreach (var index in cacheSites.Take(code.Treasure))
            {
                var site = sites[index];
                var cell = site.Room.Cells().Where(c => Passable(c) && !occupied.Contains(c) && !portals.Contains(c))
                    .OrderByDescending(c => distanceFromStart[c]).ThenBy(c => c.Y).ThenBy(c => c.X).FirstOrDefault();
                if (cell is null) return null;
                var count = 2 + code.Treasure + encounters.Next(3) + code.Width * code.Height / 700;
                for (var i = 0; i < count; i++) loot.Add(new($"cache-{index}-{i + 1}", cell));
                cacheCount++;
            }
            // Canonical drop expectations include every initial Unit. Summons retain their canonical drops;
            // this is finite setup provision, never a promise of an inexhaustible farming supply.
            var expectedDrops = (units.Sum(u => UnitContent.Find(u.UnitTypeId)!.LootDropSuccesses) + 5) / 6;
            var preset = LootPresets.Find(code.LootPreset);
            var presetSize = new LootSettings { Preset = code.LootPreset }.ResolveComposition().Sum(row => row.Count);
            var multiplier = preset.IsDynamic ? 1 : Math.Max(1, (loot.Count + expectedDrops + presetSize - 1) / presetSize);
            if (preset.IsDynamic && loot.Count > presetSize)
            {
                // Keep one marker per cache per pass, after all original random
                // draws. Positions, physical IDs and other seeded content stay intact.
                var retained = loot.GroupBy(marker => marker.Position)
                    .SelectMany(cache => cache.Select((marker, index) => (marker, index)))
                    .OrderBy(row => row.index).Take(presetSize).Select(row => row.marker.Id).ToHashSet(StringComparer.Ordinal);
                loot.RemoveAll(marker => !retained.Contains(marker.Id));
                cacheCount = loot.Select(marker => marker.Position).Distinct().Count();
            }
            var types = units.Select(u => u.UnitTypeId).Distinct(StringComparer.Ordinal).ToList();
            if (types.Contains(UnitTypeIds.Shaman) && !types.Contains(UnitTypeIds.Goblin)) types.Add(UnitTypeIds.Goblin);
            var agency = units.Select(u => new AgencyAssignment(u.UnitTypeId, u.SideId, u.SideId == HeroesVsMonsters.Heroes ? ControllerKind.Human : ControllerKind.Automated)).Distinct().ToList();
            if (types.Contains(UnitTypeIds.Shaman) && !agency.Any(a => a.UnitTypeId == UnitTypeIds.Goblin)) agency.Add(new(UnitTypeIds.Goblin, HeroesVsMonsters.Monsters, ControllerKind.Automated));
            var orders = agency.Where(a => a.SideId == HeroesVsMonsters.Monsters).Select(a =>
                a.UnitTypeId == UnitTypeIds.Goblin || a.UnitTypeId == UnitTypeIds.BombImp
                    ? new InitialGroupOrder(a.UnitTypeId, a.SideId, OrderKind.Protect, Target: OrderTarget.ForUnit("stronghold-warden"))
                    : new InitialGroupOrder(a.UnitTypeId, a.SideId, a.UnitTypeId == UnitTypeIds.SkeletonArcher || a.UnitTypeId == leader || a.UnitTypeId == UnitTypeIds.Shaman ? OrderKind.HoldPosition : OrderKind.Normal)).ToList();
            var goal = sites[Goal].Room;
            var required = code.RequiredHeroes;
            var definition = new ScenarioDefinition
            {
                Board = new() { Width = code.Width, Height = code.Height, DefaultTerrain = TerrainKind.Grass,
                    Cells = terrain.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Where(p => p.Value != TerrainKind.Grass || terrainOverrides.ContainsKey(p.Key))
                        .Select(p => new CellDefinition(p.Key, terrainOverrides.ContainsKey(p.Key) ? TerrainKind.StoneFloor : p.Value)
                        { TerrainTypeId = terrainOverrides.GetValueOrDefault(p.Key)?.Id,
                            CoalGeneration = p.Value == TerrainKind.Lava ? false : null }).ToList(),
                    Edges = edges.OrderBy(p => p.Key.Item1.Y).ThenBy(p => p.Key.Item1.X).ThenBy(p => p.Key.Item2.Y)
                        .Select(p => new EdgeDefinition(p.Key.Item1, p.Key.Item1.X == p.Key.Item2.X ? EdgeDirection.Down : EdgeDirection.Right, p.Value)).ToList() },
                UnitTypeIds = types, Units = units, Agency = agency, Orders = orders,
                Loot = new() { Preset = code.LootPreset, PresetMultiplier = multiplier > 1 ? multiplier : null }, LootMarkers = loot,
                FogOfWarSides = code.FogOfWar ? [HeroesVsMonsters.Heroes] : null,
                Objectives = required == 0 ? [new(HeroesVsMonsters.Heroes), new(HeroesVsMonsters.Monsters)] :
                    [new(HeroesVsMonsters.Heroes, ScenarioObjective.ReachArea) { Count = required == 4 ? 0 : required,
                        Selectors = [UnitSelector.SideAtStart(HeroesVsMonsters.Heroes)], Area = new(goal.X + 1, goal.Y + 1, goal.Width - 2, goal.Height - 2) },
                     new(HeroesVsMonsters.Monsters, ScenarioObjective.DefeatUnits) { Count = 5 - required, Selectors = [UnitSelector.SideAtStart(HeroesVsMonsters.Heroes)] }]
            };
            DungeonGenerator.VerifyConnectivity(definition);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ScenarioDefinitionJson.ToJson(definition))));
            return new(code, definition, hash[..16], new(sites.Select(s => s.Room).ToArray(), links, 0, Goal, destinationDistance, occupiedSites.Count, units.Count - 4, attempt + 1)
            { Profile = "OS", SitePlan = planName, SiteNames = sites.Select(s => s.Name).ToArray(), Corridors = paths, TreasureCaches = cacheCount, LootMultiplier = multiplier, AlternateRouteDistance = alternateDistance });
        }
        private bool LargeFootprintHasRoom(Cell start)
        {
            bool Open(Cell a, Cell b) => !edges.TryGetValue(Key(a, b), out var kind) || kind.Passable();
            bool Fits(Cell anchor)
            {
                var cells = FootprintGeometry.OccupiedCells(Footprint.TwoByTwo, anchor).ToArray();
                return cells.All(Passable) && cells.All(c => Neighbors(c).All(n => !cells.Contains(n) || Open(c, n)));
            }
            // A 2x2 guardian must have useful maneuvering space, not merely a valid initial square.
            HashSet<Cell> visited = [start]; Queue<Cell> queue = new(); queue.Enqueue(start);
            while (queue.TryDequeue(out var c))
            foreach (var n in Neighbors(c))
                if (!visited.Contains(n) && Fits(n) && FootprintGeometry.OccupiedCells(Footprint.TwoByTwo, c)
                    .All(p => Open(p, new(p.X + n.X - c.X, p.Y + n.Y - c.Y))))
                { visited.Add(n); queue.Enqueue(n); }
            return visited.Count >= 8;
        }
    }
}
