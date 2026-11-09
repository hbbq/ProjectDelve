namespace ProjectDelve.Engine;

// Stateless preferences over authoritative ordinary Move candidates. All route and
// knowledge calculations belong to the detached gameplay-query snapshot.
internal static class AutonomousMovementBehavior
{
    internal static string? Choose(DecisionRequest request, IGameplayQueries queries, bool explore = false,
        bool maximizeFriendlies = false)
    {
        var unitId = request.UnitId!;
        var current = queries.PositionOf(unitId);
        var closedDoors = queries.BehaviorsOf(unitId).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        if (maximizeFriendlies)
        {
            var positions = request.Candidates.Select(c => (Key: (string?)c.Key, Position: c.Destination!,
                Cost: MovementRules.Cost(c.Path!), Stay: false));
            if (request.AllowsNone) positions = positions.Append((null, current, 0, true));
            var best = positions.Select(p => (p.Key, p.Position, p.Cost, p.Stay,
                    Count: queries.CountAdjacentFriendliesFrom(unitId, p.Position)))
                .OrderByDescending(p => p.Count).ThenByDescending(p => p.Stay).ThenBy(p => p.Cost)
                .ThenBy(p => p.Position.Y).ThenBy(p => p.Position.X).ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault();
            if (best.Count > 0) return best.Key;
        }
        if (explore && queries.DistanceToExplorationPositionFrom(unitId, current, closedDoors) is { } explorationDistance)
            return Progress(p => queries.DistanceToExplorationPositionFrom(unitId, p, closedDoors), explorationDistance);
        var friendlyDistance = queries.DistanceToFriendlyPositionFrom(unitId, current, closedDoors);
        return friendlyDistance is { } distance
            ? Progress(p => queries.DistanceToFriendlyPositionFrom(unitId, p, closedDoors), distance) : null;

        string? Progress(Func<Cell, int?> distanceFrom, int distance) => request.Candidates
            .Select(c => (Candidate: c, Distance: distanceFrom(c.Destination!)))
            .Where(p => p.Distance.HasValue && p.Distance.Value < distance)
            .OrderBy(p => p.Distance).ThenBy(p => MovementRules.Cost(p.Candidate.Path!))
            .ThenBy(p => p.Candidate.Destination!.Y).ThenBy(p => p.Candidate.Destination!.X)
            .ThenBy(p => p.Candidate.Key, StringComparer.Ordinal).FirstOrDefault().Candidate?.Key;
    }
}
