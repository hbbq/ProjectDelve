namespace ProjectDelve.Engine;

public static class UnitTypeIds
{
    public const string Grunt = "grunt-type";
    public const string Zombie = "zombie-type";
    public const string Ghost = "ghost-type";
    public const string SkeletonArcher = "skeleton-archer-type";
    public const string Goblin = "goblin-type";
    public const string Troll = "troll-type";
    public const string Shaman = "shaman-type";
    public const string Barbarian = "barbarian-type";
    public const string Rogue = "rogue-type";
    public const string Cleric = "cleric-type";
    public const string Wizard = "wizard-type";
}

// Narrow canonical lookup for concrete summoning; existing game definitions take priority.
internal static class UnitContent
{
    internal static UnitType? Find(string id, IReadOnlyList<UnitType>? existing = null) =>
        existing?.SingleOrDefault(t => t.Id == id) ?? (id switch
        {
            UnitTypeIds.Grunt => UnitRoster.Grunt(),
            UnitTypeIds.Zombie => UnitRoster.Zombie(),
            UnitTypeIds.Ghost => UnitRoster.Ghost(),
            UnitTypeIds.SkeletonArcher => UnitRoster.SkeletonArcher(),
            UnitTypeIds.Goblin => UnitRoster.Goblin(),
            UnitTypeIds.Troll => UnitRoster.Troll(),
            UnitTypeIds.Shaman => UnitRoster.Shaman(),
            UnitTypeIds.Barbarian => UnitRoster.Barbarian(),
            UnitTypeIds.Rogue => UnitRoster.Rogue(),
            UnitTypeIds.Cleric => UnitRoster.Cleric(),
            UnitTypeIds.Wizard => UnitRoster.Wizard(),
            _ => null
        });
}
