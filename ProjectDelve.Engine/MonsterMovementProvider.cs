namespace ProjectDelve.Engine;

public sealed class MonsterMovementProvider(IDecisionProvider otherDecisions) : IDecisionProvider
{
    public string? Choose(DecisionRequest request, IGameplayQueries queries) =>
        request.Kind == DecisionKind.Move
            ? ChooseMovement(request, queries)
            : otherDecisions.Choose(request, queries);

    internal static string? ChooseMovement(DecisionRequest request, IGameplayQueries queries)
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

        // 3. Approach an attack position by shortest remaining traversable
        //    distance, then movement path length, then top-left board order.
        var approachingDestination = request.Candidates
            .Select(c => new
            {
                Candidate = c,
                RemainingDistance = queries.DistanceToAttackPositionFrom(unitId, c.Destination!)
            })
            .Where(x => x.RemainingDistance.HasValue)
            .OrderBy(x => x.RemainingDistance!.Value)
            .ThenBy(x => x.Candidate.Path!.Count - 1)
            .ThenBy(x => x.Candidate.Destination!.Y)
            .ThenBy(x => x.Candidate.Destination!.X)
            .FirstOrDefault();

        if (approachingDestination is not null)
            return approachingDestination.Candidate.Key;

        // No legal destination can reach an attack position: stay if allowed.
        if (request.AllowsNone) return null;
        throw new InvalidOperationException(
            "No legal movement destination has a reachable attack position, and staying is not allowed.");
    }
}
