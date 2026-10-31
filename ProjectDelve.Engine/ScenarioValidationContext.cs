namespace ProjectDelve.Engine;

// Optional authoring locations on validation failures; no separate validation rules.
public static class ScenarioValidationContext
{
    private const string PathKey = "ProjectDelve.ScenarioPath";
    public static string? PathOf(Exception error) => error.Data[PathKey] as string;

    internal static ArgumentException At(ArgumentException error, string path)
    {
        error.Data[PathKey] = path;
        return error;
    }

    // Catalog construction can fail before a GameState exists. Canonical rows
    // precede authored rows here, including historically aliased local Loot.
    internal static ArgumentException ForLocalCatalog(ArgumentException error, int canonicalCount)
    {
        var path = PathOf(error);
        if (path is null) return error;
        var opening = path.IndexOf('[');
        var closing = path.IndexOf(']');
        if (opening < 0 || closing < opening || !int.TryParse(path[(opening + 1)..closing], out var index) || index < canonicalCount)
            return error;
        return Rebase(error, path, closing, $"{path[..opening]}[{index - canonicalCount}]");
    }

    // Runtime catalogs include canonical entries and may order Units by UnitTypeIds.
    // At scenario creation only, point owner diagnostics back to the authored lists.
    internal static ArgumentException ForDefinition(ArgumentException error, GameState state, ScenarioDefinition definition)
    {
        var path = PathOf(error);
        if (path is null) return error;
        var opening = path.IndexOf('[');
        var closing = path.IndexOf(']');
        if (opening < 0 || closing < opening || !int.TryParse(path[(opening + 1)..closing], out var index) || index < 0)
            return error;
        var field = path[..opening];
        var authoredIndex = field switch
        {
            "unitTypes" when index < state.Types.Count => definition.UnitTypes?.FindIndex(type => type.Id == state.Types[index].Id),
            "equipmentTypes" when index < state.EquipmentTypes.Count => definition.EquipmentTypes?.FindIndex(type => type.Id == state.EquipmentTypes[index].Id),
            "itemTypes" when index < state.ItemTypes.Count => definition.ItemTypes?.FindIndex(type => type.Id == state.ItemTypes[index].Id),
            // The historical Loot adapter can alias local IDs, but preserves
            // their authored order after the canonical catalog.
            "lootCardDefinitions" when definition.LootCardDefinitions is { } loot &&
                index >= LootCardCatalog.All.Count && index - LootCardCatalog.All.Count < loot.Count => index - LootCardCatalog.All.Count,
            _ => null
        };
        if (authoredIndex is null or < 0 || authoredIndex == index) return error;
        return Rebase(error, path, closing, $"{field}[{authoredIndex}]");
    }

    private static ArgumentException Rebase(ArgumentException error, string path, int closing, string authoredOwner)
    {
        var runtimeOwner = path[..(closing + 1)];
        // Mechanic diagnostics also include their field path in the message.
        var message = error.Message.Replace(runtimeOwner, authoredOwner, StringComparison.Ordinal);
        if (message != error.Message)
        {
            var rebased = new ArgumentException(message, error);
            foreach (System.Collections.DictionaryEntry entry in error.Data) rebased.Data[entry.Key] = entry.Value;
            error = rebased;
        }
        return At(error, authoredOwner + path[(closing + 1)..]);
    }
}
