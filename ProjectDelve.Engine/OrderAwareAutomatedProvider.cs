namespace ProjectDelve.Engine;

// Resolves the group's standing Order while retaining established Unit Type behavior
// for every choice the Order does not override.
public sealed class OrderAwareAutomatedProvider : IDecisionProvider
{
    private readonly DefaultAutomatedProvider normal = new();

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