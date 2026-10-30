namespace ProjectDelve.Engine;

// Authored Types and physical deck composition are independent declarations.
public static class WorldCardRoster
{
    public static IReadOnlyList<WorldCardDefinition> Definitions { get; } = Array.AsReadOnly(new WorldCardDefinition[]
    {
        new("bloodlust", "Bloodlust", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Atk, 1)] },
        new("weakness", "Weakness", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Atk, -1)] },
        new("iron-skin", "Iron Skin", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Def, 1)] },
        new("vulnerability", "Vulnerability", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Def, -1)] },
        new("haste", "Haste", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Mov, 1)] },
        new("sluggishness", "Sluggishness", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Mov, -1)] },
        new("frenzy", "Frenzy", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Actions, 1)] },
        new("fatigue", "Fatigue", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Actions, -1)] },
        new("surge", "Surge", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.BonusActionUses, 1)] },
        new("hesitation", "Hesitation", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.BonusActionUses, -1)] },
        new("calm", "Calm", WorldCardLifecycle.Continuous),
        new("earthquake", "Earthquake", WorldCardLifecycle.Immediate) { Immediate = new LayDownAllUnitsMechanic() },
        new("open-sesame", "Open Sesame!", WorldCardLifecycle.Immediate) { Immediate = new SetAllDoorsMechanic(Open: true) },
        new("lockdown", "Lockdown", WorldCardLifecycle.Immediate) { Immediate = new SetAllDoorsMechanic(Open: false) },
        new("miracle", "Miracle", WorldCardLifecycle.Immediate) { Immediate = new FullyHealUnitsMechanic() },
        new("second-wind", "Second Wind", WorldCardLifecycle.Immediate) { Immediate = new HealDamagedUnitsMechanic(Amount: 1) },
        new("repulsion", "Repulsion", WorldCardLifecycle.Immediate) { Immediate = new DamageUnitsByAdjacencyMechanic(Amount: 1, Adjacent: true) },
        new("loneliness", "Loneliness", WorldCardLifecycle.Immediate) { Immediate = new DamageUnitsByAdjacencyMechanic(Amount: 1, Adjacent: false) },
        new("renewal", "Renewal", WorldCardLifecycle.Immediate) { Immediate = new ReplenishLimitedAbilitiesMechanic(Amount: 1) }
    });

    // Preserve baseline order as well as quantities: shuffling consumes the same randomness.
    public static IReadOnlyList<WorldCardQuantity> Composition { get; } = Array.AsReadOnly(new WorldCardQuantity[]
    {
        new("bloodlust", 3), new("weakness", 3), new("iron-skin", 3), new("vulnerability", 3),
        new("haste", 3), new("sluggishness", 3), new("frenzy", 1), new("fatigue", 1),
        new("surge", 1), new("hesitation", 1), new("calm", 8), new("earthquake", 1),
        new("open-sesame", 1), new("lockdown", 1), new("miracle", 1), new("second-wind", 2),
        new("repulsion", 1), new("loneliness", 1), new("renewal", 2)
    });

    // Compatibility view only; new definitions need neither a legacy enum nor a deck row.
    public static IReadOnlyList<WorldCardContent> All => WorldCards.ProjectLegacyContent(Definitions, Composition);
}
