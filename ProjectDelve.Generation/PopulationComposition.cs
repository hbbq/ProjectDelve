using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal static class PopulationComposition
{
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
