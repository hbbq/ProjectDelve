using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public static partial class PopulationGenerator
{
    private static int[] NaturalCandidates(WorldGenerationContext context, PlanningBoard board, bool[] passable,
        HashSet<int> protectedCells, HashSet<int> siteCells, PlanningBudget budget, out int quietRegions)
    {
        var w = board.Width; var path = WorldGenerationContext.WindowPath("population", context.Window);
        var weights = new Dictionary<(int X, int Y), int>();
        List<(int Cell, double Priority)> ranked = []; quietRegions = 0;
        var random = Pcg32.Stream(context.World.Seed, path + "/natural-candidates");
        for (var i = 0; i < passable.Length; i++)
        {
            if (!budget.Spend(1)) break;
            var region = (i % w / 12, i / w / 12);
            if (!weights.TryGetValue(region, out var weight))
            {
                var roll = Pcg32.Stream(context.World.Seed, path + $"/region-{region.Item1}-{region.Item2}").Next(10);
                weights.Add(region, weight = roll < 2 ? 0 : roll < 6 ? 2 : 10);
                if (weight == 0) quietRegions++;
            }
            if (weight == 0 || !passable[i] || protectedCells.Contains(i) || siteCells.Contains(i)) continue;
            var u = (random.NextUInt() + 1.0) / (uint.MaxValue + 2.0);
            ranked.Add((i, -Math.Log(u) / weight));
        }
        return ranked.OrderBy(p => p.Priority).ThenBy(p => p.Cell).Take(768).Select(p => p.Cell).ToArray();
    }

    private static IEnumerable<int> LocalGround(PlanningBoard board, bool[] passable, int start, int radius, PlanningBudget budget)
    {
        var seen = new HashSet<int> { start }; var queue = new Queue<int>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var i))
        {
            if (!budget.Spend(1)) yield break;
            yield return i;
            foreach (var j in board.Neighbors(i)) if (Distance(start, j, board.Width) <= radius && passable[j] &&
                (board.EdgeAt(EdgeKey.Between(i, j, board.Width)).Passable() || board.EdgeAt(EdgeKey.Between(i, j, board.Width)) == EdgeKind.ClosedDoor) && seen.Add(j)) queue.Enqueue(j);
        }
    }

    private static PopulationEncounter? BuildEncounter(string id, PopulationLocation location, List<int> free, int width,
        PopulationSettings settings, int remaining, Pcg32 random, PlanningBoard board, Dictionary<string, UnitType> types,
        GameState state, PlanningBudget budget, Func<int, bool> available, Action<string> fail, out int requested)
    {
        if (location.Intent is { } intent)
            return Structured(id, location, intent, free, settings, remaining, random, board, state, types, budget, fail, out requested);
        var count = location.Kind == "LoneMonster" ? 1 : location.Kind == "GoblinCamp" ? 4 + random.Next(5) : 2 + random.Next(4);
        requested = count; count = Math.Min(count, remaining);
        if (free.Count < count || count == 0) { fail("Insufficient legal occupation space."); return null; }
        if (count < requested) fail($"Unit allowance fallback: placed {count} of {requested} requested Units.");
        if (location.Path is { } path && !OpenPath(board, path, available)) { fail("Patrol path lacks open, unclaimed access."); return null; }
        var type = location.Kind == "RoadPatrol" ? UnitTypeIds.Grunt : location.Kind == "GoblinCamp" ? UnitTypeIds.Goblin :
            PopulationComposition.Monster(random, settings.Difficulty, location.Kind == "BuildingDefenders");
        if (location.Kind == "LoneMonster" && settings.Difficulty == 5 && random.Next(50) == 0 &&
            !state.Units.Any(u => u.TypeId == UnitTypeIds.RedDragon && u.SideId == HeroesVsMonsters.Monsters)) type = UnitTypeIds.RedDragon;
        if (location.Kind == "BuildingDefenders" && types[type].Footprint != Footprint.OneByOne)
        { fail("Building occupation omitted: current single-Cell portals do not certify large-footprint access."); return null; }
        var chosen = new List<int>(); var claimed = new HashSet<Cell>();
        foreach (var cell in free)
        {
            if (!Fits(types[type], cell, claimed, board, available)) continue;
            if (chosen.Count > 0 && chosen.All(j => Distance(cell, j, width) > 3)) continue;
            chosen.Add(cell); claimed.UnionWith(FootprintGeometry.OccupiedCells(types[type].Footprint, new(cell % width, cell / width)));
            if (chosen.Count == count) break;
        }
        if (chosen.Count != count) { fail("Complete footprints or clustered group access do not fit."); return null; }
        var role = location.Kind == "RoadPatrol" ? "patrol" : location.Kind == "LoneMonster" ? "lone" :
            location.Kind == "BuildingDefenders" ? "resting" : "wilderness";
        return PopulationComposition.FromRoles(id, location.Kind, location.SourceId, width,
            chosen.Select(cell => (type, cell, role, "local", (string?)null)).ToArray(), requested, random,
            location.Kind == "RoadPatrol" ? location.Path : null, types);
    }

    private static PopulationEncounter? Structured(string id, PopulationLocation location, SitePopulationIntent intent, List<int> free,
        PopulationSettings settings, int remaining, Pcg32 random, PlanningBoard board, GameState state, Dictionary<string, UnitType> types, PlanningBudget budget, Action<string> fail, out int requested)
    {
        var maximum = Math.Min(intent.MaximumUnits, settings.Density switch { PopulationDensity.Low => 8, PopulationDensity.High => 16, _ => 12 });
        requested = intent.MinimumUnits + random.Next(Math.Max(1, maximum - intent.MinimumUnits + 1));
        var wanted = Math.Min(remaining, requested);
        if (wanted < intent.MinimumUnits) { fail("Compound minimum exceeds remaining Unit allowance."); return null; }
        var width = board.Width; var separated = intent.AlertTopology == CampAlertTopology.Separated; var allowed = free.ToHashSet();
        var areas = intent.Areas.Where(a => a.Role != "supplies" && (!separated || intent.Areas.Any(b => b.Role == "resting" && b.ClusterId == a.ClusterId)))
            .OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
        var placed = new List<(string Type, int Cell, string Role, string Cluster, string? Area)>();
        var baseType = random.Next(8) == 0 ? UnitTypeIds.Zombie : UnitTypeIds.Goblin;
        // Patrol is an optional active role, not a side effect of being a camp.
        var occupied = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).Select(c => c.Y * width + c.X).ToHashSet();
        var wantsPatrol = !separated && random.Next(4) == 0 && OpenPath(board, intent.PatrolPath, i => board.Passable(i) && !occupied.Contains(i));
        var patrolCells = wantsPatrol ? intent.PatrolPath.ToHashSet() : [];
        for (var slot = 0; slot < wanted - (wantsPatrol ? 1 : 0); slot++)
        {
            if (!budget.Spend(areas.Sum(a => a.Area.Area))) break;
            var role = slot % 4 == 0 ? "sentry" : slot % 4 == 1 && !separated ? "central" : "resting";
            var options = areas.SelectMany(area => area.Area.Indices(width).Where(allowed.Contains).Select(cell => (Area: area, Cell: cell)))
                .Where(p => !patrolCells.Contains(p.Cell) && placed.All(u => u.Cell != p.Cell) &&
                    (!separated || placed.All(u => u.Cluster == p.Area.ClusterId || Distance(u.Cell, p.Cell, width) > DormantRules.AlertDistance)) &&
                    (placed.Count == 0 || separated && placed.All(u => u.Cluster != p.Area.ClusterId) ||
                        placed.Any(u => (!separated || u.Cluster == p.Area.ClusterId) && Distance(u.Cell, p.Cell, width) <= DormantRules.AlertDistance)))
                .OrderBy(p => separated ? placed.Count(u => u.Cluster == p.Area.ClusterId) : 0)
                .ThenBy(p => p.Area.Role == role ? 0 : 1)
                .ThenBy(p => Distance(p.Cell, p.Area.AnchorIndex, width)).ThenBy(p => p.Cell).ThenBy(p => p.Area.Id, StringComparer.Ordinal).ToArray();
            if (options.Length == 0) break;
            var selected = options[0]; var type = baseType;
            if (selected.Area.Role == "sentry" && settings.Difficulty >= 3 && random.Next(3) == 0) type = UnitTypeIds.SkeletonArcher;
            if (selected.Area.Role == "central" && settings.Difficulty >= 4 && random.Next(3) == 0) type = UnitTypeIds.Troll;
            placed.Add((type, selected.Cell, selected.Area.Role, selected.Area.ClusterId, selected.Area.Id));
        }
        IReadOnlyList<int>? patrol = null;
        if (wantsPatrol)
        {
            var anchor = intent.PatrolPath.FirstOrDefault(i => allowed.Contains(i) && placed.All(u => u.Cell != i) && placed.Any(u => Distance(u.Cell, i, width) <= 3), -1);
            if (anchor >= 0) { placed.Add((UnitTypeIds.Goblin, anchor, "patrol", "patrol", null)); patrol = intent.PatrolPath; }
        }
        if (placed.Count < intent.MinimumUnits) { fail("Role areas cannot realize minimum occupation and Alert spacing."); return null; }
        if (placed.Count < requested) fail($"Compound fallback: placed {placed.Count} of {requested} requested Units.");
        return PopulationComposition.FromRoles(id, location.Kind, location.SourceId, width, placed, requested, random, patrol, types) with
        { IntendedAlertTopology = intent.AlertTopology.ToString() };
    }

    private static bool Fits(UnitType type, int anchor, HashSet<Cell> prior, PlanningBoard board, Func<int, bool> available)
    {
        var width = board.Width;
        var cells = FootprintGeometry.OccupiedCells(type.Footprint, new(anchor % width, anchor / width));
        foreach (var c in cells)
        {
            if (c.X < 0 || c.Y < 0 || c.X >= width || c.Y >= board.Height || !available(c.Y * width + c.X) || prior.Contains(c)) return false;
        }
        foreach (var a in cells) foreach (var b in cells.Where(b => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1))
            if (!board.EdgeAt(EdgeKey.Between(a.Y * width + a.X, b.Y * width + b.X, width)).Passable()) return false;
        return true;
    }

    private static bool OpenPath(PlanningBoard board, IReadOnlyList<int> cells, Func<int, bool> available) => cells.Count >= 2 &&
        cells.All(i => i >= 0 && i < board.Width * board.Height && available(i)) &&
        cells.Zip(cells.Skip(1)).All(p => board.Neighbors(p.First).Contains(p.Second) && board.EdgeAt(EdgeKey.Between(p.First, p.Second, board.Width)).Passable());
    private static int Distance(int a, int b, int width) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
}
