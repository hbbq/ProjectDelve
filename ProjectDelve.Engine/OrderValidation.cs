namespace ProjectDelve.Engine;

internal static class OrderValidation
{
    internal static bool IsStructurallyValid(GroupOrder order, GameState state) =>
        order is not null && (order.Kind switch
        {
            OrderKind.GuardArea => order.Destination is null && order.Target is null && order.Route is null &&
                ValidArea(order.Area, state.Physical.Board),
            OrderKind.FollowRoute or OrderKind.PatrolRoute => order.Destination is null && order.Target is null && order.Area is null &&
                RouteRules.IsValid(order.Route, state),
            _ => order.Area is null && order.Route is null && IsStructurallyValid(order.Kind, order.Destination, order.Target, state.Physical.Board)
        });

    private static bool ValidArea(ObjectiveArea? area, Board board) => area is { X: >= 0, Y: >= 0, W: > 0, H: > 0 } a &&
        (long)a.X + a.W <= board.Width && (long)a.Y + a.H <= board.Height;

    internal static bool IsStructurallyValid(OrderKind kind, Cell? destination, OrderTarget? target, Board board)
    {
        if (!Enum.IsDefined(kind)) return false;
        if (kind == OrderKind.MoveTo)
            return destination is not null && target is null && MovementRules.Inside(board, destination);
        if (destination is not null) return false;

        return kind switch
        {
            OrderKind.Attack => target is not null && target.Kind is OrderTargetKind.Unit or OrderTargetKind.Group or OrderTargetKind.Side &&
                HasExactPayload(target),
            OrderKind.Protect => target is { Kind: OrderTargetKind.Unit } && HasExactPayload(target),
            OrderKind.InteractWith => target is { Kind: OrderTargetKind.Door } && HasExactPayload(target) &&
                target.Door is { } door && IsAdjacent(door.A, door.B) && door.IsCanonical,
            OrderKind.Normal or OrderKind.DoNothing or OrderKind.HoldPosition or OrderKind.Retreat => target is null,
            _ => false
        };
    }

    internal static bool IsValidAssignment(GroupOrder order, GameState state)
    {
        if (!IsStructurallyValid(order, state)) return false;
        if (order.Target is not { } target) return true;
        return target.Kind switch
        {
            OrderTargetKind.Unit when target.UnitId is { } id =>
                state.Units.SingleOrDefault(unit => unit.Id == id) is { CurrentHp: > 0 } unit &&
                state.Physical.Figures.Any(figure => figure.Id == id) &&
                (order.Kind == OrderKind.Attack ? state.AreHostile(order.Token.SideId, unit.SideId) :
                    order.Kind == OrderKind.Protect && unit.SideId == order.Token.SideId),
            OrderTargetKind.Group when target.Group is { } group => order.Kind == OrderKind.Attack &&
                state.AreHostile(order.Token.SideId, group.SideId) && state.Controllers.Any(controller => controller.Token == group),
            OrderTargetKind.Side when target.SideId is { } side => order.Kind == OrderKind.Attack &&
                state.AreHostile(order.Token.SideId, side) && state.Objectives.Any(objective => objective.SideId == side),
            OrderTargetKind.Door when target.Door is { } door => order.Kind == OrderKind.InteractWith &&
                state.Physical.Board.EdgeBetween(door.A, door.B) is EdgeKind.OpenDoor or EdgeKind.ClosedDoor,
            _ => false
        };
    }

    private static bool HasExactPayload(OrderTarget target)
    {
        if (!Enum.IsDefined(target.Kind)) return false;
        var unit = !string.IsNullOrWhiteSpace(target.UnitId);
        var group = target.Group is { } token && !string.IsNullOrWhiteSpace(token.TypeId) &&
            !string.IsNullOrWhiteSpace(token.SideId) && (token.GroupId is null || !string.IsNullOrWhiteSpace(token.GroupId));
        var side = !string.IsNullOrWhiteSpace(target.SideId);
        var door = target.Door is not null;
        return target.Kind switch
        {
            OrderTargetKind.Unit => unit && !group && !side && !door,
            OrderTargetKind.Group => !unit && group && !side && !door,
            OrderTargetKind.Side => !unit && !group && side && !door,
            OrderTargetKind.Door => !unit && !group && !side && door,
            _ => false
        };
    }

    private static bool IsAdjacent(Cell? a, Cell? b) => a is not null && b is not null &&
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;
}
