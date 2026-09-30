namespace ProjectDelve.Engine;

public sealed class DefaultMonsterProvider : IDecisionProvider
{
    public string? Choose(DecisionRequest request, IGameplayQueries queries) => request.Kind switch
    {
        DecisionKind.SelectUnit => SelectUnit(request, queries),
        DecisionKind.Move => MonsterMovementProvider.ChooseMovement(request, queries),
        DecisionKind.Attack => SelectAttackTarget(request, queries),
        _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported decision kind.")
    };

    private static string? SelectUnit(DecisionRequest request, IGameplayQueries queries)
    {
        // Rank eligible candidates by their current positions in top-left board order.
        var first = request.Candidates
            .Select(c => new { Candidate = c, Position = queries.PositionOf(c.Key) })
            .OrderBy(x => x.Position.Y)
            .ThenBy(x => x.Position.X)
            .FirstOrDefault();

        return first?.Candidate.Key ?? NoCandidate(request);
    }

    private static string? SelectAttackTarget(DecisionRequest request, IGameplayQueries queries)
    {
        // Rank legal targets by Manhattan distance, then top-left board order.
        // Melee's special range rule determines legality, not this preference.
        var nearest = request.Candidates
            .Select(c => new
            {
                Candidate = c,
                Distance = queries.ManhattanDistanceBetweenUnits(request.UnitId!, c.Key),
                Position = queries.PositionOf(c.Key)
            })
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Position.Y)
            .ThenBy(x => x.Position.X)
            .FirstOrDefault();

        return nearest?.Candidate.Key ?? NoCandidate(request);
    }

    private static string? NoCandidate(DecisionRequest request)
    {
        if (request.AllowsNone) return null;
        throw new InvalidOperationException("No candidate is available and choosing none is not allowed.");
    }
}
