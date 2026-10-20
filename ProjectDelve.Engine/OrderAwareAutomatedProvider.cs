namespace ProjectDelve.Engine;

// Resolves the group's standing Order while retaining established Unit Type behavior
// for every choice the Order does not override.
public sealed class OrderAwareAutomatedProvider : IDecisionProvider
{
    private readonly DefaultAutomatedProvider normal = new();
    private sealed record RankedMove(Candidate Candidate, int? Remaining, int Length);
    private sealed record MoveOption(Candidate? Candidate, string? Key, Cell Position, int Length);

    public string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.UnitId is null || request.Kind is DecisionKind.SelectUnit or DecisionKind.RollDice)
            return normal.Choose(request, queries);

        var order = queries.EffectiveOrderFor(request.UnitId);
        if (order.Kind != OrderKind.DoNothing && queries.BehaviorsOf(request.UnitId).HasFlag(UnitBehavior.Worker))
            return normal.Choose(request, queries);
        if (order.Kind == OrderKind.FollowRoute)
            return ChooseOrdinaryMovement(request, queries, (_, _) => RouteRules.ChooseMove(request, queries, order.Route!));
        return order.Kind switch
        {
            OrderKind.Normal => normal.Choose(request, queries),
            OrderKind.DoNothing => ChooseDoNothing(request, queries),
            OrderKind.HoldPosition => ChoosePositionOrder(request, queries, null),
            OrderKind.MoveTo => ChoosePositionOrder(request, queries, order.Destination!),
            OrderKind.Attack => ChooseAttack(request, queries, order.Target!),
            OrderKind.Protect => ChooseProtect(request, queries, order.Target!.UnitId!),
            OrderKind.Retreat => ChooseRetreat(request, queries),
            OrderKind.InteractWith => ChooseInteractWith(request, queries, order.Target!.Door!),
            _ => throw new ArgumentOutOfRangeException(nameof(order.Kind))
        };
    }

    private string? ChooseDoNothing(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.Kind == DecisionKind.Activation)
        {
            var end = request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.EndTurn);
            if (end is not null) return end.Key;
            var stay = request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay);
            if (stay is not null) return stay.Key;
        }
        if (request.AllowsNone) return null;
        if (request.Candidates.Count == 1) return request.Candidates[0].Key;
        return normal.Choose(request, queries);
    }

    private string? ChoosePositionOrder(DecisionRequest request, IGameplayQueries queries, Cell? destination)
    {
        if (request.IsMoveAfterAttack) return normal.Choose(request, queries);

        var normalChoice = normal.Choose(request, queries);
        if (request.Kind == DecisionKind.Activation)
        {
            var selected = request.Candidates.Single(candidate => candidate.Key == normalChoice);
            if (selected.Kind is not (ActivationChoiceKind.Move or ActivationChoiceKind.Stay)) return normalChoice;
            return SelectOrdinaryMove(request, queries, destination);
        }
        if (request.Kind == DecisionKind.Move) return SelectOrdinaryMove(request, queries, destination);
        return normalChoice;
    }

    private string? ChooseAttack(DecisionRequest request, IGameplayQueries queries, OrderTarget target)
    {
        var targetIds = queries.EligibleAttackOrderTargets(request.UnitId!, target);
        if (targetIds.Count == 0) return normal.Choose(request, queries);

        if (request.Kind == DecisionKind.Act)
            return DefaultAutomatedProvider.SelectAction(request, queries, targetIds);
        if (request.Kind == DecisionKind.Activation &&
            !request.Candidates.Any(candidate => candidate.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay))
            return DefaultAutomatedProvider.SelectActivation(request, queries, targetIds);

        return ChooseOrdinaryMovement(request, queries,
            (position, current) => SelectAttackMove(request, queries, targetIds, position, current));
    }

    private string? ChooseProtect(DecisionRequest request, IGameplayQueries queries, string protectedId)
    {
        if (!queries.IsLivingFriendlyUnit(request.UnitId!, protectedId)) return normal.Choose(request, queries);
        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        return ChooseOrdinaryMovement(request, queries, (_, current) =>
        {
            var unitId = request.UnitId!;
            var stay = StayChoice(request);
            var options = MovementOptions(request, queries).ToList();

            if (Has(request, queries, UnitBehavior.Flee))
            {
                var near = options.Select(option => new
                    {
                        Option = option,
                        Distance = queries.DistanceToUnitFrom(unitId, option.Position, protectedId, closedDoors),
                        Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position, closedDoors)
                    })
                    .Where(item => item.Distance <= queries.EffectiveMovOf(unitId))
                    .OrderByDescending(item => item.Safety)
                    .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                    .ThenBy(item => item.Option.Length)
                    .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                    .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
                if (near is not null) return near.Option.Key;

                var approach = RankApproachToUnit(request, options, queries, unitId, protectedId, closedDoors,
                    safetyFirstOnTie: true);
                return approach?.Key ?? stay ?? NoneOrForced(request);
            }

            if (Has(request, queries, UnitBehavior.MaximizeAttackDistance))
            {
                var ranged = options.Where(option =>
                        queries.IsWithinEffectiveRangeAndLos(unitId, option.Position, protectedId) &&
                        queries.CanAttackHostileFrom(unitId, option.Position))
                    .Select(option => new
                    {
                        Option = option,
                        Distance = queries.DistanceToNearestAttackableHostileFrom(unitId, option.Position)
                    })
                    .Where(item => item.Distance.HasValue)
                    .OrderByDescending(item => item.Distance)
                    .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                    .ThenBy(item => item.Option.Length)
                    .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                    .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
                if (ranged is not null) return ranged.Option.Key;
            }

            if (Has(request, queries, UnitBehavior.ProtectWithoutAdjacency))
            {
                var scored = options.Select(option => new
                    {
                        Option = option,
                        Distance = queries.DistanceToUnitFrom(unitId, option.Position, protectedId, closedDoors),
                        IsAdjacent = queries.AreUnitsAdjacentFrom(unitId, option.Position, protectedId),
                        Adjacency = queries.CountAdjacentHostilesFrom(unitId, option.Position)
                    })
                    .Where(item => item.Distance.HasValue).ToList();
                var nonAdjacent = scored.Where(item => !item.IsAdjacent).ToList();
                var pool = nonAdjacent.Count > 0 ? nonAdjacent : scored;
                var protectedChoice = pool
                    .OrderBy(item => item.Distance)
                    .ThenByDescending(item => Has(request, queries, UnitBehavior.PreferHostileAdjacency)
                        ? item.Adjacency : 0)
                    .ThenBy(item => item.Option.Length)
                    .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                    .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
                return protectedChoice?.Option.Key ?? stay ?? NoneOrForced(request);
            }

            if (queries.AreUnitsAdjacent(unitId, protectedId)) return stay ?? NoneOrForced(request);
            var currentDistance = queries.DistanceToAdjacentPositionFrom(unitId, current, protectedId, closedDoors);
            var preferred = RankedMoves(request, queries, candidate =>
                    queries.DistanceToAdjacentPositionFrom(unitId, candidate.Destination!, protectedId, closedDoors))
                .Where(item => item.Remaining.HasValue &&
                    (!currentDistance.HasValue || item.Remaining.Value < currentDistance.Value))
                .FirstOrDefault();
            return preferred?.Candidate.Key ?? stay ?? NoneOrForced(request);
        });
    }

    private string? ChooseRetreat(DecisionRequest request, IGameplayQueries queries) =>
        ChooseOrdinaryMovement(request, queries, (_, _) => DefaultAutomatedProvider.SelectFleeMovement(request, queries,
            queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.ApproachThroughClosedDoors)));

    private string? ChooseInteractWith(DecisionRequest request, IGameplayQueries queries, DoorTarget door)
    {
        if (!queries.IsClosedDoor(door) || !queries.CanInteractWithDoors(request.UnitId!))
            return normal.Choose(request, queries);
        var interaction = request.Candidates.Where(candidate =>
                candidate.Door is not null && Canonical(candidate.Door) == door &&
                (candidate.FreeAction == UnitFreeAction.OpenDoor || candidate.TryOpenDoor is not null))
            .OrderBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault();
        if (interaction is not null) return interaction.Key;
        if (Has(request, queries, UnitBehavior.Flee))
            return ChooseOrdinaryMovement(request, queries, (_, _) =>
                DefaultAutomatedProvider.SelectFleeMovement(request, queries));
        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        if (!queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, queries.PositionOf(request.UnitId!), door,
                closedDoors).HasValue)
            return normal.Choose(request, queries);
        return ChooseOrdinaryMovement(request, queries, (_, current) =>
        {
            var stay = StayChoice(request);
            var currentDistance = queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, current, door, closedDoors);
            var preferred = RankedMoves(request, queries, candidate =>
                    queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, candidate.Destination!, door, closedDoors))
                .Where(item => item.Remaining.HasValue &&
                    (!currentDistance.HasValue || item.Remaining.Value < currentDistance.Value))
                .FirstOrDefault();
            return preferred?.Candidate.Key ?? stay ?? NoneOrForced(request);
        });
    }

    private string? ChooseOrdinaryMovement(DecisionRequest request, IGameplayQueries queries,
        Func<Cell, Cell, string?> select)
    {
        if (request.IsMoveAfterAttack) return normal.Choose(request, queries);
        if (request.Kind == DecisionKind.Move)
        {
            var movePosition = queries.PositionOf(request.UnitId!);
            return select(movePosition, movePosition);
        }
        var normalChoice = normal.Choose(request, queries);
        if (request.Kind == DecisionKind.Activation)
        {
            var selected = request.Candidates.Single(candidate => candidate.Key == normalChoice);
            if (selected.Kind is not (ActivationChoiceKind.Move or ActivationChoiceKind.Stay)) return normalChoice;
        }
        else return normalChoice;
        var current = queries.PositionOf(request.UnitId!);
        return select(current, current);
    }

    private static string? SelectAttackMove(DecisionRequest request, IGameplayQueries queries,
        IReadOnlyCollection<string> targetIds, Cell _, Cell current)
    {
        if (Has(request, queries, UnitBehavior.Flee))
            return SelectFleeAttackMove(request, queries, targetIds);

        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        var stay = StayChoice(request);
        if (Has(request, queries, UnitBehavior.MaximizeAttackDistance))
        {
            var ranged = MovementOptions(request, queries)
                .Where(option => queries.CanAttackAnyFrom(request.UnitId!, option.Position, targetIds))
                .Select(option => new
                {
                    Option = option,
                    Distance = queries.DistanceToNearestAttackableHostileFrom(request.UnitId!, option.Position)
                })
                .Where(item => item.Distance.HasValue)
                .OrderByDescending(item => item.Distance)
                .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                .ThenBy(item => item.Option.Length)
                .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                .ThenBy(item => item.Option.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (ranged is not null) return ranged.Option.Key;
        }
        if (queries.CanAttackAnyFrom(request.UnitId!, current, targetIds)) return stay ?? NoneOrForced(request);
        var immediatelyAttackable = request.Candidates.Where(candidate =>
                (request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move) &&
                queries.CanAttackAnyFrom(request.UnitId!, candidate.Destination!, targetIds))
            .OrderByDescending(candidate => HostileAdjacency(request, queries, candidate.Destination!))
            .ThenBy(candidate => candidate.Path!.Count - 1)
            .ThenBy(candidate => candidate.Destination!.Y).ThenBy(candidate => candidate.Destination!.X)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault();
        if (immediatelyAttackable is not null) return immediatelyAttackable.Key;
        var preferred = RankedMoves(request, queries, candidate =>
                queries.DistanceToAttackPositionFrom(request.UnitId!, candidate.Destination!, targetIds, closedDoors))
            .Where(item => item.Remaining.HasValue).FirstOrDefault();
        return preferred?.Candidate.Key ?? stay ?? NoneOrForced(request);
    }

    private static IOrderedEnumerable<RankedMove> RankedMoves(DecisionRequest request, IGameplayQueries queries,
        Func<Candidate, int?> remaining) =>
        request.Candidates.Where(candidate => request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move)
            .Select(candidate => new RankedMove(candidate, remaining(candidate), candidate.Path!.Count - 1))
            .OrderBy(item => item.Remaining)
            .ThenByDescending(item => HostileAdjacency(request, queries, item.Candidate.Destination!))
            .ThenBy(item => item.Length)
            .ThenBy(item => item.Candidate.Destination!.Y).ThenBy(item => item.Candidate.Destination!.X)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal);

    private static DoorTarget Canonical(Edge edge) => DoorTarget.Canonical(edge.A, edge.B);

    private static string? StayChoice(DecisionRequest request) => request.Kind == DecisionKind.Activation
        ? request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay)?.Key
        : null;

    private static string? NoneOrForced(DecisionRequest request) => request.AllowsNone ? null : Forced(request);

    private static string? SelectOrdinaryMove(DecisionRequest request, IGameplayQueries queries, Cell? destination)
    {
        var stay = request.Kind == DecisionKind.Activation
            ? request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay)?.Key
            : null;
        if (destination is null) return stay ?? (request.AllowsNone ? null : Forced(request));

        var current = queries.PositionOf(request.UnitId!);
        if (current == destination) return stay ?? (request.AllowsNone ? null : Forced(request));
        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        var currentDistance = queries.RouteDistanceTo(request.UnitId!, current, destination, closedDoors);
        var flee = Has(request, queries, UnitBehavior.Flee);
        var adjacency = Has(request, queries, UnitBehavior.PreferHostileAdjacency);
        var ranked = request.Candidates
            .Where(candidate => request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move)
            .Select(candidate => new
            {
                Candidate = candidate,
                Remaining = queries.RouteDistanceTo(request.UnitId!, candidate.Destination!, destination, closedDoors),
                Length = candidate.Path!.Count - 1,
                Safety = flee
                    ? queries.DistanceToNearestHostileFrom(request.UnitId!, candidate.Destination!, closedDoors) : null,
                Adjacency = adjacency
                    ? queries.CountAdjacentHostilesFrom(request.UnitId!, candidate.Destination!) : 0
            })
            .Where(item => item.Remaining.HasValue && (!currentDistance.HasValue || item.Remaining.Value < currentDistance.Value))
            .OrderBy(item => item.Remaining)
            .ThenByDescending(item => flee ? item.Safety : null)
            .ThenByDescending(item => adjacency ? item.Adjacency : 0)
            .ThenBy(item => item.Length)
            .ThenBy(item => item.Candidate.Destination!.Y)
            .ThenBy(item => item.Candidate.Destination!.X)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        return ranked?.Candidate.Key ?? stay ?? (request.AllowsNone ? null : Forced(request));
    }

    private static string Forced(DecisionRequest request) => request.Candidates.Count == 1
        ? request.Candidates[0].Key
        : throw new InvalidOperationException("The Order could not choose a legal Stay candidate.");

    private static bool Has(DecisionRequest request, IGameplayQueries queries, UnitBehavior behavior) =>
        queries.BehaviorsOf(request.UnitId!).HasFlag(behavior);

    private static int HostileAdjacency(DecisionRequest request, IGameplayQueries queries, Cell position) =>
        Has(request, queries, UnitBehavior.PreferHostileAdjacency)
            ? queries.CountAdjacentHostilesFrom(request.UnitId!, position) : 0;

    private static IEnumerable<MoveOption> MovementOptions(DecisionRequest request, IGameplayQueries queries)
    {
        foreach (var candidate in request.Candidates.Where(candidate =>
                     request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move))
            yield return new(candidate, candidate.Key, candidate.Destination!, candidate.Path!.Count - 1);
        if (request.Kind == DecisionKind.Activation && StayChoice(request) is { } stay)
            yield return new(null, stay, queries.PositionOf(request.UnitId!), 0);
        else if (request.Kind == DecisionKind.Move && request.AllowsNone)
            yield return new(null, null, queries.PositionOf(request.UnitId!), 0);
    }

    private static MoveOption? RankApproachToUnit(DecisionRequest request, IEnumerable<MoveOption> options,
        IGameplayQueries queries,
        string unitId, string targetId, bool closedDoorsTraversable, bool safetyFirstOnTie)
    {
        var current = queries.PositionOf(unitId);
        var currentDistance = queries.DistanceToUnitFrom(unitId, current, targetId, closedDoorsTraversable);
        return options.Where(option => option.Candidate is not null)
            .Select(option => new
            {
                Option = option,
                Remaining = queries.DistanceToUnitFrom(unitId, option.Position, targetId, closedDoorsTraversable),
                Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position, closedDoorsTraversable)
            })
            .Where(item => item.Remaining.HasValue &&
                (!currentDistance.HasValue || item.Remaining < currentDistance))
            .OrderBy(item => item.Remaining)
            .ThenByDescending(item => safetyFirstOnTie ? item.Safety : null)
            .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
            .ThenBy(item => item.Option.Length)
            .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
            .ThenBy(item => item.Option.Key, StringComparer.Ordinal)
            .Select(item => item.Option).FirstOrDefault();
    }

    private static string? SelectFleeAttackMove(DecisionRequest request, IGameplayQueries queries,
        IReadOnlyCollection<string> targetIds)
    {
        var unitId = request.UnitId!;
        var reach = queries.EffectiveMovOf(unitId) * 3;
        var qualifying = MovementOptions(request, queries)
            .Select(option => new
            {
                Option = option,
                TargetDistance = targetIds.Select(id => queries.DistanceToUnitFrom(unitId, option.Position, id))
                    .Where(distance => distance.HasValue).Min(),
                Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position)
            })
            .Where(item => item.TargetDistance <= reach)
            .OrderByDescending(item => item.Safety)
            .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
            .ThenBy(item => item.Option.Length)
            .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
            .ThenBy(item => item.Option.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (qualifying is not null) return qualifying.Option.Key;

        var current = queries.PositionOf(unitId);
        var currentDistance = targetIds.Select(id => queries.DistanceToUnitFrom(unitId, current, id))
            .Where(distance => distance.HasValue).Min();
        if (currentDistance.HasValue)
        {
            var approach = MovementOptions(request, queries).Where(option => option.Candidate is not null)
                .Select(option => new
                {
                    Option = option,
                    Remaining = targetIds.Select(id => queries.DistanceToUnitFrom(unitId, option.Position, id))
                        .Where(distance => distance.HasValue).Min(),
                    Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position)
                })
                .Where(item => item.Remaining.HasValue && item.Remaining < currentDistance)
                .OrderBy(item => item.Remaining)
                .ThenByDescending(item => item.Safety)
                .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                .ThenBy(item => item.Option.Length)
                .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
            if (approach is not null) return approach.Option.Key;
        }
        return DefaultAutomatedProvider.SelectFleeMovement(request, queries);
    }
}
