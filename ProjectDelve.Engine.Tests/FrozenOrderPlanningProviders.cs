// Frozen pre-optimization providers: differential oracle for dispatch and choice parity.
namespace ProjectDelve.Engine.Tests;

internal sealed class FrozenDefaultAutomatedProvider : IDecisionProvider
{
    private static string? PreferredWorkerSummon(DecisionRequest request, IGameplayQueries queries) =>
        queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferSummonWorker)
            ? request.Candidates.Where(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic { UnitTypeId: UnitTypeIds.Worker })
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
        var objectChoice = request.Candidates.FirstOrDefault(c => (c.Ability is null && c.ObjectId is not null && c.Kind == ActivationChoiceKind.FreeAction || c.Ability?.Definition.Mechanic is AdjacentUnitPostureChangeMechanic { Selector: RelationshipSelector.Friendlies, NewPosture: Posture.Upright }));
        if (objectChoice is not null) return objectChoice.Key;
        if (request.Candidates.Any(c => c.Kind == ActivationChoiceKind.EndTurn) &&
            !request.Candidates.Any(c => c.Kind == ActivationChoiceKind.Action) &&
            request.Candidates.FirstOrDefault(c => c.Ability?.Definition.Mechanic is ExtraActionsMechanic) is { } wind)
            return wind.Key;
        if (request.Candidates.FirstOrDefault(c => c.Ability?.SourceKind == AbilitySourceKind.Equipment && c.FreeAction == UnitFreeAction.OpenDoor) is { } open) return open.Key;
        if (request.Candidates.FirstOrDefault(c => c.Ability?.Definition.Mechanic is SelfExtraMovesMechanic) is { } boost) return boost.Key;
        if (!request.Candidates.Any(c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay) &&
            BananaPlacementRules.Preferred(request, queries) is { } banana) return banana;
        if (!request.Candidates.Any(c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay) &&
            PreferredWorkerSummon(request, queries) is { } summon) return summon;
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.Worker))
            return WorkerBehavior.Choose(request, queries)!;
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.PreferReshapeRoom) &&
            request.Candidates.FirstOrDefault(c => c.Ability?.Definition.Mechanic is ReshapeRoom) is { } reshape)
            return reshape.Key;
        var positional = queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.SwapThenAttackThenDisplace);
        if (positional && RankRepositioning(request.Candidates.Where(c => c.Ability?.Definition.Mechanic is Swap), request, queries) is { } swap)
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
        if (positional && RankDisplace(request.Candidates.Where(c => c.Ability?.Definition.Mechanic is Displace), request, queries) is { } displace)
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
            queries.StoredQuantityFor(unitId, ResourceTypeIds.Diamond) == 0)
        {
            var diamonds = queries.Resources.Where(r => r.Type.Id == ResourceTypeIds.Diamond)
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
        var shell = request.Candidates.Where(c => c.Action == UnitAction.TargetUnitAttack)
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
            foreach (var preference in new[] { UnitTypeIds.Troll, ResourceTypeIds.Diamond, UnitTypeIds.Worker })
            {
                if (preference == ResourceTypeIds.Diamond && !queries.Resources.Any(resource =>
                        resource.Position == queries.PositionOf(request.UnitId!) && resource.Type.Id == ResourceTypeIds.Diamond)) continue;
                var preferred = request.Candidates.Where(candidate => preference == ResourceTypeIds.Diamond
                        ? candidate.Action == UnitAction.PickupResource
                        : candidate.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic summon && summon.UnitTypeId == preference)
                    .OrderBy(candidate => candidate.Destination?.Y).ThenBy(candidate => candidate.Destination?.X)
                    .ThenBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault();
                if (preferred is not null) return preferred.Key;
            }
            return NoCandidate(request);
        }
        if (queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.UseSummon))
        {
            var summons = request.Candidates.Where(c => c.Action == UnitAction.AdjacentUnitSummon).ToList();
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
        var door = request.Candidates.Where(c => c.TryOpenDoor is not null || c.Ability?.Definition.Mechanic is OpenDoor)
            .OrderBy(c => (occupied.Contains(c.Door!.A) ? c.Door.B : c.Door.A).Y)
            .ThenBy(c => (occupied.Contains(c.Door!.A) ? c.Door.B : c.Door.A).X)
            .FirstOrDefault();
        return door?.Key ?? NoCandidate(request);
    }

    internal static bool IsAttack(Candidate candidate) => candidate.Action is
        UnitAction.NormalAttack or UnitAction.ClawAttack or UnitAction.FireBreath or UnitAction.Fireball or UnitAction.TargetUnitAttack;

    internal static IEnumerable<string> CandidateTargets(Candidate candidate) =>
        (candidate.TargetId is null ? candidate.TargetIds : candidate.TargetIds.Prepend(candidate.TargetId))
        .Distinct(StringComparer.Ordinal);

    private static string? NoCandidate(DecisionRequest request)
    {
        if (request.AllowsNone) return null;
        throw new InvalidOperationException("No candidate is available and choosing none is not allowed.");
    }
}



// Resolves the group's standing Order while retaining established Unit Type behavior
// for every choice the Order does not override.
internal sealed class FrozenOrderAwareAutomatedProvider : IDecisionProvider
{
    private readonly FrozenDefaultAutomatedProvider normal = new();
    private sealed record RankedMove(Candidate Candidate, int? Remaining, int Length);
    private sealed record MoveOption(Candidate? Candidate, string? Key, Cell Position, int Length);

    public string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.UnitId is null || request.Kind is DecisionKind.SelectUnit or DecisionKind.RollDice)
            return normal.Choose(request, queries);

        var order = queries.EffectiveOrderFor(request.UnitId);
        if (order.Kind != OrderKind.DoNothing && queries.BehaviorsOf(request.UnitId).HasFlag(UnitBehavior.Worker))
            return normal.Choose(request, queries);
        if (order.Kind == OrderKind.FollowRoute)
            return ChooseOrdinaryMovement(request, queries, (_, _) => RouteRules.ChooseMove(request, queries, order.Route!));
        return order.Kind switch
        {
            OrderKind.Normal => normal.Choose(request, queries),
            OrderKind.DoNothing => ChooseDoNothing(request, queries),
            OrderKind.HoldPosition => ChoosePositionOrder(request, queries, null),
            OrderKind.MoveTo => ChoosePositionOrder(request, queries, order.Destination!),
            OrderKind.Attack => ChooseAttack(request, queries, order.Target!),
            OrderKind.Protect => ChooseProtect(request, queries, order.Target!.UnitId!),
            OrderKind.Retreat => ChooseRetreat(request, queries),
            OrderKind.InteractWith => ChooseInteractWith(request, queries, order.Target!.Door!),
            _ => throw new ArgumentOutOfRangeException(nameof(order.Kind))
        };
    }

    private string? ChooseDoNothing(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.Kind == DecisionKind.Activation)
        {
            var end = request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.EndTurn);
            if (end is not null) return end.Key;
            var stay = request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay);
            if (stay is not null) return stay.Key;
        }
        if (request.AllowsNone) return null;
        if (request.Candidates.Count == 1) return request.Candidates[0].Key;
        return normal.Choose(request, queries);
    }

    private string? ChoosePositionOrder(DecisionRequest request, IGameplayQueries queries, Cell? destination)
    {
        if (request.IsMoveAfterAttack) return normal.Choose(request, queries);

        var normalChoice = normal.Choose(request, queries);
        if (request.Kind == DecisionKind.Activation)
        {
            var selected = request.Candidates.Single(candidate => candidate.Key == normalChoice);
            if (selected.Kind is not (ActivationChoiceKind.Move or ActivationChoiceKind.Stay)) return normalChoice;
            return SelectOrdinaryMove(request, queries, destination);
        }
        if (request.Kind == DecisionKind.Move) return SelectOrdinaryMove(request, queries, destination);
        return normalChoice;
    }

    private string? ChooseAttack(DecisionRequest request, IGameplayQueries queries, OrderTarget target)
    {
        var targetIds = queries.EligibleAttackOrderTargets(request.UnitId!, target);
        if (targetIds.Count == 0) return normal.Choose(request, queries);

        if (request.Kind == DecisionKind.Act)
            return FrozenDefaultAutomatedProvider.SelectAction(request, queries, targetIds);
        if (request.Kind == DecisionKind.Activation &&
            !request.Candidates.Any(candidate => candidate.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay))
            return FrozenDefaultAutomatedProvider.SelectActivation(request, queries, targetIds);

        return ChooseOrdinaryMovement(request, queries,
            (position, current) => SelectAttackMove(request, queries, targetIds, position, current));
    }

    private string? ChooseProtect(DecisionRequest request, IGameplayQueries queries, string protectedId)
    {
        if (!queries.IsLivingFriendlyUnit(request.UnitId!, protectedId)) return normal.Choose(request, queries);
        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        return ChooseOrdinaryMovement(request, queries, (_, current) =>
        {
            var unitId = request.UnitId!;
            var stay = StayChoice(request);
            var options = MovementOptions(request, queries).ToList();

            if (Has(request, queries, UnitBehavior.Flee))
            {
                var near = options.Select(option => new
                    {
                        Option = option,
                        Distance = queries.DistanceToUnitFrom(unitId, option.Position, protectedId, closedDoors),
                        Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position, closedDoors)
                    })
                    .Where(item => item.Distance <= queries.EffectiveMovOf(unitId))
                    .OrderByDescending(item => item.Safety)
                    .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                    .ThenBy(item => item.Option.Length)
                    .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                    .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
                if (near is not null) return near.Option.Key;

                var approach = RankApproachToUnit(request, options, queries, unitId, protectedId, closedDoors,
                    safetyFirstOnTie: true);
                return approach?.Key ?? stay ?? NoneOrForced(request);
            }

            if (Has(request, queries, UnitBehavior.MaximizeAttackDistance))
            {
                var ranged = options.Where(option =>
                        queries.IsWithinEffectiveRangeAndLos(unitId, option.Position, protectedId) &&
                        queries.CanAttackHostileFrom(unitId, option.Position))
                    .Select(option => new
                    {
                        Option = option,
                        Distance = queries.DistanceToNearestAttackableHostileFrom(unitId, option.Position)
                    })
                    .Where(item => item.Distance.HasValue)
                    .OrderByDescending(item => item.Distance)
                    .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                    .ThenBy(item => item.Option.Length)
                    .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                    .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
                if (ranged is not null) return ranged.Option.Key;
            }

            if (Has(request, queries, UnitBehavior.ProtectWithoutAdjacency))
            {
                var scored = options.Select(option => new
                    {
                        Option = option,
                        Distance = queries.DistanceToUnitFrom(unitId, option.Position, protectedId, closedDoors),
                        IsAdjacent = queries.AreUnitsAdjacentFrom(unitId, option.Position, protectedId),
                        Adjacency = queries.CountAdjacentHostilesFrom(unitId, option.Position)
                    })
                    .Where(item => item.Distance.HasValue).ToList();
                var nonAdjacent = scored.Where(item => !item.IsAdjacent).ToList();
                var pool = nonAdjacent.Count > 0 ? nonAdjacent : scored;
                var protectedChoice = pool
                    .OrderBy(item => item.Distance)
                    .ThenByDescending(item => Has(request, queries, UnitBehavior.PreferHostileAdjacency)
                        ? item.Adjacency : 0)
                    .ThenBy(item => item.Option.Length)
                    .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                    .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
                return protectedChoice?.Option.Key ?? stay ?? NoneOrForced(request);
            }

            if (queries.AreUnitsAdjacent(unitId, protectedId)) return stay ?? NoneOrForced(request);
            var currentDistance = queries.DistanceToAdjacentPositionFrom(unitId, current, protectedId, closedDoors);
            var preferred = RankedMoves(request, queries, candidate =>
                    queries.DistanceToAdjacentPositionFrom(unitId, candidate.Destination!, protectedId, closedDoors))
                .Where(item => item.Remaining.HasValue &&
                    (!currentDistance.HasValue || item.Remaining.Value < currentDistance.Value))
                .FirstOrDefault();
            return preferred?.Candidate.Key ?? stay ?? NoneOrForced(request);
        });
    }

    private string? ChooseRetreat(DecisionRequest request, IGameplayQueries queries) =>
        ChooseOrdinaryMovement(request, queries, (_, _) => FrozenDefaultAutomatedProvider.SelectFleeMovement(request, queries,
            queries.BehaviorsOf(request.UnitId!).HasFlag(UnitBehavior.ApproachThroughClosedDoors)));

    private string? ChooseInteractWith(DecisionRequest request, IGameplayQueries queries, DoorTarget door)
    {
        if (!queries.IsClosedDoor(door) || !queries.CanInteractWithDoors(request.UnitId!))
            return normal.Choose(request, queries);
        var interaction = request.Candidates.Where(candidate =>
                candidate.Door is not null && Canonical(candidate.Door) == door &&
                (candidate.FreeAction == UnitFreeAction.OpenDoor || candidate.Ability?.Definition.Mechanic is OpenDoor || candidate.TryOpenDoor is not null))
            .OrderBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault();
        if (interaction is not null) return interaction.Key;
        if (Has(request, queries, UnitBehavior.Flee))
            return ChooseOrdinaryMovement(request, queries, (_, _) =>
                FrozenDefaultAutomatedProvider.SelectFleeMovement(request, queries));
        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        if (!queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, queries.PositionOf(request.UnitId!), door,
                closedDoors).HasValue)
            return normal.Choose(request, queries);
        return ChooseOrdinaryMovement(request, queries, (_, current) =>
        {
            var stay = StayChoice(request);
            var currentDistance = queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, current, door, closedDoors);
            var preferred = RankedMoves(request, queries, candidate =>
                    queries.DistanceToDoorInteractionPositionFrom(request.UnitId!, candidate.Destination!, door, closedDoors))
                .Where(item => item.Remaining.HasValue &&
                    (!currentDistance.HasValue || item.Remaining.Value < currentDistance.Value))
                .FirstOrDefault();
            return preferred?.Candidate.Key ?? stay ?? NoneOrForced(request);
        });
    }

    private string? ChooseOrdinaryMovement(DecisionRequest request, IGameplayQueries queries,
        Func<Cell, Cell, string?> select)
    {
        if (request.IsMoveAfterAttack) return normal.Choose(request, queries);
        if (request.Kind == DecisionKind.Move)
        {
            var movePosition = queries.PositionOf(request.UnitId!);
            return select(movePosition, movePosition);
        }
        var normalChoice = normal.Choose(request, queries);
        if (request.Kind == DecisionKind.Activation)
        {
            var selected = request.Candidates.Single(candidate => candidate.Key == normalChoice);
            if (selected.Kind is not (ActivationChoiceKind.Move or ActivationChoiceKind.Stay)) return normalChoice;
        }
        else return normalChoice;
        var current = queries.PositionOf(request.UnitId!);
        return select(current, current);
    }

    private static string? SelectAttackMove(DecisionRequest request, IGameplayQueries queries,
        IReadOnlyCollection<string> targetIds, Cell _, Cell current)
    {
        if (Has(request, queries, UnitBehavior.Flee))
            return SelectFleeAttackMove(request, queries, targetIds);

        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        var stay = StayChoice(request);
        if (Has(request, queries, UnitBehavior.MaximizeAttackDistance))
        {
            var ranged = MovementOptions(request, queries)
                .Where(option => queries.CanAttackAnyFrom(request.UnitId!, option.Position, targetIds))
                .Select(option => new
                {
                    Option = option,
                    Distance = queries.DistanceToNearestAttackableHostileFrom(request.UnitId!, option.Position)
                })
                .Where(item => item.Distance.HasValue)
                .OrderByDescending(item => item.Distance)
                .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                .ThenBy(item => item.Option.Length)
                .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                .ThenBy(item => item.Option.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (ranged is not null) return ranged.Option.Key;
        }
        if (queries.CanAttackAnyFrom(request.UnitId!, current, targetIds)) return stay ?? NoneOrForced(request);
        var immediatelyAttackable = request.Candidates.Where(candidate =>
                (request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move) &&
                queries.CanAttackAnyFrom(request.UnitId!, candidate.Destination!, targetIds))
            .OrderByDescending(candidate => HostileAdjacency(request, queries, candidate.Destination!))
            .ThenBy(candidate => MovementRules.Cost(candidate.Path!))
            .ThenBy(candidate => candidate.Destination!.Y).ThenBy(candidate => candidate.Destination!.X)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal).FirstOrDefault();
        if (immediatelyAttackable is not null) return immediatelyAttackable.Key;
        var preferred = RankedMoves(request, queries, candidate =>
                queries.DistanceToAttackPositionFrom(request.UnitId!, candidate.Destination!, targetIds, closedDoors))
            .Where(item => item.Remaining.HasValue).FirstOrDefault();
        return preferred?.Candidate.Key ?? stay ?? NoneOrForced(request);
    }

    private static IOrderedEnumerable<RankedMove> RankedMoves(DecisionRequest request, IGameplayQueries queries,
        Func<Candidate, int?> remaining) =>
        request.Candidates.Where(candidate => request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move)
            .Select(candidate => new RankedMove(candidate, remaining(candidate), MovementRules.Cost(candidate.Path!)))
            .OrderBy(item => item.Remaining)
            .ThenByDescending(item => HostileAdjacency(request, queries, item.Candidate.Destination!))
            .ThenBy(item => item.Length)
            .ThenBy(item => item.Candidate.Destination!.Y).ThenBy(item => item.Candidate.Destination!.X)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal);

    private static DoorTarget Canonical(Edge edge) => DoorTarget.Canonical(edge.A, edge.B);

    private static string? StayChoice(DecisionRequest request) => request.Kind == DecisionKind.Activation
        ? request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay)?.Key
        : null;

    private static string? NoneOrForced(DecisionRequest request) => request.AllowsNone ? null : Forced(request);

    private static string? SelectOrdinaryMove(DecisionRequest request, IGameplayQueries queries, Cell? destination)
    {
        var stay = request.Kind == DecisionKind.Activation
            ? request.Candidates.FirstOrDefault(candidate => candidate.Kind == ActivationChoiceKind.Stay)?.Key
            : null;
        if (destination is null) return stay ?? (request.AllowsNone ? null : Forced(request));

        var current = queries.PositionOf(request.UnitId!);
        if (current == destination) return stay ?? (request.AllowsNone ? null : Forced(request));
        var closedDoors = Has(request, queries, UnitBehavior.ApproachThroughClosedDoors);
        var currentDistance = queries.RouteDistanceTo(request.UnitId!, current, destination, closedDoors);
        var flee = Has(request, queries, UnitBehavior.Flee);
        var adjacency = Has(request, queries, UnitBehavior.PreferHostileAdjacency);
        var ranked = request.Candidates
            .Where(candidate => request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move)
            .Select(candidate => new
            {
                Candidate = candidate,
                Remaining = queries.RouteDistanceTo(request.UnitId!, candidate.Destination!, destination, closedDoors),
                Length = MovementRules.Cost(candidate.Path!),
                Safety = flee
                    ? queries.DistanceToNearestHostileFrom(request.UnitId!, candidate.Destination!, closedDoors) : null,
                Adjacency = adjacency
                    ? queries.CountAdjacentHostilesFrom(request.UnitId!, candidate.Destination!) : 0
            })
            .Where(item => item.Remaining.HasValue && (!currentDistance.HasValue || item.Remaining.Value < currentDistance.Value))
            .OrderBy(item => item.Remaining)
            .ThenByDescending(item => flee ? item.Safety : null)
            .ThenByDescending(item => adjacency ? item.Adjacency : 0)
            .ThenBy(item => item.Length)
            .ThenBy(item => item.Candidate.Destination!.Y)
            .ThenBy(item => item.Candidate.Destination!.X)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        return ranked?.Candidate.Key ?? stay ?? (request.AllowsNone ? null : Forced(request));
    }

    private static string Forced(DecisionRequest request) => request.Candidates.Count == 1
        ? request.Candidates[0].Key
        : throw new InvalidOperationException("The Order could not choose a legal Stay candidate.");

    private static bool Has(DecisionRequest request, IGameplayQueries queries, UnitBehavior behavior) =>
        queries.BehaviorsOf(request.UnitId!).HasFlag(behavior);

    private static int HostileAdjacency(DecisionRequest request, IGameplayQueries queries, Cell position) =>
        Has(request, queries, UnitBehavior.PreferHostileAdjacency)
            ? queries.CountAdjacentHostilesFrom(request.UnitId!, position) : 0;

    private static IEnumerable<MoveOption> MovementOptions(DecisionRequest request, IGameplayQueries queries)
    {
        foreach (var candidate in request.Candidates.Where(candidate =>
                     request.Kind == DecisionKind.Move || candidate.Kind == ActivationChoiceKind.Move))
            yield return new(candidate, candidate.Key, candidate.Destination!, MovementRules.Cost(candidate.Path!));
        if (request.Kind == DecisionKind.Activation && StayChoice(request) is { } stay)
            yield return new(null, stay, queries.PositionOf(request.UnitId!), 0);
        else if (request.Kind == DecisionKind.Move && request.AllowsNone)
            yield return new(null, null, queries.PositionOf(request.UnitId!), 0);
    }

    private static MoveOption? RankApproachToUnit(DecisionRequest request, IEnumerable<MoveOption> options,
        IGameplayQueries queries,
        string unitId, string targetId, bool closedDoorsTraversable, bool safetyFirstOnTie)
    {
        var current = queries.PositionOf(unitId);
        var currentDistance = queries.DistanceToUnitFrom(unitId, current, targetId, closedDoorsTraversable);
        return options.Where(option => option.Candidate is not null)
            .Select(option => new
            {
                Option = option,
                Remaining = queries.DistanceToUnitFrom(unitId, option.Position, targetId, closedDoorsTraversable),
                Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position, closedDoorsTraversable)
            })
            .Where(item => item.Remaining.HasValue &&
                (!currentDistance.HasValue || item.Remaining < currentDistance))
            .OrderBy(item => item.Remaining)
            .ThenByDescending(item => safetyFirstOnTie ? item.Safety : null)
            .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
            .ThenBy(item => item.Option.Length)
            .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
            .ThenBy(item => item.Option.Key, StringComparer.Ordinal)
            .Select(item => item.Option).FirstOrDefault();
    }

    private static string? SelectFleeAttackMove(DecisionRequest request, IGameplayQueries queries,
        IReadOnlyCollection<string> targetIds)
    {
        var unitId = request.UnitId!;
        var reach = queries.EffectiveMovOf(unitId) * 3;
        var qualifying = MovementOptions(request, queries)
            .Select(option => new
            {
                Option = option,
                TargetDistance = targetIds.Select(id => queries.DistanceToUnitFrom(unitId, option.Position, id))
                    .Where(distance => distance.HasValue).Min(),
                Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position)
            })
            .Where(item => item.TargetDistance <= reach)
            .OrderByDescending(item => item.Safety)
            .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
            .ThenBy(item => item.Option.Length)
            .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
            .ThenBy(item => item.Option.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (qualifying is not null) return qualifying.Option.Key;

        var current = queries.PositionOf(unitId);
        var currentDistance = targetIds.Select(id => queries.DistanceToUnitFrom(unitId, current, id))
            .Where(distance => distance.HasValue).Min();
        if (currentDistance.HasValue)
        {
            var approach = MovementOptions(request, queries).Where(option => option.Candidate is not null)
                .Select(option => new
                {
                    Option = option,
                    Remaining = targetIds.Select(id => queries.DistanceToUnitFrom(unitId, option.Position, id))
                        .Where(distance => distance.HasValue).Min(),
                    Safety = queries.DistanceToNearestHostileFrom(unitId, option.Position)
                })
                .Where(item => item.Remaining.HasValue && item.Remaining < currentDistance)
                .OrderBy(item => item.Remaining)
                .ThenByDescending(item => item.Safety)
                .ThenByDescending(item => HostileAdjacency(request, queries, item.Option.Position))
                .ThenBy(item => item.Option.Length)
                .ThenBy(item => item.Option.Position.Y).ThenBy(item => item.Option.Position.X)
                .ThenBy(item => item.Option.Key, StringComparer.Ordinal).FirstOrDefault();
            if (approach is not null) return approach.Option.Key;
        }
        return FrozenDefaultAutomatedProvider.SelectFleeMovement(request, queries);
    }
}

