using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public enum EquipmentCapability { Phase, OpenDoor }
public enum EquipmentActionMechanic { StandAdjacentFriendlyLyingUnit }
public enum EquipmentUsage { Unlimited }
public sealed record EquipmentStatModifier(Stat Stat, int Amount);
public sealed record EquipmentAbility(string Id, string Name, EquipmentActionMechanic Mechanic,
    EquipmentUsage Usage = EquipmentUsage.Unlimited)
{
    public string? DescriptionOverride { get; init; }
    // Compatibility identity only; never selects a mechanic.
    public string? CandidatePrefix { get; init; }
}

public sealed record EquipmentDefinition(string Id, string Name)
{
    public ImmutableArray<AbilityGrant> Grants { get; init; } = [];
    public ImmutableArray<LocalAbilityDefinition> LocalAbilities { get; init; } = [];
    public ImmutableArray<EquipmentStatModifier> Modifiers { get; init; } = [];
    public ImmutableArray<EquipmentCapability> Capabilities { get; init; } = [];
    public ImmutableArray<EquipmentAbility> Abilities { get; init; } = [];
    public string EntryId(string grant) => $"equipment:{Uri.EscapeDataString(Id)}:{Uri.EscapeDataString(grant)}";
    public IReadOnlyList<CardEntryDescription> CardEntries(IReadOnlyList<AbilityDefinition>? abilityDefinitions = null)
    {
        List<CardEntryDescription> entries = [];
        if (!Modifiers.IsEmpty) entries.Add(new(EntryId("modifiers"), Name, "Equipment",
            string.Join("; ", Modifiers.Select(m => $"{m.Stat.ToString().ToUpperInvariant()} {m.Amount:+0;-0;0}")) + "."));
        foreach (var capability in Capabilities)
            entries.Add(capability switch
            {
                EquipmentCapability.Phase => new(EntryId("phase"), "Phase", "Capability", "Pass through terrain and Edges; end on a legal Cell."),
                EquipmentCapability.OpenDoor => new(EntryId("open-door"), "Open Door", "Free Action", "Open a bordering Closed Door."),
                _ => throw new ArgumentException("Unsupported Equipment capability.")
            });
        foreach (var ability in Abilities)
            entries.Add(new(EntryId(ability.Id), ability.Name, "Action", ability.DescriptionOverride ?? (ability.Mechanic switch
            {
                EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit => "Stand up one adjacent friendly Lying Unit.",
                _ => throw new ArgumentException("Unsupported Equipment Action.")
            })));
        foreach (var declaration in AbilityDeclarations.Resolve(LocalAbilities, Grants, abilityDefinitions))
            entries.Add(declaration.Definition.CardEntry(EntryId(declaration.Key)));
        return entries;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Description => Describe();
    public string Describe(IReadOnlyList<AbilityDefinition>? abilityDefinitions = null) =>
        string.Join(" ", CardEntries(abilityDefinitions).Select(e => e.Category is "Action" or "Free Action" or "Bonus Action" or "Passive" ? $"{e.Category}: {e.Description}" : e.Description));
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
        HashSet<string> aliases = new(StringComparer.Ordinal);
        foreach (var d in definitions)
        {
            if (d is null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || !ids.Add(d.Id))
                throw new ArgumentException("Equipment Types require unique nonblank IDs and names.");
            if (d.Modifiers.IsDefault || d.Capabilities.IsDefault || d.Abilities.IsDefault ||
                d.Modifiers.Any(m => m is null || !Enum.IsDefined(m.Stat) || m.Amount is < -100 or > 100) ||
                d.Capabilities.Any(c => !Enum.IsDefined(c)) || d.Capabilities.Distinct().Count() != d.Capabilities.Length)
                throw new ArgumentException("Invalid Equipment modifiers or capabilities.");
            HashSet<string> grants = new(StringComparer.Ordinal) { "modifiers", "phase", "open-door" };
            foreach (var a in d.Abilities)
                if (a is null || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Name) || !grants.Add(a.Id) ||
                    !Enum.IsDefined(a.Mechanic) || a.Usage != EquipmentUsage.Unlimited ||
                    a.CandidatePrefix is not null && (a.CandidatePrefix != "goat-bell" || d.Id != "goat-bell" || !aliases.Add(a.CandidatePrefix)))
                    throw new ArgumentException("Invalid or duplicate Equipment Ability; only unlimited Actions are supported.");
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
        ? new(obj.Kind, state.Equipment(id).Name, LootCategory.Equipment, state.Equipment(id).Describe(state.AbilityDefinitions)) { EquipmentTypeId = id }
        : state.ItemTypeId(obj) is not null ? state.ItemContent(obj) : state.LootContent(obj);
    public static LootCardContent? CandidateObjectContent(this GameState state, Candidate candidate) => candidate.ItemTypeId is { } itemId
        ? state.ObjectContent(new OwnedObject(candidate.ObjectId ?? "candidate") { ItemTypeId = itemId })
        : candidate.EquipmentTypeId is { } id
        ? state.ObjectContent(new OwnedObject(candidate.ObjectId ?? "candidate", id))
        : candidate.ObjectKind is { } kind ? state.ObjectContent(new OwnedObject(candidate.ObjectId ?? "candidate", kind)) : null;
    public static IEnumerable<EquipmentDefinition> ActiveEquipment(this GameState state, string unitId) => state.IsUpright(unitId)
        ? state.Units.Single(u => u.Id == unitId).Inventory.Select(o => state.EquipmentTypeId(o)).OfType<string>().Distinct(StringComparer.Ordinal).Select(state.Equipment)
        : [];
    public static int EquipmentModifier(this GameState state, string unitId, Stat stat) => state.ActiveEquipment(unitId).SelectMany(e => e.Modifiers).Where(m => m.Stat == stat).Sum(m => m.Amount);
    public static bool EquipmentGrants(this GameState state, string unitId, EquipmentCapability capability) => state.ActiveEquipment(unitId).Any(e => e.Capabilities.Contains(capability));
}
