namespace ProjectDelve.Engine;

// Initial setup only. Content and runtime identity are resolved during game creation.
public sealed record ScenarioDefinition
{
    public LootSettings? Loot { get; init; }
    public Dictionary<string, LootUnitOverride>? LootUnitOverrides { get; init; }
    public List<LootMarker>? LootMarkers { get; init; }
    public List<GroundObject>? GroundObjects { get; init; }
    public List<string>? NeutralSides { get; init; }
    public List<string>? FogOfWarSides { get; init; }
    public List<SideObjective>? Objectives { get; init; }
    public WorldEffectsSettings? WorldEffects { get; init; }
    public required BoardDefinition Board { get; init; }
    public required List<string> UnitTypeIds { get; init; }
    public required List<UnitPlacement> Units { get; init; }
    public required List<AgencyAssignment> Agency { get; init; }
    public List<InitialGroupOrder>? Orders { get; init; }
    public List<Waypoint>? Waypoints { get; init; }
    public List<Route>? Routes { get; init; }
    // Optional concrete scenario content; declared IDs without an entry use canonical content.
    public List<UnitType>? UnitTypes { get; init; }
    public List<StoredResource>? StoredResources { get; init; }
    public List<Machine>? Machines { get; init; }
    public List<Resource>? Resources { get; init; }
    public List<CellMarker>? CellMarkers { get; init; }
}

public sealed record BoardDefinition
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public TerrainKind DefaultTerrain { get; init; } = TerrainKind.StoneFloor;
    public List<CellDefinition> Cells { get; init; } = [];
    public List<EdgeDefinition> Edges { get; init; } = [];
}

public sealed record CellDefinition(Cell Position, TerrainKind Terrain, RailDirection? Direction = null);
public enum EdgeDirection { Right, Down }
public sealed record EdgeDefinition(Cell Position, EdgeDirection Direction, EdgeKind Kind);
public sealed record UnitPlacement(string UnitTypeId, string SideId, Cell Anchor,
    Posture Posture = Posture.Upright, int? InitialHp = null, string? UnitId = null)
{
    public List<OwnedObject>? Inventory { get; init; }
}
public sealed record AgencyAssignment(string UnitTypeId, string SideId, ControllerKind Controller);
public sealed record InitialGroupOrder(string UnitTypeId, string SideId, OrderKind Kind, Cell? Destination = null,
    OrderTarget? Target = null, FollowRoute? Route = null);
