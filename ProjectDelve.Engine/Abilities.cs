using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

public enum AbilityTiming { Action, BonusAction, FreeAction, Passive, AfterAttack, WhenDefeated, InsteadOfDefeat, InsteadOfActivation, EndActivation }
public enum AbilitySourceKind { Unit, Equipment, Item }
public sealed record AbilityUsage(int? MaxUses = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HolyWaveMechanic), "holyWave")]
[JsonDerivedType(typeof(HealMechanic), "heal")]
[JsonDerivedType(typeof(FireballMechanic), "fireball")]
[JsonDerivedType(typeof(SelfModifiersMechanic), "selfModifiers")]
[JsonDerivedType(typeof(AdjacentModifiersMechanic), "adjacentModifiers")]
[JsonDerivedType(typeof(ExtraActionsMechanic), "extraActions")]
[JsonDerivedType(typeof(StandAdjacentFriendlyMechanic), "standAdjacentFriendly")]
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
[JsonDerivedType(typeof(SummonAdjacent), "summonAdjacent")]
[JsonDerivedType(typeof(SummonWorker), "summonWorker")]
[JsonDerivedType(typeof(SummonTroll), "summonTroll")]
[JsonDerivedType(typeof(PlaceBananaPeel), "placeBananaPeel")]
[JsonDerivedType(typeof(Boost), "boost")]
[JsonDerivedType(typeof(ShootShell), "shootShell")]
[JsonDerivedType(typeof(WaypointSlotMachine), "waypointSlotMachine")]
[JsonDerivedType(typeof(TryOpenDoor), "tryOpenDoor")]
[JsonDerivedType(typeof(FireBreath), "fireBreath")]
[JsonDerivedType(typeof(ClawAttack), "clawAttack")]
[JsonDerivedType(typeof(Phase), "phase")]
[JsonDerivedType(typeof(TransportResource), "transportResource")]
[JsonDerivedType(typeof(PickupResource), "pickupResource")]
[JsonDerivedType(typeof(GiveResource), "giveResource")]
[JsonDerivedType(typeof(Operate), "operate")]
[JsonDerivedType(typeof(OpenDoor), "openDoor")]
public abstract record AbilityMechanic;
public sealed record HolyWaveMechanic : AbilityMechanic;
public sealed record HealMechanic(int Amount) : AbilityMechanic;
public sealed record FireballMechanic : AbilityMechanic;
public sealed record SelfModifiersMechanic(ImmutableArray<ModifierThisTurn> Modifiers) : AbilityMechanic;
public sealed record AdjacentModifiersMechanic(ImmutableArray<ModifierThisTurn> Modifiers) : AbilityMechanic;
public sealed record ExtraActionsMechanic(int Amount) : AbilityMechanic;
public sealed record StandAdjacentFriendlyMechanic : AbilityMechanic;

// Resolution binds an owner's declaration to its physical source. Presentation
// and accounting identities follow that source and local key, never a name.
public sealed record ResolvedAbility(LocalAbilityDefinition Definition, AbilitySourceKind SourceKind,
    string SourceTypeId, string SourceInstanceId, string? SourceName = null)
{
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
    public CardEntryDescription CardEntry() => Definition.CardEntry(EntryId);
}

public static class AbilityContent
{
    public static void Validate(ImmutableArray<LocalAbilityDefinition> abilities, AbilitySourceKind source,
        string path = "localAbilities")
    {
        if (abilities.IsDefault)
            throw ScenarioValidationContext.At(new ArgumentException("Local Abilities cannot be null."), path);
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
        ValidateObjectCatalogs(state.EquipmentTypes, state.ItemTypes, state.LootCardDefinitions);
        for (var index = 0; index < state.Types.Count; index++)
            Validate(state.Types[index].LocalAbilities, AbilitySourceKind.Unit, $"unitTypes[{index}].localAbilities");
        foreach (var unit in state.Units)
        {
            var limited = state.AbilitiesOf(unit.Id).Where(ability => ability.SourceKind == AbilitySourceKind.Unit && ability.Definition.Usage.MaxUses is not null)
                .ToDictionary(ability => ability.AccountingId, ability => ability.Definition.Usage.MaxUses!.Value);
            if (unit.AbilityUseCounts.Any(pair => !limited.TryGetValue(pair.Key, out var max) || pair.Value < 0 || pair.Value > max))
                throw new ArgumentException("Ability use counters must match the Unit's limited Abilities.");
        }
    }

    // The definition boundary is shared by scenario/runtime validation and
    // authoring tools. It needs catalogs, never a Board, Unit or live game.
    public static void ValidateObjectCatalogs(IReadOnlyList<EquipmentDefinition> equipment,
        IReadOnlyList<ItemDefinition> items, IReadOnlyList<LootCardDefinition> loot)
    {
        EquipmentCatalog.Validate(equipment);
        ItemCatalog.Validate(items);
        LootCardCatalog.Validate(loot);
        for (var index = 0; index < equipment.Count; index++)
            Validate(equipment[index].LocalAbilities, AbilitySourceKind.Equipment, $"equipmentTypes[{index}].localAbilities");
        for (var index = 0; index < items.Count; index++)
            Validate(items[index].LocalAbilities, AbilitySourceKind.Item, $"itemTypes[{index}].localAbilities");
        for (var index = 0; index < loot.Count; index++)
        {
            var card = loot[index];
            if (card.ItemTypeId is { } item && !items.Any(type => type.Id == item))
                throw ScenarioValidationContext.At(new ArgumentException($"Unknown Item Type ID '{item}'."), $"lootCardDefinitions[{index}].itemTypeId");
            if (card.EquipmentTypeId is { } typeId && !equipment.Any(type => type.Id == typeId))
                throw ScenarioValidationContext.At(new ArgumentException($"Unknown Equipment Type ID '{typeId}'."), $"lootCardDefinitions[{index}].equipmentTypeId");
        }
    }
}

public static class AbilityQueries
{
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

    public static AbilityUses? AbilityUsesOf(this GameState state, string unitId, ResolvedAbility ability) =>
        ability.Definition.Usage.MaxUses is { } max
            ? new(max, max - state.Units.Single(unit => unit.Id == unitId).AbilityUseCounts.GetValueOrDefault(ability.AccountingId))
            : null;
    public static bool CanUseAbility(this GameState state, string unitId, ResolvedAbility ability) => state.IsUpright(unitId) &&
        state.Units.Single(unit => unit.Id == unitId).CurrentHp > 0 && state.AbilityUsesOf(unitId, ability) is not { RemainingUses: <= 0 } &&
        (ability.Definition.Timing != AbilityTiming.BonusAction || state.BonusActionUsesThisActivation(ability.AccountingId) < state.EffectiveBonusActionUsesPerActivation);

    public static int AbilityModifier(this GameState state, string unitId, Stat stat)
    {
        bool Relevant(LocalAbilityDefinition ability) => ability.Timing == AbilityTiming.Passive && (ability.Mechanic switch
        {
            SelfModifiersMechanic self => self.Modifiers.Any(modifier => modifier.Stat == stat),
            AdjacentModifiersMechanic adjacent => adjacent.Modifiers.Any(modifier => modifier.Stat == stat),
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
        foreach (var source in state.Units.Where(unit => unit.CurrentHp > 0))
        foreach (var mechanic in Passives(source))
        {
            var modifiers = mechanic switch
            {
                SelfModifiersMechanic self when source.Id == unitId => self.Modifiers,
                AdjacentModifiersMechanic adjacent when source.SideId == target.SideId => adjacent.Modifiers,
                _ => []
            };
            var amount = modifiers.Where(modifier => modifier.Stat == stat).Sum(modifier => modifier.Amount);
            if (amount == 0 || !state.IsUpright(source.Id)) continue;
            if (mechanic is AdjacentModifiersMechanic && !SpatialRules.AreAdjacent(state, source.Id, unitId)) continue;
            total += amount;
        }
        return total;
    }
}
