using System.Text.Json.Serialization;
using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// The key is unique only within its owning content definition. Names are printed
// presentation; mechanics, timing and usage are complete authored data.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalAbilityDefinition(string Key, string Name, AbilityTiming Timing, AbilityMechanic Mechanic)
{
    public AbilityUsage Usage { get; init; } = new();
    public string? DescriptionOverride { get; init; }
    [JsonIgnore, JsonPropertyName("costs")]
    public ImmutableArray<AbilityCost> Costs { get; init; } = [];
    // Empty costs retain the exact pre-cost serialized envelope (and content digest).
    [JsonPropertyName("costs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<AbilityCost>? SerializedCosts
    {
        get => Costs.IsDefaultOrEmpty ? null : Costs;
        init => Costs = value ?? default;
    }
    public CardEntryDescription CardEntry(string? entryId = null, IReadOnlyList<UnitType>? existingTypes = null) =>
        ContentDescriptions.AbilityEntry(this, entryId ?? Key, existingTypes);
}
