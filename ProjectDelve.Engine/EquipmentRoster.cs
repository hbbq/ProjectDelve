namespace ProjectDelve.Engine;

// Canonical Equipment content. Models, validation and rules live separately.
public static class EquipmentRoster
{
    public static IReadOnlyList<EquipmentDefinition> All { get; } = Array.AsReadOnly(new EquipmentDefinition[]
    {
        new("bedsheet", "One-Size-Fits-Most Bedsheet") { Capabilities = [EquipmentCapability.Phase] },
        new("lucky-boots", "Lucky Boots") { Modifiers = [new(Stat.Mov, 1)] },
        new("official-key", "Official-Looking Key") { Capabilities = [EquipmentCapability.OpenDoor] },
        new("goat-bell", "Emergency Goat Bell") { Abilities = [new("stand-up", "Emergency Goat Bell", EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit) { CandidatePrefix = "goat-bell" }] }
    });
}
