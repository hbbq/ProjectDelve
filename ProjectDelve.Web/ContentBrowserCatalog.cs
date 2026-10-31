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
    IReadOnlyList<ContentBrowserEntry> Entries, IReadOnlyList<ContentBrowserDiagnostic> Diagnostics);
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

    public static ContentBrowserResponse Canonical() => Build(null, null);

    // Resolved, validated dependencies only: no scenario or runtime state.
    public static ContentBrowserResponse Definitions(IReadOnlyList<EquipmentDefinition> equipment,
        IReadOnlyList<ItemDefinition> items, IReadOnlyList<LootCardDefinition> loot)
    {
        List<ContentBrowserEntry> entries = [];
        entries.AddRange(equipment.Select(type => EquipmentEntry(type, EquipmentCatalog.All.Any(canonical => canonical.Id == type.Id) ? "canonical" : "local")));
        entries.AddRange(items.Select(type => ItemEntry(type, ItemCatalog.All.Any(canonical => canonical.Id == type.Id) ? "canonical" : "local")));
        entries.AddRange(loot.Select((type, index) => LootEntry(type, index < LootCardCatalog.All.Count ? "canonical" : "local") with
        {
            Availability = index >= LootCardCatalog.All.Count && LootCardCatalog.All.Any(canonical => canonical.Id == type.Id)
                ? "legacy-collision" : null
        }));
        entries.AddRange(MechanicCatalog.All.Select(MechanicEntry));
        entries = ResolveReferences(entries);
        return new(new("definitions", "Draft content"),
            Families.Where(family => family.Id is "equipment" or "items" or "lootCards" or "mechanics")
                .Select(family => new ContentBrowserCategory(family.Id, family.Name, entries.Count(entry => entry.Family == family.Id))).ToArray(), entries, []);
    }

    public static ContentBrowserResponse Scenario(ScenarioDefinition definition)
    {
        // This state is detached, remains before Round Start, and is never returned.
        var state = GameEngine.CreateGame(definition);
        return Build(definition, state);
    }

    private static ContentBrowserResponse Build(ScenarioDefinition? source, GameState? state)
    {
        List<ContentBrowserEntry> entries = [];
        var units = state?.Types.ToList() ?? CanonicalUnitTypes.All.ToList();
        var declaredUnitIds = units.Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        // Summoning may resolve a canonical type lazily. Expose only these reachable
        // fallbacks, rather than claiming every canonical Unit is in the scenario.
        for (var index = 0; index < units.Count; index++)
            foreach (var summon in Summons(units[index]))
                if (units.All(type => type.Id != summon.UnitTypeId) && UnitContent.Find(summon.UnitTypeId, state?.Types) is { } target)
                    units.Add(target);

        IReadOnlyList<EquipmentDefinition> equipment = state is null ? EquipmentCatalog.All : state.EquipmentTypes;
        IReadOnlyList<ItemDefinition> items = state is null ? ItemCatalog.All : state.ItemTypes;
        IReadOnlyList<LootCardDefinition> loot = state is null ? LootCardCatalog.All : state.LootCardDefinitions;
        IReadOnlyList<WorldCardDefinition> world = state is null ? WorldCardCatalog.All : state.WorldCardDefinitions;
        var terrain = state?.Physical.Board.TerrainTypes ?? TerrainCatalog.All;
        var components = FixedComponentCatalog.Resolve(source?.FixedComponentTypes);
        var assemblies = TerrainRoster.Assemblies.Concat(source?.TerrainAssemblies ?? []).ToArray();

        foreach (var type in units)
        {
            var authored = source?.UnitTypes?.SingleOrDefault(t => t.Id == type.Id);
            var lootOverride = source?.LootUnitOverrides?.GetValueOrDefault(type.Id);
            var origin = authored is null && lootOverride is null ? "canonical" :
                CanonicalUnitTypes.Find(type.Id) is null ? "scenario-local" : "scenario-override";
            var refs = AbilityReferences(type.LocalAbilities).ToList();
            refs.AddRange(Summons(type).Select(summon => Reference("Summoned Unit", "units", summon.UnitTypeId)));
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
        for (var index = 0; index < loot.Count; index++)
        {
            var type = loot[index];
            // Catalog creation preserves local ordering when it aliases a historical
            // ID collision; retain the exact authored identity alongside the result.
            var localIndex = index - LootCardCatalog.All.Count;
            var authored = localIndex >= 0 ? source?.LootCardDefinitions?[localIndex] : null;
            entries.Add(LootEntry(type, Local(authored is not null)) with
            { AuthoredDefinition = authored is not null && authored.Id != type.Id ? Element(authored) : null });
        }
        foreach (var preset in LootPresets.All)
        {
            var composition = LootPresets.Resolve(preset.Id, LootCardCatalog.Effective(source?.LootCardDefinitions));
            // Dynamic selection uses authored IDs and local precedence. References
            // in this resolved view follow the historical runtime aliases, if any.
            if (preset.IsDynamic && source?.LootCardDefinitions is { } local)
                composition = composition.Select(row =>
                {
                    var localIndex = local.FindIndex(card => card.Id == row.LootCardId);
                    return localIndex < 0 ? row : row with { LootCardId = loot[LootCardCatalog.All.Count + localIndex].Id };
                }).ToArray();
            entries.Add(Entry("lootPresets", preset.Id, preset.Name, preset with { Composition = composition },
                preset.IsDynamic && source is not null ? "resolved" : "canonical", preset.Description,
                references: LootReferences(new LootSettings { Composition = composition.ToList() }.ResolveObjects(loot))));
        }
        if (source is not null && state is not null)
            entries.Add(Entry("lootPresets", "scenario", "Scenario Loot composition",
                new { composition = ResolvedLootComposition(state.LootDeck!.DrawPile) }, "resolved", "Resolved initial Loot composition.",
                references: LootReferences(state.LootDeck.DrawPile)) with { AuthoredDefinition = Element(source.Loot ?? LootSettings.Empty) });
        var scenarioWorldComposition = source?.WorldEffects is { } worldSettings ? WorldCards.ResolveComposition(worldSettings, world) : null;
        foreach (var type in world)
        {
            var references = MechanicReferences(type.Immediate ?? (object)type.Modifiers).ToList();
            var defaultCount = WorldCardRoster.Composition.SingleOrDefault(row => row.TypeId == type.Id)?.Count ?? 0;
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
        entries.Add(Entry("worldDecks", "default", "Default World Deck", new { composition = WorldCardRoster.Composition },
            "canonical", "Canonical World Card quantities.", references: WorldReferences(WorldCardRoster.Composition)));
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
            entries.Add(Entry("terrain", type.Id, type.Name, type, Local(local), type.ProductionDescription ?? "", references: refs)
                with
                {
                    AuthoredDefinition = authoredAssembly is not null ? Element(authoredAssembly) : null,
                    Sections = TerrainSections(type)
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
                    resolved.Production, resolved.ProductionDescription, resolved.Presentation
                })))]
            });
        }
        foreach (var mechanic in MechanicCatalog.All)
            entries.Add(MechanicEntry(mechanic));

        var machineTypes = MachineTypes.All.Concat(source?.Machines?.Select(m => m.Type) ?? []).DistinctBy(type => Element(type).GetRawText()).ToList();
        var resourceTypes = ResourceTypes.All.Concat(units.SelectMany(type => type.ResourceStorage?.Capacities.Select(c => c.Type) ?? []))
            .Concat(units.SelectMany(type => type.LocalAbilities.Select(a => a.Mechanic).OfType<WaypointSlotMachine>().SelectMany(slot => slot.OutputTable.Faces)))
            .Concat(machineTypes.SelectMany(type => type.Storage.Capacities.Select(c => c.Type)
                .Concat(type.Operation.Costs.Select(c => c.Type)).Concat(type.Operation.OutputTable?.Faces ?? [])
                .Concat(type.Operation.OutputType is { } output ? [output] : [])))
            .Concat(source?.Resources?.Select(resource => resource.Type) ?? [])
            .Concat(source?.StoredResources?.Select(resource => resource.Type) ?? [])
            .DistinctBy(type => Element(type).GetRawText());
        foreach (var type in resourceTypes)
            entries.Add(Entry("resources", type.Id, string.IsNullOrWhiteSpace(type.Name) ? type.Id : type.Name, type, ResourceTypes.All.Contains(type) ? "canonical" :
                ResourceTypes.All.Any(canonical => canonical.Id == type.Id) ? "scenario-override" : "scenario-local"));
        foreach (var type in machineTypes)
        {
            var refs = StorageReferences(type.Storage).Concat(type.Operation.Costs.Select(c => ResourceReference("Input", c.Type)))
                .Concat((type.Operation.OutputTable?.Faces ?? []).Select(r => ResourceReference("Output", r)));
            if (type.Operation.OutputType is { } output) refs = refs.Append(ResourceReference("Output", output));
            entries.Add(Entry("machines", type.Id, type.Name, type, MachineTypes.All.Contains(type) ? "canonical" :
                MachineTypes.All.Any(canonical => canonical.Id == type.Id) ? "scenario-override" : "scenario-local",
                type.Operation.OutputDescription, references: refs));
        }
        foreach (var type in CellMarkerTypes.All)
            entries.Add(Entry("cellMarkers", type.Id, type.Name, type, "canonical", CellMarkerTypes.Describe(type)));

        entries = ResolveReferences(entries);
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

    private static ContentBrowserEntry EquipmentEntry(EquipmentDefinition type, string origin, IReadOnlyList<UnitType>? units = null) =>
        Entry("equipment", type.Id, type.Name, type, origin, AbilitySummary(type.LocalAbilities.Length),
            type.LocalAbilities, AbilityReferences(type.LocalAbilities), units);
    private static ContentBrowserEntry ItemEntry(ItemDefinition type, string origin, IReadOnlyList<UnitType>? units = null) =>
        Entry("items", type.Id, type.Name, type, origin, type.Describe(), type.LocalAbilities, AbilityReferences(type.LocalAbilities), units);
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
    private static IEnumerable<SummonAdjacent> Summons(UnitType type) =>
        new[] { UnitAction.SummonAdjacent, UnitAction.SummonWorker, UnitAction.SummonTroll }
            .Select(action => SummoningRules.Placement(type, action)).OfType<SummonAdjacent>();
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
    private static ContentBrowserSection[] TerrainSections(TerrainDefinition type) =>
    [
        new("Terrain properties", Fields(Element(new
        {
            structure = type.Assembly is null ? "Atomic" : "Composed",
            type.Passable, type.BlocksLos, type.AllowsResourceMarkers, type.Categories
        }))),
        new("Presentation", Fields(Element(type.Presentation))),
        new("Production", Fields(Element(new { type.Production, description = type.ProductionDescription })))
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
        abilities.SelectMany(ability => MechanicReferences(ability.Mechanic));
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
    private static IEnumerable<ContentBrowserReference> StorageReferences(ResourceStorage storage) =>
        storage.Capacities.Select(capacity => ResourceReference("Resource Storage", capacity.Type));
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
