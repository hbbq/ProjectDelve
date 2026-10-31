using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

public enum AbilityTiming { Action, BonusAction, FreeAction, Passive }
public enum AbilitySourceKind { Unit, Equipment, Item }
public enum LegacyAbilityKind { None, Heal, HolyWave, Fireball, BonusAction, EquipmentAction, SecondWind, Aura }
public sealed record AbilityUsage(int? MaxUses = null);
// Persisted identity/counter bindings only. The referenced definition supplies all rules values.
public sealed record AbilityGrantCompatibility(LegacyAbilityKind Kind, string? Key = null, string? CandidatePrefix = null);
public sealed record AbilityGrant(string Id, string AbilityId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AbilityGrantCompatibility? Compatibility { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HolyWaveMechanic), "holyWave")]
[JsonDerivedType(typeof(HealMechanic), "heal")]
[JsonDerivedType(typeof(FireballMechanic), "fireball")]
[JsonDerivedType(typeof(SelfModifiersMechanic), "selfModifiers")]
[JsonDerivedType(typeof(AdjacentModifiersMechanic), "adjacentModifiers")]
[JsonDerivedType(typeof(ExtraActionsMechanic), "extraActions")]
[JsonDerivedType(typeof(StandAdjacentFriendlyMechanic), "standAdjacentFriendly")]
public abstract record AbilityMechanic;
public sealed record HolyWaveMechanic : AbilityMechanic;
public sealed record HealMechanic(int Amount) : AbilityMechanic;
public sealed record FireballMechanic : AbilityMechanic;
public sealed record SelfModifiersMechanic(ImmutableArray<ModifierThisTurn> Modifiers) : AbilityMechanic;
public sealed record AdjacentModifiersMechanic(ImmutableArray<ModifierThisTurn> Modifiers) : AbilityMechanic;
public sealed record ExtraActionsMechanic(int Amount) : AbilityMechanic;
public sealed record StandAdjacentFriendlyMechanic : AbilityMechanic;

public sealed record AbilityDefinition(string Id, string Name, AbilityTiming Timing, AbilityMechanic Mechanic)
{
    public AbilityUsage Usage { get; init; } = new();
    // Overrides have no numeric parameters of their own. For parameterized mechanics,
    // use {amount} or {modifiers}; values always come from the mechanic declaration.
    public string? DescriptionOverride { get; init; }
    public CardEntryDescription CardEntry(string? entryId = null)
    {
        var modifiers = Mechanic switch { SelfModifiersMechanic m => m.Modifiers, AdjacentModifiersMechanic m => m.Modifiers, _ => [] };
        var modifierText = string.Join("; ", modifiers.Select(m => $"{m.Stat.ToString().ToUpperInvariant()} {m.Amount:+0;-0;0}"));
        var amount = Mechanic switch { HealMechanic m => m.Amount, ExtraActionsMechanic m => m.Amount, _ => 0 };
        var description = DescriptionOverride ?? MechanicCatalog.AbilityDescriptionTemplate(this);
        return new(entryId ?? Id, Name, Timing switch { AbilityTiming.BonusAction => "Bonus Action", AbilityTiming.FreeAction => "Free Action", _ => Timing.ToString() },
            description.Replace("{amount}", amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("{modifiers}", modifierText), Usage.MaxUses);
    }
}

public sealed record ResolvedAbility(AbilityDefinition Definition, AbilitySourceKind SourceKind, string SourceTypeId,
    string SourceInstanceId, string GrantId, string AccountingId, string EntryId, string CandidatePrefix,
    LegacyAbilityKind Legacy = LegacyAbilityKind.None, string? LegacyKey = null, string? SourceName = null, string? LegacyActionId = null)
{
    public string ActionId => LegacyActionId ?? EntryId;
    public CardEntryDescription CardEntry()
    {
        var entry = Definition.CardEntry(EntryId);
        // Preserve printed native card copy while old Unit fields are adapted.
        // Parameters still come from the typed mechanic, never from separate prose.
        return Legacy switch
        {
            LegacyAbilityKind.HolyWave when Definition.Mechanic is HolyWaveMechanic => entry with { ShortDescription = "Lay down adjacent Upright enemies, then this Unit." },
            LegacyAbilityKind.Fireball when Definition.Mechanic is FireballMechanic => entry with { ShortDescription = "Attack all Units on or adjacent to a chosen Cell within RNG and LOS." },
            LegacyAbilityKind.BonusAction when Definition.Mechanic is SelfModifiersMechanic m => entry with
            {
                Description = string.Join(" & ", m.Modifiers.Select(v => $"{(v.Amount >= 0 ? "+" : "")}{v.Amount} {v.Stat.ToString().ToUpperInvariant()}")) + " this turn"
            },
            _ when SourceKind == AbilitySourceKind.Unit && (Legacy == LegacyAbilityKind.Aura || EntryId == GrantId) && Definition.Mechanic is AdjacentModifiersMechanic =>
                entry with { Description = entry.Description.TrimEnd('.') },
            _ => entry
        };
    }
}

public static class AbilityCatalog
{
    public static IReadOnlyList<AbilityDefinition> All => AbilityRoster.All;
    public static List<AbilityDefinition> Resolve(IEnumerable<AbilityDefinition>? local)
    {
        var definitions = All.Concat(local ?? []).ToList();
        ValidateDefinitions(definitions);
        return definitions;
    }
    public static void ValidateDefinitions(IReadOnlyList<AbilityDefinition> definitions)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var d in definitions)
        {
            if (d is null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || !ids.Add(d.Id) ||
                d.Usage is null || d.Usage.MaxUses is < 1 || !Enum.IsDefined(d.Timing))
                throw new ArgumentException("Abilities require unique IDs, names, supported timing and positive use limits.");
            MechanicCatalog.ThrowIfInvalid(d.Mechanic,
                new(MechanicSource.Unit, MechanicCatalog.AbilityTimingContext(d.Timing), d.Usage.MaxUses));
            MechanicCatalog.ValidateAbilityOverride(d);
        }
    }
    public static void ValidateState(GameState state)
    {
        ValidateDefinitions(state.AbilityDefinitions);
        ItemCatalog.Validate(state.ItemTypes);
        LootCardCatalog.Validate(state.LootCardDefinitions);
        void Declarations(ImmutableArray<LocalAbilityDefinition> locals, ImmutableArray<AbilityGrant> grants,
            AbilitySourceKind source, string ownerPath, IEnumerable<string>? reserved = null, string? legacyItemTypeId = null)
        {
            HashSet<string> seen = new(reserved ?? [], StringComparer.Ordinal);
            if (locals.IsDefault) throw ScenarioValidationContext.At(new ArgumentException("Local Abilities cannot be null."), ownerPath + ".localAbilities");
            var grantField = source == AbilitySourceKind.Unit ? "abilityGrants" : "grants";
            if (grants.IsDefault) throw ScenarioValidationContext.At(new ArgumentException("Ability grants cannot be null."), ownerPath + "." + grantField);
            for (var i = 0; i < locals.Length; i++)
            {
                var local = locals[i];
                var path = $"{ownerPath}.localAbilities[{i}]";
                if (local is null) throw ScenarioValidationContext.At(new ArgumentException("A local Ability declaration is required."), path);
                if (string.IsNullOrWhiteSpace(local.Key) || !seen.Add(local.Key))
                    throw ScenarioValidationContext.At(new ArgumentException("Local Abilities and grants require distinct nonblank keys within their source."), path + ".key");
                if (string.IsNullOrWhiteSpace(local.Name))
                    throw ScenarioValidationContext.At(new ArgumentException("An Ability display name is required."), path + ".name");
                if (!Enum.IsDefined(local.Timing))
                    throw ScenarioValidationContext.At(new ArgumentException("A supported Ability timing is required."), path + ".timing");
                if (local.Usage is null || local.Usage.MaxUses is < 1)
                    throw ScenarioValidationContext.At(new ArgumentException("Ability use limits must be positive."), path + ".usage");
                var diagnostics = MechanicCatalog.Validate(local.Mechanic,
                    new(MechanicCatalog.AbilitySourceContext(source), MechanicCatalog.AbilityTimingContext(local.Timing), local.Usage.MaxUses), path + ".mechanic");
                if (!diagnostics.IsEmpty)
                {
                    var first = diagnostics[0];
                    var field = first.Code == "unsupportedUsage" ? path + ".usage"
                        : first.Code == "unsupportedContext" ? path + ".timing" : first.Path;
                    throw ScenarioValidationContext.At(new ArgumentException(string.Join(" ", diagnostics.Select(d => $"{d.Path}: {d.Message}"))), field);
                }
                var definition = local.ToDefinition();
                try { MechanicCatalog.ValidateAbilityOverride(definition); }
                catch (ArgumentException error) { throw ScenarioValidationContext.At(error, path + ".descriptionOverride"); }
                try { AbilityCompatibility.Validate(new(local.Key, local.Key) { Compatibility = local.Compatibility }, definition, source); }
                catch (ArgumentException error) { throw ScenarioValidationContext.At(error, path + ".compatibility"); }
            }
            for (var i = 0; i < grants.Length; i++)
            {
                var g = grants[i];
                var path = $"{ownerPath}.{grantField}[{i}]";
                if (g is null || string.IsNullOrWhiteSpace(g.Id) || !seen.Add(g.Id))
                    throw ScenarioValidationContext.At(new ArgumentException("Local Abilities and grants require distinct nonblank keys within their source."), path + ".id");
                AbilityDefinition ability;
                try { ability = state.Ability(g.AbilityId); }
                catch (ArgumentException error) { throw ScenarioValidationContext.At(error, path + ".abilityId"); }
                var resolved = AbilityDeclarations.Resolve([], [g], state.AbilityDefinitions, legacyItemTypeId).Single();
                try
                {
                    AbilityCompatibility.Validate(resolved.Grant, ability, source);
                    MechanicCatalog.ThrowIfInvalid(ability.Mechanic, new(MechanicCatalog.AbilitySourceContext(source),
                        MechanicCatalog.AbilityTimingContext(ability.Timing), ability.Usage.MaxUses));
                }
                catch (ArgumentException error) { throw ScenarioValidationContext.At(error, path); }
            }
            if (source == AbilitySourceKind.Item && AbilityDeclarations.Resolve(locals, grants, state.AbilityDefinitions, legacyItemTypeId)
                    .Count(d => d.Compatibility?.Kind == LegacyAbilityKind.SecondWind) > 1)
                throw ScenarioValidationContext.At(new ArgumentException("An Item may preserve the historical Second Wind slot only once."), ownerPath);
        }
        for (var i = 0; i < state.Types.Count; i++)
        {
            var type = state.Types[i];
            Declarations(type.LocalAbilities, type.AbilityGrants, AbilitySourceKind.Unit, $"unitTypes[{i}]", AbilityQueries.LegacyNative(type, "validation").Select(g => g.GrantId));
            AbilityCompatibility.ValidateNativeIdentities(type, state.AbilityDefinitions);
        }
        for (var i = 0; i < state.EquipmentTypes.Count; i++)
        {
            var type = state.EquipmentTypes[i];
            Declarations(type.LocalAbilities, type.Grants, AbilitySourceKind.Equipment, $"equipmentTypes[{i}]", type.Abilities.Select(a => a.Id).Concat(["modifiers", "phase", "open-door"]));
        }
        var historicalSecondWindBound = false;
        for (var i = 0; i < state.ItemTypes.Count; i++)
        {
            var type = state.ItemTypes[i];
            Declarations(type.LocalAbilities, type.Grants, AbilitySourceKind.Item, $"itemTypes[{i}]", legacyItemTypeId: type.Id);
            if (AbilityDeclarations.Resolve(type.LocalAbilities, type.Grants, state.AbilityDefinitions, legacyItemTypeId: type.Id)
                .Any(d => d.Compatibility?.Kind == LegacyAbilityKind.SecondWind))
            {
                // This binding preserves one historical accounting identity. Normal
                // local Item abilities instead retain their Type/key independence.
                if (historicalSecondWindBound)
                    throw ScenarioValidationContext.At(new ArgumentException("Only one Item Type may preserve the historical Second Wind accounting identity."), $"itemTypes[{i}]");
                historicalSecondWindBound = true;
            }
        }
        var prefixes = state.EquipmentTypes.SelectMany(t => t.Abilities.Select(a => a.CandidatePrefix)
            .Concat(t.Grants.Select(g => g.Compatibility?.CandidatePrefix))
            .Concat(t.LocalAbilities.Select(a => a.Compatibility?.CandidatePrefix))).OfType<string>().ToArray();
        if (prefixes.Distinct(StringComparer.Ordinal).Count() != prefixes.Length)
            throw new ArgumentException("Equipment compatibility candidate prefixes must be unique.");
        foreach (var card in state.LootCardDefinitions)
            if (card.ItemTypeId is { } item) _ = state.Item(item);
            else if (card.EquipmentTypeId is { } equipment) _ = state.Equipment(equipment);
        foreach (var unit in state.Units)
        {
            var limited = state.AbilitiesOf(unit.Id).Where(g => g.SourceKind == AbilitySourceKind.Unit && g.Legacy == LegacyAbilityKind.None && g.Definition.Usage.MaxUses is not null)
                .ToDictionary(g => g.AccountingId, g => g.Definition.Usage.MaxUses!.Value);
            if (unit.AbilityUseCounts.Any(pair => !limited.TryGetValue(pair.Key, out var max) || pair.Value < 0 || pair.Value > max))
                throw new ArgumentException("Ability use counters must match their native grants.");
        }
    }
}

public static class AbilityQueries
{
    internal static string Key(string value) => Uri.EscapeDataString(value);
    // Old Bonus Action names were serialized as raw accounting keys. Keep those
    // keys readable by old saves while placing new source identities outside their
    // occupied namespace. Prefixing is deterministic from the Unit's definition;
    // physical Item copies still resolve to the same allowance.
    internal static string SourceAccountingId(UnitType bearer, string key)
    {
        while (bearer.BonusActions.Any(b => b.Name == key) || bearer.AbilityGrants.Any(g =>
                   g.Compatibility is { Kind: LegacyAbilityKind.BonusAction } binding && binding.Key == key) ||
               bearer.LocalAbilities.Any(a => a.Compatibility is { Kind: LegacyAbilityKind.BonusAction } binding && binding.Key == key)) key = "grant:" + key;
        return key;
    }
    public static IEnumerable<ResolvedAbility> NativeAbilities(UnitType type, string unitId,
        IReadOnlyList<AbilityDefinition>? definitions = null)
    {
        foreach (var ability in LegacyNative(type, unitId)) yield return ability;
        foreach (var declaration in AbilityDeclarations.Resolve(type.LocalAbilities, type.AbilityGrants, definitions))
            yield return AbilityCompatibility.ResolveNative(type, unitId, declaration.Grant, declaration.Definition);
    }
    public static AbilityDefinition Ability(this GameState state, string id) => state.AbilityDefinitions.SingleOrDefault(a => a.Id == id)
        ?? throw new ArgumentException($"Unknown Ability ID '{id}'.");
    public static IEnumerable<ResolvedAbility> AbilitiesOf(this GameState state, string unitId)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        foreach (var a in NativeAbilities(type, unit.Id, state.AbilityDefinitions)) yield return a;
        foreach (var obj in unit.Inventory)
        {
            if (state.EquipmentTypeId(obj) is { } equipmentId)
            {
                var equipment = state.Equipment(equipmentId);
                foreach (var old in equipment.Abilities)
                    yield return new(new(old.Id, old.Name, AbilityTiming.Action, new StandAdjacentFriendlyMechanic()) { DescriptionOverride = old.DescriptionOverride },
                        AbilitySourceKind.Equipment, equipment.Id, obj.Id, old.Id, SourceAccountingId(type, $"equipment:{Key(obj.Id)}:{Key(old.Id)}"), equipment.EntryId(old.Id),
                        old.CandidatePrefix ?? $"equipment:{Key(obj.Id)}:{Key(old.Id)}", LegacyAbilityKind.EquipmentAction, SourceName: equipment.Name, LegacyActionId: old.CandidatePrefix);
                foreach (var declaration in AbilityDeclarations.Resolve(equipment.LocalAbilities, equipment.Grants, state.AbilityDefinitions))
                    yield return new(declaration.Definition, AbilitySourceKind.Equipment, equipment.Id, obj.Id, declaration.Key,
                        SourceAccountingId(type, $"equipment:{Key(obj.Id)}:{Key(declaration.Key)}"), equipment.EntryId(declaration.Key),
                        declaration.Compatibility?.CandidatePrefix ?? $"equipment:{Key(obj.Id)}:{Key(declaration.Key)}",
                        declaration.Compatibility?.Kind ?? LegacyAbilityKind.None, SourceName: equipment.Name,
                        LegacyActionId: declaration.Compatibility?.CandidatePrefix);
            }
            else if (state.ItemTypeId(obj) is { } itemId)
            {
                var item = state.Item(itemId);
                foreach (var declaration in AbilityDeclarations.Resolve(item.LocalAbilities, item.Grants, state.AbilityDefinitions, legacyItemTypeId: item.Id))
                {
                    var legacy = declaration.Compatibility?.Kind == LegacyAbilityKind.SecondWind;
                    yield return new(declaration.Definition, AbilitySourceKind.Item, item.Id, obj.Id, declaration.Key,
                        SourceAccountingId(type, legacy ? "Second Wind" : $"item:{Key(item.Id)}:{Key(declaration.Key)}"), legacy ? "object:SecondWind" : $"item:{Key(item.Id)}:{Key(declaration.Key)}",
                        legacy ? $"use-object:{obj.Id}" : $"item:{Key(obj.Id)}:{Key(declaration.Key)}", legacy ? LegacyAbilityKind.SecondWind : LegacyAbilityKind.None, SourceName: item.Name);
                }
            }
        }
    }
    internal static IEnumerable<ResolvedAbility> LegacyNative(UnitType type, string unitId)
    {
        ResolvedAbility Native(string id, string name, AbilityTiming timing, AbilityMechanic mechanic, int? uses, LegacyAbilityKind legacy, string? key = null) =>
            new(new(id, name, timing, mechanic) { Usage = new(uses) }, AbilitySourceKind.Unit, type.Id, unitId, id,
                key ?? $"unit:{Key(id)}", timing == AbilityTiming.BonusAction ? $"bonus:{key}" : id,
                timing == AbilityTiming.BonusAction ? $"bonus-action:{key}" : id, legacy, key, type.DisplayName ?? type.Id);
        if (type.Actions.HasFlag(UnitAction.Heal) && type.Heal is { } heal) yield return Native("heal", type.AbilityNames.Heal ?? "Heal", AbilityTiming.Action, new HealMechanic(heal.Amount), heal.MaxUses, LegacyAbilityKind.Heal);
        if (type.Actions.HasFlag(UnitAction.HolyWave) && type.HolyWave is { } wave) yield return Native("holy-wave", type.AbilityNames.HolyWave ?? "Holy Wave", AbilityTiming.Action, new HolyWaveMechanic(), wave.MaxUses, LegacyAbilityKind.HolyWave);
        if (type.Actions.HasFlag(UnitAction.Fireball) && type.Fireball is { } fireball) yield return Native("fireball", type.AbilityNames.Fireball ?? "Fireball", AbilityTiming.Action, new FireballMechanic(), fireball.MaxUses, LegacyAbilityKind.Fireball);
        foreach (var bonus in type.BonusActions.Where(IsSimpleBonus))
            yield return Native($"bonus:{bonus.Name}", bonus.DisplayName ?? bonus.Name, AbilityTiming.BonusAction, new SelfModifiersMechanic(bonus.Modifiers), bonus.MaxUses, LegacyAbilityKind.BonusAction, bonus.Name);
        if (type.AdjacentFriendlyUnitsDefenceBonus is { } aura)
            yield return Native($"passive:{aura.Name}", type.AbilityNames.Aura ?? aura.Name, AbilityTiming.Passive, AuraMechanic(aura), null, LegacyAbilityKind.Aura);
    }
    private static AdjacentModifiersMechanic AuraMechanic(AdjacentFriendlyUnitsDefenceBonus aura) => new([new(Stat.Def, aura.Amount)]);
    public static bool IsSimpleBonus(BonusActionAbility b) => b.Boost is null && b.PlaceBananaPeel is null && b.ReshapeRoom is null && b.Swap is null && b.Displace is null;
    public static AbilityUses? AbilityUsesOf(this GameState state, string unitId, ResolvedAbility grant)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        var existing = grant.Legacy switch
        {
            LegacyAbilityKind.Heal => unit.HealUses, LegacyAbilityKind.HolyWave => unit.HolyWaveUses,
            LegacyAbilityKind.Fireball => unit.FireballUses, LegacyAbilityKind.BonusAction => unit.BonusActionUses.GetValueOrDefault(grant.LegacyKey!), _ => null
        };
        return existing ?? (grant.Definition.Usage.MaxUses is { } max ? new(max, max - unit.AbilityUseCounts.GetValueOrDefault(grant.AccountingId)) : null);
    }
    public static bool CanUseAbility(this GameState state, string unitId, ResolvedAbility grant) => state.IsUpright(unitId) &&
        state.Units.Single(u => u.Id == unitId).CurrentHp > 0 && state.AbilityUsesOf(unitId, grant) is not { RemainingUses: <= 0 } &&
        (grant.Definition.Timing != AbilityTiming.BonusAction || state.BonusActionUsesThisActivation(grant.AccountingId) < state.EffectiveBonusActionUsesPerActivation);
    public static int AbilityModifier(this GameState state, string unitId, Stat stat)
    {
        // Stat queries run for every displayed Unit and hypothetical move. Resolve
        // only passive payloads here, without constructing active choices or looking
        // up each source Unit again through the full grant/accounting adapter.
        bool Relevant(AbilityTiming timing, AbilityMechanic mechanic) => timing == AbilityTiming.Passive && (mechanic switch
        {
            SelfModifiersMechanic m => m.Modifiers.Any(v => v.Stat == stat),
            AdjacentModifiersMechanic m => m.Modifiers.Any(v => v.Stat == stat),
            _ => false
        });
        bool HasPassive(ImmutableArray<LocalAbilityDefinition> locals, ImmutableArray<AbilityGrant> grants) =>
            locals.Any(a => Relevant(a.Timing, a.Mechanic)) ||
            grants.Any(g => state.Ability(g.AbilityId) is { } a && Relevant(a.Timing, a.Mechanic));
        var declaredPassives = state.Types.Any(t => HasPassive(t.LocalAbilities, t.AbilityGrants)) ||
            state.EquipmentTypes.Any(t => HasPassive(t.LocalAbilities, t.Grants));
        if (!declaredPassives && (stat != Stat.Def || !state.Types.Any(t => t.AdjacentFriendlyUnitsDefenceBonus is not null))) return 0;
        var target = state.Units.Single(u => u.Id == unitId);
        if (target.CurrentHp <= 0 || !state.Physical.Figures.Any(f => f.Id == unitId)) return 0;
        IEnumerable<AbilityMechanic> Passives(Unit source)
        {
            var type = state.Types.Single(t => t.Id == source.TypeId);
            if (stat == Stat.Def && type.AdjacentFriendlyUnitsDefenceBonus is { } aura) yield return AuraMechanic(aura);
            if (!declaredPassives) yield break;
            foreach (var a in type.LocalAbilities)
                if (a.Timing == AbilityTiming.Passive) yield return a.Mechanic;
            foreach (var g in type.AbilityGrants)
                if (state.Ability(g.AbilityId) is { Timing: AbilityTiming.Passive } a) yield return a.Mechanic;
            foreach (var obj in source.Inventory)
                if (state.EquipmentTypeId(obj) is { } id)
                {
                    foreach (var a in state.Equipment(id).LocalAbilities)
                        if (a.Timing == AbilityTiming.Passive) yield return a.Mechanic;
                    foreach (var g in state.Equipment(id).Grants)
                        if (state.Ability(g.AbilityId) is { Timing: AbilityTiming.Passive } a) yield return a.Mechanic;
                }
        }
        var total = 0;
        foreach (var source in state.Units.Where(u => u.CurrentHp > 0))
        foreach (var mechanic in Passives(source))
        {
            if (!state.IsUpright(source.Id)) continue;
            var modifiers = mechanic switch
            {
                SelfModifiersMechanic m when source.Id == unitId => m.Modifiers,
                AdjacentModifiersMechanic m when source.SideId == target.SideId && SpatialRules.AreAdjacent(state, source.Id, unitId) => m.Modifiers,
                _ => []
            };
            total += modifiers.Where(m => m.Stat == stat).Sum(m => m.Amount);
        }
        return total;
    }
}
