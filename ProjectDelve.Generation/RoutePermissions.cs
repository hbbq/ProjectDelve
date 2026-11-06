namespace ProjectDelve.Generation;

/// <summary>Explicit, attempt-local access to cells belonging to one exact Site owner.</summary>
public sealed record RouteAccessGrant(string ExactOwnerId, IReadOnlyCollection<int> TraverseCells,
    IReadOnlyCollection<int> WritableCells, IReadOnlyCollection<EdgeKey> WritableEdges);

public sealed record RouteMembership(int Index, string RouteId);
