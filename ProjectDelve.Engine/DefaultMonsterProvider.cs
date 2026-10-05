namespace ProjectDelve.Engine;

public sealed class DefaultMonsterProvider : IDecisionProvider
{
    public string? Choose(DecisionRequest request, IGameplayQueries queries) => request.Kind switch
    {
        DecisionKind.Activation => SelectActivation(request, queries),
        DecisionKind.SelectUnit => SelectUnit(request, queries),
        DecisionKind.Move => SelectMovement(request, queries),
        DecisionKind.Act => SelectAction(request, queries),
        DecisionKind.Cleave => request.Candidates.FirstOrDefault()?.Key ?? NoCandidate(request),
        _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported decision kind.")
    };

    private static string SelectActivation(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.Candidates.Any(c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay))
        {
            var moves = request with { Kind = DecisionKind.Move,
                Candidates = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToList(),
                AllowsNone = request.Candidates.Any(c => c.Kind == ActivationChoiceKind.Stay) };
            return SelectMovement(moves, queries) ?? request.Candidates.Single(c => c.Kind == ActivationChoiceKind.Stay).Key;
        }
        var actions = request with { Kind = DecisionKind.Act,
            Candidates = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Action).ToList(), AllowsNone = true };
        return SelectAction(actions, queries) ?? request.Candidates.Single(c => c.Kind == ActivationChoiceKind.EndTurn).Key;
    }

    private static string? SelectMovement(DecisionRequest request, IGameplayQueries queries)
    {
        var unitId = request.UnitId!;
        var behaviors = queries.BehaviorsOf(unitId);
        if (behaviors.HasFlag(UnitBehavior.Flee))
        {
            var closedDoorsTraversable = behaviors.HasFlag(UnitBehavior.ApproachThroughClosedDoors);
            if (queries.DistanceToNearestHostileFrom(unitId, queries.PositionOf(unitId), closedDoorsTraversable) is null)
                return NoCandidate(request);
            var positions = request.Candidates.Select(c => (
                Key: (string?)c.Key, Position: c.Destination!, MovementLength: c.Path!.Count - 1));
            if (request.AllowsNone)
                positions = positions.Append((null, queries.PositionOf(unitId), 0));
            return positions.Select(p => new { p.Key, p.Position, p.MovementLength,
                    Distance = queries.DistanceToNearestHostileFrom(unitId, p.Position, closedDoorsTraversable) })
                .Where(p => p.Distance.HasValue)
                .OrderByDescending(p => p.Distance!.Value)
                .ThenBy(p => p.MovementLength)
                .ThenBy(p => p.Position.Y).ThenBy(p => p.Position.X)
                .FirstOrDefault()?.Key ?? NoCandidate(request);
        }
        if (request.IsMoveAfterAttack && behaviors.HasFlag(UnitBehavior.BackAwayAfterAttack))
        {
            if (!queries.HasNearbyHostileThreatFrom(unitId, queries.PositionOf(unitId)))
                return NoCandidate(request);
            var escape = request.Candidates
                .Where(c => !queries.HasNearbyHostileThreatFrom(unitId, c.Destination!))
                .OrderBy(c => c.Path!.Count - 1)
                .ThenBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X)
                .FirstOrDefault();
            return escape?.Key ?? NoCandidate(request);
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
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.UseSummon))
            return request.Candidates.Where(c => c.Action == UnitAction.SummonAdjacent)
                .OrderBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X)
                .FirstOrDefault()?.Key ?? NoCandidate(request);
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
