using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

public enum AbilityTiming { Action, BonusAction, FreeAction, Passive, AfterAttack, WhenDefeated, InsteadOfDefeat, InsteadOfActivation, EndActivation }
public enum RelationshipSelector { Friendlies, Enemies, All }
public enum AbilitySourceKind { Unit, Equipment, Item }
// Attribution for the currently resolving invocation; does not retain an
// executable definition or require a consumed Item to remain in inventory.
public sealed record AbilitySourceReference(AbilitySourceKind Kind, string TypeId, string InstanceId, string Key);
public sealed record AbilityUsage(int? MaxUses = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AdjacentUnitsPostureChangeMechanic), "adjacentUnitsPostureChange")]
[JsonDerivedType(typeof(AdjacentUnitHealMechanic), "adjacentUnitHeal")]
[JsonDerivedType(typeof(FireballMechanic), "fireball")]
[JsonDerivedType(typeof(SelfModifiersMechanic), "selfModifiers")]
[JsonDerivedType(typeof(AdjacentUnitsStatModifiersMechanic), "adjacentUnitsStatModifiers")]
[JsonDerivedType(typeof(ExtraActionsMechanic), "extraActions")]
[JsonDerivedType(typeof(AdjacentUnitPostureChangeMechanic), "adjacentUnitPostureChange")]
[JsonDerivedType(typeof(Cleave), "cleave")]
[JsonDerivedType(typeof(Fury), "fury")]
[JsonDerivedType(typeof(Backstab), "backstab")]
[JsonDerivedType(typeof(Telekinesis), "telekinesis")]
[JsonDerivedType(typeof(MoveAfterAttack), "moveAfterAttack")]
[JsonDerivedType(typeof(AdvanceOnKill), "advanceOnKill")]
[JsonDerivedType(typeof(Explosion), "explosion")]
[JsonDerivedType(typeof(Undying), "undying")]
[JsonDerivedType(typeof(RunRails), "runRails")]
[JsonDerivedType(typeof(ReshapeRoom), "reshapeRoom")]
[JsonDerivedType(typeof(Swap), "swap")]
[JsonDerivedType(typeof(Displace), "displace")]
[JsonDerivedType(typeof(AdjacentUnitSummonMechanic), "adjacentUnitSummon")]
[JsonDerivedType(typeof(PlaceBananaPeel), "placeBananaPeel")]
[JsonDerivedType(typeof(SelfExtraMovesMechanic), "selfExtraMoves")]
[JsonDerivedType(typeof(TargetUnitAttackMechanic), "targetUnitAttack")]
[JsonDerivedType(typeof(WaypointSlotMachine), "waypointSlotMachine")]
[JsonDerivedType(typeof(TryOpenDoor), "tryOpenDoor")]
[JsonDerivedType(typeof(BreakObject), "breakObject")]
[JsonDerivedType(typeof(BreakAdjacentObjects), "breakAdjacentObjects")]
[JsonDerivedType(typeof(DefeatSelf), "defeatSelf")]
[JsonDerivedType(typeof(FireBreath), "fireBreath")]
[JsonDerivedType(typeof(ClawAttack), "clawAttack")]
[JsonDerivedType(typeof(Phase), "phase")]
[JsonDerivedType(typeof(Amphibious), "amphibious")]
[JsonDerivedType(typeof(RevealDistance), "revealDistance")]
[JsonDerivedType(typeof(TransportResource), "transportResource")]
[JsonDerivedType(typeof(PickupResource), "pickupResource")]
[JsonDerivedType(typeof(GiveResource), "giveResource")]
[JsonDerivedType(typeof(Operate), "operate")]
[JsonDerivedType(typeof(OpenDoor), "openDoor")]
public abstract record AbilityMechanic;
public sealed record Amphibious : AbilityMechanic;
public sealed record RevealDistance([property: JsonRequired] int Distance) : AbilityMechanic;
public sealed record AdjacentUnitsPostureChangeMechanic([property: JsonRequired] RelationshipSelector Selector, [property: JsonRequired] Posture NewPosture) : AbilityMechanic;
public sealed record AdjacentUnitHealMechanic([property: JsonRequired] RelationshipSelector Selector, [property: JsonRequired] int Amount) : AbilityMechanic;
public sealed record FireballMechanic : AbilityMechanic;
public sealed record SelfModifiersMechanic(ImmutableArray<ModifierThisTurn> Modifiers) : AbilityMechanic;
public sealed record AdjacentUnitsStatModifiersMechanic([property: JsonRequired] RelationshipSelector Selector, [property: JsonRequired] ImmutableArray<ModifierThisTurn> Modifiers) : AbilityMechanic;
public sealed record ExtraActionsMechanic(int Amount) : AbilityMechanic;
public sealed record AdjacentUnitPostureChangeMechanic([property: JsonRequired] RelationshipSelector Selector, [property: JsonRequired] Posture NewPosture) : AbilityMechanic;

// Resolution binds an owner's declaration to its physical source. Presentation
// and accounting identities follow that source and local key, never a name.
public sealed record ResolvedAbility(LocalAbilityDefinition Definition, AbilitySourceKind SourceKind,
    string SourceTypeId, string SourceInstanceId, string? SourceName = null)
{
    [JsonIgnore]
    public AbilitySourceReference SourceReference => new(SourceKind, SourceTypeId, SourceInstanceId, Key);
    public string Key => Definition.Key;
    public string AccountingId => SourceKind switch
    {
        AbilitySourceKind.Unit => $"unit:{AbilityQueries.Key(Key)}",
        AbilitySourceKind.Equipment => $"equipment:{AbilityQueries.Key(SourceInstanceId)}:{AbilityQueries.Key(Key)}",
        AbilitySourceKind.Item => $"item:{AbilityQueries.Key(SourceTypeId)}:{AbilityQueries.Key(Key)}",
        _ => throw new ArgumentException("Unsupported Ability source.")
    };
    public string EntryId => SourceKind == AbilitySourceKind.Unit ? $"ability:{AbilityQueries.Key(Key)}"
        : $"{SourceKind.ToString().ToLowerInvariant()}:{AbilityQueries.Key(SourceTypeId)}:{AbilityQueries.Key(Key)}";
    public string CandidatePrefix => SourceKind == AbilitySourceKind.Unit ? EntryId
        : $"{SourceKind.ToString().ToLowerInvariant()}:{AbilityQueries.Key(SourceInstanceId)}:{AbilityQueries.Key(Key)}";
    public string ActionId => EntryId;
    public CardEntryDescription CardEntry(IReadOnlyList<UnitType>? existingTypes = null) => Definition.CardEntry(EntryId, existingTypes);
}

public static class AbilityContent
{
    public static void Validate(ImmutableArray<LocalAbilityDefinition> abilities, AbilitySourceKind source,
        string path = "localAbilities")
    {
        if (abilities.IsDefault)
            throw ScenarioValidationContext.At(new ArgumentException("Local Abilities cannot be null."), path);
        if (abilities.Count(a => a?.Timing == AbilityTiming.InsteadOfActivation) > 1)
            throw ScenarioValidationContext.At(new ArgumentException("Only one InsteadOfActivation Ability may replace an activation."), path);
        HashSet<string> seen = new(StringComparer.Ordinal);
        HashSet<Type> nativeMechanics = [];
        for (var index = 0; index < abilities.Length; index++)
        {
            var ability = abilities[index];
            var abilityPath = $"{path}[{index}]";
            if (ability is null)
                throw ScenarioValidationContext.At(new ArgumentException("An Ability declaration is required."), abilityPath);
            if (string.IsNullOrWhiteSpace(ability.Key) || !seen.Add(ability.Key))
                throw ScenarioValidationContext.At(new ArgumentException("Abilities require distinct nonblank keys within their source."), abilityPath + ".key");
            if (string.IsNullOrWhiteSpace(ability.Name))
                throw ScenarioValidationContext.At(new ArgumentException("An Ability display name is required."), abilityPath + ".name");
            if (!Enum.IsDefined(ability.Timing))
                throw ScenarioValidationContext.At(new ArgumentException("A supported Ability timing is required."), abilityPath + ".timing");
            if (ability.Usage is null || ability.Usage.MaxUses is < 1)
                throw ScenarioValidationContext.At(new ArgumentException("Ability use limits must be positive."), abilityPath + ".usage");
            var costDiagnostics = CostCatalog.Validate(ability.Costs,
                new(MechanicCatalog.AbilitySourceContext(source), MechanicCatalog.AbilityTimingContext(ability.Timing), ability.Usage.MaxUses), abilityPath + ".costs");
            if (!costDiagnostics.IsEmpty)
                throw ScenarioValidationContext.At(new ArgumentException(string.Join(" ", costDiagnostics.Select(d => $"{d.Path}: {d.Message}"))), costDiagnostics[0].Path);
            if (!ability.Costs.IsEmpty && !AbilityCostRules.Supports(ability.Mechanic))
                throw ScenarioValidationContext.At(new ArgumentException("executionUnavailable: This mechanic does not support Ability cost execution."), abilityPath + ".costs");
            var diagnostics = MechanicCatalog.Validate(ability.Mechanic,
                new(MechanicCatalog.AbilitySourceContext(source), MechanicCatalog.AbilityTimingContext(ability.Timing), ability.Usage.MaxUses), abilityPath + ".mechanic");
            if (!diagnostics.IsEmpty)
            {
                var first = diagnostics[0];
                var field = first.Code == "unsupportedUsage" ? abilityPath + ".usage"
                    : first.Code == "unsupportedContext" ? abilityPath + ".timing" : first.Path;
                throw ScenarioValidationContext.At(new ArgumentException(string.Join(" ", diagnostics.Select(d => $"{d.Path}: {d.Message}"))), field);
            }
            var contract = MechanicCatalog.FindByPayload(ability.Mechanic)!;
            if (contract.MaxPerSource == 1 && !nativeMechanics.Add(ability.Mechanic.GetType()))
                throw ScenarioValidationContext.At(new ArgumentException("This concrete mechanic may occur only once per source."), abilityPath + ".mechanic");
            try { MechanicCatalog.ValidateAbilityOverride(ability); }
            catch (ArgumentException error) { throw ScenarioValidationContext.At(error, abilityPath + ".descriptionOverride"); }
        }
    }

    public static void ValidateState(GameState state)
    {
        ValidateObjectCatalogs(state.EquipmentTypes, state.ItemTypes, state.LootCardDefinitions, state.Types);
        for (var index = 0; index < state.Types.Count; index++)
        {
            Validate(state.Types[index].LocalAbilities, AbilitySourceKind.Unit, $"unitTypes[{index}].localAbilities");
            ValidateReferences(state.Types[index].LocalAbilities, state.Types, $"unitTypes[{index}].localAbilities");
        }
        foreach (var unit in state.Units)
        {
            var type = state.Types.Single(type => type.Id == unit.TypeId);
            var limited = AbilityQueries.NativeAbilities(type, unit.Id).Where(ability => ability.Definition.Usage.MaxUses is not null)
                .ToDictionary(ability => ability.AccountingId, ability => ability.Definition.Usage.MaxUses!.Value);
            if (unit.AbilityUseCounts is null || unit.AbilityUseCounts.Any(pair => !limited.TryGetValue(pair.Key, out var max) || pair.Value < 0 || pair.Value > max))
                throw new ArgumentException("Ability use counters must match the Unit's limited Abilities.");
        }
        var objects = (state.LootDeck?.DrawPile ?? []).Concat(state.LootDeck?.DiscardPile ?? [])
            .Concat(state.GroundObjects.Select(ground => ground?.Object)).Concat(state.Units.SelectMany(unit => unit.Inventory));
        foreach (var obj in objects)
        {
            if (obj is null || obj.AbilityUseCounts is null)
                throw new ArgumentException("Physical objects require nonnull Ability use counters.");
            var limited = state.EquipmentTypeId(obj) is { } equipmentId
                ? state.Equipment(equipmentId).LocalAbilities.Where(ability => ability.Usage.MaxUses is not null)
                    .ToDictionary(ability => ability.Key, ability => ability.Usage.MaxUses!.Value, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
            if (obj.AbilityUseCounts.Any(pair => !limited.TryGetValue(pair.Key, out var max) || pair.Value < 0 || pair.Value > max))
                throw new ArgumentException("Physical object use counters must match its limited Equipment Abilities.");
        }
    }

    // The definition boundary is shared by scenario/runtime validation and
    // authoring tools. It needs catalogs, never a Board, Unit or live game.
    public static void ValidateObjectCatalogs(IReadOnlyList<EquipmentDefinition> equipment,
        IReadOnlyList<ItemDefinition> items, IReadOnlyList<LootCardDefinition> loot,
        IReadOnlyList<UnitType>? unitTypes = null)
    {
        EquipmentCatalog.Validate(equipment);
        ItemCatalog.Validate(items);
        LootCardCatalog.Validate(loot);
        for (var index = 0; index < equipment.Count; index++)
        {
            Validate(equipment[index].LocalAbilities, AbilitySourceKind.Equipment, $"equipmentTypes[{index}].localAbilities");
            ValidateReferences(equipment[index].LocalAbilities, unitTypes, $"equipmentTypes[{index}].localAbilities");
        }
        for (var index = 0; index < items.Count; index++)
        {
            Validate(items[index].LocalAbilities, AbilitySourceKind.Item, $"itemTypes[{index}].localAbilities");
            ValidateReferences(items[index].LocalAbilities, unitTypes, $"itemTypes[{index}].localAbilities");
        }
        for (var index = 0; index < loot.Count; index++)
        {
            var card = loot[index];
            if (card.ItemTypeId is { } item && !items.Any(type => type.Id == item))
                throw ScenarioValidationContext.At(new ArgumentException($"Unknown Item Type ID '{item}'."), $"lootCardDefinitions[{index}].itemTypeId");
            if (card.EquipmentTypeId is { } typeId && !equipment.Any(type => type.Id == typeId))
                throw ScenarioValidationContext.At(new ArgumentException($"Unknown Equipment Type ID '{typeId}'."), $"lootCardDefinitions[{index}].equipmentTypeId");
        }
    }

    // Payload validation establishes a well-formed reference. Resolve it only at
    // the content boundary, where scenario-local definitions are available.
    public static void ValidateReferences(ImmutableArray<LocalAbilityDefinition> abilities,
        IReadOnlyList<UnitType>? unitTypes, string path = "localAbilities")
    {
        for (var index = 0; index < abilities.Length; index++)
            if (abilities[index].Mechanic is AdjacentUnitSummonMechanic summon && !(unitTypes?.Any(type => type.Id == summon.UnitTypeId) ?? false))
                throw ScenarioValidationContext.At(new ArgumentException($"Unknown summoned Unit Type ID '{summon.UnitTypeId}'."),
                    $"{path}[{index}].mechanic.unitTypeId");
    }
}

public static class AbilityQueries
{
    internal static bool MatchesRelationship(GameState state, Unit source, Unit recipient, RelationshipSelector selector) => selector switch
    {
        RelationshipSelector.Friendlies => source.SideId == recipient.SideId,
        RelationshipSelector.Enemies => state.AreHostile(source.SideId, recipient.SideId),
        RelationshipSelector.All => true,
        _ => false
    };

    internal static IEnumerable<Unit> AdjacentRecipients(GameState state, Unit source, RelationshipSelector selector) =>
        state.Units.Where(recipient => recipient.CurrentHp > 0 &&
                state.Physical.Figures.Any(figure => figure.Id == recipient.Id) &&
                MatchesRelationship(state, source, recipient, selector) && SpatialRules.AreAdjacent(state, source.Id, recipient.Id))
            .OrderBy(recipient => state.Physical.Figures.Single(figure => figure.Id == recipient.Id).Position.Y)
            .ThenBy(recipient => state.Physical.Figures.Single(figure => figure.Id == recipient.Id).Position.X)
            .ThenBy(recipient => recipient.Id, StringComparer.Ordinal);

    internal static string Key(string value) => Uri.EscapeDataString(value);
    public static IEnumerable<ResolvedAbility> NativeAbilities(UnitType type, string unitId) =>
        type.LocalAbilities.Select(ability => new ResolvedAbility(ability, AbilitySourceKind.Unit, type.Id, unitId, type.DisplayName ?? type.Id));

    public static IEnumerable<ResolvedAbility> AbilitiesOf(this GameState state, string unitId)
    {
        var unit = state.Units.Single(unit => unit.Id == unitId);
        var type = state.Types.Single(type => type.Id == unit.TypeId);
        foreach (var ability in NativeAbilities(type, unit.Id)) yield return ability;
        foreach (var obj in unit.Inventory)
        {
            if (state.EquipmentTypeId(obj) is { } equipmentId)
            {
                var equipment = state.Equipment(equipmentId);
                foreach (var ability in equipment.LocalAbilities)
                    yield return new(ability, AbilitySourceKind.Equipment, equipment.Id, obj.Id, equipment.Name);
            }
            else if (state.ItemTypeId(obj) is { } itemId)
            {
                var item = state.Item(itemId);
                foreach (var ability in item.LocalAbilities)
                    yield return new(ability, AbilitySourceKind.Item, item.Id, obj.Id, item.Name);
            }
        }
    }

    public static AbilityUses? AbilityUsesOf(this GameState state, string unitId, ResolvedAbility ability)
    {
        if (ability.Definition.Usage.MaxUses is not { } max) return null;
        var unit = state.Units.Single(unit => unit.Id == unitId);
        return ability.SourceKind switch
        {
            AbilitySourceKind.Unit => new(max, max - unit.AbilityUseCounts.GetValueOrDefault(ability.AccountingId)),
            AbilitySourceKind.Equipment => state.EquipmentAbilityUsesOf(
                unit.Inventory.Single(obj => obj.Id == ability.SourceInstanceId), ability.Definition),
            _ => throw new ArgumentException("Single Use Items use physical consumption rather than Ability charges.")
        };
    }

    public static AbilityUses? EquipmentAbilityUsesOf(this GameState state, OwnedObject obj, LocalAbilityDefinition ability) =>
        ability.Usage.MaxUses is { } max ? new(max, max - obj.AbilityUseCounts.GetValueOrDefault(ability.Key)) : null;
    public static bool CanUseAbility(this GameState state, string unitId, ResolvedAbility ability) => AbilityCostRules.CanAfford(state, unitId, ability) && state.IsUpright(unitId) &&
        state.Units.Single(unit => unit.Id == unitId).CurrentHp > 0 && state.AbilityUsesOf(unitId, ability) is not { RemainingUses: <= 0 } &&
        (ability.Definition.Timing != AbilityTiming.BonusAction || state.BonusActionUsesThisActivation(ability.AccountingId) < state.EffectiveBonusActionUsesPerActivation);

    public static int AbilityModifier(this GameState state, string unitId, Stat stat)
    {
        bool Relevant(LocalAbilityDefinition ability) => ability.Timing == AbilityTiming.Passive && (ability.Mechanic switch
        {
            SelfModifiersMechanic self => self.Modifiers.Any(modifier => modifier.Stat == stat),
            AdjacentUnitsStatModifiersMechanic adjacent => adjacent.Modifiers.Any(modifier => modifier.Stat == stat),
            _ => false
        });
        if (!state.Types.Any(type => type.LocalAbilities.Any(Relevant)) &&
            !state.EquipmentTypes.Any(type => type.LocalAbilities.Any(Relevant))) return 0;
        var target = state.Units.Single(unit => unit.Id == unitId);
        if (target.CurrentHp <= 0 || !state.Physical.Figures.Any(figure => figure.Id == unitId)) return 0;
        IEnumerable<AbilityMechanic> Passives(Unit source)
        {
            var type = state.Types.Single(type => type.Id == source.TypeId);
            foreach (var ability in type.LocalAbilities)
                if (ability.Timing == AbilityTiming.Passive) yield return ability.Mechanic;
            foreach (var obj in source.Inventory)
                if (state.EquipmentTypeId(obj) is { } id)
                    foreach (var ability in state.Equipment(id).LocalAbilities)
                        if (ability.Timing == AbilityTiming.Passive) yield return ability.Mechanic;
        }
        var total = 0;
        foreach (var source in state.Units.Where(unit => unit.CurrentHp > 0 && state.Physical.Figures.Any(figure => figure.Id == unit.Id)))
        foreach (var mechanic in Passives(source))
        {
            var modifiers = mechanic switch
            {
                SelfModifiersMechanic self when source.Id == unitId => self.Modifiers,
                AdjacentUnitsStatModifiersMechanic adjacent when MatchesRelationship(state, source, target, adjacent.Selector) => adjacent.Modifiers,
                _ => []
            };
            var amount = modifiers.Where(modifier => modifier.Stat == stat).Sum(modifier => modifier.Amount);
            if (amount == 0 || !state.IsUpright(source.Id)) continue;
            if (mechanic is AdjacentUnitsStatModifiersMechanic && !SpatialRules.AreAdjacent(state, source.Id, unitId)) continue;
            total += amount;
        }
        return total;
    }
}
