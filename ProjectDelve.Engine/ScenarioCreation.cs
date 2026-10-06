using System.Globalization;

namespace ProjectDelve.Engine;

internal static class ScenarioCreation
{
    internal static GameState Create(ScenarioDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Board is not { } board || definition.UnitTypeIds is null ||
            definition.Units is null || definition.Agency is null || board.Cells is null || board.Edges is null)
            throw new ArgumentException("Scenario definition requires Board, UnitTypeIds, Units and Agency data.", nameof(definition));
        if (board.Width < 1 || board.Height < 1 || !Enum.IsDefined(board.DefaultTerrain))
            throw new ArgumentException("Invalid board dimensions or default terrain.", nameof(definition));
        bool Inside(Cell? cell) => cell is not null && cell.X >= 0 && cell.Y >= 0 && cell.X < board.Width && cell.Y < board.Height;
        if (board.Cells.Any(c => c is null || !Inside(c.Position) || !Enum.IsDefined(c.Terrain)))
            throw new ArgumentException("Invalid Cell override.", nameof(definition));
        if (board.Cells.Select(c => c.Position).Distinct().Count() != board.Cells.Count)
            throw new ArgumentException("Duplicate Cell override.", nameof(definition));
        if (board.Edges.Any(e => e is null || !Inside(e.Position) || !Enum.IsDefined(e.Direction) || !Enum.IsDefined(e.Kind) ||
            e.Direction == EdgeDirection.Right && e.Position.X == board.Width - 1 ||
            e.Direction == EdgeDirection.Down && e.Position.Y == board.Height - 1))
            throw new ArgumentException("Invalid internal Edge definition.", nameof(definition));
        if (board.Edges.Select(e => (e.Position, e.Direction)).Distinct().Count() != board.Edges.Count)
            throw new ArgumentException("Duplicate Edge Position + Direction.", nameof(definition));
        if (definition.UnitTypeIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Unit Type IDs must be nonblank.", nameof(definition));
        var declared = definition.UnitTypeIds.ToHashSet(StringComparer.Ordinal);
        if (declared.Count != definition.UnitTypeIds.Count)
            throw new ArgumentException("Duplicate declared Unit Type ID.", nameof(definition));
        foreach (var unit in definition.Units)
        {
            if (unit is null || string.IsNullOrWhiteSpace(unit.UnitTypeId) || string.IsNullOrWhiteSpace(unit.SideId) ||
                unit.Anchor is null || !Enum.IsDefined(unit.Posture))
                throw new ArgumentException("Invalid Unit placement data.", nameof(definition));
            if (!declared.Contains(unit.UnitTypeId))
                throw new ArgumentException($"Placement Unit Type '{unit.UnitTypeId}' is not declared.", nameof(definition));
        }
        foreach (var agency in definition.Agency)
            if (agency is null || string.IsNullOrWhiteSpace(agency.UnitTypeId) || string.IsNullOrWhiteSpace(agency.SideId) ||
                !Enum.IsDefined(agency.Controller) || !declared.Contains(agency.UnitTypeId))
                throw new ArgumentException("Invalid agency assignment; its Unit Type must be declared.", nameof(definition));
        if (definition.Agency.Select(a => (a.UnitTypeId, a.SideId)).Distinct().Count() != definition.Agency.Count)
            throw new ArgumentException("Duplicate agency assignment for Unit Type + Side.", nameof(definition));
        if (definition.Units.Any(u => !definition.Agency.Any(a => a.UnitTypeId == u.UnitTypeId && a.SideId == u.SideId)))
            throw new ArgumentException("Every initial Unit Type + Side needs an explicit agency assignment.", nameof(definition));

        var types = definition.UnitTypeIds.Select(id => UnitContent.Find(id)
            ?? throw new ArgumentException($"Unknown Unit Type ID '{id}'.", nameof(definition))).ToList();
        var overrides = board.Cells.ToDictionary(c => c.Position, c => c.Terrain);
        List<TerrainTile> terrain = [];
        if (board.DefaultTerrain == TerrainKind.StoneFloor)
            terrain.AddRange(board.Cells.Select(c => new TerrainTile(c.Position, c.Terrain)));
        else
            for (var y = 0; y < board.Height; y++)
            for (var x = 0; x < board.Width; x++)
            {
                var cell = new Cell(x, y);
                terrain.Add(new(cell, overrides.GetValueOrDefault(cell, board.DefaultTerrain)));
            }
        var state = new GameState
        {
            Physical = new(new Board(board.Width, board.Height, board.Edges.Select(e => new Edge(e.Position,
                e.Direction == EdgeDirection.Right ? new(e.Position.X + 1, e.Position.Y) : new(e.Position.X, e.Position.Y + 1),
                e.Kind)).ToList()) { Terrain = terrain }, []),
            Types = types, Units = [],
            Controllers = definition.Agency.Select(a => new ControllerAssignment(new(a.UnitTypeId, a.SideId), a.Controller)).ToList()
        };
        // The same validation also remains at round start. Validate content before CreateUnit.
        GameEngine.ValidateScenario(state);
        for (var index = 0; index < definition.Units.Count; index++)
        {
            var placement = definition.Units[index];
            var type = types.Single(t => t.Id == placement.UnitTypeId);
            if (placement.InitialHp is { } hp && (hp < 1 || hp > type.Hp))
                throw new ArgumentException($"InitialHp for placement {index} must be between 1 and {type.Hp}.", nameof(definition));
            // A separate namespace from current canonical summoning prefixes; runtime creation also checks collisions.
            var id = "scenario-unit-" + (index + 1).ToString(CultureInfo.InvariantCulture);
            state.PlaceUnit(type.Id, id, placement.SideId, placement.Anchor, placement.Posture);
            if (placement.InitialHp is { } initialHp) state.Units[^1] = state.Units[^1] with { CurrentHp = initialHp };
        }
        GameEngine.ValidateScenario(state);
        return state;
    }
}
