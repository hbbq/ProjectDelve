namespace ProjectDelve.Engine;

// Old saves and scenarios allowed multiple card-linked copies in one inventory.
// Preserve every physical object while moving excess legacy copies to known ground.
// Explicit Equipment Type references use the new ownership validation unchanged.
internal static class EquipmentCompatibility
{
    internal static void MigrateLegacyDuplicateOwnership(GameState state)
    {
        for (var index = 0; index < state.Units.Count; index++)
        {
            var unit = state.Units[index];
            HashSet<string> owned = new(StringComparer.Ordinal);
            var inventory = unit.Inventory;
            foreach (var item in unit.Inventory)
            {
                if (state.EquipmentTypeId(item) is not { } typeId || owned.Add(typeId) || item.EquipmentTypeId is not null)
                    continue;
                var figure = state.Physical.Figures.SingleOrDefault(f => f.Id == unit.Id)
                    ?? throw new ArgumentException("Legacy duplicate Equipment requires its owner's anchor Cell for migration.");
                inventory = inventory.Remove(item);
                state.GroundObjects.Add(new(item, figure.Position));
            }
            state.Units[index] = unit with { Inventory = inventory };
        }
    }
}
