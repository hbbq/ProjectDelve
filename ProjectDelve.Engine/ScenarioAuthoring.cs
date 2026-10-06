namespace ProjectDelve.Engine;

// These temporary authoring values expand into concrete definition data, never GameState.
public static class Scenario
{
    public sealed record Placement(Cell Anchor, Posture Posture = Posture.Upright, int? InitialHp = null);
    public sealed record UnitGroup(string UnitTypeId, string SideId, ControllerKind Controller, Placement[] Placements);

    public static Placement At(int x, int y, Posture posture = Posture.Upright, int? initialHp = null) =>
        new(new(x, y), posture, initialHp);

    public static UnitGroup Group(string unitTypeId, string sideId, ControllerKind controller, params Placement[] placements) =>
        new(unitTypeId, sideId, controller, placements);

    public static ScenarioDefinition Define(BoardDefinition board, UnitGroup[] groups, string[]? unitTypeIds = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(groups);
        List<UnitPlacement> units = [];
        List<AgencyAssignment> agency = [];
        foreach (var group in groups)
        {
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(group.Placements);
            var existing = agency.SingleOrDefault(a => a.UnitTypeId == group.UnitTypeId && a.SideId == group.SideId);
            if (existing is not null && existing.Controller != group.Controller)
                throw new ArgumentException($"Conflicting agency for '{group.UnitTypeId}' on Side '{group.SideId}'.", nameof(groups));
            if (existing is null) agency.Add(new(group.UnitTypeId, group.SideId, group.Controller));
            foreach (var placement in group.Placements)
            {
                ArgumentNullException.ThrowIfNull(placement);
                units.Add(new(group.UnitTypeId, group.SideId, placement.Anchor, placement.Posture, placement.InitialHp));
            }
        }
        return new()
        {
            Board = board,
            UnitTypeIds = unitTypeIds is null ? groups.Select(g => g.UnitTypeId).Distinct(StringComparer.Ordinal).ToList() : [.. unitTypeIds],
            Units = units,
            Agency = agency
        };
    }

    public static BoardDefinition Map(int width, int height, CellDefinition[]? cells = null,
        EdgeDefinition[]? edges = null, TerrainKind defaultTerrain = TerrainKind.StoneFloor) => new()
    {
        Width = width, Height = height, DefaultTerrain = defaultTerrain,
        Cells = cells is null ? [] : [.. cells],
        Edges = ResolveEdges(edges)
    };

    // Authoring is ordered: later entries replace a Position + Direction in its first slot.
    // Only the unique concrete result enters BoardDefinition; raw definitions stay strict.
    private static List<EdgeDefinition> ResolveEdges(EdgeDefinition[]? entries)
    {
        List<EdgeDefinition> edges = [];
        Dictionary<(Cell Position, EdgeDirection Direction), int> locations = [];
        foreach (var entry in entries ?? [])
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Position);
            var location = (entry.Position, entry.Direction);
            if (locations.TryGetValue(location, out var index)) edges[index] = entry;
            else
            {
                locations.Add(location, edges.Count);
                edges.Add(entry);
            }
        }
        return edges;
    }

    public static CellDefinition Tile(int x, int y, TerrainKind terrain) => new(new(x, y), terrain);
    public static CellDefinition[] Tiles(TerrainKind terrain, params Placement[] positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Select(position =>
        {
            ArgumentNullException.ThrowIfNull(position);
            return new CellDefinition(position.Anchor, terrain);
        }).ToArray();
    }

    public static EdgeDefinition Edge(int x, int y, EdgeDirection direction, EdgeKind kind) => new(new(x, y), direction, kind);

    public static EdgeDefinition[] Vertical(int x, int top, int bottom, EdgeKind kind) =>
        Enumerable.Range(top, checked(bottom - top + 1))
            .Select(y => Edge(x, y, EdgeDirection.Right, kind)).ToArray();

    public static EdgeDefinition[] Horizontal(int y, int left, int right, EdgeKind kind) =>
        Enumerable.Range(left, checked(right - left + 1))
            .Select(x => Edge(x, y, EdgeDirection.Down, kind)).ToArray();

    public static EdgeDefinition[] Room(int left, int top, int right, int bottom, EdgeKind kind = EdgeKind.Wall) =>
    [
        .. Vertical(left - 1, top, bottom, kind),
        .. Vertical(right, top, bottom, kind),
        .. Horizontal(top - 1, left, right, kind),
        .. Horizontal(bottom, left, right, kind)
    ];
}
