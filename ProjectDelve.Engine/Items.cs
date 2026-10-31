using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemDefinition(string Id, string Name)
{
    public ImmutableArray<LocalAbilityDefinition> LocalAbilities { get; init; } = [];
    public string Describe(IReadOnlyList<UnitType>? existingTypes = null) => string.Join(" ", LocalAbilities.Select(ability => ability.CardEntry(existingTypes: existingTypes))
        .Select(entry => $"{entry.Category}: {entry.Description}")) + " Consume this Item.";
}
public static class ItemCatalog
{
    public static IReadOnlyList<ItemDefinition> All => ItemRoster.All;
    public static List<ItemDefinition> Resolve(IEnumerable<ItemDefinition>? local)
    {
        var definitions = All.Concat(local ?? []).ToList();
        Validate(definitions);
        return definitions;
    }
    public static void Validate(IReadOnlyList<ItemDefinition> definitions)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var path = $"itemTypes[{i}]";
            if (definition is null)
                throw ScenarioValidationContext.At(new ArgumentException("An Item Type definition is required."), path);
            if (string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id))
                throw ScenarioValidationContext.At(new ArgumentException("Items require unique nonblank IDs."), path + ".id");
            if (string.IsNullOrWhiteSpace(definition.Name))
                throw ScenarioValidationContext.At(new ArgumentException("Items require nonblank names."), path + ".name");
            if (definition.LocalAbilities.IsDefault)
                throw ScenarioValidationContext.At(new ArgumentException("Local Abilities cannot be null."), path + ".localAbilities");
            if (definition.LocalAbilities.IsEmpty)
                throw ScenarioValidationContext.At(new ArgumentException("Items require at least one local Ability."), path + ".localAbilities");
        }
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HealCollectorMechanic), "healCollector")]
public abstract record LootImmediateMechanic;
public sealed record HealCollectorMechanic(int Amount) : LootImmediateMechanic;

public sealed record LootCardDefinition(string Id, string Name)
{
    public string? ItemTypeId { get; init; }
    public string? EquipmentTypeId { get; init; }
    public LootImmediateMechanic? Immediate { get; init; }
    [JsonIgnore] public LootCategory Category => Immediate is not null ? LootCategory.Immediate
        : ItemTypeId is not null ? LootCategory.Item : LootCategory.Equipment;
    [JsonIgnore] public string? Description => Immediate switch
    {
        HealCollectorMechanic heal => $"Restore {heal.Amount} HP, without exceeding maximum HP.",
        null => null,
        _ => throw new ArgumentException("Unsupported Immediate Loot mechanic.")
    };
}
public static class LootCardCatalog
{
    public static IReadOnlyList<LootCardDefinition> All => LootRoster.Definitions;
    // Authored selection and inspection use local precedence. Runtime catalogs
    // separately retain historical aliases needed by physical card references.
    public static List<LootCardDefinition> Effective(IEnumerable<LootCardDefinition>? local)
    {
        var authored = (local ?? []).ToList();
        Validate(authored);
        var localIds = authored.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        var effective = All.Where(card => !localIds.Contains(card.Id)).Concat(authored).ToList();
        Validate(effective);
        return effective;
    }
    public static List<LootCardDefinition> Resolve(IEnumerable<LootCardDefinition>? local)
    {
        var definitions = All.Concat(local ?? []).ToList(); Validate(definitions); return definitions;
    }
    public static void Validate(IReadOnlyList<LootCardDefinition> definitions)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            var path = $"lootCardDefinitions[{index}]";
            if (definition is null)
                throw ScenarioValidationContext.At(new ArgumentException("A Loot Card definition is required."), path);
            if (string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id))
                throw ScenarioValidationContext.At(new ArgumentException("Loot Cards require unique nonblank IDs."), path + ".id");
            if (string.IsNullOrWhiteSpace(definition.Name))
                throw ScenarioValidationContext.At(new ArgumentException("Loot Cards require nonblank names."), path + ".name");
            if (new object?[] { definition.ItemTypeId, definition.EquipmentTypeId, definition.Immediate }.Count(value => value is not null) != 1)
                throw ScenarioValidationContext.At(new ArgumentException("Loot Cards require exactly one Item, Equipment or supported Immediate reward."), path);
            if (definition.ItemTypeId is not null && string.IsNullOrWhiteSpace(definition.ItemTypeId))
                throw ScenarioValidationContext.At(new ArgumentException("An Item reward needs a nonblank Type ID."), path + ".itemTypeId");
            if (definition.EquipmentTypeId is not null && string.IsNullOrWhiteSpace(definition.EquipmentTypeId))
                throw ScenarioValidationContext.At(new ArgumentException("An Equipment reward needs a nonblank Type ID."), path + ".equipmentTypeId");
            if (definition.Immediate is { } immediate)
            {
                var errors = MechanicCatalog.Validate(immediate, path: path + ".immediate");
                if (!errors.IsEmpty)
                    throw ScenarioValidationContext.At(new ArgumentException(string.Join(" ", errors.Select(error => $"{error.Path}: {error.Message}"))), errors[0].Path);
            }
        }
    }
}
public static class ItemQueries
{
    public static string? ItemTypeId(this GameState state, OwnedObject obj) => obj.ItemTypeId ??
        (obj.EquipmentTypeId is null && obj.LootCardId is null && obj.Kind == LootCardKind.SecondWind ? "second-wind" : null);
    public static ItemDefinition Item(this GameState state, string id) => state.ItemTypes.SingleOrDefault(t => t.Id == id)
        ?? throw new ArgumentException($"Unknown Item Type ID '{id}'.");
    public static LootCardContent ItemContent(this GameState state, OwnedObject obj)
    {
        var item = state.Item(state.ItemTypeId(obj)!);
        return new(obj.Kind, item.Name, LootCategory.Item, item.Describe(state.Types))
            { ItemTypeId = item.Id };
    }
}
