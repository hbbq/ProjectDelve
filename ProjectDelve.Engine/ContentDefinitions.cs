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
                AbilityContent.Validate(x.LocalAbilities, AbilitySourceKind.Unit);
                ValidateUnitStructure(x);
                if (x.ResourceStorage is {} storage)
                    try { ResourceRules.ValidateCapacity(storage); }
                    catch (ArgumentException error) { throw ScenarioValidationContext.At(error, "resourceStorage"); }
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

    internal static bool InvalidUnitStructure(UnitType t) => t.DefaultController is { } controller && !Enum.IsDefined(controller) || t.WakeDistance < 0 || t.SightDistance < 0 || t.LocalAbilities.IsDefault || t.LocalAbilities.Any(a => a is null) || !Enum.IsDefined(t.Footprint) || t.Mov < UnitAuthoringSchema.MinimumStat || t.Rng < UnitAuthoringSchema.MinimumStat || t.Atk < UnitAuthoringSchema.MinimumStat || t.Def < UnitAuthoringSchema.MinimumStat || t.Hp < UnitAuthoringSchema.MinimumHp || (t.Hp > 1 && !t.Unique) ||
        (t.Actions & ~UnitAction.NormalAttack) != 0 || (t.Mechanic<RunRails>() is not null && t.Footprint != Footprint.OneByOne) ||
        (t.Behaviors & ~UnitAuthoringSchema.SupportedBehaviors) != 0 || t.AttackDefinitionOrder.IsDefault ||
        t.AttackDefinitionOrder.Any(a => !UnitAuthoringSchema.AttackActions.Contains(a)) ||
        (t.MovementPattern is {} pattern && !Enum.IsDefined(pattern)) ||
        (t.TerrainRestrictedMovement is {} restriction && (!Enum.IsDefined(restriction.Terrain) || !restriction.Terrain.Passable())) ||
        !MovementKinds.Valid(t.BaseMovementCapabilities) || t.TerrainRestriction is { IsValid: false } || t.TerrainRestriction is not null && t.TerrainRestrictedMovement is not null;
    private static void ValidateUnitStructure(UnitType type)
    {
        void Require(bool valid, string path, string message)
        { if (!valid) throw ScenarioValidationContext.At(new ArgumentException(message), path); }
        Require(!string.IsNullOrWhiteSpace(type.Id), "id", "A Unit Type requires a stable nonblank ID.");
        Require(type.DisplayName is null || !string.IsNullOrWhiteSpace(type.DisplayName), "displayName", "A supplied display name cannot be blank.");
        Require(type.WakeDistance is null or >= 0, "wakeDistance", "WakeDistance must be a nonnegative integer or omitted.");
        Require(type.DefaultController is not { } controller || Enum.IsDefined(controller), "defaultController", "Choose Human, Automated, or the existing scenario-only default.");
        foreach (var (path, value) in new[] { ("mov", type.Mov), ("rng", type.Rng), ("atk", type.Atk), ("def", type.Def), ("sightDistance", type.SightDistance) })
            Require(value >= UnitAuthoringSchema.MinimumStat, path, "Base stats must be nonnegative integers.");
        Require(type.Hp >= UnitAuthoringSchema.MinimumHp, "hp", "HP must be at least 1.");
        Require(type.Hp <= 1 || type.Unique, "unique", "Maximum HP greater than 1 requires a Unique Unit Type.");
        Require(Enum.IsDefined(type.Footprint), "footprint", "Choose a supported Footprint.");
        Require((type.Actions & ~UnitAction.NormalAttack) == 0, "actions", "Only Normal Attack belongs in Actions; declare other Actions as local Abilities.");
        Require((type.Behaviors & ~UnitAuthoringSchema.SupportedBehaviors) == 0, "behaviors", "Choose supported automated Behavior preferences.");
        Require(!type.AttackDefinitionOrder.IsDefault && type.AttackDefinitionOrder.All(UnitAuthoringSchema.AttackActions.Contains),
            "attackDefinitionOrder", "Attack definition order must contain supported Attack choices.");
        Require(type.LootDropSuccesses is >= UnitAuthoringSchema.MinimumDropSuccesses and <= UnitAuthoringSchema.MaximumDropSuccesses,
            "lootDropSuccesses", "Loot drops require 0 through 6 success faces.");
        Require(type.MovementPattern is not {} pattern || Enum.IsDefined(pattern), "movementPattern", "Choose a supported movement pattern.");
        Require(type.TerrainRestrictedMovement is not {} restriction || Enum.IsDefined(restriction.Terrain) && restriction.Terrain.Passable(),
            "terrainRestrictedMovement.terrain", "A legacy Terrain restriction requires supported passable Terrain.");
        Require(MovementKinds.Valid(type.BaseMovementCapabilities), "baseMovementCapabilities", "Movement capabilities must be normalized, valid and distinct.");
        Require(type.TerrainRestriction is not { IsValid: false }, "terrainRestriction.categories", "Terrain categories must be nonempty, valid and distinct.");
        Require(type.TerrainRestriction is null || type.TerrainRestrictedMovement is null, "terrainRestriction", "Choose either category or legacy Terrain restrictions.");
        Require(type.Mechanic<RunRails>() is null || type.Footprint == Footprint.OneByOne, "footprint", "Run Rails requires a one-Cell Footprint.");
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
            if (abilities[i] is {} ability && !ability.Costs.IsDefault)
                for (int j = 0; j < ability.Costs.Length; j++)
                    if (ability.Costs[j] is SpendResource cost)
                        Add(ContentFamily.Resource, cost.ResourceTypeId, $"localAbilities[{i}].costs[{j}].resourceTypeId");
            var path = $"localAbilities[{i}].mechanic";
            switch (abilities[i]?.Mechanic)
            {
                case AdjacentUnitSummonMechanic x: Add(ContentFamily.Unit, x.UnitTypeId, path + ".unitTypeId"); break;
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
                if (x.DestroyedTerrainTypeId is { } destroyedAssembly) Add(ContentFamily.Terrain, destroyedAssembly, "destroyedTerrainTypeId");
                Add(ContentFamily.Terrain, x.SurfaceId, "surfaceId");
                if (!x.Components.IsDefault) for (int i = 0; i < x.Components.Length; i++)
                    if (x.Components[i] is {} component) Add(ContentFamily.FixedComponent, component.ComponentId, $"components[{i}].componentId"); break;
            case TerrainDefinition x:
                if (x.DestroyedTerrainTypeId is { } destroyedTerrain) Add(ContentFamily.Terrain, destroyedTerrain, "destroyedTerrainTypeId");
                if (x.Production is DirectionalResourceProduction production) Add(ContentFamily.Resource, production.ResourceTypeId, "production.resourceTypeId");
                if (x.Assembly is {} assembly)
                {
                    Add(ContentFamily.Terrain, assembly.Surface.Id, "assembly.surface", ContentDependencyKind.Embedded, assembly.Surface);
                    for (int i = 0; i < assembly.Components.Length; i++) Add(ContentFamily.FixedComponent, assembly.Components[i].Definition.Id, $"assembly.components[{i}].definition", ContentDependencyKind.Embedded, assembly.Components[i].Definition);
                } break;
            case MachineType x:
                if (x.RequiredTerrainTypeId is { } requiredTerrain) Add(ContentFamily.Terrain, requiredTerrain, "requiredTerrainTypeId");
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
