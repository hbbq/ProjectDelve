using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    // Content resolution supplies the concrete mechanic and source. These handlers
    // never look up an effect through a Unit field, display name, or object Type ID.
    private static IEnumerable<Candidate> GrantedActionCandidates(GameState state, Unit unit)
    {
        foreach (var grant in state.AbilitiesOf(unit.Id).Where(g => g.Definition.Timing == AbilityTiming.Action && state.CanUseAbility(unit.Id, g)))
        {
            Candidate Bind(Candidate candidate) => candidate with
            {
                GrantedAbility = grant,
                AbilityName = grant.Definition.Name,
                ObjectId = grant.SourceKind == AbilitySourceKind.Unit ? null : grant.SourceInstanceId,
                ItemTypeId = grant.SourceKind == AbilitySourceKind.Item ? grant.SourceTypeId : null,
                EquipmentTypeId = grant.SourceKind == AbilitySourceKind.Equipment ? grant.SourceTypeId : null,
                EquipmentAbilityId = grant.SourceKind == AbilitySourceKind.Equipment ? grant.GrantId : null,
                EquipmentMechanic = grant.Definition.Mechanic is StandAdjacentFriendlyMechanic
                    ? EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit : null
            };

            switch (grant.Definition.Mechanic)
            {
                case HealMechanic:
                    foreach (var target in state.Units.Where(target => target.Id != unit.Id &&
                                 target.SideId == unit.SideId && target.CurrentHp > 0 &&
                                 target.CurrentHp < state.Types.Single(t => t.Id == target.TypeId).Hp &&
                                 SpatialRules.AreAdjacent(state, unit.Id, target.Id)))
                        yield return Bind(new($"{grant.CandidatePrefix}:{target.Id}", Action: UnitAction.Heal, TargetId: target.Id));
                    break;
                case HolyWaveMechanic:
                    var targets = AdjacentHostiles(state, unit).Where(u => state.IsUpright(u.Id)).Select(u => u.Id).ToImmutableArray();
                    yield return Bind(new(grant.CandidatePrefix, Action: UnitAction.HolyWave, Relevant: !targets.IsEmpty) { TargetIds = targets });
                    break;
                case FireballMechanic:
                    foreach (var candidate in FireballCandidates(state, unit, grant.CandidatePrefix)) yield return Bind(candidate);
                    break;
                case StandAdjacentFriendlyMechanic:
                    foreach (var target in state.Units.Where(u => u.CurrentHp > 0 && u.SideId == unit.SideId &&
                                 state.Physical.Figures.Any(f => f.Id == u.Id && f.Posture == Posture.Lying) &&
                                 SpatialRules.AreAdjacent(state, unit.Id, u.Id)))
                        yield return Bind(new($"{grant.CandidatePrefix}:{target.Id}", TargetId: target.Id));
                    break;
            }
        }
    }

    private static void ResolveGrantedAbility(GameState state, DecisionRequest request, Candidate choice, ResolutionEvents events)
    {
        var grant = choice.GrantedAbility!;
        if (grant.Definition.Timing != AbilityTiming.Action)
        {
            ResolveOtherGrantedAbility(state, request, choice, events);
            return;
        }
        state.ActionsUsedThisActivation++;
        ConsumeAbilityUse(state, request.UnitId!, grant, events);
        var nativeLegacy = grant.Legacy is LegacyAbilityKind.Heal or LegacyAbilityKind.HolyWave or LegacyAbilityKind.Fireball;
        events.Add(new RulesEvent("ActionUsed", request.UnitId, choice.TargetId,
            AbilityName: nativeLegacy ? null : grant.Definition.Name)
        {
            ActionId = grant.ActionId, Category = choice.Kind, Cell = choice.Destination,
            SourceUnitId = request.UnitId
        });
        switch (grant.Definition.Mechanic)
        {
            case HolyWaveMechanic:
                foreach (var targetId in choice.TargetIds)
                    if (state.Physical.Figures.Any(f => f.Id == targetId))
                        ChangePosture(state, targetId, Posture.Lying, events, request.UnitId, grant.ActionId);
                ChangePosture(state, request.UnitId!, Posture.Lying, events, request.UnitId, grant.ActionId);
                break;
            case HealMechanic heal:
                RestoreHp(state, choice.TargetId!, heal.Amount, request.UnitId!, grant.Definition.Name, grant.ActionId, events);
                break;
            case FireballMechanic:
                BeginAttack(state, request.UnitId!,
                    new(choice.TargetIds, state.EffectiveAtkOf(request.UnitId!), grant.Definition.Name), events,
                    grant.ActionId, choice.Destination, UnitAction.Fireball);
                break;
            case StandAdjacentFriendlyMechanic:
                ChangePosture(state, choice.TargetId!, Posture.Upright, events, request.UnitId, grant.ActionId);
                break;
            default:
                throw new InvalidOperationException("Unsupported Action mechanic.");
        }
    }
}
