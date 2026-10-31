namespace ProjectDelve.Engine;

// Canonical Equipment content. Models, validation and rules live separately.
public static class EquipmentRoster
{
    public static IReadOnlyList<EquipmentDefinition> All { get; } = Array.AsReadOnly(new EquipmentDefinition[]
    {
        new("bedsheet", "One-Size-Fits-Most Bedsheet")
        { LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase())] },
        new("lucky-boots", "Lucky Boots")
        { LocalAbilities = [new("movement", "Lucky Boots", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 1)]))] },
        new("official-key", "Official-Looking Key")
        { LocalAbilities = [new("open-door", "Open Door", AbilityTiming.FreeAction, new OpenDoor())] },
        new("goat-bell", "Emergency Goat Bell")
        {
            LocalAbilities = [new("stand-up", "Emergency Goat Bell", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())
            {
                Usage = new(MaxUses: null)
            }]
        }
    });
}
