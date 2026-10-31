using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemDefinition(string Id, string Name)
{
    public ImmutableArray<LocalAbilityDefinition> LocalAbilities { get; init; } = [];
    public string Describe() => string.Join(" ", LocalAbilities.Select(ability => ability.CardEntry())
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
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (definition is null) continue;
            if (definition.LocalAbilities.IsDefault)
                throw ScenarioValidationContext.At(new ArgumentException("Local Abilities cannot be null."), $"itemTypes[{i}].localAbilities");
        }
        if (definitions.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) ||
                d.LocalAbilities.IsEmpty) ||
            definitions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new ArgumentException("Items require unique IDs, names and at least one local Ability.");
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
    public static List<LootCardDefinition> Resolve(IEnumerable<LootCardDefinition>? local)
    {
        var definitions = All.Concat(local ?? []).ToList(); Validate(definitions); return definitions;
    }
    public static void Validate(IReadOnlyList<LootCardDefinition> definitions)
    {
        if (definitions.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) ||
            new object?[] { d.ItemTypeId, d.EquipmentTypeId, d.Immediate }.Count(v => v is not null) != 1 ||
            d.ItemTypeId is not null && string.IsNullOrWhiteSpace(d.ItemTypeId) ||
            d.EquipmentTypeId is not null && string.IsNullOrWhiteSpace(d.EquipmentTypeId)) ||
            definitions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new ArgumentException("Loot Cards need unique IDs, names and exactly one Item, Equipment or supported Immediate reward.");
        foreach (var definition in definitions)
            if (definition.Immediate is { } immediate)
                MechanicCatalog.ThrowIfInvalid(immediate, path: "immediate");
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
        return new(obj.Kind, item.Name, LootCategory.Item, item.Describe())
            { ItemTypeId = item.Id };
    }
}
