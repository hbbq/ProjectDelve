namespace ProjectDelve.Engine;

// Canonical Ability content only. Mechanics, validation and resolution live in
// Abilities.cs; an external catalog can supply these same serializable records.
public static class AbilityRoster
{
    public static IReadOnlyList<AbilityDefinition> All { get; } = Array.AsReadOnly(new AbilityDefinition[]
    {
        new("heal", "Heal", AbilityTiming.Action, new HealMechanic(Amount: 2))
        {
            Usage = new(MaxUses: 2)
        },
        new("holy-wave", "Holy Wave", AbilityTiming.Action, new HolyWaveMechanic())
        {
            Usage = new(MaxUses: 2)
        },
        new("fireball", "Fireball", AbilityTiming.Action, new FireballMechanic())
        {
            Usage = new(MaxUses: 2)
        },
        new("aura", "Aura", AbilityTiming.Passive, new AdjacentModifiersMechanic([new(Stat.Def, +1)]))
        {
            Usage = new(MaxUses: null)
        },
        new("rage", "Rage", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, +2)]))
        {
            Usage = new(MaxUses: 2)
        },
        new("dash", "Dash", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Mov, +2)]))
        {
            Usage = new(MaxUses: 2)
        },
        new("throwing-knife", "Throwing Knife", AbilityTiming.BonusAction,
            new SelfModifiersMechanic([new(Stat.Rng, +2), new(Stat.Atk, -1)]))
        {
            Usage = new(MaxUses: 2)
        },
        new("focus", "Focus", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, +1)]))
        {
            Usage = new(MaxUses: 2)
        },
        new("goat-bell-stand-up", "Emergency Goat Bell", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())
        {
            Usage = new(MaxUses: null)
        },
        new("second-wind", "Second Wind", AbilityTiming.BonusAction, new ExtraActionsMechanic(Amount: 1))
        {
            Usage = new(MaxUses: null)
        }
    });
}
