using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Provider data, independent of storage. Empty means empty; only the new-game adapter selects Canonical().
public sealed record ContentBundle
{
    [System.Text.Json.Serialization.JsonPropertyName("unitTypes")]
    public ImmutableArray<UnitType> Units { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("equipmentTypes")]
    public ImmutableArray<EquipmentDefinition> Equipment { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("itemTypes")]
    public ImmutableArray<ItemDefinition> Items { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("lootCardDefinitions")]
    public ImmutableArray<LootCardDefinition> LootCards { get; init; } = [];
    public ImmutableArray<LootPreset> LootPresets { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("worldCardDefinitions")]
    public ImmutableArray<WorldCardDefinition> WorldCards { get; init; } = [];
    public ImmutableArray<WorldCardQuantity> DefaultWorldDeck { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("terrainTypes")]
    public ImmutableArray<TerrainDefinition> Terrain { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("terrainAssemblies")]
    public ImmutableArray<TerrainAssemblyDefinition> Assemblies { get; init; } = [];
    [System.Text.Json.Serialization.JsonPropertyName("fixedComponentTypes")]
    public ImmutableArray<FixedComponentDefinition> FixedComponents { get; init; } = [];
    // Only named production/implicit requirements use this list. Inline Resource/Machine variants stay owned.
    public ImmutableArray<ResourceType> Resources { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public ContentSource? Source { get; init; }

    public static ContentBundle Canonical() => new()
    {
        Units = [.. CanonicalUnitTypes.All], Equipment = [.. EquipmentCatalog.All], Items = [.. ItemCatalog.All],
        LootCards = [.. LootCardCatalog.All], LootPresets = [.. Engine.LootPresets.All],
        WorldCards = [.. WorldCardCatalog.All], DefaultWorldDeck = [.. Engine.WorldCards.ResolveComposition(null)],
        Terrain = TerrainCatalog.All, FixedComponents = FixedComponentCatalog.All, Resources = ResourceTypes.All,
        Source = new(Package: "canonical-csharp")
    };
    internal IEnumerable<ContentDefinition> Definitions()
    {
        IEnumerable<ContentDefinition> Rows<T>(ImmutableArray<T> rows, string field)
        {
            if (rows.IsDefault) throw new ArgumentException($"{field} definitions must be an array.");
            var prefix = string.IsNullOrEmpty(Source?.Path) ? "" : Source.Path + ".";
            return rows.Select((value, index) => new ContentDefinition(value!,
                new(Source?.Document, Source?.Package, $"{prefix}{field}[{index}]")));
        }
        return Rows(Units, "unitTypes").Concat(Rows(Equipment, "equipmentTypes")).Concat(Rows(Items, "itemTypes"))
            .Concat(Rows(LootCards, "lootCardDefinitions")).Concat(Rows(LootPresets, "lootPresets"))
            .Concat(Rows(WorldCards, "worldCardDefinitions")).Concat(Rows(Terrain, "terrainTypes"))
            .Concat(Rows(Assemblies, "terrainAssemblies")).Concat(Rows(FixedComponents, "fixedComponentTypes"));
    }
    internal static ContentBundle Local(ScenarioDefinition scenario) => new()
    {
        Units = [.. scenario.UnitTypes ?? []], Equipment = [.. scenario.EquipmentTypes ?? []], Items = [.. scenario.ItemTypes ?? []],
        LootCards = [.. scenario.LootCardDefinitions ?? []], WorldCards = [.. scenario.WorldCardDefinitions ?? []],
        Terrain = [.. scenario.TerrainTypes ?? []], Assemblies = [.. scenario.TerrainAssemblies ?? []],
        FixedComponents = [.. scenario.FixedComponentTypes ?? []], Source = new(Package: "scenario-local")
    };
}

public sealed class ContentCompositionException(ImmutableArray<ContentDiagnostic> diagnostics)
    : ArgumentException(string.Join(" ", diagnostics.Select(d => $"{d.Owner.Family} '{d.Owner.TypeId}' {d.Path}: {d.Message}")))
{
    public ImmutableArray<ContentDiagnostic> Diagnostics { get; } = diagnostics;
}

public sealed class EffectiveContent
{
    public ContentBundle Definitions { get; }
    public ContentDependencyGraph Dependencies { get; }
    internal EffectiveContent(ContentBundle definitions, ContentDependencyGraph dependencies)
        => (Definitions, Dependencies) = (definitions, dependencies);
}

public static class ContentComposition
{
    public static void ValidateSnapshot(GameState state)
    {
        if (state.ResourceDefinitions.Any(r => !ResourceRules.ValidType(r)) ||
            state.ResourceDefinitions.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != state.ResourceDefinitions.Count)
            throw new ArgumentException("Invalid named Resource definitions in content snapshot.");
        var bundle = new ContentBundle
        {
            Units = [.. state.Types], Equipment = [.. state.EquipmentTypes], Items = [.. state.ItemTypes],
            LootCards = [.. state.LootCardDefinitions], WorldCards = [.. state.WorldCardDefinitions],
            Terrain = state.Physical.Board.TerrainTypes
        };
        var definitions = bundle.Definitions();
        if (state.WorldEffects?.Composition is {} worldComposition)
            definitions = definitions.Append(new(new WorldDeckDefinition("default-world-deck", [.. worldComposition]), new(Path: "worldEffects")));
        var graph = new ContentDependencyGraph(definitions, state.ResourceDefinitions);
        if (!graph.Diagnostics.IsEmpty)
        {
            var error = new ArgumentException(string.Join(" ", graph.Diagnostics.Select(d => d.Message)));
            error.Data["ProjectDelve.ContentDiagnostics"] = graph.Diagnostics;
            var first = graph.Diagnostics[0];
            throw ScenarioValidationContext.At(error, (first.Source?.Path ?? "") + (first.Path.Length == 0 ? "" : "." + first.Path));
        }
        if (state.ActivationTypeOrder is {} order && (order.Distinct(StringComparer.Ordinal).Count() != order.Count ||
            order.Any(id => !state.Types.Any(type => type.Id == id))))
            throw new ArgumentException("Activation Type ordering must include every participating Unit Type exactly once.");
        foreach (var machine in state.Physical.Board.Machines) ContentDefinitions.ValidateStructure(machine.Type);
    }

    public static EffectiveContent Compose(IEnumerable<ContentBundle> sources, ContentBundle? scenarioLocal = null)
    {
        var bundles = sources.ToArray();
        var allBundles = scenarioLocal is null ? bundles : [.. bundles, scenarioLocal];
        if (allBundles.Any(b => b.Resources.IsDefault || b.DefaultWorldDeck.IsDefault))
            throw new ArgumentException("Resource definitions and default World composition must be arrays.");
        var supplied = bundles.SelectMany(b => b.Definitions()).ToList();
        var duplicate = supplied.GroupBy(d => d.Identity).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ContentCompositionException([new(duplicate.Key, "", "conflictingDefinition", "Providers contain conflicting identities.",
                duplicate.First().Source, duplicate.Key, duplicate.Skip(1).First().Source)]);
        if (scenarioLocal is {} local)
        {
            var reservedTerrain = local.Terrain.FirstOrDefault(t => t.LegacyKind is not null || t.LegacyProductionIdentity is not null || t.Assembly is not null);
            if (reservedTerrain is not null)
                throw new ContentCompositionException([new(new(ContentFamily.Terrain, reservedTerrain.Id), "legacyKind", "invalidDefinition",
                    "Scenario-local Terrain cannot claim legacy identities or embed resolved assemblies; author surface/component references.", local.Source)]);
            // Only the established scenario-local Unit override is permitted.
            var overridden = local.Units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
            supplied.RemoveAll(d => d.Identity.Family == ContentFamily.Unit && overridden.Contains(d.Identity.TypeId));
            supplied.AddRange(local.Definitions());
        }
        var resources = allBundles.SelectMany(b => b.Resources).ToImmutableArray();
        Dictionary<string, ContentSource> resourceSources = new(StringComparer.Ordinal);
        foreach (var bundle in allBundles)
        for (var index = 0; index < bundle.Resources.Length; index++)
        {
            var resource = bundle.Resources[index];
            var source = new ContentSource(bundle.Source?.Document, bundle.Source?.Package, $"{(string.IsNullOrEmpty(bundle.Source?.Path) ? "" : bundle.Source.Path + ".")}resources[{index}]");
            if (!ResourceRules.ValidType(resource))
                throw new ContentCompositionException([new(new(ContentFamily.Resource, resource?.Id ?? ""), "", "invalidDefinition",
                    "A named Resource reference requires an ID and name.", source)]);
            if (!resourceSources.TryAdd(resource.Id, source))
                throw new ContentCompositionException([new(new(ContentFamily.Resource, resource.Id), "", "conflictingDefinition",
                    "Named Resource references must be unambiguous; inline variants remain owned payloads.", source,
                    new(ContentFamily.Resource, resource.Id), resourceSources[resource.Id])]);
        }
        var defaultWorldDeck = allBundles.SelectMany(b => b.DefaultWorldDeck).ToImmutableArray();
        var deckSource = allBundles.FirstOrDefault(b => !b.DefaultWorldDeck.IsEmpty)?.Source;
        supplied.Add(new(new WorldDeckDefinition("default-world-deck", defaultWorldDeck),
            new(deckSource?.Document, deckSource?.Package, "defaultWorldDeck")));
        var graph = new ContentDependencyGraph(supplied, resources);
        if (!graph.Diagnostics.IsEmpty) throw new ContentCompositionException(graph.Diagnostics);
        var terrain = supplied.Select(d => d.Value).OfType<TerrainDefinition>().ToImmutableArray();
        var components = supplied.Select(d => d.Value).OfType<FixedComponentDefinition>().ToImmutableArray();
        foreach (var assembly in supplied.Select(d => d.Value).OfType<TerrainAssemblyDefinition>())
            terrain = terrain.Add(TerrainAssemblyContent.Compose(assembly, TerrainCatalog.Find(terrain, assembly.SurfaceId), components));
        if (!terrain.IsEmpty) TerrainCatalog.Validate(terrain, againstCanonical: false);
        foreach (var tile in terrain.Where(t => t.Assembly is not null))
        foreach (var component in tile.Assembly!.Components)
        {
            var declared = components.SingleOrDefault(c => c.Id == component.Definition.Id);
            if (declared is not null && declared != component.Definition)
            {
                var owner = supplied.Single(d => d.Identity == new ContentIdentity(ContentFamily.Terrain, tile.Id));
                var target = supplied.Single(d => d.Identity == new ContentIdentity(ContentFamily.FixedComponent, declared.Id));
                throw new ContentCompositionException([new(owner.Identity, "assembly.components", "conflictingDefinition",
                    "An embedded Fixed Component conflicts with its supplied definition.", owner.Source, target.Identity, target.Source)]);
            }
        }
        var result = new ContentBundle
        {
            Units = [.. supplied.Select(d => d.Value).OfType<UnitType>()],
            Equipment = [.. supplied.Select(d => d.Value).OfType<EquipmentDefinition>()],
            Items = [.. supplied.Select(d => d.Value).OfType<ItemDefinition>()],
            LootCards = [.. supplied.Select(d => d.Value).OfType<LootCardDefinition>()],
            LootPresets = [.. supplied.Select(d => d.Value).OfType<LootPreset>()],
            WorldCards = [.. supplied.Select(d => d.Value).OfType<WorldCardDefinition>()],
            DefaultWorldDeck = defaultWorldDeck, Terrain = terrain,
            // Effective Terrain already embeds its resolved constituents. Do not register both
            // an authored assembly and its materialized Terrain under the same identity.
            Assemblies = [], FixedComponents = components, Resources = resources
        };
        return new(result, graph);
    }
}
