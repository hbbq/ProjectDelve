using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Compatibility binds a declared grant to existing serialized identities and storage.
// It never supplies a mechanic, timing, use limit or parameter.
internal static class AbilityCompatibility
{
    internal static void Validate(AbilityGrant grant, AbilityDefinition definition, AbilitySourceKind source)
    {
        if (grant.Compatibility is not { } binding) return;
        var supported = binding.Kind switch
        {
            LegacyAbilityKind.Heal or LegacyAbilityKind.HolyWave or LegacyAbilityKind.Fireball =>
                source == AbilitySourceKind.Unit && definition.Timing == AbilityTiming.Action && definition.Usage.MaxUses is not null,
            LegacyAbilityKind.BonusAction => source == AbilitySourceKind.Unit && definition.Timing == AbilityTiming.BonusAction,
            LegacyAbilityKind.Aura => source == AbilitySourceKind.Unit && definition.Timing == AbilityTiming.Passive,
            LegacyAbilityKind.EquipmentAction => source == AbilitySourceKind.Equipment && definition.Timing == AbilityTiming.Action,
            _ => false
        };
        var keyed = binding.Kind is LegacyAbilityKind.BonusAction or LegacyAbilityKind.Aura;
        if (!supported || (keyed ? string.IsNullOrWhiteSpace(binding.Key) : binding.Key is not null) ||
            // This preserves one historical alias; it is not an arbitrary candidate namespace.
            binding.CandidatePrefix is not null && (binding.Kind != LegacyAbilityKind.EquipmentAction || binding.CandidatePrefix != "goat-bell"))
            throw new ArgumentException("Unsupported Ability grant compatibility binding.");
    }

    internal static ResolvedAbility ResolveNative(UnitType type, string unitId, AbilityGrant grant, AbilityDefinition definition)
    {
        var binding = grant.Compatibility;
        var kind = binding?.Kind ?? LegacyAbilityKind.None;
        var identity = kind switch
        {
            LegacyAbilityKind.Heal => "heal",
            LegacyAbilityKind.HolyWave => "holy-wave",
            LegacyAbilityKind.Fireball => "fireball",
            LegacyAbilityKind.BonusAction => $"bonus:{binding!.Key}",
            LegacyAbilityKind.Aura => $"passive:{binding!.Key}",
            _ => grant.Id
        };
        var accounting = kind == LegacyAbilityKind.BonusAction ? binding!.Key!
            : kind == LegacyAbilityKind.None ? AbilityQueries.SourceAccountingId(type, $"unit:{AbilityQueries.Key(grant.Id)}")
            : $"unit:{AbilityQueries.Key(identity)}";
        var entry = kind == LegacyAbilityKind.None ? $"ability:{AbilityQueries.Key(grant.Id)}" : identity;
        var candidate = kind == LegacyAbilityKind.BonusAction ? $"bonus-action:{binding!.Key}" : entry;
        return new(definition, AbilitySourceKind.Unit, type.Id, unitId, grant.Id, accounting, entry, candidate,
            kind, binding?.Key, type.DisplayName ?? type.Id);
    }

    internal static void ValidateNativeIdentities(UnitType type, IReadOnlyList<AbilityDefinition> definitions)
    {
        var grants = AbilityQueries.NativeAbilities(type, "validation", definitions).ToArray();
        var entries = grants.Select(g => g.EntryId).Concat(type.BonusActions.Where(b => !AbilityQueries.IsSimpleBonus(b))
            .Select(b => ContentDescriptions.BonusEntryId(b.Name))).ToArray();
        if (entries.Distinct(StringComparer.Ordinal).Count() != entries.Length ||
            grants.Select(g => g.AccountingId).Distinct(StringComparer.Ordinal).Count() != grants.Length)
            throw new ArgumentException("Native Ability compatibility identities and counters must be distinct.");
    }

    internal static Unit InitializeUses(UnitType type, Unit unit, IReadOnlyList<AbilityDefinition> definitions)
    {
        unit = unit with
        {
            CleaveUses = unit.CleaveUses ?? (type.Cleave is { } cleave ? new(cleave.MaxUses, cleave.MaxUses) : null),
            HealUses = unit.HealUses ?? (type.Heal is { } heal ? new(heal.MaxUses, heal.MaxUses) : null),
            HolyWaveUses = unit.HolyWaveUses ?? (type.HolyWave is { } wave ? new(wave.MaxUses, wave.MaxUses) : null),
            FireballUses = unit.FireballUses ?? (type.Fireball is { } fireball ? new(fireball.MaxUses, fireball.MaxUses) : null),
            BonusActionUses = type.BonusActions.Where(b => b.MaxUses.HasValue)
                .Aggregate(unit.BonusActionUses, (uses, bonus) => uses.ContainsKey(bonus.Name) ? uses
                    : uses.Add(bonus.Name, new(bonus.MaxUses!.Value, bonus.MaxUses.Value)))
        };
        // Ordinary grants keep their existing lazy generic accounting. Only historical
        // storage bindings need dedicated counters when constructing a detached Unit.
        foreach (var grant in type.AbilityGrants.Where(g => g.Compatibility?.Kind is
                     LegacyAbilityKind.Heal or LegacyAbilityKind.HolyWave or LegacyAbilityKind.Fireball or LegacyAbilityKind.BonusAction))
        {
            var definition = definitions.SingleOrDefault(d => d.Id == grant.AbilityId)
                ?? throw new ArgumentException($"Unknown Ability ID '{grant.AbilityId}'.");
            if (definition.Usage.MaxUses is not { } max) continue;
            var uses = new AbilityUses(max, max);
            unit = grant.Compatibility!.Kind switch
            {
                LegacyAbilityKind.Heal => unit with { HealUses = unit.HealUses ?? uses },
                LegacyAbilityKind.HolyWave => unit with { HolyWaveUses = unit.HolyWaveUses ?? uses },
                LegacyAbilityKind.Fireball => unit with { FireballUses = unit.FireballUses ?? uses },
                LegacyAbilityKind.BonusAction when !unit.BonusActionUses.ContainsKey(grant.Compatibility.Key!) =>
                    unit with { BonusActionUses = unit.BonusActionUses.Add(grant.Compatibility.Key!, uses) },
                _ => unit
            };
        }
        return unit;
    }

    internal static void ValidateUses(GameState state)
    {
        foreach (var type in state.Types)
        {
            var grants = AbilityQueries.NativeAbilities(type, "validation", state.AbilityDefinitions).ToArray();
            int? Limit(LegacyAbilityKind kind) => grants.SingleOrDefault(g => g.Legacy == kind)?.Definition.Usage.MaxUses ?? kind switch
            {
                // Legacy fields could retain counters while their Action flag was disabled.
                LegacyAbilityKind.Heal => type.Heal?.MaxUses,
                LegacyAbilityKind.HolyWave => type.HolyWave?.MaxUses,
                LegacyAbilityKind.Fireball => type.Fireball?.MaxUses,
                _ => null
            };
            var bonuses = type.BonusActions.Where(b => !AbilityQueries.IsSimpleBonus(b)).Select(b => (b.Name, b.MaxUses))
                .Concat(grants.Where(g => g.Legacy == LegacyAbilityKind.BonusAction).Select(g => (Name: g.LegacyKey!, MaxUses: g.Definition.Usage.MaxUses)))
                .ToDictionary(b => b.Name, b => b.MaxUses, StringComparer.Ordinal);
            foreach (var unit in state.Units.Where(u => u.TypeId == type.Id))
            {
                if (unit.HealUses is { } heal && heal.MaxUses != Limit(LegacyAbilityKind.Heal) ||
                    unit.HolyWaveUses is { } wave && wave.MaxUses != Limit(LegacyAbilityKind.HolyWave) ||
                    unit.FireballUses is { } fireball && fireball.MaxUses != Limit(LegacyAbilityKind.Fireball) ||
                    unit.BonusActionUses.Any(p => !bonuses.TryGetValue(p.Key, out var max) || max != p.Value.MaxUses))
                    throw new ArgumentException("Ability uses must match their declared native grant or legacy Unit Type content.");
            }
        }
    }
}
