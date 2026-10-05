using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Authoring-only entries translate into the existing concrete runtime model.
// They never become part of GameState or execute game rules.
public static class UnitAuthoring
{
    public sealed record BaseStats(int Mov, int Rng, int Atk, int Def, int Hp);
    public sealed record UseLimit
    {
        public int Count { get; }
        internal UseLimit(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
        }
    }
    public sealed class UnlimitedUsage
    {
        internal UnlimitedUsage() { }
    }
    public sealed record SelfModifiers(ImmutableArray<ModifierThisTurn> Modifiers);

    public abstract class Entry
    {
        private protected Entry() { }
        internal virtual Type Slot => GetType();
        internal abstract UnitType Apply(UnitType type);
    }

    public static BaseStats Stats(int mov, int rng, int atk, int def, int hp) => new(mov, rng, atk, def, hp);
    public static UseLimit Uses(int count) => new(count);
    public static UnlimitedUsage Unlimited() => new();
    public static ModifierThisTurn Modifier(Stat stat, int amount) => new(stat, amount);
    public static SelfModifiers BonusActionSelfModifier(params ModifierThisTurn[] modifiers) => new([.. modifiers]);

    public static Entry CantAttack() => new CantAttackEntry();
    public static Entry Unique() => new UniqueEntry();
    public static Entry Footprint2x2() => new FootprintEntry();
    public static Entry OpenDoor() => new OpenDoorEntry();
    public static Entry Phase() => new PhaseEntry();
    public static TryOpenDoor TryOpenDoor(int successes) => new(successes);
    public static MoveAfterAttack MoveAfterAttack(int maxMove) => new(maxMove);
    public static Undying Undying() => new();
    public static SummonAdjacent SummonAdjacent(string unitTypeId, Posture initialPosture) => new(unitTypeId, initialPosture);
    public static Telekinesis Telekinesis() => new();
    public static Fury Fury(int atkBonus, int adjacentEnemies) =>
        new() { AtkBonus = atkBonus, AdjacentEnemyThreshold = adjacentEnemies };
    public static Backstab Backstab(int atkBonus) => new() { AtkBonus = atkBonus };
    public static Heal Heal(int amount) => new() { Amount = amount };
    public static Cleave Cleave(int triggerDamage, int damage) => new() { TriggerDamage = triggerDamage, Damage = damage };
    public static HolyWave HolyWave() => new();
    public static Fireball Fireball() => new();
    public static FireBreath FireBreath() => new();
    public static ClawAttack ClawAttack(int atkBonus = 1) => new(atkBonus);
    public static AdjacentFriendlyUnitsDefenceBonus AdjacentFriendliesDefenceBonus(int amount) => new(amount);

    // Usage overloads allow only the timing/use semantics already supported by each mechanic.
    public static Entry Ability(string name, UseLimit usage, Heal mechanic) =>
        new NamedEntry(new HealEntry(mechanic with { MaxUses = usage.Count }), name, NamedMechanic.Heal);
    public static Entry Ability(string name, UseLimit usage, Cleave mechanic) =>
        new NamedEntry(new CleaveEntry(mechanic with { MaxUses = usage.Count }), name, NamedMechanic.Cleave);
    public static Entry Ability(string name, UseLimit usage, HolyWave mechanic) =>
        new NamedEntry(new WaveEntry(mechanic with { MaxUses = usage.Count }), name, NamedMechanic.HolyWave);
    public static Entry Ability(string name, UseLimit usage, Fireball mechanic) =>
        new NamedEntry(new FireballEntry(mechanic with { MaxUses = usage.Count }), name, NamedMechanic.Fireball);
    public static Entry Ability(string name, UnlimitedUsage usage, FireBreath mechanic) =>
        new NamedEntry(new BreathEntry(), name, NamedMechanic.FireBreath);
    public static Entry Ability(string name, UnlimitedUsage usage, ClawAttack mechanic) =>
        new NamedEntry(new ClawEntry(mechanic), name, NamedMechanic.ClawAttack);
    public static Entry Ability(string name, UnlimitedUsage usage, AdjacentFriendlyUnitsDefenceBonus mechanic) =>
        new NamedEntry(new AuraEntry(mechanic), name, NamedMechanic.Aura);
    public static Entry Ability(string name, UnlimitedUsage usage, Fury mechanic) =>
        new NamedEntry(new FuryEntry(mechanic), name, NamedMechanic.Fury);
    public static Entry Ability(string name, UnlimitedUsage usage, Backstab mechanic) =>
        new NamedEntry(new BackstabEntry(mechanic), name, NamedMechanic.Backstab);
    public static Entry Ability(string name, UnlimitedUsage usage, Undying mechanic) =>
        new NamedEntry(new UndyingEntry(), name, NamedMechanic.Undying);
    public static Entry Ability(string name, UnlimitedUsage usage, Telekinesis mechanic) =>
        new NamedEntry(new TelekinesisEntry(), name, NamedMechanic.Telekinesis);
    public static Entry Ability(string name, UnlimitedUsage usage, TryOpenDoor mechanic) =>
        new NamedEntry(new DoorEntry(mechanic), name, NamedMechanic.TryOpenDoor);
    public static Entry Ability(string name, UnlimitedUsage usage, SummonAdjacent mechanic) =>
        new NamedEntry(new SummonEntry(mechanic), name, NamedMechanic.Summon);
    public static Entry Ability(string name, UnlimitedUsage usage, MoveAfterAttack mechanic) =>
        new NamedEntry(new MoveEntry(mechanic), name, NamedMechanic.MoveAfterAttack);

    // Bonus ids remain explicit persisted keys, independent of the printed name.
    public static Entry Ability(string name, UseLimit usage, SelfModifiers mechanic, string id) =>
        new BonusEntry(new(id, usage.Count, mechanic.Modifiers) { DisplayName = name == id ? null : name });

    internal static UnitType Define(string id, string name, BaseStats stats, Entry[] entries)
    {
        var type = new UnitType(id, stats.Mov, stats.Rng, stats.Atk, stats.Def, stats.Hp) { DisplayName = name };
        HashSet<Type> slots = [];
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.Slot != typeof(BonusEntry) && !slots.Add(entry.Slot))
                throw new ArgumentException("A concrete mechanic may only be attached once.", nameof(entries));
            type = entry.Apply(type);
        }
        if (type.BonusActions.Select(a => a.Name).Distinct().Count() != type.BonusActions.Length)
            throw new ArgumentException("Bonus ability ids must be unique.", nameof(entries));
        if (type.Hp > 1 && !type.Unique)
            throw new ArgumentException("Maximum HP greater than 1 requires a Unique Unit Type.", nameof(entries));
        return type;
    }

    public static UnitType WithBehaviors(this UnitType type, params UnitBehavior[] behaviors) =>
        type with { Behaviors = behaviors.Aggregate(type.Behaviors, (combined, behavior) => combined | behavior) };

    private enum NamedMechanic { Heal, Cleave, HolyWave, Fireball, FireBreath, ClawAttack, Aura, Fury, Backstab, Undying, Telekinesis, TryOpenDoor, Summon, MoveAfterAttack }
    private sealed class NamedEntry(Entry mechanic, string name, NamedMechanic kind) : Entry
    {
        internal override Type Slot => mechanic.Slot;
        internal override UnitType Apply(UnitType type)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ability printed name is required.", nameof(name));
            var names = type.AbilityNames;
            names = kind switch
            {
                NamedMechanic.Heal => names with { Heal = name == "Heal" ? null : name },
                NamedMechanic.Cleave => names with { Cleave = name == "Cleave" ? null : name },
                NamedMechanic.HolyWave => names with { HolyWave = name == "Holy Wave" ? null : name },
                NamedMechanic.Fireball => names with { Fireball = name == "Fireball" ? null : name },
                NamedMechanic.FireBreath => names with { FireBreath = name == "Fire Breath" ? null : name },
                NamedMechanic.ClawAttack => names with { ClawAttack = name == "Claw Attack" ? null : name },
                NamedMechanic.Aura => names with { Aura = name == "Aura" ? null : name },
                NamedMechanic.Fury => names with { Fury = name == "Fury" ? null : name },
                NamedMechanic.Backstab => names with { Backstab = name == "Backstab" ? null : name },
                NamedMechanic.Undying => names with { Undying = name == "Undying" ? null : name },
                NamedMechanic.Telekinesis => names with { Telekinesis = name == "Telekinesis" ? null : name },
                NamedMechanic.TryOpenDoor => names with { TryOpenDoor = name == "Try Open Door" ? null : name },
                NamedMechanic.Summon => names with { Summon = name },
                NamedMechanic.MoveAfterAttack => names with { MoveAfterAttack = name == "Move After Attack" ? null : name },
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return mechanic.Apply(type) with { AbilityNames = names };
        }
    }

    private sealed class UniqueEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Unique = true };
    }
    private sealed class FootprintEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Footprint = Footprint.TwoByTwo };
    }
    private sealed class CantAttackEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions & ~UnitAction.NormalAttack };
    }
    private sealed class OpenDoorEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { FreeActions = type.FreeActions | UnitFreeAction.OpenDoor };
    }
    private sealed class PhaseEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Phase = new() };
    }
    private sealed class DoorEntry(TryOpenDoor mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { TryOpenDoor = mechanic };
    }
    private sealed class MoveEntry(MoveAfterAttack mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { MoveAfterAttack = mechanic };
    }
    private sealed class UndyingEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Undying = new() };
    }
    private sealed class SummonEntry(SummonAdjacent mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions | UnitAction.SummonAdjacent, SummonAdjacent = mechanic };
    }
    private sealed class TelekinesisEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions | UnitAction.Telekinesis };
    }
    private sealed class FuryEntry(Fury mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Fury = mechanic };
    }
    private sealed class BackstabEntry(Backstab mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Backstab = mechanic };
    }
    private sealed class HealEntry(Heal mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions | UnitAction.Heal, Heal = mechanic };
    }
    private sealed class CleaveEntry(Cleave mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Cleave = mechanic };
    }
    private sealed class WaveEntry(HolyWave mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions | UnitAction.HolyWave, HolyWave = mechanic };
    }
    private sealed class FireballEntry(Fireball mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions | UnitAction.Fireball, Fireball = mechanic };
    }
    private sealed class BreathEntry : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions | UnitAction.FireBreath };
    }
    private sealed class ClawEntry(ClawAttack mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { Actions = type.Actions | UnitAction.ClawAttack, ClawAttack = mechanic };
    }
    private sealed class AuraEntry(AdjacentFriendlyUnitsDefenceBonus mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { AdjacentFriendlyUnitsDefenceBonus = mechanic };
    }
    private sealed class BonusEntry(BonusActionAbility mechanic) : Entry
    {
        internal override UnitType Apply(UnitType type) => type with { BonusActions = type.BonusActions.Add(mechanic) };
    }
}
