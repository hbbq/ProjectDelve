using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public enum ContentFamily { Unit, Equipment, Item, LootCard, LootPreset, WorldCard, WorldDeck, Terrain, FixedComponent, Resource, Machine }
public sealed record ContentIdentity(ContentFamily Family, string TypeId);
// Locations belong to the authoring envelope, never to gameplay definitions.
public sealed record ContentSource(string? Document = null, string? Package = null, string? Path = null);
public sealed record ContentDefinition(object Value, ContentSource? Source = null)
{
    public ContentIdentity Identity => ContentDefinitions.Identity(Value);
}
public sealed record WorldDeckDefinition(string Id, ImmutableArray<WorldCardQuantity> Composition);
public enum ContentDependencyKind { Explicit, ImplicitMechanic, Embedded }
public sealed record ContentDependency(ContentIdentity Target, string Path, ContentDependencyKind Kind, object? EmbeddedDefinition = null);
public sealed record ContentDiagnostic(ContentIdentity Owner, string Path, string Code, string Message,
    ContentSource? Source = null, ContentIdentity? Dependency = null, ContentSource? RelatedSource = null);

public static class ContentDefinitions
{
    public static ContentIdentity Identity(object value) => value switch
    {
        UnitType x => new(ContentFamily.Unit, x.Id), EquipmentDefinition x => new(ContentFamily.Equipment, x.Id),
        ItemDefinition x => new(ContentFamily.Item, x.Id), LootCardDefinition x => new(ContentFamily.LootCard, x.Id),
        LootPreset x => new(ContentFamily.LootPreset, x.Id), WorldCardDefinition x => new(ContentFamily.WorldCard, x.Id),
        WorldDeckDefinition x => new(ContentFamily.WorldDeck, x.Id), TerrainDefinition x => new(ContentFamily.Terrain, x.Id),
        TerrainAssemblyDefinition x => new(ContentFamily.Terrain, x.Id), FixedComponentDefinition x => new(ContentFamily.FixedComponent, x.Id),
        ResourceType x => new(ContentFamily.Resource, x.Id), MachineType x => new(ContentFamily.Machine, x.Id),
        _ => throw new ArgumentException("Unsupported typed content definition.", nameof(value))
    };

    public static ImmutableArray<ContentDiagnostic> Validate(ContentDefinition definition)
    {
        try { ValidateStructure(definition.Value); return []; }
        catch (ArgumentException error)
        {
            var path = ScenarioValidationContext.PathOf(error) ?? "";
            foreach (var owner in new[] { "equipmentTypes[0]", "itemTypes[0]", "lootCardDefinitions[0]" })
                if (path == owner) path = "";
                else if (path.StartsWith(owner + ".", StringComparison.Ordinal)) path = path[(owner.Length + 1)..];
            return [new(definition.Identity, path, "invalidDefinition", error.Message, definition.Source)];
        }
    }

    public static void ValidateStructure(object value)
    {
        switch (value)
        {
            case EquipmentDefinition x:
                EquipmentCatalog.Validate([x]); AbilityContent.Validate(x.LocalAbilities, AbilitySourceKind.Equipment); break;
            case ItemDefinition x:
                ItemCatalog.Validate([x]); AbilityContent.Validate(x.LocalAbilities, AbilitySourceKind.Item); break;
            case LootCardDefinition x: LootCardCatalog.Validate([x]); break;
            case WorldCardDefinition x: WorldCardCatalog.Validate([x]); break;
            case TerrainDefinition x: TerrainCatalog.Validate([x], againstCanonical: false); break;
            case TerrainAssemblyDefinition x: TerrainAssemblyContent.ValidateAuthoring(x); break;
            case FixedComponentDefinition x: FixedComponentCatalog.Validate([x], againstCanonical: false); break;
            case ResourceType x:
                if (!ResourceRules.ValidType(x)) throw new ArgumentException("Resource Types require an ID and name."); break;
            case MachineType x: MachineRules.ValidateDefinition(x); break;
            case UnitType x:
                ValidateUnitStructure(x);
                AbilityContent.Validate(x.LocalAbilities, AbilitySourceKind.Unit);
                if (x.ResourceStorage is {} storage) ResourceRules.ValidateCapacity(storage);
                break;
            case LootPreset x:
                if (string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) || x.Composition is null)
                    throw new ArgumentException("Loot presets require ID, name and composition.");
                _ = new LootSettings { Composition = x.Composition.ToList() }.ResolveComposition();
                foreach (var row in x.Composition)
                    if (row is null || row.Count < 1 || new[] {row.ItemTypeId, row.EquipmentTypeId, row.LootCardId}.Count(id => id is not null) > 1 ||
                        new[] {row.ItemTypeId, row.EquipmentTypeId, row.LootCardId}.Any(id => id is not null && string.IsNullOrWhiteSpace(id)) ||
                        row.ItemTypeId is null && row.EquipmentTypeId is null && row.LootCardId is null && !Enum.IsDefined(row.Kind))
                        throw new ArgumentException("Invalid Loot composition row.");
                break;
            case WorldDeckDefinition x:
                if (string.IsNullOrWhiteSpace(x.Id) || x.Composition.IsDefault)
                    throw new ArgumentException("World decks require an ID and composition.");
                ValidateWorldComposition(x.Composition); break;
            default: throw new ArgumentException("Unsupported typed content definition.");
        }
    }

    internal static void ValidateWorldComposition(IReadOnlyList<WorldCardQuantity> rows)
    {
        if (rows.Any(r => r is null || string.IsNullOrWhiteSpace(r.TypeId) || r.Count is < 1 or > 1000) ||
            rows.Select(r => r.TypeId).Distinct(StringComparer.Ordinal).Count() != rows.Count || rows.Sum(r => (long)r.Count) > 6000)
            throw new ArgumentException("World Deck composition requires known unique Type IDs and quantities from 1 to 1000 (at most 6000 cards).");
    }

    internal static bool InvalidUnitStructure(UnitType t) => !Enum.IsDefined(t.Footprint) || t.Mov < 0 || t.Rng < 0 || t.Atk < 0 || t.Def < 0 || t.Hp < 1 || (t.Hp > 1 && !t.Unique) ||
        (t.Actions & ~UnitAction.NormalAttack) != 0 || (t.Mechanic<RunRails>() is not null && t.Footprint != Footprint.OneByOne) ||
        (t.MovementPattern is {} pattern && !Enum.IsDefined(pattern)) ||
        (t.TerrainRestrictedMovement is {} restriction && (!Enum.IsDefined(restriction.Terrain) || !restriction.Terrain.Passable())) ||
        t.TerrainRestriction is { IsValid: false } || t.TerrainRestriction is not null && t.TerrainRestrictedMovement is not null;
    private static void ValidateUnitStructure(UnitType type)
    {
        if (string.IsNullOrWhiteSpace(type.Id) || type.LootDropSuccesses is < 0 or > 6 || InvalidUnitStructure(type)) throw new ArgumentException("Invalid Unit Type or stat domain.");
    }

    public static ImmutableArray<ContentDependency> Dependencies(object value)
    {
        List<ContentDependency> result = [];
        void Add(ContentFamily family, string id, string path, ContentDependencyKind kind = ContentDependencyKind.Explicit, object? embedded = null) =>
            result.Add(new(new(family, id), path, kind, embedded));
        void Resource(ResourceType type, string path) => Add(ContentFamily.Resource, type.Id, path, ContentDependencyKind.Embedded, type);
        void Storage(ResourceStorage? storage, string path)
        {
            if (storage is null || storage.Capacities.IsDefault) return;
            for (int i = 0; i < storage.Capacities.Length; i++)
                if (storage.Capacities[i]?.Type is {} type) Resource(type, $"{path}.capacities[{i}].type");
        }
        var abilities = value switch { UnitType x => x.LocalAbilities, EquipmentDefinition x => x.LocalAbilities, ItemDefinition x => x.LocalAbilities, _ => [] };
        if (!abilities.IsDefault)
        for (int i = 0; i < abilities.Length; i++)
        {
            var path = $"localAbilities[{i}].mechanic";
            switch (abilities[i]?.Mechanic)
            {
                case SummonAdjacent x: Add(ContentFamily.Unit, x.UnitTypeId, path + ".unitTypeId"); break;
                case SummonWorker: Add(ContentFamily.Unit, UnitTypeIds.Worker, path, ContentDependencyKind.ImplicitMechanic); break;
                case SummonTroll:
                    Add(ContentFamily.Unit, UnitTypeIds.Troll, path, ContentDependencyKind.ImplicitMechanic);
                    Add(ContentFamily.Resource, ResourceTypeIds.Diamond, path, ContentDependencyKind.ImplicitMechanic); break;
                case PlaceBananaPeel: Add(ContentFamily.Resource, ResourceTypeIds.Banana, path, ContentDependencyKind.ImplicitMechanic); break;
                case Boost: Add(ContentFamily.Resource, ResourceTypeIds.Mushroom, path, ContentDependencyKind.ImplicitMechanic); break;
                case ShootShell: Add(ContentFamily.Resource, ResourceTypeIds.Shell, path, ContentDependencyKind.ImplicitMechanic); break;
                case WaypointSlotMachine x when !x.OutputTable.Faces.IsDefault:
                    for (int j = 0; j < x.OutputTable.Faces.Length; j++) Resource(x.OutputTable.Faces[j], path + $".outputTable.faces[{j}]"); break;
            }
        }
        switch (value)
        {
            case UnitType x:
                Storage(x.ResourceStorage, "resourceStorage");
                if (x.TerrainRestrictedMovement is {} restriction && Enum.IsDefined(restriction.Terrain))
                    Add(ContentFamily.Terrain, TerrainCompatibility.ResolveTypeId(restriction.Terrain, null), "terrainRestrictedMovement.terrain");
                break;
            case LootCardDefinition x:
                if (x.EquipmentTypeId is {} equipment) Add(ContentFamily.Equipment, equipment, "equipmentTypeId");
                if (x.ItemTypeId is {} item) Add(ContentFamily.Item, item, "itemTypeId"); break;
            case LootPreset x when x.Composition is not null:
                for (int i = 0; i < x.Composition.Count; i++)
                {
                    var row = x.Composition[i]; if (row is null) continue;
                    if (row.ItemTypeId is {} rowItem) Add(ContentFamily.Item, rowItem, $"composition[{i}].itemTypeId");
                    else if (row.EquipmentTypeId is {} rowEquipment) Add(ContentFamily.Equipment, rowEquipment, $"composition[{i}].equipmentTypeId");
                    else Add(ContentFamily.LootCard, row.LootCardId ?? LootCompatibility.TypeId(row.Kind), $"composition[{i}].lootCardId");
                } break;
            case WorldDeckDefinition x when !x.Composition.IsDefault:
                for (int i = 0; i < x.Composition.Length; i++) if (x.Composition[i] is {} row) Add(ContentFamily.WorldCard, row.TypeId, $"composition[{i}].typeId"); break;
            case TerrainAssemblyDefinition x:
                Add(ContentFamily.Terrain, x.SurfaceId, "surfaceId");
                if (!x.Components.IsDefault) for (int i = 0; i < x.Components.Length; i++)
                    if (x.Components[i] is {} component) Add(ContentFamily.FixedComponent, component.ComponentId, $"components[{i}].componentId"); break;
            case TerrainDefinition x:
                if (x.Production is DirectionalResourceProduction production) Add(ContentFamily.Resource, production.ResourceTypeId, "production.resourceTypeId");
                if (x.Assembly is {} assembly)
                {
                    Add(ContentFamily.Terrain, assembly.Surface.Id, "assembly.surface", ContentDependencyKind.Embedded, assembly.Surface);
                    for (int i = 0; i < assembly.Components.Length; i++) Add(ContentFamily.FixedComponent, assembly.Components[i].Definition.Id, $"assembly.components[{i}].definition", ContentDependencyKind.Embedded, assembly.Components[i].Definition);
                } break;
            case MachineType x:
                Storage(x.Storage, "storage");
                if (x.Operation is {} operation)
                {
                    if (!operation.Costs.IsDefault) for (int i = 0; i < operation.Costs.Length; i++) if (operation.Costs[i]?.Type is {} type) Resource(type, $"operation.costs[{i}].type");
                    if (operation.OutputType is {} output) Resource(output, "operation.outputType");
                    if (operation.OutputTable is {} table && !table.Faces.IsDefault) for (int i = 0; i < table.Faces.Length; i++) Resource(table.Faces[i], $"operation.outputTable.faces[{i}]");
                } break;
        }
        return result.OrderBy(d => d.Target.Family).ThenBy(d => d.Target.TypeId, StringComparer.Ordinal).ThenBy(d => d.Path, StringComparer.Ordinal).ToImmutableArray();
    }
}
