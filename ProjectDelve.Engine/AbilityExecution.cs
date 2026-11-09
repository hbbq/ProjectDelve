using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    // Content resolution supplies the concrete mechanic and source. These handlers
    // never look up an effect through a Unit field, display name, or object Type ID.
    private static bool IsSharedActiveMechanic(AbilityMechanic mechanic) => AbilityCostRules.Supports(mechanic);

    private static Candidate BindAbilityCandidate(ResolvedAbility ability, Candidate candidate) => candidate with
    {
        Kind = ability.Definition.Timing switch
        {
            AbilityTiming.Action => ActivationChoiceKind.Action,
            AbilityTiming.BonusAction => ActivationChoiceKind.BonusAction,
            AbilityTiming.FreeAction => ActivationChoiceKind.FreeAction,
            _ => throw new InvalidOperationException("An active Ability requires an active timing.")
        },
        Ability = ability, AbilityName = ability.Definition.Name,
        ObjectId = ability.SourceKind == AbilitySourceKind.Unit ? null : ability.SourceInstanceId,
        ItemTypeId = ability.SourceKind == AbilitySourceKind.Item ? ability.SourceTypeId : null,
        EquipmentTypeId = ability.SourceKind == AbilitySourceKind.Equipment ? ability.SourceTypeId : null
    };

    private static IEnumerable<Candidate> ActiveAbilityCandidates(GameState state, Unit unit, AbilityTiming timing)
    {
        foreach (var ability in state.AbilitiesOf(unit.Id).Where(g => g.Definition.Timing == timing &&
                     IsSharedActiveMechanic(g.Definition.Mechanic) && state.CanUseAbility(unit.Id, g)))
        {
            Candidate Bind(Candidate candidate) => BindAbilityCandidate(ability, candidate);

            switch (ability.Definition.Mechanic)
            {
                case AdjacentUnitHealMechanic heal:
                    foreach (var target in AbilityQueries.AdjacentRecipients(state, unit, heal.Selector)
                                 .Where(target => target.CurrentHp < state.Types.Single(type => type.Id == target.TypeId).Hp))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}", Action: UnitAction.AdjacentUnitHeal, TargetId: target.Id));
                    break;
                case AdjacentUnitsPostureChangeMechanic posture:
                    var targets = AbilityQueries.AdjacentRecipients(state, unit, posture.Selector)
                        .Where(target => state.Physical.Figures.Single(figure => figure.Id == target.Id).Posture != posture.NewPosture)
                        .Select(target => target.Id).ToImmutableArray();
                    yield return Bind(new(ability.CandidatePrefix, Action: UnitAction.AdjacentUnitsPostureChange, Relevant: !targets.IsEmpty) { TargetIds = targets });
                    break;
                case FireballMechanic:
                    foreach (var candidate in FireballCandidates(state, unit, ability.CandidatePrefix)) yield return Bind(candidate);
                    break;
                case AdjacentUnitPostureChangeMechanic posture:
                    foreach (var target in AbilityQueries.AdjacentRecipients(state, unit, posture.Selector)
                                 .Where(target => state.Physical.Figures.Single(figure => figure.Id == target.Id).Posture != posture.NewPosture))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}", TargetId: target.Id));
                    break;
                case SelfModifiersMechanic self:
                    yield return Bind(new(ability.CandidatePrefix,
                        Relevant: timing != AbilityTiming.BonusAction || BonusActionRelevant(state, unit, self)));
                    break;
                case ExtraActionsMechanic:
                    yield return Bind(new(ability.CandidatePrefix));
                    break;
                case ProjectDelve.Engine.OpenDoor:
                case TryOpenDoor:
                    foreach (var edge in BorderingClosedDoors(state, unit))
                        yield return Bind(new($"{ability.CandidatePrefix}:{edge.A.X},{edge.A.Y}:{edge.B.X},{edge.B.Y}", Door: edge,
                            FreeAction: ability.Definition.Mechanic is ProjectDelve.Engine.OpenDoor && timing == AbilityTiming.FreeAction ? UnitFreeAction.OpenDoor : UnitFreeAction.None,
                            TryOpenDoor: ability.Definition.Mechanic as TryOpenDoor));
                    break;
                case Telekinesis:
                    var from = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
                    foreach (var target in state.Units.Where(target => state.IsUpright(target.Id) &&
                                 AttackRules.EvaluateHostileTargetFrom(state, unit.Id, from, target.Id) == UnitTargetEvaluation.Possible))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}", Action: UnitAction.Telekinesis, TargetId: target.Id));
                    break;
                case FireBreath:
                    var breathFrom = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
                    var breathTargets = state.Units.Where(target =>
                        AttackRules.EvaluateAbilityFrom(state, unit.Id, breathFrom, target.Id, ability) == UnitTargetEvaluation.Possible)
                        .Select(target => target.Id).ToImmutableArray();
                    if (!breathTargets.IsEmpty)
                        yield return Bind(new(ability.CandidatePrefix, Action: UnitAction.FireBreath) { TargetIds = breathTargets });
                    break;
                case ClawAttack:
                    var clawFrom = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
                    foreach (var target in state.Units.Where(target =>
                                 AttackRules.EvaluateAbilityFrom(state, unit.Id, clawFrom, target.Id, ability) == UnitTargetEvaluation.Possible))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}", Action: UnitAction.ClawAttack, TargetId: target.Id));
                    break;
                case TargetUnitAttackMechanic attack:
                    var attackFrom = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
                    foreach (var target in state.Units.Where(target =>
                                 AttackRules.EvaluateHostileTargetFrom(state, unit.Id, attackFrom, target.Id, attack.Rng) == UnitTargetEvaluation.Possible))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}", Action: UnitAction.TargetUnitAttack, TargetId: target.Id));
                    break;
                case AdjacentUnitSummonMechanic:
                    foreach (var candidate in SummoningRules.Candidates(state, unit, ability)) yield return Bind(candidate);
                    break;
                case PickupResource:
                case GiveResource:
                case Operate:
                    foreach (var candidate in ResourceRules.Candidates(state, unit, ability)
                                 .Concat(MachineRules.Candidates(state, unit, ability))) yield return Bind(candidate);
                    break;
                case SelfExtraMovesMechanic when !state.ActionDone:
                    yield return Bind(new(ability.CandidatePrefix));
                    break;
                case PlaceBananaPeel:
                    foreach (var candidate in BananaPlacementRules.Candidates(state, unit, ability)) yield return Bind(candidate);
                    break;
                case Swap:
                    foreach (var target in AdjacentHostiles(state, unit).Where(t => ExternalMovementRules.CanSwap(state, unit.Id, t.Id)))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}", TargetId: target.Id));
                    break;
                case Displace displace:
                    foreach (var target in AdjacentHostiles(state, unit))
                    foreach (var destination in ExternalMovementRules.Destinations(state, unit.Id, target.Id, displace.MaxMove))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}:{destination.X},{destination.Y}", Destination: destination, TargetId: target.Id));
                    break;
                case ReshapeRoom:
                    yield return Bind(new(ability.CandidatePrefix));
                    break;
            }
        }
    }

    private static void ResolveAbility(GameState state, DecisionRequest request, Candidate choice, ResolutionEvents events)
    {
        var ability = choice.Ability!;
        // Modifier/extra-Action events expose the resulting activation state,
        // preserving their existing event-time snapshots.
        if (ability.Definition.Mechanic is not (SelfModifiersMechanic or ExtraActionsMechanic or SelfExtraMovesMechanic or ReshapeRoom))
            EmitActionUsed(request, choice.Key, events);
        switch (ability.Definition.Mechanic)
        {
            case AdjacentUnitsPostureChangeMechanic posture:
                foreach (var targetId in choice.TargetIds)
                    if (state.Physical.Figures.Any(figure => figure.Id == targetId))
                        ChangePosture(state, targetId, posture.NewPosture, events, request.UnitId, ability.ActionId);
                break;
            case AdjacentUnitHealMechanic heal:
                RestoreHp(state, choice.TargetId!, heal.Amount, request.UnitId!, ability.Definition.Name, ability.ActionId, events);
                break;
            case FireballMechanic:
                BeginAttack(state, request.UnitId!,
                    new(choice.TargetIds, state.EffectiveAtkOf(request.UnitId!), ability.Definition.Name), events,
                    ability.ActionId, choice.Destination, UnitAction.Fireball, ability.Definition.Timing,
                    abilitySource: ability.SourceReference);
                break;
            case AdjacentUnitPostureChangeMechanic posture:
                ChangePosture(state, choice.TargetId!, posture.NewPosture, events, request.UnitId, ability.ActionId);
                break;
            case SelfModifiersMechanic self:
                state.ModifiersThisTurn.AddRange(self.Modifiers);
                EmitActionUsed(request, choice.Key, events);
                break;
            case ExtraActionsMechanic extra:
                state.ExtraActionsThisActivation += extra.Amount;
                EmitActionUsed(request, choice.Key, events);
                break;
            case ProjectDelve.Engine.OpenDoor:
                OpenDoor(state, request.UnitId!, choice.Door!, events, ability.ActionId, ability.SourceReference);
                break;
            case TryOpenDoor door:
                state.DoorInProgress = new(request.UnitId!, choice.Door!, door.SuccessCount, ability.ActionId)
                    { AbilitySource = ability.SourceReference };
                break;
            case Telekinesis:
                ChangePosture(state, choice.TargetId!, Posture.Lying, events, request.UnitId, ability.ActionId);
                break;
            case FireBreath:
                BeginAttack(state, request.UnitId!,
                    new(choice.TargetIds, AttackRules.AttackDice(state, request.UnitId!, null, ability), ability.Definition.Name),
                    events, ability.ActionId, choice.Destination, UnitAction.FireBreath, ability.Definition.Timing,
                    abilitySource: ability.SourceReference);
                break;
            case ClawAttack:
                BeginAttack(state, request.UnitId!,
                    new([choice.TargetId!], AttackRules.AttackDice(state, request.UnitId!, choice.TargetId, ability), ability.Definition.Name),
                    events, ability.ActionId, choice.Destination, UnitAction.ClawAttack, ability.Definition.Timing,
                    abilitySource: ability.SourceReference);
                break;
            case TargetUnitAttackMechanic attack:
                BeginAttack(state, request.UnitId!, new([choice.TargetId!], attack.Atk, ability.Definition.Name),
                    events, ability.ActionId, null, UnitAction.TargetUnitAttack, ability.Definition.Timing, attack.Rng, ability.SourceReference);
                break;
            case AdjacentUnitSummonMechanic:
                ResolveSummon(state, request.UnitId!, choice, events);
                break;
            case PickupResource:
            case GiveResource:
            case Operate:
                var operation = choice.MachineId is not null
                    ? MachineRules.Resolve(state, request.UnitId!, choice, events.Random)
                    : ResourceRules.Transfer(state, request.UnitId!, choice);
                if (operation is not null) events.Add(operation);
                break;
            case PlaceBananaPeel:
                BananaPlacementRules.Resolve(state, request.UnitId!, choice, events.Add);
                break;
            case SelfExtraMovesMechanic extra:
                state.MoveOpportunityModifierThisActivation = (int)Math.Min(int.MaxValue,
                    (long)state.MoveOpportunityModifierThisActivation + extra.Amount);
                EmitActionUsed(request, choice.Key, events);
                break;
            case Swap:
                ExternalMovementRules.SwapPlaces(state, request.UnitId!, choice.TargetId!);
                events.Add(new RulesEvent("PlacesSwapped", request.UnitId, choice.TargetId, AbilityName: ability.Definition.Name)
                    { SourceUnitId = request.UnitId, ActionId = ability.ActionId });
                ResolveArrival(state, request.UnitId!, events);
                ResolveArrival(state, choice.TargetId!, events);
                break;
            case Displace:
                var affectedIndex = state.Physical.Figures.FindIndex(f => f.Id == choice.TargetId);
                var affected = state.Physical.Figures[affectedIndex];
                state.Physical.Figures[affectedIndex] = affected with { Position = choice.Destination! };
                events.Add(new RulesEvent("UnitRepositioned", affected.Id,
                    Path: affected.Position == choice.Destination ? [affected.Position] : [affected.Position, choice.Destination!],
                    AbilityName: ability.Definition.Name)
                    { SourceUnitId = request.UnitId, ActionId = ability.ActionId, Cell = choice.Destination });
                if (affected.Position != choice.Destination) ResolveArrival(state, affected.Id, events);
                else events.LootArrivals.Add(affected.Id);
                break;
            case ReshapeRoom:
                ReshapeRoomRules.Apply(state, request.UnitId!);
                EmitActionUsed(request, choice.Key, events);
                break;
            default:
                throw new InvalidOperationException("Unsupported active Ability mechanic.");
        }
    }

    private static void ResolveSummon(GameState state, string unitId, Candidate choice, ResolutionEvents events)
    {
        var ability = choice.Ability!;
        var summon = SummoningRules.Placement(ability.Definition.Mechanic)!;
        var source = state.Units.Single(u => u.Id == unitId);
        var summonedType = state.Types.SingleOrDefault(type => type.Id == summon.UnitTypeId)
            ?? throw new InvalidOperationException("Summoned Unit Type is not defined.");
        var prefix = summonedType.Id.EndsWith("-type", StringComparison.Ordinal) ? summonedType.Id[..^5] : summonedType.Id;
        var number = 1;
        while (state.Units.Any(u => u.Id == $"{prefix}-{number}") ||
               state.Physical.Board.Machines.Any(machine => machine.Id == $"{prefix}-{number}")) number++;
        var summonedId = $"{prefix}-{number}";
        var group = new ActivationToken(summonedType.Id, source.SideId);
        if (!state.Controllers.Any(c => c.Token == group))
            state.Controllers.Add(new(group, summonedType.DefaultController ?? state.ControllerFor(source.Token)));
        state.PlaceUnit(summonedType.Id, summonedId, source.SideId, choice.Destination!, summon.InitialPosture);
        events.Add(new RulesEvent("UnitCreated", summonedId, TypeId: summonedType.Id,
            AbilityName: ability.Definition.Name, Posture: summon.InitialPosture)
            { SourceUnitId = source.Id, SideId = source.SideId, Cell = choice.Destination, ActionId = ability.ActionId });
        ResolveArrival(state, summonedId, events);
    }
}
