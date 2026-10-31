using System.Collections.Immutable;

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
            unit = ChangeAbilityUseCount(unit, ability, spent => spent + 1);
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
                Category = ability.Definition.Timing switch
                {
                    AbilityTiming.Action => ActivationChoiceKind.Action,
                    AbilityTiming.BonusAction => ActivationChoiceKind.BonusAction,
                    AbilityTiming.FreeAction => ActivationChoiceKind.FreeAction,
                    _ => throw new InvalidOperationException("Single Use Items require an active Ability timing.")
                }
            });
    }

    // Spending and replenishment resolve the same physical source and local key.
    private static Unit ChangeAbilityUseCount(Unit unit, ResolvedAbility ability, Func<int, int> change)
    {
        ImmutableDictionary<string, int> Change(ImmutableDictionary<string, int> counts, string key)
        {
            var spent = counts.GetValueOrDefault(key);
            var updated = change(spent);
            return updated == spent ? counts : counts.SetItem(key, updated);
        }
        if (ability.SourceKind == AbilitySourceKind.Unit)
            return unit with { AbilityUseCounts = Change(unit.AbilityUseCounts, ability.AccountingId) };
        if (ability.SourceKind == AbilitySourceKind.Equipment)
        {
            var equipment = unit.Inventory.Single(obj => obj.Id == ability.SourceInstanceId);
            return unit with { Inventory = unit.Inventory.Replace(equipment, equipment with
            { AbilityUseCounts = Change(equipment.AbilityUseCounts, ability.Key) }) };
        }
        throw new InvalidOperationException("Single Use Items use physical consumption rather than Ability charges.");
    }
}
