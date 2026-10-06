using static ProjectDelve.Engine.UnitAuthoring;
using static ProjectDelve.Engine.Stat;

namespace ProjectDelve.Engine;

// Canonical Unit content. Stats are MOV / RNG / ATK / DEF / HP.
// Normal Attack is baseline; Behaviors are automated policy, not card mechanics.

// Keep the formatting in this file according to this example:
// public static UnitType RedDragon(string id = UnitTypeIds.RedDragon) => UnitType.Define(id,
//     "Red Dragon",
//     Stats(2, 4, 4, 4, 8),
//     Unique(),
//     Footprint2x2(),
//     Ability("Fire Breath", Unlimited(), FireBreath()),
//     Ability("Claw Attack", Unlimited(), ClawAttack())
// ).WithBehaviors(UnitBehavior.PreferFireBreathThenClaw);

public static class UnitRoster
{
    public static UnitType RedDragon(string id = UnitTypeIds.RedDragon) => UnitType.Define(id, 
        "Red Dragon", 
        Stats(2, 4, 4, 4, 8), 
        Unique(), 
        Footprint2x2(),
        Ability("Fire Breath", Unlimited(), FireBreath()),
        Ability("Claw Attack", Unlimited(), ClawAttack())
    ).WithBehaviors(UnitBehavior.PreferFireBreathThenClaw);

    public static UnitType Ghost(string id = UnitTypeIds.Ghost) => UnitType.Define(id, 
        "Ghost", 
        Stats(2, 1, 3, 3, 1), 
        Phase()
    );

    public static UnitType Grunt(string id = UnitTypeIds.Grunt) => UnitType.Define(id, 
        "Grunt", 
        Stats(3, 1, 3, 3, 1)
    );

    public static UnitType Zombie(string id = UnitTypeIds.Zombie) => UnitType.Define(id, 
        "Zombie", 
        Stats(2, 1, 3, 3, 1),
        Ability("Break Door", Unlimited(), TryOpenDoor(successes: 2))
    ).WithBehaviors(UnitBehavior.ApproachThroughClosedDoors);

    public static UnitType SkeletonArcher(string id = UnitTypeIds.SkeletonArcher) => UnitType.Define(id, 
        "Skeleton Archer", 
        Stats(3, 4, 3, 3, 1)
    ).WithBehaviors(UnitBehavior.MaximizeAttackDistance);

    public static UnitType Goblin(string id = UnitTypeIds.Goblin) => UnitType.Define(id, 
        "Goblin", 
        Stats(4, 1, 2, 2, 1),
        Ability("Move After Attack", Unlimited(), MoveAfterAttack(maxMove: 1))
    ).WithBehaviors(UnitBehavior.BackAwayAfterAttack);

    public static UnitType Troll(string id = UnitTypeIds.Troll) => UnitType.Define(id, 
        "Troll", 
        Stats(2, 1, 4, 4, 1),
        Ability("Smash Door", Unlimited(), TryOpenDoor(successes: 4)),
        Ability("Undying", Unlimited(), Undying())
    ).WithBehaviors(UnitBehavior.ApproachThroughClosedDoors);

    public static UnitType Shaman(string id = UnitTypeIds.Shaman) => UnitType.Define(id, 
        "Shaman", 
        Stats(2, 0, 0, 3, 1),
        CantAttack(),
        Ability("Summon Goblin", Unlimited(), SummonAdjacent(UnitTypeIds.Goblin, Posture.Lying))
    ).WithBehaviors(UnitBehavior.Flee, UnitBehavior.UseSummon);

    public static UnitType Barbarian(string id = UnitTypeIds.Barbarian) => UnitType.Define(id,
        "Barbarian",
        Stats(3, 1, 4, 3, 5),
        Unique(),
        OpenDoor(),
        Ability("Fury", Unlimited(), Fury(atkBonus: +1, adjacentEnemies: 2)),
        Ability("Cleave", Uses(2), Cleave(triggerDamage: 2, damage: 1)),
        Ability("Rage", Uses(2), BonusActionSelfModifier(Modifier(Atk, +2)), id: "Rage")
    );

    public static UnitType Rogue(string id = UnitTypeIds.Rogue) => UnitType.Define(id,
        "Rogue",
        Stats(4, 1, 3, 2, 4),
        Unique(),
        OpenDoor(),
        Ability("Backstab", Unlimited(), Backstab(atkBonus: +1)),
        Ability("Dash", Uses(2), BonusActionSelfModifier(Modifier(Mov, +2)), id: "Dash"),
        Ability("Throwing Knife", Uses(2),
        BonusActionSelfModifier(Modifier(Rng, +2), Modifier(Atk, -1)), id: "Throwing Knife")
    );

    public static UnitType Cleric(string id = UnitTypeIds.Cleric) => UnitType.Define(id,
        "Cleric",
        Stats(3, 1, 3, 3, 4),
        Unique(),
        OpenDoor(),
        Ability("Heal", Uses(2), Heal(amount: 2)),
        Ability("Holy Wave", Uses(2), HolyWave()),
        Ability("Aura", Unlimited(), AdjacentFriendliesDefenceBonus(+1))
    );

    public static UnitType Wizard(string id = UnitTypeIds.Wizard) => UnitType.Define(id,
        "Wizard",
        Stats(2, 4, 3, 2, 4),
        Unique(),
        OpenDoor(),
        Ability("Fireball", Uses(2), Fireball()),
        Ability("Telekinesis", Unlimited(), Telekinesis()),
        Ability("Focus", Uses(2), BonusActionSelfModifier(Modifier(Atk, +1)), id: "Focus")
    );

}
