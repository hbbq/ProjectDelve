using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Layers describe presentation, never occupancy or LOS. The initial renderer
// supports at most four components, including at most one raised model.
public enum FixedComponentLayer { Object, Decal }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FixedComponentPresentation(
    [property: JsonRequired] string Recipe,
    FixedComponentLayer Layer = FixedComponentLayer.Object);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FixedComponentDefinition(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool BlocksMovement,
    [property: JsonRequired] bool BlocksLos)
{
    public FixedComponentPresentation Presentation { get; init; } = new("generic");
}

public static class FixedComponentCatalog
{
    public static ImmutableArray<FixedComponentDefinition> All => FixedComponentRoster.All;

    public static ImmutableArray<FixedComponentDefinition> Resolve(IEnumerable<FixedComponentDefinition>? local)
    {
        var definitions = All.AddRange(local ?? []);
        Validate(definitions);
        return definitions;
    }

    public static void Validate(ImmutableArray<FixedComponentDefinition> definitions)
    {
        if (definitions.IsDefault) throw new ArgumentException("Fixed Component definitions must be an array.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition is null || !TerrainContentKey.Valid(definition.Id) || !ids.Add(definition.Id) ||
                string.IsNullOrWhiteSpace(definition.Name) || definition.Presentation is null ||
                !TerrainContentKey.Valid(definition.Presentation.Recipe) || !Enum.IsDefined(definition.Presentation.Layer))
                throw new ArgumentException("Fixed Components require unique IDs, names and valid presentation recipes and layers.");
            if (All.SingleOrDefault(c => c.Id == definition.Id) is { } canonical &&
                (definition.BlocksMovement != canonical.BlocksMovement || definition.BlocksLos != canonical.BlocksLos))
                throw new ArgumentException("Canonical Fixed Component gameplay properties cannot be replaced.");
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FixedComponentPlacement(
    [property: JsonRequired] string Slot,
    [property: JsonRequired] string ComponentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? QuarterTurns = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TerrainAssemblyDefinition(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string SurfaceId)
{
    public ImmutableArray<FixedComponentPlacement> Components { get; init; } = [];
}

// Resolved snapshots contain their constituents. Rendering, replay and observed
// state never need a second catalog lookup or undiscovered component definitions.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolvedFixedComponent(
    [property: JsonRequired] string Slot,
    [property: JsonRequired] FixedComponentDefinition Definition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? QuarterTurns = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolvedTerrainAssembly(
    [property: JsonRequired] TerrainDefinition Surface,
    [property: JsonRequired] ImmutableArray<ResolvedFixedComponent> Components);

internal static class TerrainAssemblyContent
{
    internal static void ValidateAuthoring(TerrainAssemblyDefinition assembly)
    {
        if (assembly is null || !TerrainContentKey.Valid(assembly.Id) || string.IsNullOrWhiteSpace(assembly.Name) ||
            !TerrainContentKey.Valid(assembly.SurfaceId) || assembly.Components.IsDefault || assembly.Components.Length > 4 ||
            assembly.Components.Any(c => c is null || !TerrainContentKey.Valid(c.Slot) ||
                !TerrainContentKey.Valid(c.ComponentId) || c.QuarterTurns is < 0 or > 3) ||
            assembly.Components.Select(c => c.Slot).Distinct(StringComparer.Ordinal).Count() != assembly.Components.Length)
            throw new ArgumentException("Terrain assemblies require IDs, names, a surface and at most four uniquely named component slots with quarter-turns from 0 through 3.");
    }

    internal static TerrainDefinition Compose(TerrainAssemblyDefinition assembly, TerrainDefinition surface,
        IEnumerable<FixedComponentDefinition> components)
    {
        ValidateAuthoring(assembly);
        ValidateSurface(surface);
        var definitions = components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var resolved = assembly.Components.Select(c => new ResolvedFixedComponent(c.Slot,
            definitions.GetValueOrDefault(c.ComponentId) ?? throw new ArgumentException($"Unknown Fixed Component Type ID '{c.ComponentId}'."), c.QuarterTurns)).ToImmutableArray();
        ValidatePlacements(resolved);
        return new(assembly.Id, assembly.Name, surface.Passable && !resolved.Any(c => c.Definition.BlocksMovement),
            surface.BlocksLos || resolved.Any(c => c.Definition.BlocksLos))
        {
            Categories = surface.Categories,
            AllowsResourceMarkers = surface.AllowsResourceMarkers,
            Production = surface.Production,
            Assembly = new(surface, resolved)
        };
    }

    internal static void ValidateResolved(TerrainDefinition terrain)
    {
        if (terrain.Assembly is not { } assembly) return;
        ValidateSurface(assembly.Surface);
        TerrainCatalog.Validate([assembly.Surface]);
        ValidatePlacements(assembly.Components);
        if (terrain.Passable != (assembly.Surface.Passable && !assembly.Components.Any(c => c.Definition.BlocksMovement)) ||
            terrain.BlocksLos != (assembly.Surface.BlocksLos || assembly.Components.Any(c => c.Definition.BlocksLos)) ||
            terrain.AllowsResourceMarkers != assembly.Surface.AllowsResourceMarkers ||
            !terrain.Categories.SequenceEqual(assembly.Surface.Categories) || terrain.Production != assembly.Surface.Production)
            throw new ArgumentException("Resolved Terrain properties must match the declared surface and Fixed Components.");
    }

    private static void ValidateSurface(TerrainDefinition surface)
    {
        if (surface is null || surface.Assembly is not null)
            throw new ArgumentException("An assembly surface must be one atomic Terrain definition; nested assemblies and circular composition are unsupported.");
        if (surface.LegacyKind is TerrainKind.Water or TerrainKind.Rail or TerrainKind.Tree or
            TerrainKind.StoneFloorWithTable or TerrainKind.FruitTree or TerrainKind.SolidRock)
            throw new ArgumentException("This canonical Terrain has specialized compatibility or an existing fixed object and cannot be an assembly surface.");
    }

    private static void ValidatePlacements(ImmutableArray<ResolvedFixedComponent> components)
    {
        if (components.IsDefault || components.Length > 4 ||
            components.Any(c => c is null || !TerrainContentKey.Valid(c.Slot) || c.Definition is null || c.QuarterTurns is < 0 or > 3) ||
            components.Select(c => c.Slot).Distinct(StringComparer.Ordinal).Count() != components.Length)
            throw new ArgumentException("Resolved Fixed Components require at most four distinct slots and valid orientations.");
        foreach (var component in components) FixedComponentCatalog.Validate([component.Definition]);
        if (components.Count(c => c.Definition.Presentation.Layer == FixedComponentLayer.Object) > 1)
            throw new ArgumentException("A Terrain tile supports one raised Object component; additional components must use the flat Decal presentation layer.");
    }
}
