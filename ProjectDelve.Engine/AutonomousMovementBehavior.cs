namespace ProjectDelve.Engine;

internal enum MovementPolicyOutcome { Inapplicable, Stay, Move }

// A valid goal can deliberately choose Stay. Only Inapplicable permits fallback.
internal readonly record struct MovementPolicyResult(MovementPolicyOutcome Outcome, string? CandidateKey = null)
{
    internal static MovementPolicyResult Inapplicable => new(MovementPolicyOutcome.Inapplicable);
    internal static MovementPolicyResult Stay => new(MovementPolicyOutcome.Stay);
    internal static MovementPolicyResult Move(string key) => new(MovementPolicyOutcome.Move, key);
}

// Stateless preferences over authoritative ordinary Move candidates. No target or
// route survives the detached decision snapshot.
internal static class AutonomousMovementBehavior
{
    internal static MovementPolicyResult Explore(DecisionRequest request, IGameplayQueries queries)
    {
        var actor = request.UnitId!;
        var immediate = request.Candidates.Select(c => (Candidate: c,
                Score: queries.PotentialDiscoveryAlongPath(actor, c.Path!)))
            .Where(p => p.Score > 0).OrderByDescending(p => p.Score)
            .ThenBy(p => MovementRules.Cost(p.Candidate.Path!))
            .ThenBy(p => p.Candidate.Destination!.Y).ThenBy(p => p.Candidate.Destination!.X)
            .ThenBy(p => p.Candidate.Key, StringComparer.Ordinal).FirstOrDefault();
        if (immediate.Candidate is not null) return MovementPolicyResult.Move(immediate.Candidate.Key);
        var distance = queries.DistanceToExplorationPositionFrom(actor, queries.PositionOf(actor));
        return distance is null ? MovementPolicyResult.Inapplicable :
            Progress(request, p => queries.DistanceToExplorationPositionFrom(actor, p), distance.Value);
    }

    internal static MovementPolicyResult FollowSummoner(DecisionRequest request, IGameplayQueries queries, bool optimizeAdjacency) =>
        queries.SummonerOf(request.UnitId!) is { } target
            ? Follow(request, queries, target, optimizeAdjacency) : MovementPolicyResult.Inapplicable;

    internal static MovementPolicyResult FollowNearestFriendly(DecisionRequest request, IGameplayQueries queries, bool optimizeAdjacency)
    {
        var actor = request.UnitId!;
        var current = queries.PositionOf(actor);
        var doors = queries.BehaviorsOf(actor).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        var target = queries.KnownFriendlyUnitIds(actor)
            .Select(id => (Id: id, Distance: queries.DistanceToFollowPositionFrom(actor, current, id, doors)))
            .Where(p => p.Distance.HasValue).OrderBy(p => p.Distance)
            .ThenBy(p => queries.PositionOf(p.Id).Y).ThenBy(p => queries.PositionOf(p.Id).X)
            .ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        return target.Id is null ? MovementPolicyResult.Inapplicable : Follow(request, queries, target.Id, optimizeAdjacency);
    }

    private static MovementPolicyResult Follow(DecisionRequest request, IGameplayQueries queries, string target, bool optimizeAdjacency)
    {
        var actor = request.UnitId!;
        var current = queries.PositionOf(actor);
        var doors = queries.BehaviorsOf(actor).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        int? Distance(Cell p) => queries.DistanceToFollowPositionFrom(actor, p, target, doors);
        if (Distance(current) is not { } distance) return MovementPolicyResult.Inapplicable;
        if (!optimizeAdjacency) return Progress(request, Distance, distance);
        var positions = request.Candidates.Select(c => (Key: (string?)c.Key, Position: c.Destination!, Cost: MovementRules.Cost(c.Path!), Stay: false));
        if (request.AllowsNone) positions = positions.Append((null, current, 0, true));
        var best = positions.Select(p => (p.Key, p.Position, p.Cost, p.Stay, Distance: Distance(p.Position),
                Count: queries.CountAdjacentFriendliesFrom(actor, p.Position)))
            .Where(p => p.Distance.HasValue && (p.Stay || p.Distance == 0 || p.Distance < distance))
            .OrderBy(p => p.Distance).ThenByDescending(p => p.Count).ThenByDescending(p => p.Stay)
            .ThenBy(p => p.Cost).ThenBy(p => p.Position.Y).ThenBy(p => p.Position.X)
            .ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault();
        return best.Key is null ? MovementPolicyResult.Stay : MovementPolicyResult.Move(best.Key);
    }

    // Preserve this separately authored global preference for other content.
    internal static MovementPolicyResult MaximizeFriendlies(DecisionRequest request, IGameplayQueries queries)
    {
        var actor = request.UnitId!;
        var positions = request.Candidates.Select(c => (Key: (string?)c.Key, Position: c.Destination!, Cost: MovementRules.Cost(c.Path!), Stay: false));
        if (request.AllowsNone) positions = positions.Append((null, queries.PositionOf(actor), 0, true));
        var best = positions.Select(p => (p.Key, p.Position, p.Cost, p.Stay, Count: queries.CountAdjacentFriendliesFrom(actor, p.Position)))
            .OrderByDescending(p => p.Count).ThenByDescending(p => p.Stay).ThenBy(p => p.Cost)
            .ThenBy(p => p.Position.Y).ThenBy(p => p.Position.X).ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault();
        return best.Count == 0 ? MovementPolicyResult.Inapplicable : best.Key is null ? MovementPolicyResult.Stay : MovementPolicyResult.Move(best.Key);
    }

    private static MovementPolicyResult Progress(DecisionRequest request, Func<Cell, int?> distanceFrom, int distance)
    {
        var best = request.Candidates.Select(c => (Candidate: c, Distance: distanceFrom(c.Destination!)))
            .Where(p => p.Distance.HasValue && p.Distance.Value < distance).OrderBy(p => p.Distance)
            .ThenBy(p => MovementRules.Cost(p.Candidate.Path!))
            .ThenBy(p => p.Candidate.Destination!.Y).ThenBy(p => p.Candidate.Destination!.X)
            .ThenBy(p => p.Candidate.Key, StringComparer.Ordinal).FirstOrDefault().Candidate;
        return best is null ? MovementPolicyResult.Stay : MovementPolicyResult.Move(best.Key);
    }
}
