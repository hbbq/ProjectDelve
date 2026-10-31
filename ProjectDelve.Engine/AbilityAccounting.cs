namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    // ApplyDecision commits the ordinary Action budget before calling this once.
    // Limited uses and Item ownership change before the concrete mechanic runs.
    private static void ConsumeAbilityUse(GameState state, string unitId, ResolvedAbility ability, ResolutionEvents events)
    {
        var index = state.Units.FindIndex(unit => unit.Id == unitId);
        var unit = state.Units[index];
        if (ability.Definition.Usage.MaxUses is not null)
            unit = unit with
            {
                AbilityUseCounts = unit.AbilityUseCounts.SetItem(ability.AccountingId,
                    unit.AbilityUseCounts.GetValueOrDefault(ability.AccountingId) + 1)
            };
        if (ability.Definition.Timing == AbilityTiming.BonusAction)
            state.BonusActionUseCountsThisActivation[ability.AccountingId] = state.BonusActionUsesThisActivation(ability.AccountingId) + 1;
        OwnedObject? consumed = null;
        if (ability.SourceKind == AbilitySourceKind.Item)
        {
            consumed = unit.Inventory.Single(obj => obj.Id == ability.SourceInstanceId);
            unit = unit with { Inventory = unit.Inventory.Remove(consumed) };
            state.LootDeck ??= new();
            state.LootDeck.DiscardPile.Add(consumed);
        }
        state.Units[index] = unit;
        if (consumed is not null)
            events.Add(new RulesEvent("ItemUsed", unitId, AbilityName: ability.Definition.Name)
            {
                ObjectCard = consumed, ActionId = ability.ActionId,
                Category = ability.Definition.Timing == AbilityTiming.Action ? ActivationChoiceKind.Action : ActivationChoiceKind.BonusAction
            });
    }

    private static IEnumerable<Candidate> OtherAbilityCandidates(GameState state, Unit unit)
    {
        foreach (var ability in state.AbilitiesOf(unit.Id).Where(ability =>
                     ability.Definition.Timing == AbilityTiming.BonusAction &&
                     ability.Definition.Mechanic is SelfModifiersMechanic or ExtraActionsMechanic &&
                     state.CanUseAbility(unit.Id, ability)))
        {
            yield return new(ability.CandidatePrefix, Kind: ActivationChoiceKind.BonusAction,
                Relevant: ability.Definition.Mechanic is not SelfModifiersMechanic self || BonusActionRelevant(state, unit, self))
            {
                Ability = ability, AbilityName = ability.Definition.Name,
                ObjectId = ability.SourceKind == AbilitySourceKind.Unit ? null : ability.SourceInstanceId,
                EquipmentTypeId = ability.SourceKind == AbilitySourceKind.Equipment ? ability.SourceTypeId : null,
                ItemTypeId = ability.SourceKind == AbilitySourceKind.Item ? ability.SourceTypeId : null
            };
        }
    }

    private static void ResolveOtherAbility(GameState state, DecisionRequest request, Candidate choice, ResolutionEvents events)
    {
        var ability = choice.Ability!;
        switch (ability.Definition.Mechanic)
        {
            case SelfModifiersMechanic self:
                state.ModifiersThisTurn.AddRange(self.Modifiers);
                break;
            case ExtraActionsMechanic extra:
                state.ExtraActionsThisActivation += extra.Amount;
                break;
            default: throw new InvalidOperationException("Unsupported Bonus Action mechanic.");
        }
        events.Add(new RulesEvent("ActionUsed", request.UnitId, AbilityName: ability.Definition.Name)
            { ActionId = ability.ActionId, Category = choice.Kind, SourceUnitId = request.UnitId });
    }
}
