using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// The key is unique only within its owning content definition. Names are printed
// presentation; mechanics, timing and usage are complete authored data.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalAbilityDefinition(string Key, string Name, AbilityTiming Timing, AbilityMechanic Mechanic)
{
    public AbilityUsage Usage { get; init; } = new();
    public string? DescriptionOverride { get; init; }
    public CardEntryDescription CardEntry(string? entryId = null, IReadOnlyList<UnitType>? existingTypes = null) =>
        ContentDescriptions.AbilityEntry(this, entryId ?? Key, existingTypes);
}
