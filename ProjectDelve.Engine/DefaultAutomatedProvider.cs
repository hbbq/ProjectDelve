namespace ProjectDelve.Engine;

public sealed class DefaultAutomatedProvider : IDecisionProvider
{
    private static string? PreferredWorkerSummon(DecisionRequest request, IGameplayQueries queries) =>
        queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferSummonWorker)
            ? request.Candidates.Where(c => c.Action == UnitAction.SummonWorker)
                .OrderBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X)
                .ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault()?.Key
            : null;

    public string? Choose(DecisionRequest request, IGameplayQueries queries) => request.Kind switch
    {
        DecisionKind.RollDice => request.Candidates.Single().Key,
        DecisionKind.Activation => SelectActivation(request, queries),
        DecisionKind.SelectUnit => SelectUnit(request, queries),
        DecisionKind.Move => SelectMovement(request, queries),
        DecisionKind.Act => SelectAction(request, queries),
        DecisionKind.Cleave => request.Candidates.FirstOrDefault()?.Key ?? NoCandidate(request),
        _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported decision kind.")
    };

    internal static string SelectActivation(DecisionRequest request, IGameplayQueries queries,
        IReadOnlyCollection<string>? orderedTargetIds = null)
    {
        var objectChoice = request.Candidates.FirstOrDefault(c => (c.ObjectId is not null && c.Kind == ActivationChoiceKind.FreeAction || c.EquipmentMechanic == EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit));
        if (objectChoice is not null) return objectChoice.Key;
        if (request.Candidates.Any(c => c.Kind == ActivationChoiceKind.EndTurn) &&
            !request.Candidates.Any(c => c.Kind == ActivationChoiceKind.Action) &&
            request.Candidates.FirstOrDefault(c => c.ObjectKind == LootCardKind.SecondWind) is { } wind)
            return wind.Key;
        if (request.Candidates.FirstOrDefault(c => c.EquipmentGranted && c.FreeAction == UnitFreeAction.OpenDoor) is { } open) return open.Key;
        if (request.Candidates.FirstOrDefault(c => c.BonusAction?.Boost is not null) is { } boost) return boost.Key;
        if (!request.Candidates.Any(c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay) &&
            BananaPlacementRules.Preferred(request, queries) is { } banana) return banana;
        if (!request.Candidates.Any(c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay) &&
            PreferredWorkerSummon(request, queries) is { } summon) return summon;
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.Worker))
            return WorkerBehavior.Choose(request, queries)!;
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferReshapeRoom) &&
            request.Candidates.FirstOrDefault(c => c.BonusAction?.ReshapeRoom is not null) is { } reshape)
            return reshape.Key;
        var positional = queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.SwapThenAttackThenDisplace);
        if (positional && RankRepositioning(request.Candidates.Where(c => c.BonusAction?.Swap is not null), request, queries) is { } swap)
            return swap.Key;
        if (request.Candidates.Any(c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay))
        {
            var moves = request with { Kind = DecisionKind.Move,
                Candidates = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToList(),
                AllowsNone = request.Candidates.Any(c => c.Kind == ActivationChoiceKind.Stay) };
            return SelectMovement(moves, queries) ?? request.Candidates.Single(c => c.Kind == ActivationChoiceKind.Stay).Key;
        }
        var actions = request with { Kind = DecisionKind.Act,
            Candidates = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Action).ToList(), AllowsNone = true };
        var action = SelectAction(actions, queries, orderedTargetIds);
        if (action is not null) return action;
        if (positional && RankDisplace(request.Candidates.Where(c => c.BonusAction?.Displace is not null), request, queries) is { } displace)
            return displace.Key;
        return request.Candidates.Single(c => c.Kind == ActivationChoiceKind.EndTurn).Key;
    }

    // Legal candidates already include all targeting/placement restrictions. Rank by the
    // ordinary nearest-target policy, then destination board order for reproducible play.
    private static Candidate? RankRepositioning(IEnumerable<Candidate> candidates, DecisionRequest request, IGameplayQueries queries) =>
        ThenByTargetAndDestination(candidates.OrderBy(c =>
            queries.ManhattanDistanceBetweenUnits(request.UnitId!, c.TargetId!)), queries).FirstOrDefault();

    private static Candidate? RankDisplace(IEnumerable<Candidate> candidates, DecisionRequest request, IGameplayQueries queries)
    {
        var sourceCells = queries.OccupiedCellsOf(request.UnitId!);
        // Score the complete supplied target/destination pair, without generating placements.
        var ranked = candidates.OrderByDescending(c =>
                SpatialRules.ManhattanDistance(sourceCells, [c.Destination!]) -
                queries.ManhattanDistanceBetweenUnits(request.UnitId!, c.TargetId!))
            .ThenBy(c => c.Destination == queries.PositionOf(c.TargetId!) ? 0 : 1)
            .ThenBy(c => queries.ManhattanDistanceBetweenUnits(request.UnitId!, c.TargetId!));
        return ThenByTargetAndDestination(ranked, queries).FirstOrDefault();
    }

    private static IOrderedEnumerable<Candidate> ThenByTargetAndDestination(IOrderedEnumerable<Candidate> ranked, IGameplayQueries queries) =>
        ranked.ThenBy(c => queries.PositionOf(c.TargetId!).Y).ThenBy(c => queries.PositionOf(c.TargetId!).X)
            .ThenBy(c => c.Destination?.Y).ThenBy(c => c.Destination?.X)
            .ThenBy(c => c.Key, StringComparer.Ordinal);

    private static string? SelectMovement(DecisionRequest request, IGameplayQueries queries)
    {
        var unitId = request.UnitId!;
        var behaviors = queries.BehaviorsOf(unitId);
        if (behaviors.HasFlag(UnitBehavior.Worker)) return WorkerBehavior.Choose(request, queries);
        if (behaviors.HasFlag(UnitBehavior.SeekDiamond) && !request.IsMoveAfterAttack &&
            queries.StoredQuantityFor(unitId, ResourceTypes.Diamond) == 0)
        {
            var diamonds = queries.Resources.Where(r => r.Type.Id == ResourceTypes.Diamond.Id)
                .Select(r => r.Position).ToHashSet();
            if (request.AllowsNone && diamonds.Contains(queries.PositionOf(unitId))) return null;
            var resourceMove = request.Candidates.Where(c => diamonds.Contains(c.Destination!))
                .OrderBy(c => MovementRules.Cost(c.Path!))
                .ThenBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X)
                .ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();
            if (resourceMove is not null) return resourceMove.Key;
        }
        if (behaviors.HasFlag(UnitBehavior.Flee))
            return SelectFleeMovement(request, queries,
                behaviors.HasFlag(UnitBehavior.ApproachThroughClosedDoors));
        if (request.IsMoveAfterAttack && behaviors.HasFlag(UnitBehavior.BackAwayAfterAttack))
        {
            if (!queries.HasNearbyHostileThreatFrom(unitId, queries.PositionOf(unitId)))
                return NoCandidate(request);
            var escape = request.Candidates
                .Where(c => !queries.HasNearbyHostileThreatFrom(unitId, c.Destination!))
                .OrderBy(c => MovementRules.Cost(c.Path!))
                .ThenBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X)
                .FirstOrDefault();
            return escape?.Key ?? NoCandidate(request);
        }
        if (behaviors.HasFlag(UnitBehavior.MaximizeAttackDistance))
        {
            var positions = request.Candidates.Select(c => (
                Key: (string?)c.Key, Position: c.Destination!, MovementLength: MovementRules.Cost(c.Path!)));
            if (request.AllowsNone)
                positions = positions.Append((null, queries.PositionOf(unitId), 0));

            var preferred = positions
                .Select(p => new { p.Key, p.Position, p.MovementLength,
                    Distance = queries.DistanceToNearestAttackableHostileFrom(unitId, p.Position) })
                .Where(p => p.Distance.HasValue)
                .OrderByDescending(p => p.Distance!.Value)
                .ThenByDescending(p => behaviors.HasFlag(UnitBehavior.PreferHostileAdjacency)
                    ? queries.CountAdjacentHostilesFrom(unitId, p.Position) : 0)
                .ThenBy(p => p.MovementLength)
                .ThenBy(p => p.Position.Y)
                .ThenBy(p => p.Position.X)
                .FirstOrDefault();
            if (preferred is not null) return preferred.Key;
        }

        return ApproachMovementProvider.ChooseMovement(request, queries,
            closedDoorsTraversable: behaviors.HasFlag(UnitBehavior.ApproachThroughClosedDoors));
    }

    internal static string? SelectFleeMovement(DecisionRequest request, IGameplayQueries queries,
        bool closedDoorsTraversable = false)
    {
        var unitId = request.UnitId!;
        var current = queries.PositionOf(unitId);
        var currentDistance = queries.DistanceToNearestHostileFrom(unitId, current, closedDoorsTraversable);
        var stay = request.Kind == DecisionKind.Activation
            ? request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay)?.Key
            : null;
        if (!currentDistance.HasValue) return stay ?? NoCandidate(request);

        var preferred = request.Candidates.Where(c => request.Kind == DecisionKind.Move || c.Kind == ActivationChoiceKind.Move)
            .Select(c => new
            {
                Candidate = c,
                Distance = queries.DistanceToNearestHostileFrom(unitId, c.Destination!, closedDoorsTraversable),
                MovementLength = MovementRules.Cost(c.Path!)
            })
            .Where(item => item.Distance > currentDistance)
            .OrderByDescending(item => item.Distance)
            .ThenByDescending(item => queries.BehaviorsOf(unitId).HasFlag(UnitBehavior.PreferHostileAdjacency)
                ? queries.CountAdjacentHostilesFrom(unitId, item.Candidate.Destination!) : 0)
            .ThenBy(item => item.MovementLength)
            .ThenBy(item => item.Candidate.Destination!.Y)
            .ThenBy(item => item.Candidate.Destination!.X)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        return preferred?.Candidate.Key ?? stay ?? NoCandidate(request);
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

    internal static string? SelectAction(DecisionRequest request, IGameplayQueries queries,
        IReadOnlyCollection<string>? orderedTargetIds = null)
    {
        var shell = request.Candidates.Where(c => c.Action == UnitAction.ShootShell)
            .OrderBy(c => queries.ManhattanDistanceBetweenUnits(request.UnitId!, c.TargetId!))
            .ThenBy(c => queries.PositionOf(c.TargetId!).Y).ThenBy(c => queries.PositionOf(c.TargetId!).X)
            .ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();
        if (shell is not null) return shell.Key;
        if (BananaPlacementRules.Preferred(request, queries) is { } banana) return banana;
        if (PreferredWorkerSummon(request, queries) is { } summon) return summon;
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.Worker))
            return WorkerBehavior.Choose(request, queries);
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferTrollThenDiamondThenWorker))
        {
            foreach (var action in new[] { UnitAction.SummonTroll, UnitAction.PickupResource, UnitAction.SummonWorker })
            {
                if (action == UnitAction.PickupResource && !queries.Resources.Any(r =>
                        r.Position == queries.PositionOf(request.UnitId!) && r.Type.Id == ResourceTypes.Diamond.Id)) continue;
                var preferred = request.Candidates.Where(c => c.Action == action)
                    .OrderBy(c => c.Destination?.Y).ThenBy(c => c.Destination?.X)
                    .ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();
                if (preferred is not null) return preferred.Key;
            }
            return NoCandidate(request);
        }
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.UseSummon))
        {
            var summons = request.Candidates.Where(c => c.Action == UnitAction.SummonAdjacent).ToList();
            if (summons.Count > 0)
            {
                if (orderedTargetIds is { Count: > 0 })
                {
                    var towardTarget = summons.Select(candidate => new
                        {
                            Candidate = candidate,
                            Distance = queries.OpenRouteDistanceFromSummonPlacement(request.UnitId!,
                                candidate.Destination!, orderedTargetIds)
                        })
                        .Where(item => item.Distance.HasValue)
                        .OrderBy(item => item.Distance)
                        .ThenBy(item => item.Candidate.Destination!.Y)
                        .ThenBy(item => item.Candidate.Destination!.X)
                        .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (towardTarget is not null) return towardTarget.Candidate.Key;
                }
                return summons.OrderBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X)
                    .ThenBy(c => c.Key, StringComparer.Ordinal).First().Key;
            }
        }

        orderedTargetIds ??= [];
        var rankedAttacks = request.Candidates.Where(IsAttack)
            .OrderByDescending(candidate => CandidateTargets(candidate).Count(orderedTargetIds.Contains))
            .ThenByDescending(candidate => queries.EffectiveAttackDice(request.UnitId!, candidate))
            .ThenByDescending(candidate => CandidateTargets(candidate)
                .Count(targetId => queries.IsHostileTo(request.UnitId!, targetId)))
            .ThenByDescending(candidate => queries.IsUnlimitedAttack(request.UnitId!, candidate))
            .ThenBy(candidate => queries.AttackDefinitionOrder(request.UnitId!, candidate))
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal).ToArray();
        var attack = rankedAttacks.FirstOrDefault();
        // Keep the existing Action/Order priorities. Once Normal Attack is chosen,
        // rank its legal targets by DEF, retaining the original ordering for ties.
        if (attack?.Action == UnitAction.NormalAttack &&
            queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferLowestEffectiveDefense))
        {
            var orderedMatches = CandidateTargets(attack).Count(orderedTargetIds.Contains);
            attack = rankedAttacks.Where(c => c.Action == UnitAction.NormalAttack &&
                    CandidateTargets(c).Count(orderedTargetIds.Contains) == orderedMatches)
                .OrderBy(c => queries.EffectiveDefOf(c.TargetId!)).First();
        }

        if (attack is not null) return attack.Key;
        var occupied = queries.OccupiedCellsOf(request.UnitId!);
        var door = request.Candidates.Where(c => c.TryOpenDoor is not null)
            .OrderBy(c => (occupied.Contains(c.Door!.A) ? c.Door.B : c.Door.A).Y)
            .ThenBy(c => (occupied.Contains(c.Door!.A) ? c.Door.B : c.Door.A).X)
            .FirstOrDefault();
        return door?.Key ?? NoCandidate(request);
    }

    internal static bool IsAttack(Candidate candidate) => candidate.Action is
        UnitAction.NormalAttack or UnitAction.ClawAttack or UnitAction.FireBreath or UnitAction.Fireball or UnitAction.ShootShell;

    internal static IEnumerable<string> CandidateTargets(Candidate candidate) =>
        (candidate.TargetId is null ? candidate.TargetIds : candidate.TargetIds.Prepend(candidate.TargetId))
        .Distinct(StringComparer.Ordinal);

    private static string? NoCandidate(DecisionRequest request)
    {
        if (request.AllowsNone) return null;
        throw new InvalidOperationException("No candidate is available and choosing none is not allowed.");
    }
}
