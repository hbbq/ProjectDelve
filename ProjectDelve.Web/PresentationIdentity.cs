using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Presentation adapts the resolved declaration. Unknown authored IDs remain intact
// so clients can apply their neutral fallback without changing gameplay identity.
internal static class PresentationIdentity
{
    internal static string? Effective(LocalAbilityDefinition ability) =>
        ability.PresentationProfileId ?? MechanicCatalog.FindByPayload(ability.Mechanic)?.Id;

    internal static string? Choice(Candidate candidate, DecisionRequest request, GameState state)
    {
        if (candidate.Ability is { } grant) return Effective(grant.Definition);
        if (candidate.Kind == ActivationChoiceKind.Move && request.IsMoveAfterAttack && request.UnitId is { } unitId)
        {
            var followUp = state.AbilitiesOf(unitId).SingleOrDefault(ability => state.AdvanceOnKillCells is not null
                ? ability.Definition.Mechanic is AdvanceOnKill : ability.Definition.Mechanic is MoveAfterAttack);
            if (followUp is not null) return Effective(followUp.Definition);
        }
        return candidate.Kind switch
        {
            ActivationChoiceKind.Move => "move",
            ActivationChoiceKind.Stay => "stay",
            ActivationChoiceKind.EndTurn => "end-turn",
            ActivationChoiceKind.SelectUnit => "activate",
            ActivationChoiceKind.RollDice => "roll-dice",
            _ when candidate.Action == UnitAction.NormalAttack => "standard-attack",
            _ when candidate.Kind == ActivationChoiceKind.FreeAction && candidate.ObjectId is not null => "collect-object",
            _ => null
        };
    }
}
