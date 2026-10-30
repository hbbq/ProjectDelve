namespace ProjectDelve.Engine;

// Canonical Ability content only. Mechanics, validation and resolution live in
// Abilities.cs; an external catalog can supply these same serializable records.
public static class AbilityRoster
{
    public static IReadOnlyList<AbilityDefinition> All { get; } = Array.AsReadOnly(new AbilityDefinition[]
    {
        new("second-wind", "Second Wind", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))
    });
}
