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
        if (definition.WorldEffects is { } world && (world.CardsPerRound < 0 || world.Cycling < 1))
            throw ScenarioValidationContext.At(new ArgumentException("World Effects require CardsPerRound >= 0 and Cycling >= 1.", nameof(definition)), "worldEffects");
        if (board.Width < 1 || board.Height < 1 || !Enum.IsDefined(board.DefaultTerrain))
            throw ScenarioValidationContext.At(new ArgumentException("Invalid board dimensions or default terrain.", nameof(definition)), "board");
        bool Inside(Cell? cell) => cell is not null && cell.X >= 0 && cell.Y >= 0 && cell.X < board.Width && cell.Y < board.Height;
        HashSet<Cell> seenCells = [];
        for (var index = 0; index < board.Cells.Count; index++)
        {
            var cell = board.Cells[index];
            if (cell is null || !Inside(cell.Position) || !Enum.IsDefined(cell.Terrain))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Cell override.", nameof(definition)), $"board.cells[{index}]");
            if (!seenCells.Add(cell.Position))
                throw ScenarioValidationContext.At(new ArgumentException("Duplicate Cell override.", nameof(definition)), $"board.cells[{index}]");
        }
        HashSet<(Cell, EdgeDirection)> seenEdges = [];
        for (var index = 0; index < board.Edges.Count; index++)
        {
            var edge = board.Edges[index];
            if (edge is null || !Inside(edge.Position) || !Enum.IsDefined(edge.Direction) || !Enum.IsDefined(edge.Kind) ||
                edge.Direction == EdgeDirection.Right && edge.Position.X == board.Width - 1 ||
                edge.Direction == EdgeDirection.Down && edge.Position.Y == board.Height - 1)
                throw ScenarioValidationContext.At(new ArgumentException("Invalid internal Edge definition.", nameof(definition)), $"board.edges[{index}]");
            if (!seenEdges.Add((edge.Position, edge.Direction)))
                throw ScenarioValidationContext.At(new ArgumentException("Duplicate Edge Position + Direction.", nameof(definition)), $"board.edges[{index}]");
        }
        if (definition.UnitTypeIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Unit Type IDs must be nonblank.", nameof(definition));
        var declared = definition.UnitTypeIds.ToHashSet(StringComparer.Ordinal);
        if (declared.Count != definition.UnitTypeIds.Count)
            throw new ArgumentException("Duplicate declared Unit Type ID.", nameof(definition));
        for (var index = 0; index < definition.Units.Count; index++)
        {
            var unit = definition.Units[index];
            if (unit is null || string.IsNullOrWhiteSpace(unit.UnitTypeId) || string.IsNullOrWhiteSpace(unit.SideId) ||
                unit.Anchor is null || !Enum.IsDefined(unit.Posture))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Unit placement data.", nameof(definition)), $"units[{index}]");
            if (!declared.Contains(unit.UnitTypeId))
                throw ScenarioValidationContext.At(new ArgumentException($"Placement Unit Type '{unit.UnitTypeId}' is not declared.", nameof(definition)), $"units[{index}]");
        }
        HashSet<(string, string)> seenAgency = [];
        for (var index = 0; index < definition.Agency.Count; index++)
        {
            var agency = definition.Agency[index];
            if (agency is null || string.IsNullOrWhiteSpace(agency.UnitTypeId) || string.IsNullOrWhiteSpace(agency.SideId) ||
                !Enum.IsDefined(agency.Controller) || !declared.Contains(agency.UnitTypeId))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid agency assignment; its Unit Type must be declared.", nameof(definition)), $"agency[{index}]");
            if (!seenAgency.Add((agency.UnitTypeId, agency.SideId)))
                throw ScenarioValidationContext.At(new ArgumentException("Duplicate agency assignment for Unit Type + Side.", nameof(definition)), $"agency[{index}]");
        }
        for (var index = 0; index < definition.Units.Count; index++)
            if (!definition.Agency.Any(a => a.UnitTypeId == definition.Units[index].UnitTypeId && a.SideId == definition.Units[index].SideId))
                throw ScenarioValidationContext.At(new ArgumentException("Every initial Unit Type + Side needs an explicit agency assignment.", nameof(definition)), $"units[{index}]");

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
            Types = types, Units = [], WorldEffects = definition.WorldEffects,
            WorldDeck = definition.WorldEffects is null ? null : WorldCards.CreateDeck(),
            Controllers = definition.Agency.Select(a => new ControllerAssignment(new(a.UnitTypeId, a.SideId), a.Controller)).ToList()
        };
        // The same validation also remains at round start. Validate content before CreateUnit.
        GameEngine.ValidateScenario(state);
        for (var index = 0; index < definition.Units.Count; index++)
        {
            var placement = definition.Units[index];
            var type = types.Single(t => t.Id == placement.UnitTypeId);
            if (placement.InitialHp is { } hp && (hp < 1 || hp > type.Hp))
                throw ScenarioValidationContext.At(new ArgumentException($"InitialHp for placement {index} must be between 1 and {type.Hp}.", nameof(definition)), $"units[{index}]");
            // A separate namespace from current canonical summoning prefixes; runtime creation also checks collisions.
            var id = "scenario-unit-" + (index + 1).ToString(CultureInfo.InvariantCulture);
            try { state.PlaceUnit(type.Id, id, placement.SideId, placement.Anchor, placement.Posture); }
            catch (ArgumentException error) { throw ScenarioValidationContext.At(error, $"units[{index}]"); }
            if (placement.InitialHp is { } initialHp) state.Units[^1] = state.Units[^1] with { CurrentHp = initialHp };
        }
        GameEngine.ValidateScenario(state);
        return state;
    }
}
