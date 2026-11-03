using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public static class WorkspaceScenarioExport
{
    // Export current definitions as ordinary scenario-local declarations, with no pins.
    public static ScenarioDefinition Capture(ScenarioDefinition scenario, ContentBundle workspace, ContentBundle canonical)
    {
        if (ScenarioContentReferences.Identities(workspace).IsEmpty && workspace.DefaultWorldDeck.IsEmpty) return scenario;
        if (!workspace.LootPresets.IsEmpty || !workspace.Resources.IsEmpty || !workspace.DefaultWorldDeck.IsEmpty)
            throw new ArgumentException("Workspace preset, Resource and World Deck definitions cannot be embedded in this scenario format. Resolve these dependencies before export.");
        if (workspace.Terrain.Any(t => t.LegacyKind is not null || t.LegacyProductionIdentity is not null || t.Assembly is not null))
            throw new ArgumentException("Workspace Terrain with legacy identities or embedded assemblies cannot be represented as scenario-local content. Export requires authored surface/component definitions.");
        var effective = ScenarioContentReferences.ScenarioCatalog(ContentComposition.ComposeScopes(canonical, workspace).Definitions, scenario);
        var ids = ScenarioContentReferences.Identities(workspace).ToHashSet();
        var closure = ids.Concat(effective.Dependencies.Closure(ids)).ToHashSet();
        var content = ScenarioContentReferences.Filter(effective.Definitions, closure.Contains);
        List<T> Merge<T>(IEnumerable<T> captured, IEnumerable<T>? local) where T : class =>
            captured.Concat(local ?? []).GroupBy(x => ContentDefinitions.Identity(x)).Select(g => g.Last()).ToList();
        return scenario with
        {
            UnitTypes = Merge(content.Units, scenario.UnitTypes), EquipmentTypes = Merge(content.Equipment, scenario.EquipmentTypes),
            ItemTypes = Merge(content.Items, scenario.ItemTypes), LootCardDefinitions = Merge(content.LootCards, scenario.LootCardDefinitions),
            WorldCardDefinitions = Merge(content.WorldCards, scenario.WorldCardDefinitions),
            TerrainTypes = Merge(content.Terrain.Where(t => t.LegacyKind is null && t.Assembly is null), scenario.TerrainTypes),
            TerrainAssemblies = Merge(workspace.Assemblies, scenario.TerrainAssemblies), FixedComponentTypes = Merge(content.FixedComponents, scenario.FixedComponentTypes)
        };
    }
}
