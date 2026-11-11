namespace ProjectDelve.Engine;

internal static class ScenarioCreation
{
    internal static GameState Create(ScenarioDefinition definition, ContentBundle baseContent, bool composeLocal = true)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Board is not { } board || definition.UnitTypeIds is null ||
            definition.Units is null || definition.Agency is null || board.Cells is null || board.Edges is null)
            throw new ArgumentException("Scenario definition requires Board, UnitTypeIds, Units and Agency data.", nameof(definition));
        // Resolve live presets against the explicitly selected provider before applying legacy aliases.
        if (composeLocal && definition.ContentReferences is { Count: > 0 })
            baseContent = ScenarioContentReferences.Compose(baseContent, definition.ContentReferences).Definitions;
        var localCards = composeLocal ? definition.LootCardDefinitions ?? [] : [];
        var localIds = localCards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var effectiveCards = baseContent.LootCards.Where(c => !localIds.Contains(c.Id)).Concat(localCards).ToArray();
        var lootComposition = (definition.Loot ?? new LootSettings()).ResolveComposition(effectiveCards, baseContent.LootPresets);
        definition = definition with { Loot = new() { Composition = lootComposition.ToList() } };
        EffectiveContent content;
        try { content = ContentComposition.Compose([baseContent], composeLocal ? ContentBundle.Local(definition) : null); }
        catch (ContentCompositionException error)
        {
            var diagnostic = error.Diagnostics[0];
            var path = diagnostic.Source?.Path ?? "";
            if (diagnostic.Path.Length > 0) path += (path.Length > 0 ? "." : "") + diagnostic.Path;
            throw ScenarioValidationContext.At(new ArgumentException($"{path}: {diagnostic.Message}", error), path);
        }
        var catalog = content.Definitions;
        if (definition.WorldEffects is { } world && (world.CardsPerRound < 0 || world.Cycling < 1))
            throw ScenarioValidationContext.At(new ArgumentException("World Effects require CardsPerRound >= 0 and Cycling >= 1.", nameof(definition)), "worldEffects");
        var terrainTypes = catalog.Terrain;
        TerrainDefinition ResolveTerrain(TerrainKind legacy, string? typeId, bool? coalGeneration = null)
        {
            var resolvedId = TerrainCompatibility.ResolveTypeId(legacy, typeId, coalGeneration);
            return TerrainCatalog.Find(terrainTypes, resolvedId);
        }
        var defaultTerrain = ResolveTerrain(board.DefaultTerrain, board.DefaultTerrainTypeId);
        if (board.Width < 1 || board.Height < 1 || !Enum.IsDefined(board.DefaultTerrain) || defaultTerrain.LegacyKind == TerrainKind.Rail ||
            board.DefaultTerrainTypeId is not null && board.DefaultTerrain != TerrainKind.StoneFloor && defaultTerrain.LegacyKind != board.DefaultTerrain)
            throw ScenarioValidationContext.At(new ArgumentException("Invalid board dimensions or default terrain.", nameof(definition)), "board");
        bool Inside(Cell? cell) => cell is not null && cell.X >= 0 && cell.Y >= 0 && cell.X < board.Width && cell.Y < board.Height;
        HashSet<Cell> seenCells = [];
        for (var index = 0; index < board.Cells.Count; index++)
        {
            var cell = board.Cells[index];
            if (cell is null || !Inside(cell.Position) || !Enum.IsDefined(cell.Terrain))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Cell override.", nameof(definition)), $"board.cells[{index}]");
            TerrainDefinition resolved;
            try { resolved = ResolveTerrain(cell.Terrain, cell.TerrainTypeId, cell.CoalGeneration); }
            catch (ArgumentException error) { throw ScenarioValidationContext.At(error, $"board.cells[{index}]"); }
            if ((resolved.LegacyKind == TerrainKind.Rail ? cell.Direction is null || !Enum.IsDefined(cell.Direction.Value) : cell.Direction is not null) ||
                resolved.LegacyKind != TerrainKind.Lava && cell.CoalGeneration is not null ||
                cell.TerrainTypeId is not null && cell.Terrain != TerrainKind.StoneFloor && resolved.LegacyKind != cell.Terrain)
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Terrain placement properties.", nameof(definition)), $"board.cells[{index}]");
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
                unit.GroupId is not null && string.IsNullOrWhiteSpace(unit.GroupId) ||
                unit.Anchor is null || unit.WakeDistance < 0 || !Enum.IsDefined(unit.Posture) ||
                unit.UnitId is not null && string.IsNullOrWhiteSpace(unit.UnitId))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Unit placement data.", nameof(definition)), $"units[{index}]");
            if (!declared.Contains(unit.UnitTypeId))
                throw ScenarioValidationContext.At(new ArgumentException($"Placement Unit Type '{unit.UnitTypeId}' is not declared.", nameof(definition)), $"units[{index}]");
        }
        var authoredUnitIds = definition.Units.Where(unit => unit.UnitId is not null)
            .Select(unit => unit.UnitId!).ToHashSet(StringComparer.Ordinal);
        if (authoredUnitIds.Count != definition.Units.Count(unit => unit.UnitId is not null))
            throw new ArgumentException("Authored Unit IDs must be unique.", nameof(definition));
        HashSet<(string, string, string?)> seenAgency = [];
        for (var index = 0; index < definition.Agency.Count; index++)
        {
            var agency = definition.Agency[index];
            if (agency is null || string.IsNullOrWhiteSpace(agency.UnitTypeId) || string.IsNullOrWhiteSpace(agency.SideId) ||
                agency.GroupId is not null && string.IsNullOrWhiteSpace(agency.GroupId) ||
                !Enum.IsDefined(agency.Controller) || !declared.Contains(agency.UnitTypeId))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid agency assignment; its Unit Type must be declared.", nameof(definition)), $"agency[{index}]");
            if (!seenAgency.Add((agency.UnitTypeId, agency.SideId, agency.GroupId)))
                throw ScenarioValidationContext.At(new ArgumentException("Duplicate agency assignment for Unit Type + Side.", nameof(definition)), $"agency[{index}]");
        }
        for (var index = 0; index < definition.Units.Count; index++)
            if (!definition.Agency.Any(a => a.UnitTypeId == definition.Units[index].UnitTypeId && a.SideId == definition.Units[index].SideId &&
                (a.GroupId == definition.Units[index].GroupId || a.GroupId is null)) &&
                catalog.Units.Single(t => t.Id == definition.Units[index].UnitTypeId).DefaultController is null)
                throw ScenarioValidationContext.At(new ArgumentException("Every initial Unit Type + Side needs an explicit agency assignment.", nameof(definition)), $"units[{index}]");

        var orders = definition.Orders ?? [];
        HashSet<(string, string, string?)> seenOrders = [];
        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];
            if (order is null || string.IsNullOrWhiteSpace(order.UnitTypeId) || string.IsNullOrWhiteSpace(order.SideId) ||
                !declared.Contains(order.UnitTypeId) ||
                order.GroupId is not null && string.IsNullOrWhiteSpace(order.GroupId) ||
                (!definition.Agency.Any(a => a.UnitTypeId == order.UnitTypeId && a.SideId == order.SideId &&
                    (a.GroupId == order.GroupId || a.GroupId is null)) &&
                    catalog.Units.SingleOrDefault(t => t.Id == order.UnitTypeId)?.DefaultController is null))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid Order assignment or group.", nameof(definition)), $"orders[{index}]");
            if (!seenOrders.Add((order.UnitTypeId, order.SideId, order.GroupId)))
                throw ScenarioValidationContext.At(new ArgumentException("Duplicate Order assignment for Unit Type + Side.", nameof(definition)), $"orders[{index}]");
        }

        var resolvedAgency = ScenarioGroups.Controllers(definition, catalog.Units);
        var sides = definition.Units.Select(u => u.SideId).Concat(resolvedAgency.Select(a => a.SideId))
            .Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (definition.NeutralSides is { } neutral &&
            (neutral.Any(s => s is null || !sides.Contains(s)) || neutral.Distinct().Count() != neutral.Count))
            throw new ArgumentException("Neutral Sides must be unique, known Sides.", nameof(definition));
        var objectives = definition.Objectives ?? [];
        if (definition.FogOfWarSides is { } fog &&
            (fog.Any(s => s is null || !sides.Contains(s)) || fog.Distinct().Count() != fog.Count))
            throw ScenarioValidationContext.At(new ArgumentException("Fog of War Sides must be unique, known Sides.", nameof(definition)), "fogOfWarSides");
        HashSet<string> objectiveSides = new(StringComparer.Ordinal);
        for (var index = 0; index < objectives.Count; index++)
        {
            var objective = objectives[index];
            if (objective is null || !sides.Contains(objective.SideId) || !Enum.IsDefined(objective.Objective) ||
                !objectiveSides.Add(objective.SideId))
                throw ScenarioValidationContext.At(new ArgumentException("Invalid or duplicate Side Objective.", nameof(definition)), $"objectives[{index}]");
        }

        var customTypes = definition.UnitTypes ?? [];
        if (customTypes.Any(t => t is null || string.IsNullOrWhiteSpace(t.Id)) ||
            customTypes.Select(t => t.Id).Distinct().Count() != customTypes.Count)
            throw new ArgumentException("Scenario Unit Types require unique nonblank IDs.", nameof(definition));
        var types = definition.UnitTypeIds.Select(id => UnitContent.Find(id, catalog.Units)
            ?? throw new ArgumentException($"Unknown Unit Type ID '{id}'.", nameof(definition))).ToList();
        foreach (var (id, settings) in definition.LootUnitOverrides ?? [])
        {
            var index = types.FindIndex(t => t.Id == id);
            if (index < 0 || settings is null || settings.DropSuccesses is < 0 or > 6)
                throw new ArgumentException("Loot overrides require declared Unit Types and drop successes from 0 to 6.");
            var type = types[index];
            types[index] = type with { LootDropSuccesses = settings.DropSuccesses ?? type.LootDropSuccesses,
                CanHaveItems = settings.CanHaveItems ?? type.CanHaveItems, CanHaveEquipment = settings.CanHaveEquipment ?? type.CanHaveEquipment };
        }
        var overrides = board.Cells.ToDictionary(c => c.Position);
        List<TerrainTile> terrain = [];
        TerrainTile Tile(Cell position, CellDefinition? authored)
        {
            var resolved = authored is null ? defaultTerrain : ResolveTerrain(authored.Terrain, authored.TerrainTypeId, authored.CoalGeneration);
            return new(position, resolved.LegacyKind ?? TerrainKind.StoneFloor, authored?.Direction)
            {
                TypeId = authored is null ? board.DefaultTerrainTypeId : authored.TerrainTypeId,
                CoalGeneration = authored?.CoalGeneration
            };
        }
        if (board.DefaultTerrain == TerrainKind.StoneFloor && board.DefaultTerrainTypeId is null)
            terrain.AddRange(board.Cells.Select(c => Tile(c.Position, c)));
        else
            for (var y = 0; y < board.Height; y++)
            for (var x = 0; x < board.Width; x++)
            {
                var cell = new Cell(x, y);
                var authored = overrides.GetValueOrDefault(cell);
                terrain.Add(Tile(cell, authored));
            }
        // Capture closure of every selected object definition as well as initial Unit Types.
        // Summoning granted by Equipment can introduce Units absent from the setup roster.
        var roots = catalog.Equipment.Select(x => new ContentIdentity(ContentFamily.Equipment, x.Id))
            .Concat(catalog.Items.Select(x => new ContentIdentity(ContentFamily.Item, x.Id)))
            .Concat(definition.UnitTypeIds.Select(id => new ContentIdentity(ContentFamily.Unit, id)));
        var requiredUnits = content.Dependencies.Closure(roots).Where(x => x.Family == ContentFamily.Unit).Select(x => x.TypeId).ToHashSet(StringComparer.Ordinal);
        types.AddRange(catalog.Units.Where(t => requiredUnits.Contains(t.Id) && !types.Any(existing => existing.Id == t.Id)));
        var state = new GameState
        {
            LootDeck = new LootDeck(),
            EquipmentTypes = [.. catalog.Equipment],
            ItemTypes = [.. catalog.Items],
            LootCardDefinitions = [.. catalog.LootCards],
            WorldCardDefinitions = [.. catalog.WorldCards],
            ResourceDefinitions = [.. catalog.Resources], ActivationTypeOrder = [.. definition.UnitTypeIds],
            LootMarkers = [.. definition.LootMarkers ?? []], GroundObjects = [.. definition.GroundObjects ?? []],
            Physical = new(new Board(board.Width, board.Height, board.Edges.Select(e => new Edge(e.Position,
                e.Direction == EdgeDirection.Right ? new(e.Position.X + 1, e.Position.Y) : new(e.Position.X, e.Position.Y + 1),
                e.Kind)).ToList(), terrainTypes) { Terrain = terrain, Machines = [.. definition.Machines ?? []] }, []),
            NeutralSides = [.. definition.NeutralSides ?? []], Types = types, Units = [], StoredResources = [.. definition.StoredResources ?? []], WorldEffects = definition.WorldEffects is {} worldSettings ? worldSettings with { Composition = [.. worldSettings.Composition ?? catalog.DefaultWorldDeck] } : null,
            Waypoints = [.. definition.Waypoints ?? []], Routes = [.. definition.Routes ?? []],
            Objectives = sides.OrderBy(side => side, StringComparer.Ordinal)
                .Select(side => objectives.SingleOrDefault(o => o.SideId == side) ?? new SideObjective(side)).ToList(),
            Controllers = resolvedAgency.Select(a => new ControllerAssignment(new(a.UnitTypeId, a.SideId, a.GroupId), a.Controller)).ToList()
        };
        // Setup defaults are copied once into each independently controlled identity.
        var identities = definition.Units.Select(u => new ActivationToken(u.UnitTypeId, u.SideId, u.GroupId))
            .Concat(definition.Agency.Select(a => new ActivationToken(a.UnitTypeId, a.SideId, a.GroupId)))
            .Concat(orders.Select(o => new ActivationToken(o.UnitTypeId, o.SideId, o.GroupId))).Distinct().ToList();
        try
        {
            state.WorldDeck = definition.WorldEffects is null ? null
                : WorldCards.CreateDeck(state.WorldEffects, state.WorldCardDefinitions);
        }
        catch (ArgumentException error) { throw ScenarioValidationContext.At(error, "worldEffects.composition"); }
        state.LootDeck.DrawPile = (definition.Loot ?? new LootSettings()).ResolveObjects(state.LootCardDefinitions);
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
        void ValidateScenario()
        {
            try { GameEngine.ValidateScenario(state); }
            catch (ArgumentException error) { throw ScenarioValidationContext.ForDefinition(error, state, definition); }
        }
        ValidateScenario();
        var initialUnitIds = ScenarioIdentity.UnitIds(definition.Units, (definition.Machines ?? []).Select(m => m.Id));
        for (var index = 0; index < definition.Units.Count; index++)
        {
            var placement = definition.Units[index];
            var type = types.Single(t => t.Id == placement.UnitTypeId);
            if (placement.InitialHp is { } hp && (hp < 1 || hp > type.Hp))
                throw ScenarioValidationContext.At(new ArgumentException($"InitialHp for placement {index} must be between 1 and {type.Hp}.", nameof(definition)), $"units[{index}]");
            // Preserve existing deterministic IDs when free, while reserving every authored identity first.
            var id = initialUnitIds[index];
            try { state.PlaceUnit(type.Id, id, placement.SideId, placement.Anchor, placement.Posture, placement.GroupId,
                placement.Dormant, placement.WakeDistance, inventory: placement.Inventory); }
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
        foreach (var token in identities)
        {
            var order = orders.SingleOrDefault(o => o.UnitTypeId == token.TypeId && o.SideId == token.SideId && o.GroupId == token.GroupId)
                ?? orders.SingleOrDefault(o => o.UnitTypeId == token.TypeId && o.SideId == token.SideId && o.GroupId is null);
            if (order is null) continue;
            var runtimeOrder = new GroupOrder(token, order.Kind, order.Destination, order.Target, order.Route, order.Area);
            if (!OrderValidation.IsValidAssignment(runtimeOrder, state))
                throw ScenarioValidationContext.At(new ArgumentException(
                    "Invalid Order kind, target, destination, or target relationship.", nameof(definition)), $"orders[{orders.IndexOf(order)}]");
            state.AssignOrder(token, runtimeOrder.Kind, runtimeOrder.Destination, runtimeOrder.Target, runtimeOrder.Route, runtimeOrder.Area);
        }
        ValidateObjectiveSelections(state, definition);
        GameEngine.CaptureObjectiveStartUnits(state);
        EquipmentCompatibility.MigrateLegacyDuplicateOwnership(state);
        ValidateScenario();
        // Defer vision until every Unit, terrain object and initial setting exists.
        state.FogOfWarSides = [.. definition.FogOfWarSides ?? []];
        FogOfWar.Refresh(state);
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
                    UnitSelectorKind.Unit => selector.SideId is null && selector.UnitTypeId is null && selector.GroupId is null &&
                                             state.Units.Any(u => u.Id == selector.UnitId),
                    UnitSelectorKind.SideAtStart => selector.UnitId is null && selector.UnitTypeId is null && selector.GroupId is null &&
                                                    state.Controllers.Any(c => c.Token.SideId == selector.SideId),
                    UnitSelectorKind.Group => selector.UnitId is null &&
                                             state.Controllers.Any(c => c.Token.SideId == selector.SideId && c.Token.TypeId == selector.UnitTypeId && c.Token.GroupId == selector.GroupId),
                    _ => false
                });
            if (!valid) throw ScenarioValidationContext.At(new ArgumentException("Invalid Objective count, selectors, references, or area.", nameof(definition)),
                $"objectives[{definition.Objectives!.FindIndex(o => o.SideId == objective.SideId)}]");
        }
    }
}
