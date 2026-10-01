namespace ProjectDelve.Engine;

public sealed class DefaultMonsterProvider : IDecisionProvider
{
    public string? Choose(DecisionRequest request, IGameplayQueries queries) => request.Kind switch
    {
        DecisionKind.SelectUnit => SelectUnit(request, queries),
        DecisionKind.Move => MonsterMovementProvider.ChooseMovement(request, queries,
            closedDoorsTraversable: queries.BehaviorsOf(request.UnitId!)
                .HasFlag(UnitBehavior.ApproachThroughClosedDoors)),
        DecisionKind.Act => SelectAction(request, queries),
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

    private static string? SelectAction(DecisionRequest request, IGameplayQueries queries)
    {
        // Rank legal targets by Manhattan distance, then top-left board order.
        // Melee's special range rule determines legality, not this preference.
        var nearest = request.Candidates
            .Where(c => c.Action == UnitAction.NormalAttack)
            .Select(c => new
            {
                Candidate = c,
                Distance = queries.ManhattanDistanceBetweenUnits(request.UnitId!, c.TargetId!),
                Position = queries.PositionOf(c.TargetId!)
            })
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Position.Y)
            .ThenBy(x => x.Position.X)
            .FirstOrDefault();

        if (nearest is not null) return nearest.Candidate.Key;
        var from = queries.PositionOf(request.UnitId!);
        var door = request.Candidates.Where(c => c.TryOpenDoor is not null)
            .OrderBy(c => (c.Door!.A == from ? c.Door.B : c.Door.A).Y)
            .ThenBy(c => (c.Door!.A == from ? c.Door.B : c.Door.A).X)
            .FirstOrDefault();
        return door?.Key ?? NoCandidate(request);
    }

    private static string? NoCandidate(DecisionRequest request)
    {
        if (request.AllowsNone) return null;
        throw new InvalidOperationException("No candidate is available and choosing none is not allowed.");
    }
}
