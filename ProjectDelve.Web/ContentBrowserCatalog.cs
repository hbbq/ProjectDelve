using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record ContentBrowserContext(string Kind, string Name);
public sealed record ContentBrowserCategory(string Id, string Name, int Count);
public sealed record ContentBrowserDiagnostic(string Message, string? Path = null, string? Code = null);
public sealed record ContentBrowserFailure(ContentBrowserContext Context, IReadOnlyList<ContentBrowserDiagnostic> Diagnostics);
public sealed record ContentBrowserResponse(ContentBrowserContext Context, IReadOnlyList<ContentBrowserCategory> Categories,
    IReadOnlyList<ContentBrowserEntry> Entries, IReadOnlyList<ContentBrowserDiagnostic> Diagnostics)
{
    public UnitAuthoringContract UnitAuthoring => UnitAuthoringSchema.Contract;
    // Payment support is independent of the deferred interactive cost editor.
    public ImmutableArray<AbilityCostDescriptor> Costs => CostCatalog.All;
    public bool CostExecutionAvailable => true;
    public bool CostEditingAvailable => false;
}
public sealed record ContentBrowserField(string Label, JsonElement Value);
public sealed record ContentBrowserSection(string Title, IReadOnlyList<ContentBrowserField> Fields);
public sealed record ContentBrowserReference(string Label, string TargetKey, string Family, string Id, bool Exists = false)
{
    // Inline types can retain the same identity with different presentation/data.
    // This is only used while selecting the matching catalog entry for the link.
    [JsonIgnore] public string? DefinitionIdentity { get; init; }
}
public sealed record ContentBrowserAbility(string Key, string Name, string Timing, AbilityUsage Usage,
    string MechanicId, JsonElement Parameters, string Description, string OwnerKey);
public sealed record ContentBrowserEntry(string Key, string Family, string Id, string Name, string Origin, string Summary,
    IReadOnlyList<ContentBrowserSection> Sections, IReadOnlyList<ContentBrowserAbility> Abilities,
    IReadOnlyList<ContentBrowserReference> References, JsonElement Definition)
{
    public JsonElement? AuthoredDefinition { get; init; }
    public MechanicDescriptor? Mechanic { get; init; }
    public string? Availability { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Published { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PackageId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? PackageRevision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReferenceDigest { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReferencePackageId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReferenceLibraryId { get; init; }
}

// A definition-only projection of the same catalogs used by scenario creation.
// It contains no physical instances, board positions, choices, counters or game snapshots.
public static class ContentBrowserCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly (string Id, string Name)[] Families =
    [
        ("units", "Units"), ("equipment", "Equipment"), ("items", "Items"),
        ("lootCards", "Loot Cards"), ("lootPresets", "Loot Presets"),
        ("worldCards", "World Cards"), ("worldDecks", "World Decks"),
        ("terrain", "Terrain"), ("fixedComponents", "Fixed Components"),
        ("terrainAssemblies", "Terrain Assemblies"), ("mechanics", "Mechanics"),
        ("resources", "Resources"), ("machines", "Machines"), ("cellMarkers", "Cell Markers")
    ];

    public static ContentBrowserResponse Canonical(ContentBundle? canonical = null) => Build(null, null, canonical ?? ContentBundle.Canonical());

    public static ContentBrowserResponse WorkspaceOrigins(ContentBrowserResponse catalog, ContentBundle workspace, ContentBundle canonical)
    {
        var owned = ContentWorkspace.Identities(workspace).ToHashSet();
        var canonicalIds = ContentWorkspace.Identities(canonical).ToHashSet();
        return catalog with { Entries = catalog.Entries.Select(entry =>
        {
            if (entry.Origin != "canonical") return entry;
            var family = entry.Family switch
            {
                "units" => ContentFamily.Unit, "equipment" => ContentFamily.Equipment, "items" => ContentFamily.Item,
                "lootCards" => ContentFamily.LootCard, "lootPresets" => ContentFamily.LootPreset, "worldCards" => ContentFamily.WorldCard,
                "worldDecks" => ContentFamily.WorldDeck, "terrain" or "terrainAssemblies" => ContentFamily.Terrain,
                "fixedComponents" => ContentFamily.FixedComponent, "resources" => ContentFamily.Resource, _ => (ContentFamily?)null
            };
            if (family is null) return entry;
            var identity = new ContentIdentity(family.Value, family == ContentFamily.WorldDeck && entry.Id == "default" ? "default-world-deck" : entry.Id);
            return owned.Contains(identity) ? entry with { Origin = canonicalIds.Contains(identity) ? "workspace-override" : "workspace" } : entry;
        }).ToArray() };
    }

    // Resolved, validated dependencies only: no scenario or runtime state.
    public static ContentBrowserResponse Definitions(IReadOnlyList<EquipmentDefinition> equipment,
        IReadOnlyList<ItemDefinition> items, IReadOnlyList<LootCardDefinition> loot,
        IReadOnlyList<UnitType>? units = null, IReadOnlySet<string>? localUnitIds = null, ContentBundle? canonical = null)
    {
        canonical ??= ContentBundle.Canonical();
        units ??= canonical.Units;
        List<ContentBrowserEntry> entries = [];
        foreach (var type in units)
            entries.Add(Entry("units", type.Id, type.DisplayName ?? type.Id, type,
                localUnitIds?.Contains(type.Id) == true ? !canonical.Units.Any(unit => unit.Id == type.Id) ? "scenario-local" : "scenario-override" : "canonical",
                $"MOV {type.Mov} · RNG {type.Rng} · ATK {type.Atk} · DEF {type.Def} · HP {type.Hp}. {AbilitySummary(type.LocalAbilities.Length)}",
                type.LocalAbilities, AbilityReferences(type.LocalAbilities).Concat(StorageReferences(type.ResourceStorage)), units) with { Sections = UnitSections(type) });
        entries.AddRange(equipment.Select(type => EquipmentEntry(type, canonical.Equipment.Any(row => row.Id == type.Id) ? "canonical" : "local", units)));
        entries.AddRange(items.Select(type => ItemEntry(type, canonical.Items.Any(row => row.Id == type.Id) ? "canonical" : "local", units)));
        entries.AddRange(loot.Select((type, index) => LootEntry(type, index < canonical.LootCards.Length ? "canonical" : "local") with
        {
            Availability = index >= canonical.LootCards.Length && canonical.LootCards.Any(row => row.Id == type.Id)
                ? "legacy-collision" : null
        }));
        entries.AddRange(MechanicCatalog.All.Select(MechanicEntry));
        var resources = canonical.Resources.Concat(units.SelectMany(type => type.ResourceStorage?.Capacities.Select(c => c.Type) ?? []))
            .Concat(units.SelectMany(type => type.LocalAbilities.Select(ability => ability.Mechanic)
            .OfType<WaypointSlotMachine>().SelectMany(slot => slot.OutputTable.Faces))).DistinctBy(type => Element(type).GetRawText());
        entries.AddRange(resources.Select(type => Entry("resources", type.Id, string.IsNullOrWhiteSpace(type.Name) ? type.Id : type.Name,
            type, canonical.Resources.Contains(type) ? "canonical" : "scenario-override")));
        entries = MarkPublications(ResolveReferences(entries), canonical);
        return new(new("definitions", "Draft content"),
            Families.Where(family => family.Id is "units" or "equipment" or "items" or "lootCards" or "mechanics" or "resources")
                .Select(family => new ContentBrowserCategory(family.Id, family.Name, entries.Count(entry => entry.Family == family.Id))).ToArray(), entries, []);
    }

    public static ContentBrowserResponse Scenario(ScenarioDefinition definition, ContentBundle? canonical = null)
    {
        // This state is detached, remains before Round Start, and is never returned.
        canonical ??= ContentBundle.Canonical();
        var state = GameEngine.CreateGame(definition, canonical);
        var provider = ScenarioContentReferences.Compose(canonical, definition.ContentReferences).Definitions;
        return Pinned(Build(definition, state, provider), definition.ContentReferences, definition.UnitTypes?.Select(u => u.Id).ToHashSet());
    }

    public static ContentBrowserResponse Pinned(ContentBrowserResponse catalog, IReadOnlyList<ScenarioContentReference>? references,
        IReadOnlySet<string>? localUnits = null)
    {
        if (references is null || references.Count == 0) return catalog;
        var families = new Dictionary<string, ContentFamily> { ["equipment"] = ContentFamily.Equipment, ["lootCards"] = ContentFamily.LootCard,
            ["items"] = ContentFamily.Item, ["units"] = ContentFamily.Unit, ["resources"] = ContentFamily.Resource, ["terrain"] = ContentFamily.Terrain };
        return catalog with { Entries = catalog.Entries.Select(entry =>
        {
            if (!families.TryGetValue(entry.Family, out var family)) return entry;
            var identity = new ContentIdentity(family, entry.Id);
            var pin = references.FirstOrDefault(r => r.Origins.Any(o => o.Identity == identity));
            var origin = pin?.Origins.FirstOrDefault(o => o.Identity == identity);
            if (pin is null || origin is null) return entry;
            return entry with { Origin = family == ContentFamily.Unit && localUnits?.Contains(entry.Id) == true ? "scenario-override" : "library-pinned",
                PackageId = origin.PackageId, PackageRevision = origin.Revision, ReferenceDigest = pin.SnapshotDigest,
                ReferencePackageId = pin.PackageId, ReferenceLibraryId = pin.LibraryId };
        }).ToArray() };
    }

    private static ContentBrowserResponse Build(ScenarioDefinition? source, GameState? state, ContentBundle canonical)
    {
        List<ContentBrowserEntry> entries = [];
        var units = state?.Types.ToList() ?? canonical.Units.ToList();
        var declaredUnitIds = (source?.UnitTypeIds ?? units.Select(type => type.Id).ToList()).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<EquipmentDefinition> equipment = state is null ? canonical.Equipment : state.EquipmentTypes;
        IReadOnlyList<ItemDefinition> items = state is null ? canonical.Items : state.ItemTypes;
        // New-game composition has already captured transitive Summoning dependencies.

        IReadOnlyList<LootCardDefinition> loot = state is null ? canonical.LootCards : state.LootCardDefinitions;
        IReadOnlyList<WorldCardDefinition> world = state is null ? canonical.WorldCards : state.WorldCardDefinitions;
        var terrain = state?.Physical.Board.TerrainTypes ?? canonical.Terrain;
        var components = FixedComponentCatalog.Resolve(source?.FixedComponentTypes, canonical.FixedComponents);
        var assemblies = canonical.Terrain.Where(t => t.Assembly is not null).Select(t => new TerrainAssemblyDefinition(t.Id, t.Name, t.Assembly!.Surface.Id) { Components = [.. t.Assembly.Components.Select(c => new FixedComponentPlacement(c.Slot, c.Definition.Id, c.QuarterTurns))] }).Concat(source?.TerrainAssemblies ?? []).ToArray();

        foreach (var type in units)
        {
            var authored = source?.UnitTypes?.SingleOrDefault(t => t.Id == type.Id);
            var lootOverride = source?.LootUnitOverrides?.GetValueOrDefault(type.Id);
            var origin = authored is null && lootOverride is null ? "canonical" :
                !canonical.Units.Any(unit => unit.Id == type.Id) ? "scenario-local" : "scenario-override";
            var refs = AbilityReferences(type.LocalAbilities).ToList();
            if (type.ResourceStorage is { } storage) refs.AddRange(StorageReferences(storage));
            if (type.TerrainRestrictedMovement is { } restriction)
                refs.Add(Reference("Restricted terrain", "terrain", TerrainCatalog.Legacy(restriction.Terrain).Id));
            var entry = Entry("units", type.Id, type.DisplayName ?? type.Id, type, origin,
                $"MOV {type.Mov} · RNG {type.Rng} · ATK {type.Atk} · DEF {type.Def} · HP {type.Hp}. {AbilitySummary(type.LocalAbilities.Length)}",
                type.LocalAbilities, refs, units) with
            {
                Availability = source is null ? "canonical" : declaredUnitIds.Contains(type.Id) ? "declared" : "referenced",
                Sections = UnitSections(type),
                AuthoredDefinition = lootOverride is not null ? Element(new { unitType = authored ?? UnitContent.Find(type.Id), lootOverride })
                    : authored is not null ? Element(authored) : null
            };
            entries.Add(entry);
        }
        foreach (var type in equipment)
            entries.Add(EquipmentEntry(type, Local(source?.EquipmentTypes?.Any(t => t.Id == type.Id) == true), units));
        foreach (var type in items)
            entries.Add(ItemEntry(type, Local(source?.ItemTypes?.Any(t => t.Id == type.Id) == true), units));
        foreach (var type in loot)
            entries.Add(LootEntry(type, Local(source?.LootCardDefinitions?.Any(card => card.Id == type.Id) == true)));
        foreach (var preset in canonical.LootPresets)
        {
            var composition = LootPresets.Resolve(preset.Id, LootCardCatalog.Effective(source?.LootCardDefinitions, canonical.LootCards), canonical.LootPresets);
            entries.Add(Entry("lootPresets", preset.Id, preset.Name, preset with { Composition = composition },
                preset.IsDynamic && source is not null ? "resolved" : "canonical", preset.Description,
                references: LootReferences(new LootSettings { Composition = composition.ToList() }.ResolveObjects(loot))));
        }
        if (source is not null && state is not null)
            entries.Add(Entry("lootPresets", "scenario", "Scenario Loot composition",
                new { composition = ResolvedLootComposition(state.LootDeck!.DrawPile) }, "resolved", "Resolved initial Loot composition.",
                references: LootReferences(state.LootDeck.DrawPile)) with { AuthoredDefinition = Element(source.Loot ?? LootSettings.Empty) });
        var scenarioWorldComposition = source?.WorldEffects is { } worldSettings ? WorldCards.ResolveComposition(worldSettings with { Composition = worldSettings.Composition ?? canonical.DefaultWorldDeck }, world) : null;
        foreach (var type in world)
        {
            var references = MechanicReferences(type.Immediate ?? (object)type.Modifiers).ToList();
            var defaultCount = canonical.DefaultWorldDeck.SingleOrDefault(row => row.TypeId == type.Id)?.Count ?? 0;
            List<ContentBrowserField> quantities = [new("Default World Deck", Element(defaultCount))];
            if (defaultCount > 0) references.Add(Reference("Default World Deck", "worldDecks", "default"));
            if (scenarioWorldComposition is not null)
            {
                var count = scenarioWorldComposition.SingleOrDefault(row => row.TypeId == type.Id)?.Count ?? 0;
                quantities.Add(new("Scenario World Deck", Element(count)));
                if (count > 0) references.Add(Reference("Scenario World Deck", "worldDecks", "scenario"));
            }
            var entry = Entry("worldCards", type.Id, type.Name, type, Local(source?.WorldCardDefinitions?.Any(t => t.Id == type.Id) == true),
                type.Description, references: references);
            entries.Add(entry with { Sections = [.. entry.Sections, new("Deck quantities", quantities)] });
        }
        entries.Add(Entry("worldDecks", "default", "Default World Deck", new { composition = canonical.DefaultWorldDeck },
            "canonical", "Canonical World Card quantities.", references: WorldReferences(canonical.DefaultWorldDeck)));
        if (source?.WorldEffects is { } settings)
            entries.Add(Entry("worldDecks", "scenario", "Scenario World Deck", settings with
                { Composition = scenarioWorldComposition }, "resolved", "Resolved initial World Deck settings.",
                references: WorldReferences(scenarioWorldComposition!)) with { AuthoredDefinition = Element(settings) });
        foreach (var type in terrain)
        {
            var authoredAssembly = assemblies.SingleOrDefault(a => a.Id == type.Id);
            List<ContentBrowserReference> refs = [];
            if (type.Production is { } production) refs.AddRange(MechanicReferences(production));
            if (authoredAssembly is not null) refs.Add(Reference("Assembly declaration", "terrainAssemblies", type.Id));
            var local = source?.TerrainTypes?.Any(t => t.Id == type.Id) == true || source?.TerrainAssemblies?.Any(t => t.Id == type.Id) == true;
            entries.Add(Entry("terrain", type.Id, type.Name, type, Local(local), EnvironmentalProductionContent.Describe(type.Production, canonical.Resources) ?? "", references: refs)
                with
                {
                    AuthoredDefinition = authoredAssembly is not null ? Element(authoredAssembly) : null,
                    Sections = TerrainSections(type, canonical.Resources)
                });
        }
        foreach (var type in components)
            entries.Add(Entry("fixedComponents", type.Id, type.Name, type,
                Local(source?.FixedComponentTypes?.Any(t => t.Id == type.Id) == true)));
        foreach (var assembly in assemblies)
        {
            var entry = Entry("terrainAssemblies", assembly.Id, assembly.Name, assembly,
                Local(source?.TerrainAssemblies?.Any(t => t.Id == assembly.Id) == true), references:
                new[] { Reference("Surface", "terrain", assembly.SurfaceId), Reference("Resolved Terrain", "terrain", assembly.Id) }
                    .Concat(assembly.Components.Select(c => Reference($"Component: {c.Slot}", "fixedComponents", c.ComponentId))));
            var resolved = terrain.Single(type => type.Id == assembly.Id);
            entries.Add(entry with
            {
                Sections = [.. entry.Sections, new("Resolved Terrain", Fields(Element(new
                {
                    resolved.Passable, resolved.BlocksLos, resolved.AllowsResourceMarkers, resolved.Categories,
                    resolved.Production, productionDescription = EnvironmentalProductionContent.Describe(resolved.Production, canonical.Resources), resolved.Presentation
                })))]
            });
        }
        foreach (var mechanic in MechanicCatalog.All)
            entries.Add(MechanicEntry(mechanic));

        var machineTypes = MachineTypes.All.Concat(source?.Machines?.Select(m => m.Type) ?? []).DistinctBy(type => Element(type).GetRawText()).ToList();
        var resourceTypes = canonical.Resources.Concat(units.SelectMany(type => type.ResourceStorage?.Capacities.Select(c => c.Type) ?? []))
            .Concat(units.SelectMany(type => type.LocalAbilities.Select(a => a.Mechanic).OfType<WaypointSlotMachine>().SelectMany(slot => slot.OutputTable.Faces)))
            .Concat(machineTypes.SelectMany(type => type.Storage.Capacities.Select(c => c.Type)
                .Concat(type.Operation.Costs.Select(c => c.Type)).Concat(type.Operation.OutputTable?.Faces ?? [])
                .Concat(type.Operation.OutputType is { } output ? [output] : [])))
            .Concat(source?.Resources?.Select(resource => resource.Type) ?? [])
            .Concat(source?.StoredResources?.Select(resource => resource.Type) ?? [])
            .DistinctBy(type => Element(type).GetRawText());
        foreach (var type in resourceTypes)
            entries.Add(Entry("resources", type.Id, string.IsNullOrWhiteSpace(type.Name) ? type.Id : type.Name, type, canonical.Resources.Contains(type) ? "canonical" :
                canonical.Resources.Any(row => row.Id == type.Id) ? "scenario-override" : "scenario-local"));
        foreach (var type in machineTypes)
        {
            var refs = StorageReferences(type.Storage).Concat(type.Operation.Costs.Select(c => ResourceReference("Input", c.Type)))
                .Concat((type.Operation.OutputTable?.Faces ?? []).Select(r => ResourceReference("Output", r)));
            if (type.Operation.OutputType is { } output) refs = refs.Append(ResourceReference("Output", output));
            entries.Add(Entry("machines", type.Id, type.Name, type, MachineTypes.All.Contains(type) ? "canonical" :
                MachineTypes.All.Any(row => row.Id == type.Id) ? "scenario-override" : "scenario-local",
                type.Operation.OutputDescription, references: refs));
        }
        foreach (var type in CellMarkerTypes.All)
            entries.Add(Entry("cellMarkers", type.Id, type.Name, type, "canonical", CellMarkerTypes.Describe(type)));

        entries = MarkPublications(ResolveReferences(entries), canonical);
        return new(new(source is null ? "canonical" : "scenario", source is null ? "Canonical content" : "Scenario context"),
            Families.Select(family => new ContentBrowserCategory(family.Id, family.Name, entries.Count(e => e.Family == family.Id))).ToArray(), entries, []);
    }

    private static List<ContentBrowserEntry> ResolveReferences(List<ContentBrowserEntry> entries)
    {
        // Inline Machine/Resource definitions can share an ID while carrying
        // distinct values. Preserve each definition rather than dropping one.
        HashSet<string> keys = new(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var key = entry.Key;
            for (var variant = 2; !keys.Add(key); variant++) key = $"{entry.Key}:variant:{variant}";
            entries[index] = entry with { Key = key };
        }
        return entries.Select(entry => entry with
        {
            References = entry.References.DistinctBy(reference => (reference.Label, reference.TargetKey, reference.DefinitionIdentity))
                .Select(reference =>
                {
                    if (reference.DefinitionIdentity is null) return reference with { Exists = keys.Contains(reference.TargetKey) };
                    var target = entries.FirstOrDefault(candidate => candidate.Family == reference.Family && candidate.Id == reference.Id &&
                        candidate.Definition.GetRawText() == reference.DefinitionIdentity);
                    return reference with { TargetKey = target?.Key ?? reference.TargetKey, Exists = target is not null };
                }).ToArray()
        }).ToList();
    }

    public static ContentBrowserResponse Published(ContentBrowserResponse catalog, ContentBundle canonical)
        => catalog with { Entries = MarkPublications(catalog.Entries.ToList(), canonical) };
    private static List<ContentBrowserEntry> MarkPublications(List<ContentBrowserEntry> entries, ContentBundle canonical) => entries.Select(e => e with
    { Published = canonical.Publications.Any(p => p.Identity.TypeId == e.Id && (e.Family == "equipment" && p.Identity.Family == ContentFamily.Equipment || e.Family == "lootCards" && p.Identity.Family == ContentFamily.LootCard)) }).ToList();

    private static ContentBrowserEntry EquipmentEntry(EquipmentDefinition type, string origin, IReadOnlyList<UnitType>? units = null) =>
        Entry("equipment", type.Id, type.Name, type, origin, AbilitySummary(type.LocalAbilities.Length),
            type.LocalAbilities, AbilityReferences(type.LocalAbilities), units);
    private static ContentBrowserEntry ItemEntry(ItemDefinition type, string origin, IReadOnlyList<UnitType>? units = null) =>
        Entry("items", type.Id, type.Name, type, origin, type.Describe(units), type.LocalAbilities, AbilityReferences(type.LocalAbilities), units);
    private static ContentBrowserEntry LootEntry(LootCardDefinition type, string origin)
    {
        List<ContentBrowserReference> references = [];
        if (type.EquipmentTypeId is { } equipmentId) references.Add(Reference("Equipment reward", "equipment", equipmentId));
        if (type.ItemTypeId is { } itemId) references.Add(Reference("Item reward", "items", itemId));
        if (type.Immediate is { } immediate) references.AddRange(MechanicReferences(immediate));
        return Entry("lootCards", type.Id, type.Name, type, origin, type.Description ?? type.Category.ToString(), references: references);
    }
    private static ContentBrowserEntry MechanicEntry(MechanicDescriptor mechanic) =>
        Entry("mechanics", mechanic.Id, mechanic.Name, mechanic, "canonical", mechanic.Description.Template) with
        {
            Mechanic = mechanic,
            Sections =
            [
                new("Configurable parameters", [new("Parameters", Element(mechanic.Parameters))]),
                new("Fixed Engine behavior", Fields(Element(new
                {
                    mechanic.Family, mechanic.Profiles, mechanic.Context, mechanic.Targeting,
                    mechanic.Dependencies, mechanic.Grantable, mechanic.MaxPerSource, mechanic.SerializedBinding, mechanic.Description
                })))
            ]
        };

    private static ContentBrowserEntry Entry<T>(string family, string id, string name, T definition, string origin,
        string summary = "", IEnumerable<LocalAbilityDefinition>? abilities = null,
        IEnumerable<ContentBrowserReference>? references = null, IReadOnlyList<UnitType>? units = null)
    {
        var key = Key(family, id);
        var value = Element(definition);
        return new(key, family, id, name, origin, summary, [new("Definition values", Fields(value, "id", "name", "localAbilities"))],
            (abilities ?? []).Select(ability => new ContentBrowserAbility(ability.Key, ability.Name, ability.Timing.ToString(), ability.Usage,
                MechanicCatalog.FindByPayload(ability.Mechanic)!.Id, Parameters(ability.Mechanic),
                ContentDescriptions.AbilityEntry(ability, ability.Key, units).Description, key)).ToArray(),
            (references ?? []).ToArray(), value);
    }

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Json);
    private static IEnumerable<SummonAdjacent> Summons(IEnumerable<LocalAbilityDefinition> abilities) =>
        abilities.Select(ability => SummoningRules.Placement(ability.Mechanic)).OfType<SummonAdjacent>();
    private static ContentBrowserSection[] UnitSections(UnitType type) =>
    [
        new("Stats", Fields(Element(new { type.Mov, type.Rng, type.Atk, type.Def, type.Hp }))),
        new("Movement and structure", Fields(Element(new
        {
            type.Footprint, type.Unique, type.Actions, type.MovementPattern,
            type.TerrainRestrictedMovement, type.TerrainRestriction, type.ResourceStorage
        }))),
        new("Loot", Fields(Element(new { type.CanHaveItems, type.CanHaveEquipment, type.LootDropSuccesses }))),
        new("Automated policies", Fields(Element(new { type.Behaviors, type.AttackDefinitionOrder })))
    ];
    private static ContentBrowserSection[] TerrainSections(TerrainDefinition type, IReadOnlyList<ResourceType>? resources = null) =>
    [
        new("Terrain properties", Fields(Element(new
        {
            structure = type.Assembly is null ? "Atomic" : "Composed",
            type.Passable, type.BlocksLos, type.AllowsResourceMarkers, type.Categories
        }))),
        new("Presentation", Fields(Element(type.Presentation))),
        new("Production", Fields(Element(new { type.Production, description = EnvironmentalProductionContent.Describe(type.Production, resources) })))
    ];
    private static string Local(bool local) => local ? "scenario-local" : "canonical";
    private static string AbilitySummary(int count) => $"{count} owned {(count == 1 ? "Ability" : "Abilities")}.";
    private static string Key(string family, string id) => family + ":" + id;
    private static ContentBrowserReference Reference(string label, string family, string id) => new(label, Key(family, id), family, id);
    private static ContentBrowserField[] Fields(JsonElement value, params string[] excluded) => value.EnumerateObject()
        .Where(property => !excluded.Contains(property.Name)).Select(property => new ContentBrowserField(Label(property.Name), property.Value.Clone())).ToArray();
    private static string Label(string value) => value is "mov" or "rng" or "atk" or "def" or "hp" ? value.ToUpperInvariant() :
        Regex.Replace(char.ToUpperInvariant(value[0]) + value[1..], "([a-z0-9])([A-Z])", "$1 $2").Replace(" Los", " LOS", StringComparison.Ordinal);
    private static JsonElement Parameters(AbilityMechanic payload) => Element(Element(payload).EnumerateObject()
        .Where(property => property.Name != "kind").ToDictionary(property => property.Name, property => property.Value.Clone()));
    private static IEnumerable<ContentBrowserReference> AbilityReferences(IEnumerable<LocalAbilityDefinition> abilities) =>
        abilities.SelectMany(ability => MechanicReferences(ability.Mechanic))
            .Concat(Summons(abilities).Select(summon => Reference("Summoned Unit", "units", summon.UnitTypeId)));
    private static IEnumerable<ContentBrowserReference> MechanicReferences(object payload)
    {
        var descriptor = MechanicCatalog.FindByPayload(payload)!;
        yield return Reference("Mechanic", "mechanics", descriptor.Id);
        var value = JsonSerializer.SerializeToElement(payload, payload.GetType(), Json);
        foreach (var reference in ParameterReferences(value, descriptor.Parameters)) yield return reference;
    }
    private static IEnumerable<ContentBrowserReference> ParameterReferences(JsonElement value, IEnumerable<MechanicParameter> parameters)
    {
        if (value.ValueKind != JsonValueKind.Object) yield break;
        foreach (var parameter in parameters)
        {
            if (!value.TryGetProperty(parameter.Name, out var member)) continue;
            foreach (var reference in ParameterValueReferences(member, parameter))
                yield return parameter.ReferenceCatalog == "resources" && value.TryGetProperty("name", out var name) && name.ValueKind is JsonValueKind.String or JsonValueKind.Null
                    ? reference with { DefinitionIdentity = Element(new ResourceType(reference.Id, name.GetString()!)).GetRawText() } : reference;
        }
    }
    private static IEnumerable<ContentBrowserReference> ParameterValueReferences(JsonElement value, MechanicParameter parameter)
    {
        if (parameter.ReferenceCatalog is { } family && value.ValueKind == JsonValueKind.String)
            yield return Reference(Label(parameter.Name), family, value.GetString()!);
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var reference in ParameterReferences(value, parameter.Members)) yield return reference;
        if (value.ValueKind == JsonValueKind.Array && parameter.Item is { } item)
            foreach (var child in value.EnumerateArray())
                foreach (var reference in ParameterValueReferences(child, item)) yield return reference;
    }
    private static IEnumerable<ContentBrowserReference> StorageReferences(ResourceStorage? storage) =>
        storage?.Capacities.Select(capacity => ResourceReference("Resource Storage", capacity.Type)) ?? [];
    private static ContentBrowserReference ResourceReference(string label, ResourceType type) =>
        Reference(label, "resources", type.Id) with { DefinitionIdentity = Element(type).GetRawText() };
    private static IEnumerable<ContentBrowserReference> LootReferences(IEnumerable<OwnedObject> objects) => objects.Select(card =>
        card.LootCardId is { } loot ? Reference("Loot Card", "lootCards", loot) : card.ItemTypeId is { } item
            ? Reference("Item", "items", item) : Reference("Equipment", "equipment", card.EquipmentTypeId!));
    private static object ResolvedLootComposition(IEnumerable<OwnedObject> objects) => objects
        .GroupBy(card => (card.LootCardId, card.ItemTypeId, card.EquipmentTypeId))
        .Select(group => new { group.Key.LootCardId, group.Key.ItemTypeId, group.Key.EquipmentTypeId, count = group.Count() }).ToArray();
    private static IEnumerable<ContentBrowserReference> WorldReferences(IEnumerable<WorldCardQuantity> composition) =>
        composition.Select(row => Reference("World Card", "worldCards", row.TypeId));
}
