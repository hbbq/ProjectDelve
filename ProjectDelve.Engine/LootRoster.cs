namespace ProjectDelve.Engine;

// Authored canonical Loot content. Catalogs validate/resolve these declarations;
// the compatibility helpers project Item/Equipment metadata without duplicating it.
public static class LootRoster
{
    public const string DefaultPresetId = "Classic";
    public static IReadOnlyList<LootCardDefinition> Definitions { get; } = Array.AsReadOnly(new LootCardDefinition[]
    {
        new("pocket-breakfast", "Pocket Breakfast") { Immediate = new HealCollectorMechanic(Amount: 2) },
        new("second-wind", "Second Wind") { ItemTypeId = "second-wind" },
        new("bedsheet", "One-Size-Fits-Most Bedsheet") { EquipmentTypeId = "bedsheet" },
        new("lucky-boots", "Lucky Boots") { EquipmentTypeId = "lucky-boots" },
        new("official-key", "Official-Looking Key") { EquipmentTypeId = "official-key" },
        new("goat-bell", "Emergency Goat Bell") { EquipmentTypeId = "goat-bell" }
    });

    public static IReadOnlyList<LootCardContent> All => LootCompatibility.ProjectLegacyCards(
        Definitions, ItemCatalog.All, EquipmentCatalog.All);

    public static IReadOnlyList<LootPreset> Presets { get; } = Array.AsReadOnly(new LootPreset[]
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
}
