namespace ProjectDelve.Engine;

public sealed class DefaultMonsterProvider : IDecisionProvider
{
    public string? Choose(DecisionRequest request, IGameplayQueries queries) => request.Kind switch
    {
        DecisionKind.SelectUnit => SelectUnit(request, queries),
        DecisionKind.Move => SelectMovement(request, queries),
        DecisionKind.Act => SelectAction(request, queries),
        _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported decision kind.")
    };

    private static string? SelectMovement(DecisionRequest request, IGameplayQueries queries)
    {
        var unitId = request.UnitId!;
        var behaviors = queries.BehaviorsOf(unitId);
        if (request.IsMoveAfterAttack && behaviors.HasFlag(UnitBehavior.RetreatAfterAttack))
        {
            var positions = request.Candidates.Select(c => (
                Key: (string?)c.Key, Position: c.Destination!, MovementLength: c.Path!.Count - 1));
            if (request.AllowsNone)
                positions = positions.Append((null, queries.PositionOf(unitId), 0));
            var ranked = positions.Select(p => new { p.Key, p.Position, p.MovementLength,
                Distance = queries.DistanceToNearestHostileFrom(unitId, p.Position) }).ToList();
            if (ranked.All(p => p.Distance is null))
                throw new InvalidOperationException(
                    "RetreatAfterAttack ranking is unspecified when no hostile is reachable by ordinary approach.");
            return ranked.OrderByDescending(p => p.Distance)
                .ThenBy(p => p.MovementLength).ThenBy(p => p.Position.Y).ThenBy(p => p.Position.X)
                .First().Key;
        }
        if (behaviors.HasFlag(UnitBehavior.MaximizeAttackDistance))
        {
            var positions = request.Candidates.Select(c => (
                Key: (string?)c.Key, Position: c.Destination!, MovementLength: c.Path!.Count - 1));
            if (request.AllowsNone)
                positions = positions.Append((null, queries.PositionOf(unitId), 0));

            var preferred = positions
                .Select(p => new { p.Key, p.Position, p.MovementLength,
                    Distance = queries.DistanceToNearestAttackableHostileFrom(unitId, p.Position) })
                .Where(p => p.Distance.HasValue)
                .OrderByDescending(p => p.Distance!.Value)
                .ThenBy(p => p.MovementLength)
                .ThenBy(p => p.Position.Y)
                .ThenBy(p => p.Position.X)
                .FirstOrDefault();
            if (preferred is not null) return preferred.Key;
        }

        return MonsterMovementProvider.ChooseMovement(request, queries,
            closedDoorsTraversable: behaviors.HasFlag(UnitBehavior.ApproachThroughClosedDoors));
    }

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
