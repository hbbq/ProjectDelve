namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ConsumeAbilityUse(GameState state, string unitId, ResolvedAbility grant, ResolutionEvents events)
    {
        var index = state.Units.FindIndex(u => u.Id == unitId);
        var unit = state.Units[index];
        if (state.AbilityUsesOf(unitId, grant) is { } uses)
        {
            var remaining = new AbilityUses(uses.MaxUses, uses.RemainingUses - 1);
            unit = grant.Legacy switch
            {
                LegacyAbilityKind.Heal => unit with { HealUses = remaining },
                LegacyAbilityKind.HolyWave => unit with { HolyWaveUses = remaining },
                LegacyAbilityKind.Fireball => unit with { FireballUses = remaining },
                LegacyAbilityKind.BonusAction => unit with { BonusActionUses = unit.BonusActionUses.SetItem(grant.LegacyKey!, remaining) },
                _ => unit with { AbilityUseCounts = unit.AbilityUseCounts.SetItem(grant.AccountingId, uses.MaxUses - remaining.RemainingUses) }
            };
        }
        if (grant.Definition.Timing == AbilityTiming.BonusAction)
        {
            state.BonusActionUseCountsThisActivation[grant.AccountingId] = state.BonusActionUsesThisActivation(grant.AccountingId) + 1;
            if (grant.Legacy == LegacyAbilityKind.BonusAction) state.BonusActionsUsedThisActivation.Add(grant.AccountingId);
        }
        OwnedObject? consumed = null;
        if (grant.SourceKind == AbilitySourceKind.Item)
        {
            consumed = unit.Inventory.Single(o => o.Id == grant.SourceInstanceId);
            unit = unit with { Inventory = unit.Inventory.Remove(consumed) };
            state.LootDeck ??= new();
            state.LootDeck.DiscardPile.Add(consumed);
        }
        state.Units[index] = unit;
        // Only the legacy Bonus Action path defers ItemUsed until after its effect.
        // An Action granted by that same Item identity uses ordinary consumption events.
        if (consumed is not null && !(grant.Legacy == LegacyAbilityKind.SecondWind &&
                grant.Definition.Timing == AbilityTiming.BonusAction))
            events.Add(new RulesEvent("ItemUsed", unitId, AbilityName: grant.Definition.Name)
                { ObjectCard = consumed, ActionId = grant.EntryId, Category = grant.Definition.Timing == AbilityTiming.Action ? ActivationChoiceKind.Action : ActivationChoiceKind.BonusAction });
    }

    private static IEnumerable<Candidate> GrantedOtherCandidates(GameState state, Unit unit)
    {
        foreach (var grant in state.AbilitiesOf(unit.Id).Where(g => g.Definition.Timing == AbilityTiming.BonusAction && state.CanUseAbility(unit.Id, g)))
        {
            var bonus = grant.Definition.Mechanic is SelfModifiersMechanic self
                ? new BonusActionAbility(grant.AccountingId, grant.Definition.Usage.MaxUses, self.Modifiers) { DisplayName = grant.Definition.Name }
                : null;
            yield return new(grant.CandidatePrefix, Kind: ActivationChoiceKind.BonusAction,
                Relevant: bonus is null || BonusActionRelevant(state, unit, bonus), BonusAction: bonus)
            {
                GrantedAbility = grant, AbilityName = grant.Definition.Name,
                ObjectId = grant.SourceKind == AbilitySourceKind.Unit ? null : grant.SourceInstanceId,
                ObjectKind = grant.Legacy == LegacyAbilityKind.SecondWind ? LootCardKind.SecondWind : null,
                EquipmentTypeId = grant.SourceKind == AbilitySourceKind.Equipment ? grant.SourceTypeId : null,
                ItemTypeId = grant.SourceKind == AbilitySourceKind.Item ? grant.SourceTypeId : null
            };
        }
    }

    private static void ResolveOtherGrantedAbility(GameState state, DecisionRequest request, Candidate choice, ResolutionEvents events)
    {
        var grant = choice.GrantedAbility!;
        ConsumeAbilityUse(state, request.UnitId!, grant, events);
        switch (grant.Definition.Mechanic)
        {
            case SelfModifiersMechanic self:
                state.ModifiersThisTurn.AddRange(self.Modifiers);
                break;
            case ExtraActionsMechanic extra:
                state.ExtraActionsThisActivation += extra.Amount;
                break;
            default: throw new InvalidOperationException("Unsupported Bonus Action mechanic.");
        }
        if (grant.Legacy == LegacyAbilityKind.SecondWind)
            events.Add(new RulesEvent("ItemUsed", request.UnitId, AbilityName: grant.Definition.Name)
            { ObjectCard = state.LootDeck!.DiscardPile.Single(o => o.Id == grant.SourceInstanceId), ActionId = "second-wind", Category = choice.Kind });
        else
        {
            events.Add(new RulesEvent("ActionUsed", request.UnitId, AbilityName: grant.Definition.Name)
            { ActionId = grant.EntryId, Category = choice.Kind, SourceUnitId = request.UnitId });
            if (grant.Legacy == LegacyAbilityKind.BonusAction)
                events.Add(new RulesEvent("AbilityUsed", request.UnitId, AbilityName: grant.Definition.Name));
        }
    }
}
