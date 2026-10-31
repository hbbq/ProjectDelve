using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EquipmentDefinition(string Id, string Name)
{
    public ImmutableArray<LocalAbilityDefinition> LocalAbilities { get; init; } = [];
    public string EntryId(string key) => $"equipment:{Uri.EscapeDataString(Id)}:{Uri.EscapeDataString(key)}";
    public IReadOnlyList<CardEntryDescription> CardEntries() =>
        LocalAbilities.Select(ability => ability.CardEntry(EntryId(ability.Key))).ToArray();
    [System.Text.Json.Serialization.JsonIgnore]
    public string Description => Describe();
    public string Describe() =>
        string.Join(" ", CardEntries().Select(e => e.Category is "Action" or "Free Action" or "Bonus Action" or "Passive" ? $"{e.Category}: {e.Description}" : e.Description));
}

public static class EquipmentCatalog
{
    public static IReadOnlyList<EquipmentDefinition> All => EquipmentRoster.All;
    public static List<EquipmentDefinition> Resolve(IEnumerable<EquipmentDefinition>? local)
    {
        var definitions = All.Concat(local ?? []).ToList();
        Validate(definitions);
        return definitions;
    }
    public static void Validate(IReadOnlyList<EquipmentDefinition> definitions)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (var index = 0; index < definitions.Count; index++)
        {
            var d = definitions[index];
            var path = $"equipmentTypes[{index}]";
            if (d is null)
                throw ScenarioValidationContext.At(new ArgumentException("An Equipment Type definition is required."), path);
            if (string.IsNullOrWhiteSpace(d.Id) || !ids.Add(d.Id))
                throw ScenarioValidationContext.At(new ArgumentException("Equipment Types require unique nonblank IDs."), path + ".id");
            if (string.IsNullOrWhiteSpace(d.Name))
                throw ScenarioValidationContext.At(new ArgumentException("Equipment Types require nonblank names."), path + ".name");
        }
    }
    public static string? LegacyTypeId(LootCardKind kind) => kind switch
    {
        LootCardKind.Bedsheet => "bedsheet", LootCardKind.LuckyBoots => "lucky-boots",
        LootCardKind.OfficialKey => "official-key", LootCardKind.GoatBell => "goat-bell", _ => null
    };
}

public static class EquipmentQueries
{
    public static string? EquipmentTypeId(this GameState state, OwnedObject obj) => obj.EquipmentTypeId ?? (obj.ItemTypeId is null && obj.LootCardId is null ? EquipmentCatalog.LegacyTypeId(obj.Kind) : null);
    public static EquipmentDefinition Equipment(this GameState state, string id) => state.EquipmentTypes.SingleOrDefault(d => d.Id == id)
        ?? throw new ArgumentException($"Unknown Equipment Type ID '{id}'.");
    public static LootCardContent ObjectContent(this GameState state, OwnedObject obj) => state.EquipmentTypeId(obj) is { } id
        ? new(obj.Kind, state.Equipment(id).Name, LootCategory.Equipment, state.Equipment(id).Describe()) { EquipmentTypeId = id }
        : state.ItemTypeId(obj) is not null ? state.ItemContent(obj) : state.LootContent(obj);
    public static LootCardContent? CandidateObjectContent(this GameState state, Candidate candidate) => candidate.ItemTypeId is { } itemId
        ? state.ObjectContent(new OwnedObject(candidate.ObjectId ?? "candidate") { ItemTypeId = itemId })
        : candidate.EquipmentTypeId is { } id
        ? state.ObjectContent(new OwnedObject(candidate.ObjectId ?? "candidate", id))
        : candidate.ObjectKind is { } kind ? state.ObjectContent(new OwnedObject(candidate.ObjectId ?? "candidate", kind)) : null;
    public static IEnumerable<EquipmentDefinition> ActiveEquipment(this GameState state, string unitId) => state.IsUpright(unitId)
        ? state.Units.Single(u => u.Id == unitId).Inventory.Select(o => state.EquipmentTypeId(o)).OfType<string>().Distinct(StringComparer.Ordinal).Select(state.Equipment)
        : [];
}
