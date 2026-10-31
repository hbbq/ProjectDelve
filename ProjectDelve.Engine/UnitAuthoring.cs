using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Authoring helpers produce the same owner-local records that JSON supplies.
// They do not register content or retain a second mechanical representation.
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
    public sealed class UnlimitedUsage { internal UnlimitedUsage() { } }
    public abstract class Entry
    {
        private protected Entry() { }
        internal abstract UnitType Apply(UnitType type);
    }
    private sealed class UpdateEntry(Func<UnitType, UnitType> update) : Entry
    {
        internal override UnitType Apply(UnitType type) => update(type);
    }

    public static BaseStats Stats(int mov, int rng, int atk, int def, int hp) => new(mov, rng, atk, def, hp);
    public static UseLimit Uses(int count) => new(count);
    public static UnlimitedUsage Unlimited() => new();
    public static ModifierThisTurn Modifier(Stat stat, int amount) => new(stat, amount);
    public static SelfModifiersMechanic BonusActionSelfModifier(params ModifierThisTurn[] modifiers) => new([.. modifiers]);
    public static Entry LocalAbility(LocalAbilityDefinition ability) => new UpdateEntry(type =>
    {
        ArgumentNullException.ThrowIfNull(ability);
        return type with { LocalAbilities = type.LocalAbilities.Add(ability) };
    });
    public static Entry Ability(string name, UseLimit usage, AbilityMechanic mechanic, string id) =>
        LocalAbility(new(id, name, TimingFor(mechanic), mechanic) { Usage = new(usage.Count) });
    public static Entry Ability(string name, UnlimitedUsage usage, AbilityMechanic mechanic, string id) =>
        LocalAbility(new(id, name, TimingFor(mechanic), mechanic) { Usage = new(null) });

    // Each helper denotes one already established concrete timing. For a shared
    // mechanic with several supported timings, author a LocalAbility directly.
    private static AbilityTiming TimingFor(AbilityMechanic mechanic) => mechanic switch
    {
        ProjectDelve.Engine.Fury or ProjectDelve.Engine.Backstab or ProjectDelve.Engine.Phase or ProjectDelve.Engine.TransportResource or AdjacentModifiersMechanic => AbilityTiming.Passive,
        ProjectDelve.Engine.Cleave or ProjectDelve.Engine.MoveAfterAttack or ProjectDelve.Engine.AdvanceOnKill => AbilityTiming.AfterAttack,
        ProjectDelve.Engine.Explosion => AbilityTiming.WhenDefeated,
        ProjectDelve.Engine.Undying => AbilityTiming.InsteadOfDefeat,
        ProjectDelve.Engine.RunRails => AbilityTiming.InsteadOfActivation,
        ProjectDelve.Engine.WaypointSlotMachine => AbilityTiming.EndActivation,
        ProjectDelve.Engine.ReshapeRoom or ProjectDelve.Engine.Swap or ProjectDelve.Engine.Displace or ProjectDelve.Engine.PlaceBananaPeel or ProjectDelve.Engine.Boost or SelfModifiersMechanic => AbilityTiming.BonusAction,
        ProjectDelve.Engine.OpenDoor => AbilityTiming.FreeAction,
        _ => AbilityTiming.Action
    };

    public static Entry CantAttack() => new UpdateEntry(t => t with { Actions = UnitAction.None });
    public static Entry Unique() => new UpdateEntry(t => t with { Unique = true });
    public static Entry Footprint2x2() => new UpdateEntry(t => t with { Footprint = Footprint.TwoByTwo });
    public static Entry OpenDoor() => Ability("Open Door", Unlimited(), new OpenDoor(), "open-door");
    public static Entry Phase() => Ability("Phase", Unlimited(), new Phase(), "phase");
    public static Entry TransportResource() => Ability("Transport Resource", Unlimited(), new TransportResource(), "transport-resource");
    public static Entry PickupResource() => Ability("Pickup Resource", Unlimited(), new PickupResource(), "pickup-resource");
    public static Entry Operate() => Ability("Operate", Unlimited(), new Operate(), "operate");
    public static Entry GiveResource() => Ability("Give Resource", Unlimited(), new GiveResource(), "give-resource");
    public static Entry SummonWorker(int sideLimit) => Ability("Summon Worker", Unlimited(), new SummonWorker(sideLimit), "summon-worker");
    public static Entry SummonTroll(int diamondCost) => Ability("Summon Troll", Unlimited(), new SummonTroll(diamondCost), "summon-troll");
    public static Entry PlaceBananaPeel(int bananaCost) => Ability("Place Banana Peel", Unlimited(), new PlaceBananaPeel(bananaCost), "place-banana-peel");
    public static Entry Boost(int mushroomCost) => Ability("Boost", Unlimited(), new Boost(mushroomCost), "boost");
    public static Entry ShootShell(int shellCost, int rng, int atk) => Ability("Shoot Shell", Unlimited(), new ShootShell(shellCost, rng, atk), "shoot-shell");
    public static Entry WaypointSlotMachine() => Ability("Waypoint Slot Machine", Unlimited(), new WaypointSlotMachine(RacingSlotTable.Table), "waypoint-slot-machine");
    public static Entry RunRails(int steps) => Ability($"Run Rails ({steps})", Unlimited(), new RunRails(steps), "run-rails");
    public static Entry AdvanceOnKill() => Ability("Advance On Kill", Unlimited(), new AdvanceOnKill(), "advance-on-kill");
    public static Entry Loot(int dropSuccesses, bool items = false, bool equipment = false) => new UpdateEntry(t =>
        dropSuccesses is >= 0 and <= 6
            ? t with { LootDropSuccesses = dropSuccesses, CanHaveItems = items, CanHaveEquipment = equipment }
            : throw new ArgumentOutOfRangeException(nameof(dropSuccesses)));
    public static Entry ResourceStorage(ResourceType type, int capacity) => ResourceStorage(new ResourceStorageCapacity(type, capacity));
    public static Entry ResourceStorage(params ResourceStorageCapacity[] capacities) => new UpdateEntry(t => t with { ResourceStorage = new([.. capacities]) });
    public static Entry TerrainRestrictedMovement(TerrainKind terrain) => new UpdateEntry(t => t with { TerrainRestrictedMovement = new(terrain) });
    public static Entry TerrainRestriction(params string[] categories) => new UpdateEntry(t => t with { TerrainRestriction = new([.. categories]) });
    public static Entry StraightOrthogonal() => Pattern(MovementPattern.StraightOrthogonal);
    public static Entry DiagonalCorridor() => Pattern(MovementPattern.DiagonalCorridor);
    public static Entry StraightOrthogonalOrDiagonalCorridor() => Pattern(MovementPattern.StraightOrthogonalOrDiagonalCorridor);
    public static Entry Adjacent() => Pattern(MovementPattern.Adjacent);
    public static Entry LShape() => Pattern(MovementPattern.LShape);
    private static Entry Pattern(MovementPattern pattern) => new UpdateEntry(t => t with { MovementPattern = pattern });

    public static TryOpenDoor TryOpenDoor(int successes) => new(successes);
    public static MoveAfterAttack MoveAfterAttack(int maxMove) => new(maxMove);
    public static Undying Undying() => new();
    public static Explosion Explosion(int damage) => new() { Damage = damage };
    public static SummonAdjacent SummonAdjacent(string unitTypeId, Posture initialPosture) => new(unitTypeId, initialPosture);
    public static Telekinesis Telekinesis() => new();
    public static ReshapeRoom ReshapeRoom() => new();
    public static Swap Swap() => new();
    public static Displace Displace(int maxMove) => new(maxMove);
    public static Fury Fury(int atkBonus, int adjacentEnemies) => new() { AtkBonus = atkBonus, AdjacentEnemyThreshold = adjacentEnemies };
    public static Backstab Backstab(int atkBonus) => new() { AtkBonus = atkBonus };
    public static HealMechanic Heal(int amount) => new(amount);
    public static Cleave Cleave(int triggerDamage, int damage) => new() { TriggerDamage = triggerDamage, Damage = damage };
    public static HolyWaveMechanic HolyWave() => new();
    public static FireballMechanic Fireball() => new();
    public static FireBreath FireBreath() => new();
    public static ClawAttack ClawAttack(int atkBonus = 1) => new(atkBonus);
    public static AdjacentModifiersMechanic AdjacentFriendliesDefenceBonus(int amount) => new([new(Stat.Def, amount)]);

    internal static UnitType Define(string id, string name, BaseStats stats, Entry[] entries)
    {
        var type = new UnitType(id, stats.Mov, stats.Rng, stats.Atk, stats.Def, stats.Hp) { DisplayName = name };
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            type = entry.Apply(type);
        }
        AbilityContent.Validate(type.LocalAbilities, AbilitySourceKind.Unit);
        if (type.Hp > 1 && !type.Unique)
            throw new ArgumentException("Maximum HP greater than 1 requires a Unique Unit Type.", nameof(entries));
        var attacks = (type.HasAction(UnitAction.NormalAttack) ? new[] { UnitAction.NormalAttack } : [])
            .Concat(type.LocalAbilities.Select(a => a.Mechanic switch
            {
                FireballMechanic => UnitAction.Fireball, ProjectDelve.Engine.FireBreath => UnitAction.FireBreath,
                ProjectDelve.Engine.ClawAttack => UnitAction.ClawAttack, ProjectDelve.Engine.ShootShell => UnitAction.ShootShell,
                _ => UnitAction.None
            }).Where(a => a != UnitAction.None)).ToImmutableArray();
        return type with { AttackDefinitionOrder = attacks.Length > 1 ? attacks : [] };
    }
    public static UnitType WithBehaviors(this UnitType type, params UnitBehavior[] behaviors) =>
        type with { Behaviors = behaviors.Aggregate(type.Behaviors, (combined, behavior) => combined | behavior) };
}
