namespace ProjectDelve.Engine;

public enum LootCardKind { PocketBreakfast, SecondWind, Bedsheet, LuckyBoots, OfficialKey, GoatBell }
public enum LootCategory { Immediate, Item, Equipment }
// Card identity is independent of its source: initial inventories need no Loot Deck.
public sealed record OwnedObject(string Id, LootCardKind Kind)
{
    public LootCardContent Content => LootCards.Content(Kind);
}
public sealed record GroundObject(OwnedObject Object, Cell Position);
public sealed record LootMarker(string Id, Cell Position);
public sealed record LootCardContent(LootCardKind Kind, string Name, LootCategory Category, string Text,
    int Healing = 0, int MovementBonus = 0, int ExtraActions = 0);

public static class LootCards
{
    public static IReadOnlyList<LootCardContent> All { get; } = Array.AsReadOnly(new LootCardContent[]
    {
        new(LootCardKind.PocketBreakfast, "Pocket Breakfast", LootCategory.Immediate, "Restore 2 HP, without exceeding maximum HP.", Healing: 2),
        new(LootCardKind.SecondWind, "Second Wind", LootCategory.Item, "Bonus Action: gain +1 Action this activation. Consume this Item.", ExtraActions: 1),
        new(LootCardKind.Bedsheet, "One-Size-Fits-Most Bedsheet", LootCategory.Equipment, "Grants Phase."),
        new(LootCardKind.LuckyBoots, "Lucky Boots", LootCategory.Equipment, "MOV +1.", MovementBonus: 1),
        new(LootCardKind.OfficialKey, "Official-Looking Key", LootCategory.Equipment, "Grants Open Door."),
        new(LootCardKind.GoatBell, "Emergency Goat Bell", LootCategory.Equipment, "Action: stand up one adjacent friendly Lying Unit.")
    });

    public static LootCardContent Content(LootCardKind kind) => All.Single(c => c.Kind == kind);
}

// Composition contains actual copies, not weights. Draw removes a card permanently.
public sealed record LootCardQuantity(LootCardKind Kind, int Count);
public sealed record LootPreset(string Id, string Name, string Description, IReadOnlyList<LootCardQuantity> Composition);
public static class LootPresets
{
    public const string Default = "Classic";
    public static IReadOnlyList<LootPreset> All { get; } = Array.AsReadOnly(new LootPreset[]
    {
        new("Classic", "Classic", "Practical supplies: breakfast, extra Actions, boots and keys.", Array.AsReadOnly(new LootCardQuantity[]
            { new(LootCardKind.PocketBreakfast, 4), new(LootCardKind.SecondWind, 3), new(LootCardKind.LuckyBoots, 2), new(LootCardKind.OfficialKey, 1) })),
        new("Fun", "Fun", "A little of everything, with extra bedsheets and goat bells.", Array.AsReadOnly(new LootCardQuantity[]
            { new(LootCardKind.PocketBreakfast, 2), new(LootCardKind.SecondWind, 2), new(LootCardKind.LuckyBoots, 1), new(LootCardKind.OfficialKey, 1), new(LootCardKind.Bedsheet, 2), new(LootCardKind.GoatBell, 2) })),
        new("Experimental", "Experimental", "Phase, standing allies and extra Actions. Uses existing cards; no new experimental effects yet.", Array.AsReadOnly(new LootCardQuantity[]
            { new(LootCardKind.Bedsheet, 3), new(LootCardKind.GoatBell, 3), new(LootCardKind.SecondWind, 4) })),
        new("FarOut", "Far Out", "Bedsheets and goat bells only. A playful placeholder; no presentation-altering effects yet.", Array.AsReadOnly(new LootCardQuantity[]
            { new(LootCardKind.Bedsheet, 5), new(LootCardKind.GoatBell, 5) }))
    });
    public static LootPreset Find(string id) => All.SingleOrDefault(p => p.Id == id)
        ?? throw new ArgumentException($"Unknown Loot preset '{id}'.");
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
        return Composition is null ? this with { Preset = Preset ?? LootPresets.Default } : this;
    }
    public IReadOnlyList<LootCardQuantity> ResolveComposition()
    {
        var normalized = Normalize();
        IReadOnlyList<LootCardQuantity> rows = normalized.Composition ?? LootPresets.Find(normalized.Preset!).Composition
            .Select(r => r with { Count = checked(r.Count * (normalized.PresetMultiplier ?? 1)) }).ToList();
        if (rows.Any(r => r is null || !Enum.IsDefined(r.Kind) || r.Count is < 1 or > 1000) ||
            rows.Select(r => r.Kind).Distinct().Count() != rows.Count || rows.Sum(r => r.Count) > 6000)
            throw new ArgumentException("Custom Loot needs unique card IDs with quantities from 1 to 1000.");
        return rows;
    }
    public List<LootCardKind> ResolveCards() => ResolveComposition().SelectMany(r => Enumerable.Repeat(r.Kind, r.Count)).ToList();
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
    internal static bool CanOwn(UnitType type, LootCardKind kind) => LootCards.Content(kind).Category switch
    {
        LootCategory.Item => type.CanHaveItems,
        LootCategory.Equipment => type.CanHaveEquipment,
        _ => false
    };

    internal static void Validate(GameState state)
    {
        if (state.LootMarkers.Count > (state.LootDeck?.DrawPile.Count ?? 0))
            throw new ArgumentException("Every unopened Loot marker requires an available draw card.");
        if (state.Types.Any(t => t.LootDropSuccesses is < 0 or > 6))
            throw new ArgumentException("Loot drop successes must be between 0 and 6.");
        if (state.GroundObjects.Any(g => g is null || g.Object is null))
            throw new ArgumentException("Known ground objects require an object card.");
        var objects = (state.LootDeck?.DrawPile ?? []).Concat(state.LootDeck?.DiscardPile ?? [])
            .Concat(state.GroundObjects.Select(g => g.Object)).Concat(state.Units.SelectMany(u => u.Inventory)).ToArray();
        if (objects.Any(o => o is null || string.IsNullOrWhiteSpace(o.Id) || !Enum.IsDefined(o.Kind)) ||
            objects.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != objects.Length)
            throw new ArgumentException("Object cards require unique identities and supported kinds.");
        if (state.LootMarkers.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id) || m.Position is null ||
                !MovementRules.Inside(state.Physical.Board, m.Position)) ||
            state.LootMarkers.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != state.LootMarkers.Count)
            throw new ArgumentException("Loot markers require unique identities and Cells on the board.");
        if (state.GroundObjects.Any(g => g is null || g.Position is null ||
                !MovementRules.Inside(state.Physical.Board, g.Position) || LootCards.Content(g.Object.Kind).Category == LootCategory.Immediate))
            throw new ArgumentException("Known ground objects require an Item or Equipment and a Cell on the board.");
        foreach (var unit in state.Units)
            if (unit.Inventory.Any(o => unit.CurrentHp <= 0 || !CanOwn(state.Types.Single(t => t.Id == unit.TypeId), o.Kind)))
                throw new ArgumentException("An inventory requires a living owner with the corresponding ownership capability.");
    }
}
