namespace ProjectDelve.Engine;

// A stateless work preference over authoritative movement and Action queries.
internal static class WorkerBehavior
{
    private sealed record Goal(Cell Position, int Distance, string Identity);

    internal static string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        var unitId = request.UnitId!;
        var current = queries.PositionOf(unitId);
        var cargo = queries.Resources.SingleOrDefault(r => r.Position == current);
        var paths = queries.PlanningPathsFrom(unitId, current);
        var transport = queries.CanTransportResource(unitId);
        var moves = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move ||
            request.Kind == DecisionKind.Move).ToList();
        var actions = request.Candidates.Where(c => c.Action.HasValue).ToList();
        var stay = request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay)?.Key;
        var canMove = moves.Count > 0 || stay is not null || request.Kind == DecisionKind.Move;

        List<Goal> DeliveryGoals(Resource resource, Cell origin)
        {
            var routes = origin == current ? paths : queries.PlanningPathsFrom(unitId, origin);
            return routes.Where(p => p.Key == origin || transport &&
                    !queries.Resources.Any(r => r.Position == p.Key))
                .SelectMany(p => queries.ResourceActionsFrom(unitId, p.Key, resource.Type)
                    .Where(a => a.Action == UnitAction.GiveResource)
                    .Select(a => new Goal(p.Key, p.Value.Count - 1, a.MachineId ?? a.TargetId!)))
                .OrderBy(g => g.Distance).ThenBy(g => g.Position.Y).ThenBy(g => g.Position.X)
                .ThenBy(g => g.Identity, StringComparer.Ordinal).ToList();
        }

        string? Action(UnitAction action) => actions.Where(c => c.Action == action)
            .OrderBy(c => queries.ResourceRecipientPosition(c).Y)
            .ThenBy(c => queries.ResourceRecipientPosition(c).X)
            .ThenBy(c => c.MachineId ?? c.TargetId, StringComparer.Ordinal)
            .ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault()?.Key;

        string? Approach(Goal goal, bool preserveCargo)
        {
            if (goal.Distance == 0) return stay;
            return moves.Where(c => !preserveCargo || !queries.Resources.Any(r => r.Position == c.Destination))
                .Select(c => new { Candidate = c, Remaining = queries.PlanningPathsFrom(unitId, c.Destination!)
                    .GetValueOrDefault(goal.Position)?.Count - 1 })
                .Where(p => p.Remaining.HasValue && p.Remaining < goal.Distance)
                .OrderBy(p => p.Remaining).ThenBy(p => p.Candidate.Path!.Count)
                .ThenBy(p => p.Candidate.Destination!.Y).ThenBy(p => p.Candidate.Destination!.X)
                .ThenBy(p => p.Candidate.Key, StringComparer.Ordinal).FirstOrDefault()?.Candidate.Key ?? stay;
        }

        // Delivery is checked afresh even after another Worker fills a shared store.
        if (cargo is not null && DeliveryGoals(cargo, current).FirstOrDefault() is { } delivery)
        {
            if (canMove) return Approach(delivery, preserveCargo: true) ?? Fallback();
            return Action(UnitAction.GiveResource) ?? Fallback();
        }
        if (Action(UnitAction.Operate) is { } operate)
            return canMove ? stay ?? operate : operate;
        // Move requests do not contain Actions; consult the same Action rules at the origin.
        if (canMove && queries.ResourceActionsFrom(unitId, current, cargo?.Type)
                .Any(c => c.Action == UnitAction.Operate))
            return stay ?? Fallback();

        if (canMove)
        {
            var resourceGoal = queries.Resources.Where(r => r.Position != current && paths.ContainsKey(r.Position))
                .Where(r => DeliveryGoals(r, r.Position).Count > 0)
                .Select(r => new Goal(r.Position, paths[r.Position].Count - 1, r.Type.Id))
                .OrderBy(g => g.Distance).ThenBy(g => g.Position.Y).ThenBy(g => g.Position.X)
                .ThenBy(g => g.Identity, StringComparer.Ordinal).FirstOrDefault();
            if (resourceGoal is not null) return Approach(resourceGoal, preserveCargo: false) ?? Fallback();

            // Project the marker that an ordinary Move would actually leave under the operator.
            var machineGoal = paths.SelectMany(p =>
                queries.ResourceActionsFrom(unitId, p.Key,
                    queries.Resources.SingleOrDefault(r => r.Position == p.Key)?.Type ?? (transport ? cargo?.Type : null))
                .Where(a => a.Action == UnitAction.Operate)
                .Select(a => new Goal(p.Key, p.Value.Count - 1, a.MachineId!)))
                .OrderBy(g => g.Distance).ThenBy(g => g.Position.Y).ThenBy(g => g.Position.X)
                .ThenBy(g => g.Identity, StringComparer.Ordinal).FirstOrDefault();
            if (machineGoal is not null) return Approach(machineGoal, preserveCargo: false) ?? Fallback();
        }
        return Fallback();

        string? Fallback() => stay ?? request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.EndTurn)?.Key
            ?? (request.AllowsNone ? null : throw new InvalidOperationException("Worker has no legal Stay / Skip choice."));
    }
}
