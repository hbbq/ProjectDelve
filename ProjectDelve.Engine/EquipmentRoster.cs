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
        new("binoculars", "Binoculars")
        { LocalAbilities = [new("sight", "Binoculars", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.SightDistance, 3)]))] },
        new("swim-tube", "Swim Tube")
        { LocalAbilities = [new("amphibious", "Amphibious", AbilityTiming.Passive, new Amphibious())] },
        new("official-key", "Official-Looking Key")
        { LocalAbilities = [new("open-door", "Open Door", AbilityTiming.FreeAction, new OpenDoor())] },
        new("goat-bell", "Emergency Goat Bell")
        {
            LocalAbilities = [new("stand-up", "Emergency Goat Bell", AbilityTiming.Action, new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Friendlies, Posture.Upright))
            {
                Usage = new(MaxUses: null)
            }]
        }
    });
}
