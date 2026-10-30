using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record ItemDefinition(string Id, string Name)
{
    public ImmutableArray<AbilityGrant> Grants { get; init; } = [];
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
        if (definitions.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || d.Grants.IsDefaultOrEmpty) ||
            definitions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new ArgumentException("Items require unique IDs, names and at least one Ability grant.");
    }
}

// This milestone adds object rewards only. Existing Immediate Loot retains its
// concrete implementation; World Cards and arbitrary effect lists are not migrated.
public sealed record LootCardDefinition(string Id, string Name)
{
    public string? ItemTypeId { get; init; }
    public string? EquipmentTypeId { get; init; }
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
            (d.ItemTypeId is null) == (d.EquipmentTypeId is null)) || definitions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new ArgumentException("Loot Cards need unique IDs, names and exactly one Item or Equipment reference.");
    }
}
public static class ItemQueries
{
    public static string? ItemTypeId(this GameState state, OwnedObject obj) => obj.ItemTypeId ??
        (obj.EquipmentTypeId is null && obj.Kind == LootCardKind.SecondWind ? "second-wind" : null);
    public static ItemDefinition Item(this GameState state, string id) => state.ItemTypes.SingleOrDefault(t => t.Id == id)
        ?? throw new ArgumentException($"Unknown Item Type ID '{id}'.");
    public static LootCardContent ItemContent(this GameState state, OwnedObject obj)
    {
        var item = state.Item(state.ItemTypeId(obj)!);
        var entries = item.Grants.Select(g => state.Ability(g.AbilityId).CardEntry());
        return new(obj.Kind, item.Name, LootCategory.Item, string.Join(" ", entries.Select(e => $"{e.Category}: {e.Description}")) + " Consume this Item.")
            { ItemTypeId = item.Id };
    }
}
