using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Input compatibility for the canonical Ability extraction. These aliases only
// distinguish previously legal local IDs from newly canonical definitions; they
// never select mechanics, timing, usage, or gameplay targets.
internal static class AbilityCatalogCompatibility
{
    private static readonly HashSet<string> NewlyCanonicalIds = new(StringComparer.Ordinal)
    {
        "heal", "holy-wave", "fireball", "aura", "rage", "dash", "throwing-knife", "focus", "goat-bell-stand-up"
    };

    internal static ScenarioDefinition ResolveLocalIdCollisions(ScenarioDefinition source)
    {
        if (source.AbilityDefinitions is not { } local) return source;
        var canonicalIds = AbilityCatalog.All.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var references = (source.UnitTypes ?? []).OfType<UnitType>().SelectMany(t => Grants(t.AbilityGrants))
            .Concat((source.EquipmentTypes ?? []).OfType<EquipmentDefinition>().SelectMany(t => Grants(t.Grants)))
            .Concat((source.ItemTypes ?? []).OfType<ItemDefinition>().SelectMany(t => Grants(t.Grants)));
        var used = local.OfType<AbilityDefinition>().Select(d => d.Id).Concat(canonicalIds)
            .Concat(references.Select(g => g.AbilityId)).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        foreach (var id in local.OfType<AbilityDefinition>().Select(d => d.Id)
            .Where(id => canonicalIds.Contains(id) && NewlyCanonicalIds.Contains(id)).Distinct().Order(StringComparer.Ordinal))
            aliases.Add(id, AvailableId("local-ability:", id, used));
        if (aliases.Count == 0) return source;

        ImmutableArray<AbilityGrant> Rewrite(ImmutableArray<AbilityGrant> grants) => grants.IsDefault ? grants
            : grants.Select(g => g is not null && aliases.TryGetValue(g.AbilityId, out var alias)
                ? g with { AbilityId = alias } : g!).ToImmutableArray();
        return source with
        {
            AbilityDefinitions = local.Select(d => d is not null && aliases.TryGetValue(d.Id, out var alias)
                ? d with { Id = alias } : d!).ToList(),
            UnitTypes = source.UnitTypes?.Select(t => t is null ? t! : t with { AbilityGrants = Rewrite(t.AbilityGrants) }).ToList(),
            EquipmentTypes = source.EquipmentTypes?.Select(t => t is null ? t! : t with { Grants = Rewrite(t.Grants) }).ToList(),
            ItemTypes = source.ItemTypes?.Select(t => t is null ? t! : t with { Grants = Rewrite(t.Grants) }).ToList()
        };
    }

    // An omitted Equipment catalog means canonical default Equipment. An older
    // explicit Ability catalog may lack its newly extracted dependencies, or may
    // already use those IDs for unrelated local Abilities. Preserve that saved
    // catalog and its existing references, rebinding only the default Equipment.
    // Explicit Equipment catalogs (including filtered observations) stay intact.
    internal static void NormalizeMissingEquipmentCatalog(GameState state, bool equipmentCatalogProvided)
    {
        if (equipmentCatalogProvided) return;
        var used = state.AbilityDefinitions.OfType<AbilityDefinition>().Select(d => d.Id)
            .Concat(state.Types.OfType<UnitType>().SelectMany(t => Grants(t.AbilityGrants)).Select(g => g.AbilityId))
            .Concat(state.ItemTypes.OfType<ItemDefinition>().SelectMany(t => Grants(t.Grants)).Select(g => g.AbilityId))
            .Concat(state.EquipmentTypes.OfType<EquipmentDefinition>().SelectMany(t => Grants(t.Grants)).Select(g => g.AbilityId))
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        foreach (var id in state.EquipmentTypes.OfType<EquipmentDefinition>().SelectMany(t => Grants(t.Grants))
            .Select(g => g.AbilityId).Distinct(StringComparer.Ordinal))
        {
            var canonical = AbilityCatalog.All.Single(d => d.Id == id);
            var existing = state.AbilityDefinitions.SingleOrDefault(d => d.Id == id);
            if (existing == canonical) continue;
            if (existing is null)
            {
                state.AbilityDefinitions.Add(canonical);
                used.Add(id);
                continue;
            }
            var alias = AvailableId("canonical-equipment:", id, used);
            state.AbilityDefinitions.Add(canonical with { Id = alias });
            aliases.Add(id, alias);
        }
        if (aliases.Count == 0) return;
        state.EquipmentTypes = state.EquipmentTypes.Select(t => t with
        {
            Grants = t.Grants.Select(g => aliases.TryGetValue(g.AbilityId, out var alias) ? g with { AbilityId = alias } : g).ToImmutableArray()
        }).ToList();
    }

    private static IEnumerable<AbilityGrant> Grants(ImmutableArray<AbilityGrant> grants) => grants.IsDefault
        ? [] : grants.OfType<AbilityGrant>();

    private static string AvailableId(string prefix, string id, HashSet<string> used)
    {
        var alias = prefix + id;
        while (!used.Add(alias)) alias = prefix + alias;
        return alias;
    }
}
