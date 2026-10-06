namespace ProjectDelve.Engine;

public sealed class ApproachMovementProvider(IDecisionProvider otherDecisions) : IDecisionProvider
{
    public string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.Kind == DecisionKind.Activation && request.Candidates.Any(c => c.Kind == ActivationChoiceKind.Stay))
            return ChooseMovement(request with { Kind = DecisionKind.Move, AllowsNone = true,
                Candidates = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToList() }, queries) ?? "stay";
        return request.Kind == DecisionKind.Move ? ChooseMovement(request, queries) : otherDecisions.Choose(request, queries);
    }

    internal static string? ChooseMovement(DecisionRequest request, IGameplayQueries queries,
        bool closedDoorsTraversable = false)
    {
        var unitId = request.UnitId!;
        var current = queries.PositionOf(unitId);

        // 1. If already able to attack a hostile, stay.
        if (queries.CanAttackHostileFrom(unitId, current) && request.AllowsNone)
            return null;

        // 2. Reach an attack position using the shortest movement path,
        //    then break ties in top-left board order (Y, X).
        var attackDestination = request.Candidates
            .Where(c => queries.CanAttackHostileFrom(unitId, c.Destination!))
            .OrderBy(c => c.Path!.Count - 1)
            .ThenBy(c => c.Destination!.Y)
            .ThenBy(c => c.Destination!.X)
            .FirstOrDefault();

        if (attackDestination is not null)
            return attackDestination.Key;

        // 3. Rank movement and, when allowed, staying by remaining approach
        //    distance, then movement path length, then top-left board order.
        var approachOptions = request.Candidates
            .Select(c => (
                Key: (string?)c.Key,
                Position: c.Destination!,
                MovementLength: c.Path!.Count - 1,
                RemainingDistance: queries.DistanceToAttackPositionFrom(unitId, c.Destination!, closedDoorsTraversable)));
        if (request.AllowsNone)
            approachOptions = approachOptions.Append((null, current, 0,
                queries.DistanceToAttackPositionFrom(unitId, current, closedDoorsTraversable)));

        var approachingDestination = approachOptions
            .Where(x => x.RemainingDistance.HasValue)
            .OrderBy(x => x.RemainingDistance!.Value)
            .ThenBy(x => x.MovementLength)
            .ThenBy(x => x.Position.Y)
            .ThenBy(x => x.Position.X)
            .FirstOrDefault();

        if (approachingDestination.RemainingDistance.HasValue)
            return approachingDestination.Key;

        // No legal destination can reach an attack position: stay if allowed.
        if (request.AllowsNone) return null;
        throw new InvalidOperationException(
            "No legal movement destination has a reachable attack position, and staying is not allowed.");
    }
}
