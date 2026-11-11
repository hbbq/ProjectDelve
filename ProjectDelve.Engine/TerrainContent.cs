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
    // Existing authored blockers remain opaque unless vision is explicitly different.
    public bool BlocksVision { get; init; } = BlocksLos;
    public ImmutableArray<string> Categories { get; init; } = [];
    public bool AllowsResourceMarkers { get; init; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DestroyedTerrainTypeId { get; init; }
    public TerrainPresentation Presentation { get; init; } = new("generic");
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedTerrainAssembly? Assembly { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnvironmentalProduction? Production { get; init; }
    [JsonIgnore]
    public string? ProductionDescription => EnvironmentalProductionContent.Describe(Production);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyTerrainProductionIdentity? LegacyProductionIdentity { get; init; }
    // Compatibility only for legacy serialization, artwork, Rail and Water rules.
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
    private static ImmutableDictionary<TerrainKind, TerrainDefinition> LegacyDefinitions =>
        TerrainRoster.All.Where(d => d.LegacyKind is not null && d.Id != TerrainCompatibility.OrdinaryLavaId)
            .ToImmutableDictionary(d => d.LegacyKind!.Value);
    public static ImmutableArray<TerrainDefinition> Resolve(IEnumerable<TerrainDefinition>? local,
        IEnumerable<TerrainAssemblyDefinition>? assemblies = null,
        IEnumerable<FixedComponentDefinition>? fixedComponents = null, ContentBundle? canonical = null)
    {
        var localDefinitions = (local ?? []).ToImmutableArray();
        if (localDefinitions.Any(d => d?.Assembly is not null))
            throw new ArgumentException("Author composed Terrain through terrainAssemblies, using surface and component references.");
        if (canonical is not null && localDefinitions.Any(d => d.LegacyKind is not null || d.LegacyProductionIdentity is not null))
            throw new ArgumentException("Scenario-local Terrain cannot claim a legacy identity.");
        var definitions = (canonical?.Terrain ?? All).AddRange(localDefinitions);
        Validate(definitions, againstCanonical: canonical is null);
        var componentDefinitions = FixedComponentCatalog.Resolve(fixedComponents, canonical?.FixedComponents);
        var sources = (assemblies ?? []).ToArray();
        HashSet<string> ids = definitions.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var assembly in sources)
        {
            TerrainAssemblyContent.ValidateAuthoring(assembly);
            if (!ids.Add(assembly.Id)) throw new ArgumentException("Terrain Types and assemblies must have unique IDs.");
        }
        var sourcesById = sources.ToDictionary(a => a.Id, StringComparer.Ordinal);
        foreach (var assembly in sources)
        {
            HashSet<string> path = new(StringComparer.Ordinal) { assembly.Id };
            var next = assembly.SurfaceId;
            while (sourcesById.TryGetValue(next, out var parent))
            {
                if (!path.Add(next)) throw new ArgumentException("Circular Terrain composition is not allowed.");
                next = parent.SurfaceId;
            }
            if (sourcesById.ContainsKey(assembly.SurfaceId))
                throw new ArgumentException("Nested Terrain assemblies are unsupported; reference an atomic surface.");
            definitions = definitions.Add(TerrainAssemblyContent.Compose(assembly,
                Find(definitions, assembly.SurfaceId), componentDefinitions));
        }
        Validate(definitions, againstCanonical: canonical is null);
        ValidateDestructionReferences(definitions);
        if (canonical is not null && definitions.Any(d => d.Production is DirectionalResourceProduction production && !canonical.Resources.Any(r => r.Id == production.ResourceTypeId)))
            throw new ArgumentException("The Resource reference does not resolve in the selected content.");
        return definitions;
    }

    public static void Validate(ImmutableArray<TerrainDefinition> definitions, bool againstCanonical = true)
    {
        if (definitions.IsDefaultOrEmpty) throw new ArgumentException("Terrain catalog cannot be empty.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        Dictionary<string, FixedComponentDefinition> resolvedComponents = new(StringComparer.Ordinal);
        Dictionary<string, TerrainDefinition> resolvedSurfaces = new(StringComparer.Ordinal);
        foreach (var d in definitions)
        {
            if (d is null || !ValidKey(d.Id) || string.IsNullOrWhiteSpace(d.Name) || !ids.Add(d.Id) ||
                d.Categories.IsDefault || d.Categories.Any(c => !ValidKey(c)) ||
                d.Categories.Distinct(StringComparer.Ordinal).Count() != d.Categories.Length ||
                d.Presentation is null || !ValidKey(d.Presentation.Recipe))
                throw new ArgumentException("Terrain Types require unique IDs, names, valid categories and a presentation recipe.");
            if (d.Passable && !d.AllowsResourceMarkers)
                throw new ArgumentException("Passable Terrain must allow Resource markers; Transport Resource requires a legal destination for its marker.");
            if (d.Id == TerrainCompatibility.LegacyLavaId)
                throw new ArgumentException("'lava' is a reserved legacy reference; use ordinary-lava or coal-lava.");
            EnvironmentalProductionContent.Validate(d.Production);
            if (d.DestroyedTerrainTypeId is { } result && (!ValidKey(result) || result == d.Id))
                throw new ArgumentException("Destroyed Terrain must reference a different valid Terrain Type ID.");
            if (againstCanonical && d.Production is DirectionalResourceProduction production && !ResourceTypes.All.Any(r => r.Id == production.ResourceTypeId))
                throw new ArgumentException("The resource reference does not resolve.");
            TerrainAssemblyContent.ValidateResolved(d);
            if (d.Assembly is { } assembly)
            {
                if (resolvedSurfaces.TryGetValue(assembly.Surface.Id, out var previousSurface) && !EquivalentSurface(previousSurface, assembly.Surface))
                    throw new ArgumentException("One Surface Type ID cannot identify different resolved definitions in the same catalog.");
                resolvedSurfaces[assembly.Surface.Id] = assembly.Surface;
                foreach (var component in assembly.Components)
                {
                    if (resolvedComponents.TryGetValue(component.Definition.Id, out var previousComponent) && previousComponent != component.Definition)
                        throw new ArgumentException("One Fixed Component Type ID cannot identify different resolved definitions in the same catalog.");
                    resolvedComponents[component.Definition.Id] = component.Definition;
                }
            }
            if (d.LegacyKind is { } legacy && (!Enum.IsDefined(legacy) || againstCanonical && !All.Any(c => c.Id == d.Id && c.LegacyKind == legacy)))
                throw new ArgumentException("Only canonical Terrain Types may carry a legacy identity.");
            if (d.LegacyProductionIdentity is {} identity && (!Enum.IsDefined(identity) || againstCanonical && !All.Any(c => c.Id == d.Id && c.LegacyProductionIdentity == identity)))
                throw new ArgumentException("Only the corresponding canonical Terrain may use a legacy production event identity.");
            if (againstCanonical && All.SingleOrDefault(c => c.Id == d.Id) is { } canonical &&
                (d.LegacyKind != canonical.LegacyKind || d.Passable != canonical.Passable ||
                 d.BlocksLos != canonical.BlocksLos || d.BlocksVision != canonical.BlocksVision || d.AllowsResourceMarkers != canonical.AllowsResourceMarkers ||
                 d.Production != canonical.Production || d.LegacyProductionIdentity != canonical.LegacyProductionIdentity || d.DestroyedTerrainTypeId != canonical.DestroyedTerrainTypeId ||
                 !d.Categories.SequenceEqual(canonical.Categories)))
                throw new ArgumentException("Canonical Terrain gameplay definitions cannot be replaced.");
        }
        foreach (var surface in resolvedSurfaces.Values)
            if (definitions.SingleOrDefault(d => d.Id == surface.Id) is { } topLevel && !EquivalentSurface(topLevel, surface))
                throw new ArgumentException("A resolved assembly surface must match its catalog definition.");
    }

    private static bool EquivalentSurface(TerrainDefinition a, TerrainDefinition b) =>
        a.Assembly is null && b.Assembly is null && a.Id == b.Id && a.Name == b.Name && a.Passable == b.Passable &&
        a.BlocksLos == b.BlocksLos && a.BlocksVision == b.BlocksVision && a.AllowsResourceMarkers == b.AllowsResourceMarkers &&
        a.Categories.SequenceEqual(b.Categories) && a.Production == b.Production && a.Presentation == b.Presentation &&
        a.LegacyKind == b.LegacyKind && a.LegacyProductionIdentity == b.LegacyProductionIdentity && a.DestroyedTerrainTypeId == b.DestroyedTerrainTypeId;

    public static void ValidateDestructionReferences(IEnumerable<TerrainDefinition> definitions)
    {
        var catalog = definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var definition in catalog.Values)
        {
            HashSet<string> path = new(StringComparer.Ordinal) { definition.Id };
            var current = definition;
            while (current.DestroyedTerrainTypeId is { } id)
            {
                if (!catalog.TryGetValue(id, out var result)) throw new ArgumentException($"Unknown destroyed Terrain Type ID '{id}'.");
                if (!path.Add(id)) throw new ArgumentException("Circular Terrain destruction is not allowed.");
                if (result.LegacyKind == TerrainKind.Rail) throw new ArgumentException("Destroyed Terrain cannot require Rail direction setup.");
                current = result;
            }
        }
    }

    internal static bool ValidKey(string? value) => TerrainContentKey.Valid(value);

    public static TerrainDefinition Legacy(TerrainKind kind) => LegacyDefinitions.TryGetValue(kind, out var definition)
        ? definition : throw new ArgumentException("Unknown legacy Terrain kind.", nameof(kind));

    public static TerrainDefinition Legacy(TerrainKind kind, bool? coalGeneration) =>
        Find(All, TerrainCompatibility.ResolveTypeId(kind, null, coalGeneration));

    public static TerrainDefinition Find(IEnumerable<TerrainDefinition> definitions, string id) =>
        definitions.SingleOrDefault(d => d.Id == id) ?? throw new ArgumentException($"Unknown Terrain Type ID '{id}'.");

    internal static bool Accepts(UnitType type, TerrainDefinition terrain) =>
        (type.TerrainRestriction is null || type.TerrainRestriction.Accepts(terrain)) &&
        (type.TerrainRestrictedMovement is null || AcceptsLegacy(type.TerrainRestrictedMovement.Terrain, terrain));

    internal static bool AcceptsLegacy(TerrainKind legacy, TerrainDefinition terrain) => legacy == TerrainKind.Asphalt
        ? terrain.Categories.Contains(TerrainCategories.RacingGround)
        : terrain.LegacyKind == legacy;
}

internal static class TerrainContentKey
{
    internal static bool Valid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.');
}
