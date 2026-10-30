using static ProjectDelve.Engine.UnitAuthoring;

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
//     Ability("Claw Attack", Unlimited(), ClawAttack(atkBonus: +1))
// );

public static class UnitRoster
{
    public static UnitType RoyalQueen(string id = UnitTypeIds.RoyalQueen) => UnitType.Define(id,
        "Royal Queen", Stats(16, 1, 4, 5, 3), Loot(2), Unique(), StraightOrthogonalOrDiagonalCorridor(), AdvanceOnKill()
    );

    public static UnitType WitchQueen(string id = UnitTypeIds.WitchQueen) => UnitType.Define(id,
        "Witch Queen", Stats(16, 1, 4, 5, 3), Loot(2), Unique(), StraightOrthogonalOrDiagonalCorridor(), AdvanceOnKill()
    );

    public static UnitType RoyalKing(string id = UnitTypeIds.RoyalKing) => UnitType.Define(id,
        "Royal King", Stats(2, 1, 2, 3, 5), Loot(2), Unique(), Adjacent(),
        Grant("aura", "aura", new(LegacyAbilityKind.Aura, Key: "Aura"))
    );

    public static UnitType GoblinKing(string id = UnitTypeIds.GoblinKing) => UnitType.Define(id,
        "Goblin King", Stats(2, 1, 2, 3, 5), Loot(2), Unique(), Adjacent(),
        Grant("aura", "aura", new(LegacyAbilityKind.Aura, Key: "Aura"))
    );

    public static UnitType RoyalBishop(string id = UnitTypeIds.RoyalBishop) => UnitType.Define(id,
        "Royal Bishop", Stats(16, 1, 3, 4, 1), Loot(1), DiagonalCorridor(), AdvanceOnKill()
    );

    public static UnitType GoblinBishop(string id = UnitTypeIds.GoblinBishop) => UnitType.Define(id,
        "Goblin Bishop", Stats(16, 1, 3, 4, 1), Loot(1), DiagonalCorridor(), AdvanceOnKill()
    );

    public static UnitType RoyalKnight(string id = UnitTypeIds.RoyalKnight) => UnitType.Define(id,
        "Royal Knight", Stats(3, 1, 3, 4, 1), Loot(1), LShape()
    );

    public static UnitType HobbyKnight(string id = UnitTypeIds.HobbyKnight) => UnitType.Define(id,
        "Hobby Knight", Stats(3, 1, 3, 4, 1), Loot(1), LShape()
    );

    public static UnitType RoyalPeasant(string id = UnitTypeIds.RoyalPeasant) => UnitType.Define(id,
        "Royal Peasant", Stats(1, 1, 2, 2, 1), Loot(1)
    ).WithBehaviors(UnitBehavior.PreferLowestEffectiveDefense);

    public static UnitType GoblinPeasant(string id = UnitTypeIds.GoblinPeasant) => UnitType.Define(id,
        "Goblin Peasant", Stats(1, 1, 2, 2, 1), Loot(1)
    ).WithBehaviors(UnitBehavior.PreferLowestEffectiveDefense);

    public static UnitType CastleGuard(string id = UnitTypeIds.CastleGuard) => UnitType.Define(id,
        "Castle Guard", Stats(16, 1, 3, 4, 1), Loot(2), StraightOrthogonal(), AdvanceOnKill()
    );

    public static UnitType TowerTroll(string id = UnitTypeIds.TowerTroll) => UnitType.Define(id,
        "Tower Troll", Stats(16, 1, 3, 4, 1), Loot(2), StraightOrthogonal(), AdvanceOnKill()
    );

    public static UnitType DiamondKart(string id = UnitTypeIds.DiamondKart) => UnitType.Define(
        id, "Diamond Kart", Stats(5, 0, 0, 3, 4), Loot(1), Unique(), CantAttack(),
        ResourceStorage(new(ResourceTypes.Banana, 1), new(ResourceTypes.Mushroom, 1), new(ResourceTypes.Shell, 1)),
        PlaceBananaPeel(bananaCost: 1), Boost(mushroomCost: 1), ShootShell(shellCost: 1, rng: 4, atk: 3), WaypointSlotMachine(),
        TerrainRestriction(TerrainCategories.RacingGround)
    ).WithBehaviors(UnitBehavior.RouteLookahead, UnitBehavior.PreferBananaPeel);

    public static UnitType ChiefMechanic(string id = UnitTypeIds.ChiefMechanic) => UnitType.Define(
        id, "Chief Mechanic", Stats(3, 0, 0, 4, 3), Loot(2),
        Unique(), CantAttack(), TransportResource(), GiveResource(), Operate(), SummonWorker(sideLimit: 3)
    ).WithBehaviors(UnitBehavior.Worker, UnitBehavior.PreferSummonWorker);

    public static UnitType Locomotive(string id = UnitTypeIds.Locomotive) => UnitType.Define(id,
        "Locomotive",
        Stats(0, 0, 0, 0, 1), Loot(1),
        CantAttack(),
        RunRails(steps: 4)
    );

    public static UnitType DisplacerDemon(string id = UnitTypeIds.DisplacerDemon) => UnitType.Define(id,
        "Displacer Demon",
        Stats(3, 1, 4, 4, 1), Loot(2),
        Ability("Swap", Unlimited(), Swap(), id: "Swap"),
        Ability("Displace", Unlimited(), Displace(maxMove: 1), id: "Displace"),
        Grant("aura", "aura", new(LegacyAbilityKind.Aura, Key: "Aura"))
    ).WithBehaviors(UnitBehavior.SwapThenAttackThenDisplace);

    // Digital-only playtest content.
    public static UnitType LabyrinthShaper(string id = UnitTypeIds.LabyrinthShaper) => UnitType.Define(id,
        "Labyrinth Shaper",
        Stats(2, 1, 2, 2, 5), Loot(2),
        Unique(),
        Ability("Reshape Room", Unlimited(), ReshapeRoom(), id: "Reshape Room")
    ).WithBehaviors(UnitBehavior.PreferReshapeRoom);

    public static UnitType RedDragon(string id = UnitTypeIds.RedDragon) => UnitType.Define(id,
        "Red Dragon",
        Stats(2, 4, 4, 4, 8), Loot(3),
        Unique(),
        Footprint2x2(),
        Ability("Fire Breath", Unlimited(), FireBreath()),
        Ability("Claw Attack", Unlimited(), ClawAttack(atkBonus: +1))
    );

    public static UnitType Ghost(string id = UnitTypeIds.Ghost) => UnitType.Define(id,
        "Ghost",
        Stats(2, 1, 3, 3, 1), Loot(1),
        Phase()
    );

    public static UnitType Grunt(string id = UnitTypeIds.Grunt) => UnitType.Define(id,
        "Grunt",
        Stats(3, 1, 3, 3, 1), Loot(1)
    );

    public static UnitType Zombie(string id = UnitTypeIds.Zombie) => UnitType.Define(id,
        "Zombie",
        Stats(2, 1, 3, 3, 1), Loot(1),
        TransportResource(),
        Ability("Break Door", Unlimited(), TryOpenDoor(successes: 2))
    ).WithBehaviors(UnitBehavior.ApproachThroughClosedDoors);

    public static UnitType SkeletonArcher(string id = UnitTypeIds.SkeletonArcher) => UnitType.Define(id,
        "Skeleton Archer",
        Stats(3, 4, 3, 3, 1), Loot(1)
    ).WithBehaviors(UnitBehavior.MaximizeAttackDistance);

    public static UnitType Goblin(string id = UnitTypeIds.Goblin) => UnitType.Define(id,
        "Goblin",
        Stats(4, 1, 2, 2, 1), Loot(1),
        Ability("Move After Attack", Unlimited(), MoveAfterAttack(maxMove: 1))
    ).WithBehaviors(UnitBehavior.BackAwayAfterAttack);

    public static UnitType BombImp(string id = UnitTypeIds.BombImp) => UnitType.Define(id,
        "Bomb Imp",
        Stats(3, 1, 2, 2, 1), Loot(1),
        Ability("Explosion", Unlimited(), Explosion(damage: 1))
    ).WithBehaviors(UnitBehavior.PreferHostileAdjacency, UnitBehavior.ProtectWithoutAdjacency);

    public static UnitType Troll(string id = UnitTypeIds.Troll) => UnitType.Define(id,
        "Troll",
        Stats(2, 1, 4, 4, 1), Loot(2),
        Ability("Smash Door", Unlimited(), TryOpenDoor(successes: 4)),
        Ability("Undying", Unlimited(), Undying())
    ).WithBehaviors(UnitBehavior.ApproachThroughClosedDoors);

    public static UnitType Worker(string id = UnitTypeIds.Worker) => UnitType.Define(id,
        "Worker",
        Stats(4, 0, 0, 2, 1), Loot(1),
        CantAttack(), TransportResource(), GiveResource(), Operate()
    ).WithBehaviors(UnitBehavior.Worker);

    public static UnitType Geomancer(string id = UnitTypeIds.Geomancer) => UnitType.Define(id,
        "Geomancer",
        Stats(2, 0, 0, 3, 1), Loot(2),
        CantAttack(),
        ResourceStorage(ResourceTypes.Diamond, 1),
        PickupResource(),
        SummonWorker(sideLimit: 3),
        SummonTroll(diamondCost: 1)
    ).WithBehaviors(UnitBehavior.SeekDiamond, UnitBehavior.Flee, UnitBehavior.PreferTrollThenDiamondThenWorker);

    public static UnitType Shaman(string id = UnitTypeIds.Shaman) => UnitType.Define(id,
        "Shaman",
        Stats(2, 0, 0, 3, 1), Loot(2),
        CantAttack(),
        Ability("Summon Goblin", Unlimited(), SummonAdjacent(UnitTypeIds.Goblin, Posture.Lying))
    ).WithBehaviors(UnitBehavior.Flee, UnitBehavior.UseSummon);

    public static UnitType Barbarian(string id = UnitTypeIds.Barbarian) => UnitType.Define(id,
        "Barbarian",
        Stats(3, 1, 4, 3, 5), Loot(1, items: true, equipment: true),
        Unique(),
        OpenDoor(),
        Ability("Fury", Unlimited(), Fury(atkBonus: +1, adjacentEnemies: 2)),
        Ability("Cleave", Uses(2), Cleave(triggerDamage: 2, damage: 1)),
        Grant("rage", "rage", new(LegacyAbilityKind.BonusAction, Key: "Rage"))
    );

    public static UnitType Rogue(string id = UnitTypeIds.Rogue) => UnitType.Define(id,
        "Rogue",
        Stats(4, 1, 3, 2, 4), Loot(1, items: true, equipment: true),
        Unique(),
        OpenDoor(),
        Ability("Backstab", Unlimited(), Backstab(atkBonus: +1)),
        Grant("dash", "dash", new(LegacyAbilityKind.BonusAction, Key: "Dash")),
        Grant("throwing-knife", "throwing-knife", new(LegacyAbilityKind.BonusAction, Key: "Throwing Knife"))
    );

    public static UnitType Cleric(string id = UnitTypeIds.Cleric) => UnitType.Define(id,
        "Cleric",
        Stats(3, 1, 3, 3, 4), Loot(1, items: true, equipment: true),
        Unique(),
        OpenDoor(),
        Grant("heal", "heal", new(LegacyAbilityKind.Heal)),
        Grant("holy-wave", "holy-wave", new(LegacyAbilityKind.HolyWave)),
        Grant("aura", "aura", new(LegacyAbilityKind.Aura, Key: "Aura"))
    );

    public static UnitType Wizard(string id = UnitTypeIds.Wizard) => UnitType.Define(id,
        "Wizard",
        Stats(3, 4, 3, 2, 4), Loot(1, items: true, equipment: true),
        Unique(),
        OpenDoor(),
        Grant("fireball", "fireball", new(LegacyAbilityKind.Fireball)),
        Ability("Telekinesis", Unlimited(), Telekinesis()),
        Grant("focus", "focus", new(LegacyAbilityKind.BonusAction, Key: "Focus"))
    ) with { AttackDefinitionOrder = [UnitAction.NormalAttack, UnitAction.Fireball] };

}
