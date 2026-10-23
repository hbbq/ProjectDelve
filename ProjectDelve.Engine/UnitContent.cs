namespace ProjectDelve.Engine;

public static class UnitTypeIds
{
    public const string RoyalQueen = "royal-queen-type";
    public const string WitchQueen = "witch-queen-type";
    public const string RoyalKing = "royal-king-type";
    public const string GoblinKing = "goblin-king-type";
    public const string RoyalBishop = "royal-bishop-type";
    public const string GoblinBishop = "goblin-bishop-type";
    public const string RoyalKnight = "royal-knight-type";
    public const string HobbyKnight = "hobby-knight-type";
    public const string RoyalPeasant = "royal-peasant-type";
    public const string GoblinPeasant = "goblin-peasant-type";
    public const string CastleGuard = "castle-guard-type";
    public const string TowerTroll = "tower-troll-type";
    public const string DiamondKart = "diamond-kart-type";
    public const string ChiefMechanic = "chief-mechanic-type";
    public const string Locomotive = "locomotive-type";
    public const string DisplacerDemon = "displacer-demon-type";
    public const string LabyrinthShaper = "labyrinth-shaper-type";
    public const string RedDragon = "red-dragon-type";
    public const string Grunt = "grunt-type";
    public const string Zombie = "zombie-type";
    public const string Ghost = "ghost-type";
    public const string SkeletonArcher = "skeleton-archer-type";
    public const string Goblin = "goblin-type";
    public const string BombImp = "bomb-imp-type";
    public const string Troll = "troll-type";
    public const string Worker = "worker-type";
    public const string Geomancer = "geomancer-type";
    public const string Shaman = "shaman-type";
    public const string Barbarian = "barbarian-type";
    public const string Rogue = "rogue-type";
    public const string Cleric = "cleric-type";
    public const string Wizard = "wizard-type";
}

// Canonical lookup for initial setup and concrete summoning; existing game definitions take priority.
public static class UnitContent
{
    public static UnitType? Find(string id, IReadOnlyList<UnitType>? existing = null) =>
        existing?.SingleOrDefault(t => t.Id == id) ?? CanonicalUnitTypes.Find(id);
}

// One registry for canonical lookup and enumeration. Factories keep each creation detached.
public static class CanonicalUnitTypes
{
    private static readonly IReadOnlyDictionary<string, Func<UnitType>> Factories =
        new Dictionary<string, Func<UnitType>>(StringComparer.Ordinal)
        {
            [UnitTypeIds.RoyalQueen] = () => UnitRoster.RoyalQueen(),
            [UnitTypeIds.WitchQueen] = () => UnitRoster.WitchQueen(),
            [UnitTypeIds.RoyalKing] = () => UnitRoster.RoyalKing(),
            [UnitTypeIds.GoblinKing] = () => UnitRoster.GoblinKing(),
            [UnitTypeIds.RoyalBishop] = () => UnitRoster.RoyalBishop(),
            [UnitTypeIds.GoblinBishop] = () => UnitRoster.GoblinBishop(),
            [UnitTypeIds.RoyalKnight] = () => UnitRoster.RoyalKnight(),
            [UnitTypeIds.HobbyKnight] = () => UnitRoster.HobbyKnight(),
            [UnitTypeIds.RoyalPeasant] = () => UnitRoster.RoyalPeasant(),
            [UnitTypeIds.GoblinPeasant] = () => UnitRoster.GoblinPeasant(),
            [UnitTypeIds.CastleGuard] = () => UnitRoster.CastleGuard(),
            [UnitTypeIds.TowerTroll] = () => UnitRoster.TowerTroll(),
            [UnitTypeIds.DiamondKart] = () => UnitRoster.DiamondKart(),
            [UnitTypeIds.ChiefMechanic] = () => UnitRoster.ChiefMechanic(),
            [UnitTypeIds.Locomotive] = () => UnitRoster.Locomotive(),
            [UnitTypeIds.DisplacerDemon] = () => UnitRoster.DisplacerDemon(),
            [UnitTypeIds.LabyrinthShaper] = () => UnitRoster.LabyrinthShaper(),
            [UnitTypeIds.RedDragon] = () => UnitRoster.RedDragon(),
            [UnitTypeIds.Grunt] = () => UnitRoster.Grunt(),
            [UnitTypeIds.Zombie] = () => UnitRoster.Zombie(),
            [UnitTypeIds.Ghost] = () => UnitRoster.Ghost(),
            [UnitTypeIds.SkeletonArcher] = () => UnitRoster.SkeletonArcher(),
            [UnitTypeIds.Goblin] = () => UnitRoster.Goblin(),
            [UnitTypeIds.BombImp] = () => UnitRoster.BombImp(),
            [UnitTypeIds.Troll] = () => UnitRoster.Troll(),
            [UnitTypeIds.Worker] = () => UnitRoster.Worker(),
            [UnitTypeIds.Geomancer] = () => UnitRoster.Geomancer(),
            [UnitTypeIds.Shaman] = () => UnitRoster.Shaman(),
            [UnitTypeIds.Barbarian] = () => UnitRoster.Barbarian(),
            [UnitTypeIds.Rogue] = () => UnitRoster.Rogue(),
            [UnitTypeIds.Cleric] = () => UnitRoster.Cleric(),
            [UnitTypeIds.Wizard] = () => UnitRoster.Wizard()
        };

    public static IEnumerable<UnitType> All => Factories.Values.Select(create => create());
    public static UnitType? Find(string id) => Factories.TryGetValue(id, out var create) ? create() : null;
}
