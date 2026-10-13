namespace ProjectDelve.Engine;

// Resolves the group's standing Order while retaining established Unit Type behavior
// for every choice the Order does not override.
public sealed class OrderAwareAutomatedProvider : IDecisionProvider
{
    private readonly DefaultAutomatedProvider normal = new();
    private sealed record RankedMove(Candidate Candidate, int? Remaining, int Length);

    public string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.UnitId is null || request.Kind is DecisionKind.SelectUnit or DecisionKind.RollDice)
            return normal.Choose(request, queries);

        var order = queries.EffectiveOrderFor(request.UnitId);
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

        if (request.Kind == DecisionKind.Act || request.Kind == DecisionKind.Activation &&
            !request.Candidates.Any(candidate => candidate.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay))
        {
            var matching = request.Candidates.Where(candidate => IsMatchingAttack(candidate, targetIds)).ToList();
            if (matching.Count > 0)
            {
                var normalChoice = normal.Choose(request, queries);
                if (normalChoice is not null && matching.Any(candidate => candidate.Key == normalChoice))
                    return normalChoice;
                return matching.Select(candidate => new
                    {
                        Candidate = candidate,
                        Target = MatchingTargetIds(candidate, targetIds)
                            .Select(id => new { Id = id, Distance = queries.ManhattanDistanceBetweenUnits(request.UnitId!, id),
                                Position = queries.PositionOf(id) })
                            .OrderBy(item => item.Distance).ThenBy(item => item.Position.Y).ThenBy(item => item.Position.X)
                            .ThenBy(item => item.Id, StringComparer.Ordinal).First()
                    })
                    .OrderBy(item => item.Target.Distance)
                    .ThenBy(item => item.Target.Position.Y).ThenBy(item => item.Target.Position.X)
                    .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal).First().Candidate.Key;
            }
        }

        return ChooseOrdinaryMovement(request, queries,
            (position, current) => SelectAttackMove(request, queries, targetIds, position, current));
    }

    private string? ChooseProtect(DecisionRequest request, IGameplayQueries queries, string protectedId)
    {
        if (!queries.IsLivingFriendlyUnit(request.UnitId!, protectedId)) return normal.Choose(request, queries);
        return ChooseOrdinaryMovement(request, queries, (position, current) =>
        {
            var stay = StayChoice(request);
            if (queries.AreUnitsAdjacent(request.UnitId!, protectedId)) return stay ?? NoneOrForced(request);
            var currentDistance = queries.DistanceToAdjacentPositionFrom(request.UnitId!, current, protectedId);
            var preferred = RankedMoves(request, candidate =>
                    queries.DistanceToAdjacentPositionFrom(request.UnitId!, candidate.Destination!, protectedId))
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
        if (!queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, queries.PositionOf(request.UnitId!), door).HasValue)
            return normal.Choose(request, queries);
        return ChooseOrdinaryMovement(request, queries, (_, current) =>
        {
            var stay = StayChoice(request);
            var currentDistance = queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, current, door);
            var preferred = RankedMoves(request, candidate =>
                    queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, candidate.Destination!, door))
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
        var stay = StayChoice(request);
        if (queries.CanAttackAnyFrom(request.UnitId!, current, targetIds)) return stay ?? NoneOrForced(request);
        var immediatelyAttackable = request.Candidates.Where(candidate =>
                (request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move) &&
                queries.CanAttackAnyFrom(request.UnitId!, candidate.Destination!, targetIds))
            .OrderBy(candidate => candidate.Path!.Count - 1)
            .ThenBy(candidate => candidate.Destination!.Y).ThenBy(candidate => candidate.Destination!.X)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault();
        if (immediatelyAttackable is not null) return immediatelyAttackable.Key;
        var preferred = RankedMoves(request, candidate =>
                queries.DistanceToAttackPositionFrom(request.UnitId!, candidate.Destination!, targetIds))
            .Where(item => item.Remaining.HasValue).FirstOrDefault();
        return preferred?.Candidate.Key ?? stay ?? NoneOrForced(request);
    }

    private static IOrderedEnumerable<RankedMove> RankedMoves(DecisionRequest request, Func<Candidate, int?> remaining) =>
        request.Candidates.Where(candidate => request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move)
            .Select(candidate => new RankedMove(candidate, remaining(candidate), candidate.Path!.Count - 1))
            .OrderBy(item => item.Remaining).ThenBy(item => item.Length)
            .ThenBy(item => item.Candidate.Destination!.Y).ThenBy(item => item.Candidate.Destination!.X)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal);

    private static bool IsMatchingAttack(Candidate candidate, IReadOnlyCollection<string> targetIds) =>
        candidate.Action is UnitAction.NormalAttack or UnitAction.ClawAttack or UnitAction.FireBreath or UnitAction.Fireball &&
        MatchingTargetIds(candidate, targetIds).Any();

    private static IEnumerable<string> MatchingTargetIds(Candidate candidate, IReadOnlyCollection<string> targetIds) =>
        (candidate.TargetId is null ? candidate.TargetIds : candidate.TargetIds.Prepend(candidate.TargetId))
            .Where(targetIds.Contains).Distinct(StringComparer.Ordinal);

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
        var currentDistance = queries.RouteDistanceTo(request.UnitId!, current, destination);
        var ranked = request.Candidates
            .Where(candidate => candidate.Kind == ActivationChoiceKind.Move)
            .Select(candidate => new
            {
                Candidate = candidate,
                Remaining = queries.RouteDistanceTo(request.UnitId!, candidate.Destination!, destination),
                Length = candidate.Path!.Count - 1
            })
            .Where(item => item.Remaining.HasValue && (!currentDistance.HasValue || item.Remaining.Value < currentDistance.Value))
            .OrderBy(item => item.Remaining)
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
}
