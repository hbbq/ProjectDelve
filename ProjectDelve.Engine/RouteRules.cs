using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public enum RouteMode { Once, Loop, PingPong }
public sealed record Waypoint(string Id, ObjectiveArea Area);
public sealed record Route(string Id, ImmutableArray<string> WaypointIds, RouteMode Mode = RouteMode.Once)
{
    public bool Equals(Route? other) => other is not null && Id == other.Id && Mode == other.Mode &&
        (WaypointIds.IsDefault || other.WaypointIds.IsDefault ? WaypointIds.IsDefault == other.WaypointIds.IsDefault : WaypointIds.SequenceEqual(other.WaypointIds));
    public override int GetHashCode() => HashCode.Combine(Id, Mode);
}
public sealed record FollowRoute(string RouteId, int CurrentIndex = 0, int Direction = 1);

public static class RouteRules
{
    internal static void ValidateDefinitions(GameState state)
    {
        var board = state.Physical.Board;
        if (state.Waypoints.Any(w => w is null || string.IsNullOrWhiteSpace(w.Id) ||
                w.Area is not { X: >= 0, Y: >= 0, W: > 0, H: > 0 } a ||
                (long)a.X + a.W > board.Width || (long)a.Y + a.H > board.Height) ||
            state.Waypoints.Select(w => w.Id).Distinct().Count() != state.Waypoints.Count)
            throw new ArgumentException("Waypoints require unique IDs and on-board rectangles.");
        if (state.Routes.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) || !Enum.IsDefined(r.Mode) ||
                r.WaypointIds.IsDefaultOrEmpty || r.WaypointIds.Any(id => !state.Waypoints.Any(w => w.Id == id))) ||
            state.Routes.Select(r => r.Id).Distinct().Count() != state.Routes.Count)
            throw new ArgumentException("Routes require unique IDs and nonempty resolved Waypoint references.");
    }
    internal static bool IsValid(FollowRoute? progress, GameState state) => progress is not null &&
        state.Routes.SingleOrDefault(r => r.Id == progress.RouteId) is { } route &&
        progress.CurrentIndex >= 0 && progress.CurrentIndex < route.WaypointIds.Length &&
        progress.Direction is 1 or -1 && (route.Mode == RouteMode.PingPong || progress.Direction == 1);

    public static bool Contains(ObjectiveArea area, Cell cell) => cell.X >= area.X && cell.Y >= area.Y &&
        cell.X < (long)area.X + area.W && cell.Y < (long)area.Y + area.H;

    public static FollowRoute? Next(FollowRoute route, Route definition)
    {
        var count = definition.WaypointIds.Length;
        if (definition.Mode == RouteMode.Once)
            return route.CurrentIndex + 1 == count ? null : route with { CurrentIndex = route.CurrentIndex + 1 };
        if (definition.Mode == RouteMode.Loop)
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
        if (order.Route is not { } route) return;
        var definition = state.Routes.Single(r => r.Id == route.RouteId);
        var waypoint = state.Waypoints.Single(w => w.Id == definition.WaypointIds[route.CurrentIndex]);
        if (!FootprintGeometry.OccupiedCells(state, unitId).Any(c => Contains(waypoint.Area, c))) return;
        var next = Next(route, definition);
        state.AssignOrder(token, next is null ? OrderKind.Normal : OrderKind.FollowRoute, route: next);
        state.WaypointAdvancedThisActivation = true;
        emit(new RulesEvent("WaypointReached", unitId, TypeId: unit.TypeId)
            { SideId = unit.SideId, RouteId = definition.Id, WaypointId = waypoint.Id });
        emit(new RulesEvent(next is null ? "RouteCompleted" : "RouteAdvanced", unitId, TypeId: unit.TypeId)
            { SideId = unit.SideId, RouteId = definition.Id, WaypointId = waypoint.Id });
    }

    internal static string? ChooseMove(DecisionRequest request, IGameplayQueries queries, FollowRoute route)
    {
        var id = request.UnitId!;
        var definition = queries.RouteDefinition(route.RouteId);
        var area = queries.WaypointArea(definition.WaypointIds[route.CurrentIndex]);
        var closedDoors = queries.BehaviorsOf(id).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        int? Distance(Cell position, ObjectiveArea target) => queries.RouteDistanceToArea(id, position, target, closedDoors);
        var current = Distance(queries.PositionOf(id), area);
        var next = Next(route, definition);
        var lookahead = queries.BehaviorsOf(id).HasFlag(UnitBehavior.RouteLookahead) && next is not null;
        var options = request.Candidates.Where(c => request.Kind == DecisionKind.Move || c.Kind == ActivationChoiceKind.Move)
            .Select(c => (Key: (string?)c.Key, Position: c.Destination!, Length: MovementRules.Cost(c.Path!)));
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
                ? Distance(x.Option.Position, queries.WaypointArea(definition.WaypointIds[next!.CurrentIndex])) ?? int.MaxValue : 0)
            .ThenBy(x => x.Option.Length)
            .ThenBy(x => x.Option.Position.Y).ThenBy(x => x.Option.Position.X)
            .ThenBy(x => x.Option.Key, StringComparer.Ordinal).FirstOrDefault()?.Option.Key ?? stay;
    }
}
