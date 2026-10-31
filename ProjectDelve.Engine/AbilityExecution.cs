using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    // Content resolution supplies the concrete mechanic and source. These handlers
    // never look up an effect through a Unit field, display name, or object Type ID.
    private static bool IsSharedActiveMechanic(AbilityMechanic mechanic) => mechanic is
        HolyWaveMechanic or HealMechanic or FireballMechanic or StandAdjacentFriendlyMechanic or SelfModifiersMechanic or ExtraActionsMechanic;

    private static IEnumerable<Candidate> AbilityActionCandidates(GameState state, Unit unit)
    {
        foreach (var ability in state.AbilitiesOf(unit.Id).Where(g => g.Definition.Timing == AbilityTiming.Action &&
                     IsSharedActiveMechanic(g.Definition.Mechanic) && state.CanUseAbility(unit.Id, g)))
        {
            Candidate Bind(Candidate candidate) => candidate with
            {
                Ability = ability,
                AbilityName = ability.Definition.Name,
                ObjectId = ability.SourceKind == AbilitySourceKind.Unit ? null : ability.SourceInstanceId,
                ItemTypeId = ability.SourceKind == AbilitySourceKind.Item ? ability.SourceTypeId : null,
                EquipmentTypeId = ability.SourceKind == AbilitySourceKind.Equipment ? ability.SourceTypeId : null
            };

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
            }
        }
    }

    private static void ResolveAbility(GameState state, DecisionRequest request, Candidate choice, ResolutionEvents events)
    {
        var ability = choice.Ability!;
        if (ability.Definition.Timing != AbilityTiming.Action)
        {
            ResolveOtherAbility(state, request, choice, events);
            return;
        }
        events.Add(new RulesEvent("ActionUsed", request.UnitId, choice.TargetId,
            AbilityName: ability.Definition.Name)
        {
            ActionId = ability.ActionId, Category = choice.Kind, Cell = choice.Destination,
            SourceUnitId = request.UnitId
        });
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
                    ability.ActionId, choice.Destination, UnitAction.Fireball);
                break;
            case StandAdjacentFriendlyMechanic:
                ChangePosture(state, choice.TargetId!, Posture.Upright, events, request.UnitId, ability.ActionId);
                break;
            default:
                throw new InvalidOperationException("Unsupported Action mechanic.");
        }
    }
}
