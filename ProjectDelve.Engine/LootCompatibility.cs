namespace ProjectDelve.Engine;

// Historical card identities are confined to input and metadata adapters. The
// resolved Loot definition supplies the reward; identity never selects its effect.
internal static class LootCompatibility
{
    private static readonly IReadOnlyDictionary<LootCardKind, string> TypeIds = new Dictionary<LootCardKind, string>
    {
        [LootCardKind.PocketBreakfast] = "pocket-breakfast",
        [LootCardKind.SecondWind] = "second-wind",
        [LootCardKind.Bedsheet] = "bedsheet",
        [LootCardKind.LuckyBoots] = "lucky-boots",
        [LootCardKind.OfficialKey] = "official-key",
        [LootCardKind.GoatBell] = "goat-bell"
    };

    internal static string TypeId(LootCardKind kind) => TypeIds.TryGetValue(kind, out var id)
        ? id : throw new ArgumentException("Unknown legacy Loot Card kind.");
    internal static LootCardDefinition Definition(LootCardKind kind) => LootCardCatalog.All.Single(d => d.Id == TypeId(kind));
    internal static LootCardKind? LegacyKind(string id) => TypeIds.Where(p => p.Value == id).Select(p => (LootCardKind?)p.Key).SingleOrDefault();
    internal static string ActionId(string id) => LegacyKind(id) is not null ? id : $"loot:{Uri.EscapeDataString(id)}";

    // Before canonical Loot definitions existed, these IDs were legal local IDs.
    // Keep authored documents intact and distinguish their explicit references
    // from legacy enum rows when resolving a fresh game's content. The alias is
    // local to that resolved catalog; it never renames Item or Equipment Types.
    internal static ScenarioDefinition ResolveLocalIdCollisions(ScenarioDefinition source)
    {
        if (source.LootCardDefinitions is not { } local) return source;
        var canonicalIds = LootCardCatalog.All.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var explicitIds = (source.Loot?.Composition ?? []).OfType<LootCardQuantity>().Select(row => row.LootCardId)
            .Concat(source.Units.OfType<UnitPlacement>().SelectMany(unit => unit.Inventory ?? []).OfType<OwnedObject>().Select(obj => obj.LootCardId))
            .Concat((source.GroundObjects ?? []).OfType<GroundObject>().Select(ground => ground.Object?.LootCardId)).OfType<string>();
        // An otherwise unresolved authored reference must not accidentally become
        // valid merely because an internal alias happens to use that spelling.
        var used = local.Where(d => d is not null).Select(d => d.Id).Concat(canonicalIds).Concat(explicitIds).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        foreach (var id in local.Where(d => d is not null && canonicalIds.Contains(d.Id)).Select(d => d.Id).Distinct().Order(StringComparer.Ordinal))
        {
            var alias = $"local-loot:{id}";
            while (!used.Add(alias)) alias = $"local-loot:{alias}";
            aliases.Add(id, alias);
        }
        if (aliases.Count == 0) return source;
        OwnedObject Object(OwnedObject obj) => obj is not null && obj.LootCardId is { } id && aliases.TryGetValue(id, out var alias)
            ? obj with { LootCardId = alias } : obj!;
        return source with
        {
            LootCardDefinitions = local.Select(d => d is not null && aliases.TryGetValue(d.Id, out var alias) ? d with { Id = alias } : d!).ToList(),
            Loot = source.Loot?.Composition is { } composition ? source.Loot with
            {
                Composition = composition.Select(row => row is not null && row.LootCardId is { } id && aliases.TryGetValue(id, out var alias)
                    ? row with { LootCardId = alias } : row!).ToList()
            } : source.Loot,
            Units = source.Units.Select(unit => unit is not null && unit.Inventory is { } inventory
                ? unit with { Inventory = inventory.Select(Object).ToList() } : unit!).ToList(),
            GroundObjects = source.GroundObjects?.Select(ground => ground is null ? ground! : ground with { Object = Object(ground.Object) }).ToList()
        };
    }

    internal static IReadOnlyList<LootCardContent> ProjectLegacyCards(IReadOnlyList<LootCardDefinition> definitions,
        IReadOnlyList<ItemDefinition> items, IReadOnlyList<EquipmentDefinition> equipment) =>
        TypeIds.Select(pair =>
        {
            var definition = definitions.Single(d => d.Id == pair.Value);
            if (definition.ItemTypeId is { } itemId)
            {
                var item = items.Single(i => i.Id == itemId);
                var granted = item.LocalAbilities;
                var entries = granted.Select(a => a.CardEntry()).ToArray();
                // Historical metadata retains sentence casing; any supported
                // grant list can supply it, without requiring Second Wind's shape.
                var text = string.Join(" ", entries.Select(e => $"{e.Category}: " +
                    (e.Description.Length == 0 ? "" : char.ToLowerInvariant(e.Description[0]) + e.Description[1..])));
                var extra = granted.Length == 1 && granted[0].Mechanic is ExtraActionsMechanic action ? action.Amount : 0;
                return new LootCardContent(pair.Key, item.Name, LootCategory.Item, text + " Consume this Item.", ExtraActions: extra)
                    { LegacyKind = pair.Key };
            }
            if (definition.EquipmentTypeId is { } equipmentId)
            {
                var type = equipment.Single(e => e.Id == equipmentId);
                return new LootCardContent(pair.Key, type.Name, LootCategory.Equipment, type.Describe(),
                    MovementBonus: type.LocalAbilities.Where(a => a.Timing == AbilityTiming.Passive).Select(a => a.Mechanic)
                        .OfType<SelfModifiersMechanic>().SelectMany(m => m.Modifiers).Where(m => m.Stat == Stat.Mov).Sum(m => m.Amount))
                    { EquipmentTypeId = type.Id, LegacyKind = pair.Key };
            }
            return new LootCardContent(pair.Key, definition.Name, LootCategory.Immediate, definition.Description!,
                Healing: definition.Immediate is HealCollectorMechanic healing ? healing.Amount : 0)
                { LegacyKind = pair.Key };
        }).ToArray();

    // Pre-migration snapshots explicitly stored an empty (or local-only) Loot
    // catalog. Restore only definitions needed by their legacy physical deck.
    // Observations have no LootDeck and must never acquire hidden catalog data.
    internal static void MigrateLegacyCatalog(GameState state)
    {
        if (state.LootDeck is not { } deck) return;
        foreach (var kind in deck.DrawPile.Concat(deck.DiscardPile)
            .Where(c => c.LootCardId is null && c.ItemTypeId is null && c.EquipmentTypeId is null).Select(c => c.Kind).Distinct())
        {
            var id = TypeId(kind);
            if (!state.LootCardDefinitions.Any(d => d.Id == id))
                state.LootCardDefinitions.Add(LootCardCatalog.All.Single(d => d.Id == id));
        }
    }
}
