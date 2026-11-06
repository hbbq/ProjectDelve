namespace ProjectDelve.Generation;

public enum RoutePurpose { ThroughRoute, SiteConnection, ExplorationLink }
public enum RouteEndpointKind { Cell, Boundary, Site, Network }
public sealed record RouteEndpoint(string Id, IReadOnlyCollection<int> Cells, RouteEndpointKind Kind = RouteEndpointKind.Cell,
    CardinalSide? Side = null, string? SiteId = null);
public sealed record RouteRequirements(int Width = 1, int RequiredFootprint = 1, PreparationPolicy? Preparation = null,
    int MaxLength = 1000, int MaxWaterCells = 0, int MaxWaterRun = 0, bool ClosedDoorsTraversable = false,
    int MaxAttempts = 3, int MaxExpandedStates = 60000, IReadOnlyCollection<int>? AvoidCells = null, string? SurfaceTypeId = null)
{
    public static RouteRequirements For(RouteProfileKind profile, int intensity = 2, bool waterCrossings = false) => new(
        Width: profile == RouteProfileKind.Road ? 2 : 1,
        Preparation: new(ClearTrees: true, ExcavateRock: profile is RouteProfileKind.RockPassage or RouteProfileKind.Road,
            FillWater: waterCrossings, MaxChangedCells: 400 * Math.Clamp(intensity, 1, 3), MaxCost: 2000 * Math.Clamp(intensity, 1, 3)),
        MaxWaterCells: waterCrossings ? 12 * Math.Clamp(intensity, 1, 3) : 0,
        MaxWaterRun: waterCrossings ? 4 * Math.Clamp(intensity, 1, 3) : 0);
}
public sealed record RoutePreferences(IReadOnlyCollection<int>? PreferredCells = null, int TurnPenalty = 2,
    int VariationWeight = 2, IReadOnlyCollection<int>? AvoidCells = null)
{
    public static RoutePreferences For(RouteProfileKind profile) => profile switch
    {
        RouteProfileKind.ForestTrail => new(TurnPenalty: 2, VariationWeight: 3),
        RouteProfileKind.Road => new(TurnPenalty: 10, VariationWeight: 1),
        RouteProfileKind.RockPassage => new(TurnPenalty: 8, VariationWeight: 1),
        _ => new(TurnPenalty: 4, VariationWeight: 2)
    };
}
public sealed record RouteRequest(string Key, CellBounds Area, RouteEndpoint Start, RouteEndpoint Destination,
    RouteProfileKind ProfileKind = RouteProfileKind.MinorPath, RoutePurpose Purpose = RoutePurpose.ThroughRoute,
    WorldWindow? Window = null, RouteRequirements? Requirements = null, RoutePreferences? Preferences = null,
    IReadOnlyCollection<RouteAccessGrant>? AccessGrants = null);
public sealed record RouteRun(int WorldX, int WorldY, CardinalSide Direction, int Length);
public sealed record RouteGeometry(int WorldX, int WorldY, IReadOnlyList<RouteRun> Runs)
{
    public IEnumerable<int> LocalIndices(int width, WorldWindow? window = null)
    {
        var ox = window?.OffsetX ?? 0; var oy = window?.OffsetY ?? 0;
        yield return checked((WorldY - oy) * width + (WorldX - ox));
        foreach (var run in Runs)
        {
            var dx = run.Direction == CardinalSide.East ? 1 : run.Direction == CardinalSide.West ? -1 : 0;
            var dy = run.Direction == CardinalSide.South ? 1 : run.Direction == CardinalSide.North ? -1 : 0;
            for (var n = 1; n <= run.Length; n++)
                yield return checked((run.WorldY + dy * n - oy) * width + (run.WorldX + dx * n - ox));
        }
    }
}
public sealed record RoutePort(string Id, int AnchorIndex, int WorldX, int WorldY, RouteEndpointKind Kind,
    CardinalSide? Side = null, string? SiteId = null);
public sealed record RouteStats(int Attempts, int ExpandedStates, int QueuePeak, long SearchCost, int Length, int Turns, int ReusedCells);
public sealed record RouteCrossing(GeneratedArea Footprint);
public sealed record RouteTunnelEntrance(int AnchorIndex, int WorldX, int WorldY, EdgeKey Edge);
public sealed record RouteInfo(string Id, RoutePurpose Purpose, RouteProfileKind Profile, GeneratedArea Corridor,
    RouteGeometry Geometry, RoutePort Start, RoutePort End, int VerifiedFootprint, int RealizedWidth,
    PreparationSummary Preparation, RouteStats Stats, IReadOnlyList<RouteCrossing> Crossings,
    IReadOnlyList<RouteTunnelEntrance> TunnelEntrances, string MaterializationId, long SourceRevision)
{
    public bool RequiresOpeningDoors { get; init; }
    public string SurfaceTypeId { get; init; } = "";
    public IReadOnlyList<string> RealizedSurfaceTypeIds { get; init; } = [];
}
public sealed record RoutePlan(BoardPatch Patch, RouteInfo Route, IReadOnlyList<ConstructionOperation> Operations)
{
    public double SearchMilliseconds { get; init; }
    public double MaterializationMilliseconds { get; init; }
}
