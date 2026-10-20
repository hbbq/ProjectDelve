using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public enum RouteMode { Once, Loop, PingPong }
public sealed record FollowRoute(ImmutableArray<ObjectiveArea> Waypoints, RouteMode Mode = RouteMode.Once,
    int CurrentIndex = 0, int Direction = 1)
{
    public bool Equals(FollowRoute? other) => other is not null && Mode == other.Mode &&
        CurrentIndex == other.CurrentIndex && Direction == other.Direction &&
        (Waypoints.IsDefault || other.Waypoints.IsDefault ? Waypoints.IsDefault == other.Waypoints.IsDefault :
            Waypoints.SequenceEqual(other.Waypoints));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode); hash.Add(CurrentIndex); hash.Add(Direction);
        if (!Waypoints.IsDefault) foreach (var waypoint in Waypoints) hash.Add(waypoint);
        return hash.ToHashCode();
    }
}

public static class RouteRules
{
    internal static bool IsValid(FollowRoute? route, Board board) => route is not null &&
        !route.Waypoints.IsDefaultOrEmpty && Enum.IsDefined(route.Mode) &&
        route.CurrentIndex >= 0 && route.CurrentIndex < route.Waypoints.Length &&
        route.Direction is 1 or -1 && (route.Mode == RouteMode.PingPong || route.Direction == 1) &&
        route.Waypoints.All(a => a is not null && a.X >= 0 && a.Y >= 0 && a.W > 0 && a.H > 0 &&
            (long)a.X + a.W <= board.Width && (long)a.Y + a.H <= board.Height);

    public static bool Contains(ObjectiveArea area, Cell cell) => cell.X >= area.X && cell.Y >= area.Y &&
        cell.X < (long)area.X + area.W && cell.Y < (long)area.Y + area.H;

    public static FollowRoute? Next(FollowRoute route)
    {
        var count = route.Waypoints.Length;
        if (route.Mode == RouteMode.Once)
            return route.CurrentIndex + 1 == count ? null : route with { CurrentIndex = route.CurrentIndex + 1 };
        if (route.Mode == RouteMode.Loop)
            return route with { CurrentIndex = (route.CurrentIndex + 1) % count };
        if (count == 1) return route;
        var direction = route.Direction;
        if (route.CurrentIndex + direction < 0 || route.CurrentIndex + direction >= count) direction = -direction;
        return route with { CurrentIndex = route.CurrentIndex + direction, Direction = direction };
    }

    internal static void CompleteMove(GameState state, string unitId, Action<RulesEvent> emit)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        var token = new ActivationToken(unit.TypeId, unit.SideId);
        var order = state.OrderFor(token);
        if (order.Route is not { } route || !FootprintGeometry.OccupiedCells(state, unitId)
            .Any(c => Contains(route.Waypoints[route.CurrentIndex], c))) return;
        var next = Next(route);
        state.AssignOrder(token, next is null ? OrderKind.Normal : OrderKind.FollowRoute, route: next);
        emit(new RulesEvent(next is null ? "RouteCompleted" : "RouteAdvanced", unitId, TypeId: unit.TypeId)
            { SideId = unit.SideId });
    }

    internal static string? ChooseMove(DecisionRequest request, IGameplayQueries queries, FollowRoute route)
    {
        var id = request.UnitId!;
        var area = route.Waypoints[route.CurrentIndex];
        var closedDoors = queries.BehaviorsOf(id).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        int? Distance(Cell position, ObjectiveArea target) => queries.RouteDistanceToArea(id, position, target, closedDoors);
        var current = Distance(queries.PositionOf(id), area);
        var next = Next(route);
        var lookahead = queries.BehaviorsOf(id).HasFlag(UnitBehavior.RouteLookahead) && next is not null;
        var options = request.Candidates.Where(c => request.Kind == DecisionKind.Move || c.Kind == ActivationChoiceKind.Move)
            .Select(c => (Key: (string?)c.Key, Position: c.Destination!, Length: c.Path!.Count - 1));
        var stay = request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay)?.Key;
        if (stay is not null || request.AllowsNone)
            options = options.Append((stay, queries.PositionOf(id), 0));
        var moves = options
            .Select(c => new { Option = c, Distance = Distance(c.Position, area) })
            .Where(x => x.Distance.HasValue && (x.Distance == 0 || !current.HasValue || x.Distance < current))
            .ToList();
        // Stay is a completed ordinary Move, and can complete the current waypoint.
        return moves.OrderBy(x => x.Distance)
            .ThenBy(x => x.Distance == 0 && lookahead
                ? Distance(x.Option.Position, next!.Waypoints[next.CurrentIndex]) ?? int.MaxValue : 0)
            .ThenBy(x => x.Option.Length)
            .ThenBy(x => x.Option.Position.Y).ThenBy(x => x.Option.Position.X)
            .ThenBy(x => x.Option.Key, StringComparer.Ordinal).FirstOrDefault()?.Option.Key ?? stay;
    }
}
