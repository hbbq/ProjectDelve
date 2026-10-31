using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Key belongs to this source definition only. It is independent of the printed
// name, mechanic, list position and any optional shared Ability catalog.
public sealed record LocalAbilityDefinition(string Key, string Name, AbilityTiming Timing, AbilityMechanic Mechanic)
{
    public AbilityUsage Usage { get; init; } = new();
    public string? DescriptionOverride { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AbilityGrantCompatibility? Compatibility { get; init; }

    internal AbilityDefinition ToDefinition() => new(Key, Name, Timing, Mechanic)
        { Usage = Usage, DescriptionOverride = DescriptionOverride };
}

// A transient resolution view, never a second serialized catalog. Keeping the
// original definition for legacy references preserves existing pending payloads.
internal sealed record AbilityDeclaration(LocalAbilityDefinition Content, AbilityDefinition Definition)
{
    internal string Key => Content.Key;
    internal AbilityGrantCompatibility? Compatibility => Content.Compatibility;
    internal AbilityGrant Grant => new(Key, Definition.Id) { Compatibility = Compatibility };
}

internal static class AbilityDeclarations
{
    internal static IEnumerable<AbilityDeclaration> Resolve(ImmutableArray<LocalAbilityDefinition> locals,
        ImmutableArray<AbilityGrant> grants, IReadOnlyList<AbilityDefinition>? definitions = null,
        string? legacyItemTypeId = null)
    {
        if (locals.IsDefault || grants.IsDefault) throw new ArgumentException("Ability lists cannot be null.");
        foreach (var local in locals)
        {
            if (local is null) throw new ArgumentException("A local Ability declaration is required.");
            yield return new(local, local.ToDefinition());
        }
        // A fully local source does not depend on or initialize a shared catalog.
        if (grants.IsEmpty) yield break;
        definitions ??= AbilityCatalog.All;
        foreach (var grant in grants)
        {
            if (grant is null) throw new ArgumentException("An Ability grant is required.");
            var definition = definitions.SingleOrDefault(d => d.Id == grant.AbilityId)
                ?? throw new ArgumentException($"Unknown Ability ID '{grant.AbilityId}'.");
            var compatibility = grant.Compatibility;
            // Historical Item identity is read only at the old-reference boundary.
            // New local content carries an explicit binding when it preserves this slot.
            if (compatibility is null && legacyItemTypeId == "second-wind" && grant.Id == "use")
                compatibility = new(LegacyAbilityKind.SecondWind);
            yield return new(new(grant.Id, definition.Name, definition.Timing, definition.Mechanic)
            {
                Usage = definition.Usage, DescriptionOverride = definition.DescriptionOverride,
                Compatibility = compatibility
            }, definition);
        }
    }
}
