namespace ProjectDelve.Engine;

// A detached decision chooses its target afresh. No target or route is retained.
internal static class EquippedEnemyBehavior
{
    internal static string? ChooseMovement(DecisionRequest request, IGameplayQueries queries)
    {
        var actor = request.UnitId!;
        var current = queries.PositionOf(actor);
        var doors = queries.BehaviorsOf(actor).HasFlag(UnitBehavior.ApproachThroughClosedDoors);
        var target = queries.KnownEquippedHostileUnitIds(actor)
            .Select(id => (Id: id, Distance: queries.DistanceToRepossessionPositionFrom(actor, current, id, doors)))
            .Where(t => t.Distance.HasValue).OrderBy(t => t.Distance)
            .ThenBy(t => queries.PositionOf(t.Id).Y).ThenBy(t => queries.PositionOf(t.Id).X)
            .ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
        if (target.Id is null) return Stay();
        var options = request.Candidates.Select(c => (Key: (string?)c.Key, Position: c.Destination!,
            Cost: MovementRules.Cost(c.Path!), Distance: queries.DistanceToRepossessionPositionFrom(actor, c.Destination!, target.Id, doors)));
        if (request.AllowsNone) options = options.Append((null, current, 0, target.Distance));
        return options.Where(p => p.Distance.HasValue).OrderBy(p => p.Distance)
            .ThenBy(p => p.Cost).ThenBy(p => p.Position.Y).ThenBy(p => p.Position.X)
            .ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault().Key ?? Stay();

        string? Stay() => request.AllowsNone ? null : throw new InvalidOperationException("No reachable equipped enemy and staying is not allowed.");
    }
}
