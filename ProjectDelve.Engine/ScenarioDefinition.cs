namespace ProjectDelve.Engine;

// Initial setup only. Content and runtime identity are resolved during game creation.
public sealed record ScenarioDefinition
{
    public WorldEffectsSettings? WorldEffects { get; init; }
    public required BoardDefinition Board { get; init; }
    public required List<string> UnitTypeIds { get; init; }
    public required List<UnitPlacement> Units { get; init; }
    public required List<AgencyAssignment> Agency { get; init; }
}

public sealed record BoardDefinition
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public TerrainKind DefaultTerrain { get; init; } = TerrainKind.StoneFloor;
    public List<CellDefinition> Cells { get; init; } = [];
    public List<EdgeDefinition> Edges { get; init; } = [];
}

public sealed record CellDefinition(Cell Position, TerrainKind Terrain);
public enum EdgeDirection { Right, Down }
public sealed record EdgeDefinition(Cell Position, EdgeDirection Direction, EdgeKind Kind);
public sealed record UnitPlacement(string UnitTypeId, string SideId, Cell Anchor,
    Posture Posture = Posture.Upright, int? InitialHp = null);
public sealed record AgencyAssignment(string UnitTypeId, string SideId, ControllerKind Controller);
