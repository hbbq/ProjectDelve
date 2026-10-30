using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Visual recipes never supply rules. Unknown recipes use the client's generic fallback.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TerrainPresentation([property: JsonRequired] string Recipe);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TerrainDefinition([property: JsonRequired] string Id, [property: JsonRequired] string Name,
    [property: JsonRequired] bool Passable, [property: JsonRequired] bool BlocksLos)
{
    public ImmutableArray<string> Categories { get; init; } = [];
    public bool AllowsResourceMarkers { get; init; } = true;
    public TerrainPresentation Presentation { get; init; } = new("generic");
    // Compatibility only for the still-unmigrated production, Rail and Water rules.
    // Scenario-local Types cannot claim a legacy identity.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TerrainKind? LegacyKind { get; init; }
}

public static class TerrainCategories
{
    public const string RacingGround = "racing-ground";
}

// A Unit accepts a Cell matching any declared category. Ordinary passability is separate.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TerrainRestriction(ImmutableArray<string> Categories)
{
    public bool Accepts(TerrainDefinition terrain) => Categories.Any(terrain.Categories.Contains);
    internal bool IsValid => !Categories.IsDefaultOrEmpty && Categories.All(TerrainCatalog.ValidKey) &&
        Categories.Distinct(StringComparer.Ordinal).Count() == Categories.Length;
}

public static class TerrainCatalog
{
    public static ImmutableArray<TerrainDefinition> All => TerrainRoster.All;
    private static readonly ImmutableDictionary<TerrainKind, TerrainDefinition> LegacyDefinitions =
        TerrainRoster.All.ToImmutableDictionary(d => d.LegacyKind!.Value);
    public static ImmutableArray<TerrainDefinition> Resolve(IEnumerable<TerrainDefinition>? local)
    {
        var definitions = All.AddRange(local ?? []);
        Validate(definitions);
        return definitions;
    }

    public static void Validate(ImmutableArray<TerrainDefinition> definitions)
    {
        if (definitions.IsDefaultOrEmpty) throw new ArgumentException("Terrain catalog cannot be empty.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var d in definitions)
        {
            if (d is null || !ValidKey(d.Id) || string.IsNullOrWhiteSpace(d.Name) || !ids.Add(d.Id) ||
                d.Categories.IsDefault || d.Categories.Any(c => !ValidKey(c)) ||
                d.Categories.Distinct(StringComparer.Ordinal).Count() != d.Categories.Length ||
                d.Presentation is null || !ValidKey(d.Presentation.Recipe))
                throw new ArgumentException("Terrain Types require unique IDs, names, valid categories and a presentation recipe.");
            if (d.Passable && !d.AllowsResourceMarkers)
                throw new ArgumentException("Passable Terrain must allow Resource markers; Transport Resource requires a legal destination for its marker.");
            if (d.LegacyKind is { } legacy && (!Enum.IsDefined(legacy) || Legacy(legacy).Id != d.Id))
                throw new ArgumentException("Only canonical Terrain Types may carry a legacy identity.");
            if (All.SingleOrDefault(c => c.Id == d.Id) is { } canonical &&
                (d.LegacyKind != canonical.LegacyKind || d.Passable != canonical.Passable ||
                 d.BlocksLos != canonical.BlocksLos || d.AllowsResourceMarkers != canonical.AllowsResourceMarkers ||
                 !d.Categories.SequenceEqual(canonical.Categories)))
                throw new ArgumentException("Canonical Terrain gameplay definitions cannot be replaced.");
        }
    }

    internal static bool ValidKey(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.');

    public static TerrainDefinition Legacy(TerrainKind kind) => LegacyDefinitions.TryGetValue(kind, out var definition)
        ? definition : throw new ArgumentException("Unknown legacy Terrain kind.", nameof(kind));

    public static TerrainDefinition Find(IEnumerable<TerrainDefinition> definitions, string id) =>
        definitions.SingleOrDefault(d => d.Id == id) ?? throw new ArgumentException($"Unknown Terrain Type ID '{id}'.");

    internal static bool Accepts(UnitType type, TerrainDefinition terrain) =>
        (type.TerrainRestriction is null || type.TerrainRestriction.Accepts(terrain)) &&
        (type.TerrainRestrictedMovement is null || AcceptsLegacy(type.TerrainRestrictedMovement.Terrain, terrain));

    internal static bool AcceptsLegacy(TerrainKind legacy, TerrainDefinition terrain) => legacy == TerrainKind.Asphalt
        ? terrain.Categories.Contains(TerrainCategories.RacingGround)
        : terrain.LegacyKind == legacy;
}
