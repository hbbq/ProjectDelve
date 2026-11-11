using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record PopulationMonster(string UnitTypeId, int Threat, int RelativeWeight = 2)
{
    internal int Weight(int difficulty) => RelativeWeight * HeroesVsMonsters.ThreatWeight(difficulty, Threat);
}

internal static class PopulationComposition
{
    // References only: definitions and abilities come from the supplied effective content.
    // Keep optional published content out of the C#-only standalone Dungeon Generator.
    private static readonly IReadOnlyList<PopulationMonster> Monsters = [
        .. HeroesVsMonsters.Pool.Where(p => p.UnitTypeId != UnitTypeIds.Shaman)
            .Select(p => new PopulationMonster(p.UnitTypeId, p.Threat)),
        new("demolition-imp", 3, RelativeWeight: 1)
    ];

    internal static PopulationMonster[] Pool(IEnumerable<string> availableTypeIds)
    {
        var available = availableTypeIds.ToHashSet(StringComparer.Ordinal);
        return Monsters.Where(p => available.Contains(p.UnitTypeId)).ToArray();
    }

    internal static string Select(Pcg32 random, int difficulty, IReadOnlyList<PopulationMonster> pool)
    {
        var weights = pool.Select(p => p.Weight(difficulty)).ToArray();
        var draw = random.Next(weights.Sum());
        for (var i = 0; i < pool.Count; i++) { draw -= weights[i]; if (draw < 0) return pool[i].UnitTypeId; }
        throw new InvalidOperationException("Empty Monster selection.");
    }

    internal static string Monster(Pcg32 random, int difficulty, IEnumerable<string> availableTypeIds, bool building = false)
    {
        if (building) return difficulty >= 4 && random.Next(4) == 0 ? UnitTypeIds.Troll :
            difficulty >= 3 && random.Next(5) == 0 ? UnitTypeIds.Ghost : UnitTypeIds.Zombie;
        // Existing coarse authoring weights, never gameplay ratings. Summoners are
        // excluded here: this milestone bounds initial occupation, not summoned crowds.
        return Select(random, difficulty, Pool(availableTypeIds));
    }

    internal static PopulationEncounter FromRoles(string id, string kind, string source, int width,
        IReadOnlyList<(string Type, int Cell, string Role, string Cluster, string? Area)> placements,
        int requested, Pcg32 random, IReadOnlyList<int>? patrol = null, IReadOnlyDictionary<string, UnitType>? types = null)
    {
        var units = new List<UnitPlacement>(); var roles = new List<PopulationRoleAssignment>();
        var orders = new List<InitialGroupOrder>(); var agency = new List<AgencyAssignment>();
        var groups = new Dictionary<(string Type, string Role, string Cluster), string>();
        var waypoints = (patrol ?? []).Select((cell, k) => new Waypoint(id + "/waypoint-" + k,
            new(cell % width, cell / width, 1, 1))).ToArray();
        Route[] routes = waypoints.Length >= 2 ? [new(id + "/route", [.. waypoints.Select(p => p.Id)], RouteMode.PingPong)] : [];
        foreach (var p in placements)
        {
            var key = (p.Type, p.Role, p.Cluster);
            if (!groups.TryGetValue(key, out var group))
            {
                groups.Add(key, group = id + "/group-" + groups.Count);
                agency.Add(new(p.Type, HeroesVsMonsters.Monsters, ControllerKind.Automated, group));
                var order = p.Role == "patrol" ? OrderKind.PatrolRoute : p.Role is "lone" or "wilderness" ? OrderKind.Normal : OrderKind.GuardArea;
                var cluster = placements.Where(x => x.Cluster == p.Cluster).SelectMany(x => FootprintGeometry.OccupiedCells(
                    types?.GetValueOrDefault(x.Type)?.Footprint ?? UnitContent.Find(x.Type)?.Footprint ?? Footprint.OneByOne,
                    new(x.Cell % width, x.Cell / width))).ToArray();
                var left = cluster.Min(c => c.X); var top = cluster.Min(c => c.Y);
                orders.Add(new(p.Type, HeroesVsMonsters.Monsters, order, GroupId: group,
                    Route: order == OrderKind.PatrolRoute ? new(routes.Single().Id) : null,
                    Area: order == OrderKind.GuardArea ? new(left, top, cluster.Max(c => c.X) - left + 1, cluster.Max(c => c.Y) - top + 1) : null));
            }
            var uid = id + "/unit-" + units.Count;
            units.Add(new(p.Type, HeroesVsMonsters.Monsters, new(p.Cell % width, p.Cell / width),
                Posture: p.Role == "resting" && random.Next(2) == 0 ? Posture.Lying : Posture.Upright,
                UnitId: uid, GroupId: group, Dormant: p.Role != "patrol",
                WakeDistance: p.Role is "sentry" or "patrol" ? 5 : p.Role is "resting" or "central" ? 1 : 6));
            roles.Add(new(uid, p.Role, p.Area));
        }
        return new(id, kind, source, groups.Values.ToArray(), units, orders, routes, waypoints)
        { Agency = agency, Roles = roles, RequestedUnits = requested };
    }

    internal static PopulationEncounter Compose(string id, string kind, string source, string type, int[] cells, int width,
        bool dormant, bool lying, bool split, CellBounds bounds, IReadOnlyList<Waypoint>? waypoints = null, IReadOnlyList<Route>? routes = null)
    {
        var groups = Enumerable.Range(0, split ? 2 : 1).Select(i => id + "/group-" + i).ToArray();
        var units = cells.Select((i, k) => new UnitPlacement(type, HeroesVsMonsters.Monsters, new(i % width, i / width),
            Posture: lying && k % 2 == 0 ? Posture.Lying : Posture.Upright, UnitId: id + "/unit-" + k,
            GroupId: groups[k % groups.Length], Dormant: dormant)).ToArray();
        var orders = groups.Select(group => routes is { Count: > 0 }
            ? new InitialGroupOrder(type, HeroesVsMonsters.Monsters, OrderKind.FollowRoute, Route: new(routes[0].Id), GroupId: group)
            : new InitialGroupOrder(type, HeroesVsMonsters.Monsters, OrderKind.GuardArea, GroupId: group,
                Area: new(bounds.X, bounds.Y, bounds.Width, bounds.Height))).ToArray();
        return new(id, kind, source, groups, units, orders, routes ?? [], waypoints ?? [])
        { Agency = groups.Select(group => new AgencyAssignment(type, HeroesVsMonsters.Monsters, ControllerKind.Automated, group)).ToArray() };
    }
}
internal static partial class GoblinCampPopulationGenerator
{
    internal static PopulationEncounter Build(string id, PopulationLocation location, int[] cells, int width, Pcg32 random) =>
        PopulationComposition.Compose(id, "GoblinCamp", location.SourceId, UnitTypeIds.Goblin, cells, width, true, false,
            cells.Length >= 4 && random.Next(2) == 0, location.Bounds);
}
internal static partial class ZombieHousePopulationGenerator
{
    internal static PopulationEncounter Build(string id, PopulationLocation location, int[] cells, int width, Pcg32 random) =>
        PopulationComposition.Compose(id, "ZombieHouse", location.SourceId, UnitTypeIds.Zombie, cells, width, true, random.Next(2) == 0, false, location.Bounds);
}
internal static partial class RoadPatrolPopulationGenerator
{
    internal static PopulationEncounter Build(string id, PopulationLocation location, int[] cells, int width, Pcg32 random)
    {
        var path = location.Path!.ToArray();
        if (random.Next(2) == 0) Array.Reverse(path);
        var waypoints = path.Select((i, k) => new Waypoint(id + "/waypoint-" + k, new(i % width, i / width, 1, 1))).ToArray();
        Route[] routes = [new(id + "/route", [.. waypoints.Select(p => p.Id)], RouteMode.PingPong)];
        return PopulationComposition.Compose(id, "RoadPatrol", location.SourceId, UnitTypeIds.Grunt, cells, width, false, false, false,
            location.Bounds, waypoints, routes);
    }
}
