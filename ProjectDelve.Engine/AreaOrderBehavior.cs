namespace ProjectDelve.Engine;

// Decision-time analysis only: no alert, pursuit target or formation state survives a choice.
internal static class AreaOrderBehavior
{
    internal static ObjectiveArea Home(IGameplayQueries queries, GroupOrder order)
    {
        if (order.Kind == OrderKind.GuardArea) return order.Area!;
        var route = queries.RouteDefinition(order.Route!.RouteId);
        return queries.WaypointArea(route.WaypointIds[order.Route.CurrentIndex]);
    }

    internal static int Engagement(IGameplayQueries queries, string id) =>
        (int)Math.Min(int.MaxValue - 2L, (long)queries.EffectiveMovOf(id) + queries.EffectiveRngOf(id));
    internal static int Leash(IGameplayQueries queries, string id) => Engagement(queries, id) + 2;

    internal static IReadOnlyList<string> Threats(IGameplayQueries queries, string id, ObjectiveArea home)
    {
        var current = queries.PositionOf(id);
        var closed = queries.BehaviorsOf(id).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        var flee = queries.BehaviorsOf(id).HasFlag(UnitBehavior.Flee);
        var ignoreLeash = queries.BehaviorsOf(id).HasFlag(UnitBehavior.IgnoreLeash);
        return queries.KnownHostileUnitIds(id).Where(target =>
            (ignoreLeash || queries.FootprintDistanceToArea(target, queries.PositionOf(target), home) <= Leash(queries, id)) &&
            queries.ManhattanDistanceBetweenUnits(id, target) <= Engagement(queries, id) &&
            (flee || queries.DistanceToAttackPositionFrom(id, current, new[] { target }, closed).HasValue)).ToArray();
    }
}

public sealed partial class OrderAwareAutomatedProvider
{
    private string? ChooseAreaOrder(DecisionRequest request, IGameplayQueries queries, GroupOrder order)
    {
        if (request.IsMoveAfterAttack) return normal.Choose(request, queries);
        var id = request.UnitId!;
        var home = AreaOrderBehavior.Home(queries, order);
        var targets = AreaOrderBehavior.Threats(queries, id, home);
        if (request.Kind == DecisionKind.Act)
            return DefaultAutomatedProvider.SelectAction(request, queries, targets);
        if (request.Kind == DecisionKind.Activation && !request.Candidates.Any(c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay))
            return DefaultAutomatedProvider.SelectActivation(request, queries, targets);
        return ChooseOrdinaryMovement(request, queries, (_, current) =>
        {
            if (Has(request, queries, UnitBehavior.Flee) && (order.Kind == OrderKind.GuardArea || targets.Count > 0))
                return SelectAreaReturn(request, queries, home, flee: true);
            if (targets.Count > 0 && (Has(request, queries, UnitBehavior.IgnoreLeash) || queries.FootprintDistanceToArea(id, current, home) <= AreaOrderBehavior.Leash(queries, id)))
            {
                // Change preferences only. Every remaining destination is still an Engine candidate.
                var bounded = request with { Candidates = request.Candidates.Where(c =>
                    c.Kind is not (ActivationChoiceKind.Move or ActivationChoiceKind.Stay) && request.Kind != DecisionKind.Move ||
                    Has(request, queries, UnitBehavior.IgnoreLeash) || queries.FootprintDistanceToArea(id, c.Destination ?? current, home) <= AreaOrderBehavior.Leash(queries, id)).ToList() };
                if (MovementOptions(bounded, queries).Any(option =>
                    Has(request, queries, UnitBehavior.IgnoreLeash) || queries.FootprintDistanceToArea(id, option.Position, home) <= AreaOrderBehavior.Leash(queries, id)))
                    return SelectAttackMove(bounded, queries, targets, current, current);
            }
            return order.Kind == OrderKind.PatrolRoute
                ? RouteRules.ChooseMove(request, queries, order.Route!)
                : SelectAreaReturn(request, queries, home, flee: false);
        });
    }

    private static string? SelectAreaReturn(DecisionRequest request, IGameplayQueries queries, ObjectiveArea home, bool flee)
    {
        var id = request.UnitId!;
        var closed = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        var current = queries.PositionOf(id);
        var options = MovementOptions(request, queries).ToList();
        if (flee)
        {
            var inside = options.Where(o => queries.FootprintDistanceToArea(id, o.Position, home) == 0)
                .Select(o => new { Option = o, Safety = queries.DistanceToNearestHostileFrom(id, o.Position, closed) })
                .OrderByDescending(o => o.Safety)
                .ThenByDescending(o => HostileAdjacency(request, queries, o.Option.Position))
                .ThenBy(o => o.Option.Length).ThenBy(o => o.Option.Position.Y).ThenBy(o => o.Option.Position.X)
                .ThenBy(o => o.Option.Key, StringComparer.Ordinal).FirstOrDefault();
            if (inside is not null) return inside.Option.Key;
        }
        else if (queries.FootprintDistanceToArea(id, current, home) == 0)
            return StayChoice(request) ?? NoneOrForced(request);
        var remaining = queries.WholeFootprintRouteDistanceToArea(id, current, home, closed);
        var approach = options.Select(o => new
            {
                Option = o, Remaining = queries.WholeFootprintRouteDistanceToArea(id, o.Position, home, closed),
                Safety = flee ? queries.DistanceToNearestHostileFrom(id, o.Position, closed) : null
            })
            .Where(o => o.Remaining.HasValue && (!remaining.HasValue || o.Remaining < remaining))
            .OrderBy(o => o.Remaining).ThenByDescending(o => o.Safety)
            .ThenByDescending(o => HostileAdjacency(request, queries, o.Option.Position))
            .ThenBy(o => o.Option.Length).ThenBy(o => o.Option.Position.Y).ThenBy(o => o.Option.Position.X)
            .ThenBy(o => o.Option.Key, StringComparer.Ordinal).FirstOrDefault();
        return approach?.Option.Key ?? StayChoice(request) ?? NoneOrForced(request);
    }
}
