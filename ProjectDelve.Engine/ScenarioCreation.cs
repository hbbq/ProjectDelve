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
        if (board.Width < 1 || board.Height < 1 || !Enum.IsDefined(board.DefaultTerrain) || board.DefaultTerrain == TerrainKind.Rail)
            throw ScenarioValidationContext.At(new ArgumentException("Invalid board dimensions or default terrain.", nameof(definition)), "board");
        bool Inside(Cell? cell) => cell is not null && cell.X >= 0 && cell.Y >= 0 && cell.X < board.Width && cell.Y < board.Height;
        HashSet<Cell> seenCells = [];
        for (var index = 0; index < board.Cells.Count; index++)
        {
            var cell = board.Cells[index];
            if (cell is null || !Inside(cell.Position) || !Enum.IsDefined(cell.Terrain) ||
                (cell.Terrain == TerrainKind.Rail ? cell.Direction is null || !Enum.IsDefined(cell.Direction.Value) : cell.Direction is not null))
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
                unit.Anchor is null || !Enum.IsDefined(unit.Posture) ||
                unit.UnitId is not null && string.IsNullOrWhiteSpace(unit.UnitId))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Unit placement data.", nameof(definition)), $"units[{index}]");
            if (!declared.Contains(unit.UnitTypeId))
                throw ScenarioValidationContext.At(new ArgumentException($"Placement Unit Type '{unit.UnitTypeId}' is not declared.", nameof(definition)), $"units[{index}]");
        }
        var authoredUnitIds = definition.Units.Where(unit => unit.UnitId is not null)
            .Select(unit => unit.UnitId!).ToHashSet(StringComparer.Ordinal);
        if (authoredUnitIds.Count != definition.Units.Count(unit => unit.UnitId is not null))
            throw new ArgumentException("Authored Unit IDs must be unique.", nameof(definition));
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

        var orders = definition.Orders ?? [];
        HashSet<(string, string)> seenOrders = [];
        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];
            if (order is null || string.IsNullOrWhiteSpace(order.UnitTypeId) || string.IsNullOrWhiteSpace(order.SideId) ||
                !definition.Agency.Any(a => a.UnitTypeId == order.UnitTypeId && a.SideId == order.SideId))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Order assignment or group.", nameof(definition)), $"orders[{index}]");
            if (!seenOrders.Add((order.UnitTypeId, order.SideId)))
                throw ScenarioValidationContext.At(new ArgumentException("Duplicate Order assignment for Unit Type + Side.", nameof(definition)), $"orders[{index}]");
        }

        var sides = definition.Units.Select(u => u.SideId).Concat(definition.Agency.Select(a => a.SideId))
            .Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (definition.NeutralSides is { } neutral &&
            (neutral.Any(s => s is null || !sides.Contains(s)) || neutral.Distinct().Count() != neutral.Count))
            throw new ArgumentException("Neutral Sides must be unique, known Sides.", nameof(definition));
        var objectives = definition.Objectives ?? [];
        HashSet<string> objectiveSides = new(StringComparer.Ordinal);
        for (var index = 0; index < objectives.Count; index++)
        {
            var objective = objectives[index];
            if (objective is null || !sides.Contains(objective.SideId) || !Enum.IsDefined(objective.Objective) ||
                !objectiveSides.Add(objective.SideId))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid or duplicate Side Objective.", nameof(definition)), $"objectives[{index}]");
        }

        var customTypes = definition.UnitTypes ?? [];
        if (customTypes.Any(t => t is null || string.IsNullOrWhiteSpace(t.Id) || !declared.Contains(t.Id)) ||
            customTypes.Select(t => t.Id).Distinct().Count() != customTypes.Count)
            throw new ArgumentException("Scenario Unit Types must be unique and declared.", nameof(definition));
        var types = definition.UnitTypeIds.Select(id => UnitContent.Find(id, customTypes)
            ?? throw new ArgumentException($"Unknown Unit Type ID '{id}'.", nameof(definition))).ToList();
        var overrides = board.Cells.ToDictionary(c => c.Position);
        List<TerrainTile> terrain = [];
        if (board.DefaultTerrain == TerrainKind.StoneFloor)
            terrain.AddRange(board.Cells.Select(c => new TerrainTile(c.Position, c.Terrain, c.Direction)));
        else
            for (var y = 0; y < board.Height; y++)
            for (var x = 0; x < board.Width; x++)
            {
                var cell = new Cell(x, y);
                var authored = overrides.GetValueOrDefault(cell);
                terrain.Add(new(cell, authored?.Terrain ?? board.DefaultTerrain, authored?.Direction));
            }
        var state = new GameState
        {
            Physical = new(new Board(board.Width, board.Height, board.Edges.Select(e => new Edge(e.Position,
                e.Direction == EdgeDirection.Right ? new(e.Position.X + 1, e.Position.Y) : new(e.Position.X, e.Position.Y + 1),
                e.Kind)).ToList()) { Terrain = terrain, Machines = [.. definition.Machines ?? []] }, []),
            NeutralSides = [.. definition.NeutralSides ?? []], Types = types, Units = [], StoredResources = [.. definition.StoredResources ?? []], WorldEffects = definition.WorldEffects,
            Waypoints = [.. definition.Waypoints ?? []], Routes = [.. definition.Routes ?? []],
            Objectives = sides.OrderBy(side => side, StringComparer.Ordinal)
                .Select(side => objectives.SingleOrDefault(o => o.SideId == side) ?? new SideObjective(side)).ToList(),
            WorldDeck = definition.WorldEffects is null ? null : WorldCards.CreateDeck(),
            Controllers = definition.Agency.Select(a => new ControllerAssignment(new(a.UnitTypeId, a.SideId), a.Controller)).ToList()
        };
        for (var index = 0; index < (definition.Resources?.Count ?? 0); index++)
        {
            try
            {
                var resource = definition.Resources![index];
                ResourceRules.Validate(resource, state.Physical.Board);
                state.PlaceResource(resource.Type, resource.Position);
            }
            catch (ArgumentException error) { throw ScenarioValidationContext.At(error, $"resources[{index}]"); }
        }
        // The same validation also remains at round start. Validate content before CreateUnit.
        GameEngine.ValidateScenario(state);
        var initialUnitIds = ScenarioIdentity.UnitIds(definition.Units, (definition.Machines ?? []).Select(m => m.Id));
        for (var index = 0; index < definition.Units.Count; index++)
        {
            var placement = definition.Units[index];
            var type = types.Single(t => t.Id == placement.UnitTypeId);
            if (placement.InitialHp is { } hp && (hp < 1 || hp > type.Hp))
                throw ScenarioValidationContext.At(new ArgumentException($"InitialHp for placement {index} must be between 1 and {type.Hp}.", nameof(definition)), $"units[{index}]");
            // Preserve existing deterministic IDs when free, while reserving every authored identity first.
            var id = initialUnitIds[index];
            try { state.PlaceUnit(type.Id, id, placement.SideId, placement.Anchor, placement.Posture); }
            catch (ArgumentException error) { throw ScenarioValidationContext.At(error, $"units[{index}]"); }
            if (placement.InitialHp is { } initialHp) state.Units[^1] = state.Units[^1] with { CurrentHp = initialHp };
        }
        for (var index = 0; index < (definition.CellMarkers?.Count ?? 0); index++)
        {
            try
            {
                var marker = definition.CellMarkers![index];
                CellMarkerRules.Validate(marker, state.Physical.Board);
                state.PlaceCellMarker(marker.Type, marker.Position);
            }
            catch (ArgumentException error) { throw ScenarioValidationContext.At(error, $"cellMarkers[{index}]"); }
        }
        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];
            var runtimeOrder = new GroupOrder(new(order.UnitTypeId, order.SideId), order.Kind,
                order.Destination, order.Target, order.Route);
            if (!OrderValidation.IsValidAssignment(runtimeOrder, state))
                throw ScenarioValidationContext.At(new ArgumentException(
                    "Invalid Order kind, target, destination, or target relationship.", nameof(definition)), $"orders[{index}]");
            state.AssignOrder(runtimeOrder.Token, runtimeOrder.Kind, runtimeOrder.Destination, runtimeOrder.Target, runtimeOrder.Route);
        }
        ValidateObjectiveSelections(state, definition);
        GameEngine.CaptureObjectiveStartUnits(state);
        GameEngine.ValidateScenario(state);
        return state;
    }

    private static void ValidateObjectiveSelections(GameState state, ScenarioDefinition definition)
    {
        foreach (var objective in state.Objectives)
        {
            if (objective.Objective is ScenarioObjective.EliminateEnemies or ScenarioObjective.NoObjective) continue;
            var valid = objective.Count >= 0 && !objective.Selectors.IsDefaultOrEmpty;
            if (objective.Objective == ScenarioObjective.ReachArea)
                valid &= objective.Area is { X: >= 0, Y: >= 0, W: > 0, H: > 0 } area &&
                         (long)area.X + area.W <= state.Physical.Board.Width && (long)area.Y + area.H <= state.Physical.Board.Height;
            else valid &= objective.Area is null;
            foreach (var selector in objective.Selectors.IsDefault ? [] : objective.Selectors)
                valid &= selector is not null && (selector.Kind switch
                {
                    UnitSelectorKind.Unit => selector.SideId is null && selector.UnitTypeId is null &&
                                             state.Units.Any(u => u.Id == selector.UnitId),
                    UnitSelectorKind.SideAtStart => selector.UnitId is null && selector.UnitTypeId is null &&
                                                    state.Controllers.Any(c => c.Token.SideId == selector.SideId),
                    UnitSelectorKind.Group => selector.UnitId is null &&
                                             state.Controllers.Any(c => c.Token.SideId == selector.SideId && c.Token.TypeId == selector.UnitTypeId),
                    _ => false
                });
            if (!valid) throw ScenarioValidationContext.At(new ArgumentException("Invalid Objective count, selectors, references, or area.", nameof(definition)),
                $"objectives[{definition.Objectives!.FindIndex(o => o.SideId == objective.SideId)}]");
        }
    }
}
