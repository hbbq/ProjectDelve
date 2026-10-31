namespace ProjectDelve.Engine;

// Canonical Item content only. Each Item owns its complete local Abilities;
// physical consumption and use accounting are shared Engine operations.
public static class ItemRoster
{
    public static IReadOnlyList<ItemDefinition> All { get; } = Array.AsReadOnly(new ItemDefinition[]
    {
        new("second-wind", "Second Wind")
        {
            LocalAbilities = [new("use", "Second Wind", AbilityTiming.BonusAction, new ExtraActionsMechanic(Amount: 1))
            {
                Usage = new(MaxUses: null)
            }]
        }
    });
}
