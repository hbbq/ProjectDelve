namespace ProjectDelve.Engine;

// Canonical World Card content; the existing concrete mechanics are unchanged.
public static class WorldCardRoster
{
    public static IReadOnlyList<WorldCardContent> All { get; } = Array.AsReadOnly(new WorldCardContent[]
    {
        new(WorldEffect.Bloodlust, "Bloodlust", "All Units have ATK +1.", true, 3),
        new(WorldEffect.Weakness, "Weakness", "All Units have ATK -1.", true, 3),
        new(WorldEffect.IronSkin, "Iron Skin", "All Units have DEF +1.", true, 3),
        new(WorldEffect.Vulnerability, "Vulnerability", "All Units have DEF -1.", true, 3),
        new(WorldEffect.Haste, "Haste", "All Units have MOV +1.", true, 3),
        new(WorldEffect.Sluggishness, "Sluggishness", "All Units have MOV -1.", true, 3),
        new(WorldEffect.Frenzy, "Frenzy", "All Units have +1 Action per activation.", true, 1),
        new(WorldEffect.Fatigue, "Fatigue", "All Units have -1 Action per activation.", true, 1),
        new(WorldEffect.Surge, "Surge", "Each unique Bonus Action may be used one additional time per activation.", true, 1),
        new(WorldEffect.Hesitation, "Hesitation", "Each unique Bonus Action may be used one fewer time per activation.", true, 1),
        new(WorldEffect.Calm, "Calm", "No Effect.", true, 8),
        new(WorldEffect.Earthquake, "Earthquake", "Lay down all Units.", false, 1),
        new(WorldEffect.OpenSesame, "Open Sesame!", "Open all Doors.", false, 1),
        new(WorldEffect.Lockdown, "Lockdown", "Close all Doors.", false, 1),
        new(WorldEffect.Miracle, "Miracle", "Fully heal all Units.", false, 1),
        new(WorldEffect.SecondWind, "Second Wind", "Heal every damaged Unit for 1 HP.", false, 2),
        new(WorldEffect.Repulsion, "Repulsion", "Deal 1 damage to every Unit adjacent to another Unit.", false, 1),
        new(WorldEffect.Loneliness, "Loneliness", "Deal 1 damage to every Unit not adjacent to another Unit.", false, 1),
        new(WorldEffect.Renewal, "Renewal", "Replenish 1 use of every limited-use Ability.", false, 2)
    });
}
