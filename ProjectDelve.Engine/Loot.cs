namespace ProjectDelve.Engine;

public enum LootCardKind { PocketBreakfast, SecondWind, Bedsheet, LuckyBoots, OfficialKey, GoatBell }
public enum LootCategory { Immediate, Item, Equipment }
// Card identity is independent of its source: initial inventories need no Loot Deck.
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record OwnedObject(string Id, LootCardKind Kind = LootCardKind.PocketBreakfast)
{
    public string? ItemTypeId { get; init; }
    public string? LootCardId { get; init; }
    public string? EquipmentTypeId { get; init; }
    public OwnedObject(string id, string equipmentTypeId) : this(id) => EquipmentTypeId = equipmentTypeId;
    [System.Text.Json.Serialization.JsonIgnore]
    public LootCardContent Content => EquipmentTypeId is null && ItemTypeId is null && LootCardId is null ? LootCards.Content(Kind) : throw new InvalidOperationException("Resolve typed objects through the game content context.");
}
public sealed record GroundObject(OwnedObject Object, Cell Position);
public sealed record LootMarker(string Id, Cell Position);
public sealed record LootCardContent(LootCardKind Kind, string Name, LootCategory Category, string Text,
    int Healing = 0, int MovementBonus = 0, int ExtraActions = 0)
{
    public string? ItemTypeId { get; init; }
    public string? LootCardId { get; init; }
    public string? EquipmentTypeId { get; init; }
    // Optional artwork compatibility metadata, never an effect selector.
    public LootCardKind? LegacyKind { get; init; }
}

public static class LootCards
{
    public static IReadOnlyList<LootCardContent> All => LootRoster.All;

    public static LootCardContent Content(LootCardKind kind) => All.Single(c => c.Kind == kind);
}

public static class LootQueries
{
    public static LootCardDefinition LootCard(this GameState state, OwnedObject card)
    {
        // Source-less enum cards predate the catalog. A historical local card may
        // have the same ID as a newly canonical card, so it must not intercept
        // this compatibility reference. New deck instances always have typed IDs.
        if (card.LootCardId is not { } id) return LootCompatibility.Definition(card.Kind);
        return state.LootCardDefinitions.SingleOrDefault(d => d.Id == id)
            ?? throw new ArgumentException($"Unknown Loot Card ID '{id}'.");
    }
    public static LootCardContent LootContent(this GameState state, OwnedObject card)
    {
        var definition = state.LootCard(card);
        if (definition.Immediate is not HealCollectorMechanic heal)
            throw new ArgumentException("An object reward requires its resolved Item or Equipment Type reference.");
        return new(card.Kind, definition.Name, LootCategory.Immediate, definition.Description!, Healing: heal.Amount)
            { LootCardId = definition.Id, LegacyKind = LootCompatibility.LegacyKind(definition.Id) };
    }
}

// Composition contains actual copies, not weights. Draw removes a card permanently.
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record LootCardQuantity(LootCardKind Kind = LootCardKind.PocketBreakfast, int Count = 1)
{
    public string? ItemTypeId { get; init; }
    public string? LootCardId { get; init; }
    public string? EquipmentTypeId { get; init; }
    public LootCardQuantity(string equipmentTypeId, int count) : this(Count: count) => EquipmentTypeId = equipmentTypeId;
}
public sealed record LootPreset(string Id, string Name, string Description, IReadOnlyList<LootCardQuantity> Composition)
{
    public bool IsDynamic { get; init; }
}
public static class LootPresets
{
    public const string Default = LootRoster.DefaultPresetId;
    public const string AllPresetId = "All";
    public static IReadOnlyList<LootPreset> All => LootRoster.Presets;
    public static LootPreset Find(string id) => All.SingleOrDefault(p => p.Id == id)
        ?? throw new ArgumentException($"Unknown Loot preset '{id}'.");
    public static IReadOnlyList<LootCardQuantity> Resolve(string id, IReadOnlyList<LootCardDefinition>? definitions = null)
    {
        var preset = Find(id);
        if (!preset.IsDynamic) return preset.Composition;
        var effective = definitions ?? LootCardCatalog.All;
        LootCardCatalog.Validate(effective);
        return effective.OrderBy(card => card.Id, StringComparer.Ordinal)
            .Select(card => new LootCardQuantity { LootCardId = card.Id, Count = 1 }).ToArray();
    }
}
public sealed record LootSettings
{
    [System.Text.Json.Serialization.JsonConstructor]
    public LootSettings() { }
    public LootSettings(List<LootCardKind> cards) => Composition = cards.GroupBy(k => k).Select(g => new LootCardQuantity(g.Key, g.Count())).ToList();
    public string? Preset { get; init; }
    // Null preserves the original unscaled serialization and means one copy.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? PresetMultiplier { get; init; }
    public List<LootCardQuantity>? Composition { get; init; }
    // Read compatibility for the original repeated-ID format; normalized on export.
    public List<LootCardKind>? Cards { get; init; }
    public static LootSettings Empty => new() { Composition = [] };
    public LootSettings Normalize()
    {
        if (PresetMultiplier is < 1 or > 100 || PresetMultiplier is not null && (Composition is not null || Cards is not null))
            throw new ArgumentException("Loot preset multiplier must be 1..100 and is only valid for live presets.");
        if (Cards is not null)
        {
            if (Preset is not null || Composition is not null) throw new ArgumentException("Loot must specify either a preset or a Custom composition.");
            return new(Cards);
        }
        if (Preset is not null && Composition is not null) throw new ArgumentException("A live Loot preset cannot contain Custom cards.");
        var normalized = Composition is null ? this with { Preset = Preset ?? LootPresets.Default } : this;
        if (normalized.Preset is { } preset && normalized.PresetMultiplier is > 1 && LootPresets.Find(preset).IsDynamic)
            throw new ArgumentException("A dynamic All Loot preset contains exactly one of each card and cannot be multiplied.");
        return normalized;
    }
    public IReadOnlyList<LootCardQuantity> ResolveComposition(IReadOnlyList<LootCardDefinition>? cards = null)
    {
        var normalized = Normalize();
        IReadOnlyList<LootCardQuantity> rows = normalized.Composition ?? LootPresets.Resolve(normalized.Preset!, cards)
            .Select(r => r with { Count = checked(r.Count * (normalized.PresetMultiplier ?? 1)) }).ToList();
        if (rows.Any(r => r is null || !Enum.IsDefined(r.Kind) || r.Count is < 1 or > 1000) ||
            rows.Select(r => r.LootCardId is { } cardId ? ("loot", cardId) : r.ItemTypeId is { } itemId ? ("item", itemId) : (r.EquipmentTypeId ?? EquipmentCatalog.LegacyTypeId(r.Kind)) is { } id ? ("equipment", id) : ("card", r.Kind.ToString())).Distinct().Count() != rows.Count || rows.Sum(r => r.Count) > 6000)
            throw new ArgumentException("Custom Loot needs unique card IDs with quantities from 1 to 1000.");
        return rows;
    }
    public List<OwnedObject> ResolveObjects(IReadOnlyList<LootCardDefinition>? cards = null) => ResolveComposition(cards)
        .SelectMany(r => Enumerable.Repeat(r, r.Count)).Select((r, i) =>
        {
            if (new[] { r.ItemTypeId, r.EquipmentTypeId, r.LootCardId }.Count(x => x is not null) > 1)
                throw new ArgumentException("Loot composition needs one typed reference per row.");
            var id = r.LootCardId ?? (r.ItemTypeId is null && r.EquipmentTypeId is null ? LootCompatibility.TypeId(r.Kind) : null);
            var card = id is not null ? (cards ?? LootCardCatalog.All).SingleOrDefault(c => c.Id == id)
                ?? throw new ArgumentException($"Unknown Loot Card ID '{id}'.") : null;
            return new OwnedObject($"loot-card-{i + 1}", r.Kind)
            { EquipmentTypeId = card?.EquipmentTypeId ?? r.EquipmentTypeId, ItemTypeId = card?.ItemTypeId ?? r.ItemTypeId, LootCardId = id };
        }).ToList();
    public List<LootCardKind> ResolveCards()
    {
        var rows = ResolveComposition();
        if (rows.Any(r => r.EquipmentTypeId is not null || r.ItemTypeId is not null || r.LootCardId is not null)) throw new InvalidOperationException("Use ResolveObjects for independent Equipment references.");
        return rows.SelectMany(r => Enumerable.Repeat(r.Kind, r.Count)).ToList();
    }
}
public sealed record LootUnitOverride(int? DropSuccesses = null, bool? CanHaveItems = null, bool? CanHaveEquipment = null);
public sealed class LootDeck
{
    public List<OwnedObject> DrawPile { get; set; } = [];
    public List<OwnedObject> DiscardPile { get; set; } = [];
    internal LootDeck Copy() => new() { DrawPile = [.. DrawPile], DiscardPile = [.. DiscardPile] };
}

internal static class LootRules
{
    private static void ValidateCardProvenance(GameState state, OwnedObject obj)
    {
        if (obj.LootCardId is not { } id) return;
        var card = state.LootCardDefinitions.SingleOrDefault(c => c.Id == id)
            ?? throw new ArgumentException($"Unknown Loot Card ID '{id}'.");
        if (state.ItemTypeId(obj) != card.ItemTypeId || state.EquipmentTypeId(obj) != card.EquipmentTypeId)
            throw new ArgumentException($"Object '{obj.Id}' does not match its source Loot Card '{id}'.");
    }

    internal static bool CanOwn(GameState state, Unit unit, OwnedObject obj) => state.EquipmentTypeId(obj) is { } id
        ? state.Types.Single(t => t.Id == unit.TypeId).CanHaveEquipment && !unit.Inventory.Any(o => state.EquipmentTypeId(o) == id)
        : state.ItemTypeId(obj) is not null && state.Types.Single(t => t.Id == unit.TypeId).CanHaveItems;

    internal static void Validate(GameState state)
    {
        EquipmentCatalog.Validate(state.EquipmentTypes);
        AbilityContent.ValidateState(state);
        if (state.LootMarkers.Count > (state.LootDeck?.DrawPile.Count ?? 0))
            throw new ArgumentException("Every unopened Loot marker requires an available draw card.");
        if (state.Types.Any(t => t.LootDropSuccesses is < 0 or > 6))
            throw new ArgumentException("Loot drop successes must be between 0 and 6.");
        if (state.GroundObjects.Any(g => g is null || g.Object is null))
            throw new ArgumentException("Known ground objects require an object card.");
        var objects = (state.LootDeck?.DrawPile ?? []).Concat(state.LootDeck?.DiscardPile ?? [])
            .Concat(state.GroundObjects.Select(g => g.Object)).Concat(state.Units.SelectMany(u => u.Inventory)).ToArray();
        if (objects.Any(o => o is null || string.IsNullOrWhiteSpace(o.Id) || !Enum.IsDefined(o.Kind) || o.ItemTypeId is not null && o.EquipmentTypeId is not null) ||
            objects.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != objects.Length)
            throw new ArgumentException("Object cards require unique identities and supported kinds.");
        foreach (var obj in objects)
        {
            ValidateCardProvenance(state, obj);
            _ = state.ObjectContent(obj);
        }
        if (state.LootMarkers.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Position is null ||
                !MovementRules.Inside(state.Physical.Board, m.Position)) ||
            state.LootMarkers.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != state.LootMarkers.Count)
            throw new ArgumentException("Loot markers require unique identities and Cells on the board.");
        if (state.GroundObjects.Any(g => g is null || g.Position is null ||
                !MovementRules.Inside(state.Physical.Board, g.Position) || state.ObjectContent(g.Object).Category == LootCategory.Immediate))
            throw new ArgumentException("Known ground objects require an Item or Equipment and a Cell on the board.");
        foreach (var unit in state.Units)
        {
            var equipment = unit.Inventory.Select(state.EquipmentTypeId).OfType<string>().ToArray();
            if (equipment.Distinct(StringComparer.Ordinal).Count() != equipment.Length)
                throw new ArgumentException("A Unit may own at most one Equipment instance of each Equipment Type ID.");
            var type = state.Types.Single(t => t.Id == unit.TypeId);
            if (unit.Inventory.Any(o => unit.CurrentHp <= 0 || (state.EquipmentTypeId(o) is not null ? !type.CanHaveEquipment : state.ItemTypeId(o) is null || !type.CanHaveItems)))
                throw new ArgumentException("An inventory requires a living owner with the corresponding ownership capability.");
        }
    }
}
