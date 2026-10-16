namespace ProjectDelve.Engine;

// Resource Types are content, independent of Side and Unit occupancy.
public sealed record ResourceType(string Id, string Name);
public static class ResourceTypes
{
    public static ResourceType Coal { get; } = new("coal", "Coal");
}
public sealed record Resource(ResourceType Type, Cell Position);

internal static class ResourceRules
{
    internal static void Validate(Resource resource, Board board)
    {
        if (resource is null || resource.Type is null || string.IsNullOrWhiteSpace(resource.Type.Id) ||
            string.IsNullOrWhiteSpace(resource.Type.Name) || resource.Position is null ||
            !MovementRules.Inside(board, resource.Position))
            throw new ArgumentException("A Resource requires a Resource Type and a Cell on the Board.");
    }

    internal static void TransportDuringOwnMove(GameState state, string unitId, Cell from, Cell to)
    {
        var type = state.Types.Single(type => type.Id == state.Units.Single(unit => unit.Id == unitId).TypeId);
        // Larger-footprint transport has no established rules.
        if (from == to || type.TransportResource is null || type.Footprint != Footprint.OneByOne ||
            state.Resources.Any(resource => resource.Position == to)) return;
        var index = state.Resources.FindIndex(resource => resource.Position == from);
        if (index >= 0) state.Resources[index] = state.Resources[index] with { Position = to };
    }
}
