namespace ProjectDelve.Engine;

// Canonical Item content only. A grant references the authoritative Ability;
// physical consumption and use accounting are shared Engine operations.
public static class ItemRoster
{
    public static IReadOnlyList<ItemDefinition> All { get; } = Array.AsReadOnly(new ItemDefinition[]
    {
        new("second-wind", "Second Wind") { Grants = [new("use", "second-wind")] }
    });
}
