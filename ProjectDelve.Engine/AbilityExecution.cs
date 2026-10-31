using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    // Content resolution supplies the concrete mechanic and source. These handlers
    // never look up an effect through a Unit field, display name, or object Type ID.
    private static bool IsSharedActiveMechanic(AbilityMechanic mechanic) => mechanic is
        HolyWaveMechanic or HealMechanic or FireballMechanic or StandAdjacentFriendlyMechanic or SelfModifiersMechanic or ExtraActionsMechanic or
        ProjectDelve.Engine.OpenDoor or TryOpenDoor or Telekinesis;

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
                case HealMechanic:
                    foreach (var target in state.Units.Where(target => target.Id != unit.Id &&
                                 target.SideId == unit.SideId && target.CurrentHp > 0 &&
                                 target.CurrentHp < state.Types.Single(t => t.Id == target.TypeId).Hp &&
                                 SpatialRules.AreAdjacent(state, unit.Id, target.Id)))
                        yield return Bind(new($"{ability.CandidatePrefix}:{target.Id}", Action: UnitAction.Heal, TargetId: target.Id));
                    break;
                case HolyWaveMechanic:
                    var targets = AdjacentHostiles(state, unit).Where(u => state.IsUpright(u.Id)).Select(u => u.Id).ToImmutableArray();
                    yield return Bind(new(ability.CandidatePrefix, Action: UnitAction.HolyWave, Relevant: !targets.IsEmpty) { TargetIds = targets });
                    break;
                case FireballMechanic:
                    foreach (var candidate in FireballCandidates(state, unit, ability.CandidatePrefix)) yield return Bind(candidate);
                    break;
                case StandAdjacentFriendlyMechanic:
                    foreach (var target in state.Units.Where(u => u.CurrentHp > 0 && u.SideId == unit.SideId &&
                                 state.Physical.Figures.Any(f => f.Id == u.Id && f.Posture == Posture.Lying) &&
                                 SpatialRules.AreAdjacent(state, unit.Id, u.Id)))
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
            }
        }
    }

    private static void ResolveAbility(GameState state, DecisionRequest request, Candidate choice, ResolutionEvents events)
    {
        var ability = choice.Ability!;
        // Modifier/extra-Action events expose the resulting activation state,
        // preserving their existing event-time snapshots.
        if (ability.Definition.Mechanic is not (SelfModifiersMechanic or ExtraActionsMechanic))
            EmitActionUsed(request, choice.Key, events);
        switch (ability.Definition.Mechanic)
        {
            case HolyWaveMechanic:
                foreach (var targetId in choice.TargetIds)
                    if (state.Physical.Figures.Any(f => f.Id == targetId))
                        ChangePosture(state, targetId, Posture.Lying, events, request.UnitId, ability.ActionId);
                ChangePosture(state, request.UnitId!, Posture.Lying, events, request.UnitId, ability.ActionId);
                break;
            case HealMechanic heal:
                RestoreHp(state, choice.TargetId!, heal.Amount, request.UnitId!, ability.Definition.Name, ability.ActionId, events);
                break;
            case FireballMechanic:
                BeginAttack(state, request.UnitId!,
                    new(choice.TargetIds, state.EffectiveAtkOf(request.UnitId!), ability.Definition.Name), events,
                    ability.ActionId, choice.Destination, UnitAction.Fireball, ability.Definition.Timing);
                break;
            case StandAdjacentFriendlyMechanic:
                ChangePosture(state, choice.TargetId!, Posture.Upright, events, request.UnitId, ability.ActionId);
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
                OpenDoor(state, request.UnitId!, choice.Door!, events, ability.ActionId);
                break;
            case TryOpenDoor door:
                state.DoorInProgress = new(request.UnitId!, choice.Door!, door.SuccessCount, ability.ActionId);
                break;
            case Telekinesis:
                ChangePosture(state, choice.TargetId!, Posture.Lying, events, request.UnitId, ability.ActionId);
                break;
            default:
                throw new InvalidOperationException("Unsupported active Ability mechanic.");
        }
    }
}
