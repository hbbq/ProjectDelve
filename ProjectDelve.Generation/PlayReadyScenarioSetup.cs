using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record PlayReadySetupDiagnostic(string Path, string Message, string? LocationId = null,
    string? GroupId = null, IReadOnlyList<Cell>? Positions = null);
public sealed record PlayReadyValidationResult(string Status, bool Valid,
    IReadOnlyList<PlayReadySetupDiagnostic> Diagnostics);

/// <summary>Publishes initial geographic declarations and recomputes their validity on import.</summary>
public static class PlayReadyScenarioSetup
{
    internal static ScenarioSetup Publish(WorldGenerationContext context)
    {
        var deployment = context.Deployment ?? throw new InvalidOperationException("Deployment is missing.");
        var registry = context.Completion?.Registry ?? throw new InvalidOperationException("Required locations are missing.");
        var width = context.Window.Width;
        Cell Cell(int i) => new(i % width, i / width);
        var settings = new HeroDeploymentSettings(deployment.Capacity);
        return new(1, new(deployment.Capacity, deployment.Area.Indices(width).Order().Select(Cell).ToList(),
            deployment.ManeuverCore.Indices(width).Order().Select(Cell).ToList(), deployment.PlacementOrder.Select(Cell).ToList(),
            Cell(deployment.DepartureFromIndex), Cell(deployment.DepartureIndex), settings.MinimumArea, settings.MinimumCore),
            registry.Locations.Select(l => new ScenarioRequiredLocation(l.Id, l.ProducerId, l.SourceId,
                (ScenarioAccessClass)l.AccessClass, l.SpecialReason, l.Groups.Select(g => new ScenarioAccessGroup(g.Id,
                    (ScenarioAccessCoverage)g.Coverage, g.Positions.Select(Cell).ToList())).ToList())).ToList());
    }

    public static PlayReadyValidationResult Validate(ScenarioDefinition definition, ContentBundle? content = null)
        => ValidateCore(definition, () => GameEngine.CreateGame(definition, content ?? ContentBundle.Canonical()));

    /// <summary>Validates against the unmodified matching initial materialization of this definition.</summary>
    public static PlayReadyValidationResult Validate(ScenarioDefinition definition, GameState state)
        => ValidateCore(definition, () => state);

    // Consumers call this only after successful validation of the same initial definition/state.
    public static IReadOnlySet<int> ReachableFromDeployment(ScenarioDefinition definition, GameState state)
    {
        var first = definition.Setup!.Deployment.PlacementOrder[0];
        return MandatoryConnectivityRepair.ActualReachable(state.Physical.Board,
            [first.Y * state.Physical.Board.Width + first.X]);
    }

    private static PlayReadyValidationResult ValidateCore(ScenarioDefinition definition, Func<GameState> materialize)
    {
        List<PlayReadySetupDiagnostic> diagnostics = [];
        PlayReadyValidationResult Result() => new(diagnostics.Count == 0 ? "Valid" : "Invalid", diagnostics.Count == 0, diagnostics);
        void Error(string path, string message) => diagnostics.Add(new(path, message));
        if (definition.Setup is not { } setup) return new("Legacy", false, []);
        if (setup.Version != 1) Error("setup.version", "Unsupported setup version.");
        var width = definition.Board.Width; var height = definition.Board.Height;
        if (width < 1 || height < 1 || (long)width * height > int.MaxValue)
            Error("board", "Setup requires valid Board dimensions.");
        bool InBounds(Cell? cell) => cell is not null && cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < height;
        bool Cells(List<Cell>? cells, string path, bool nonempty = true)
        {
            if (cells is null || nonempty && cells.Count == 0) { Error(path, "Cells must be initialized and nonempty."); return false; }
            if (cells.Any(c => !InBounds(c))) Error(path, "Cell lies outside the Board.");
            if (cells.Distinct().Count() != cells.Count) Error(path, "Duplicate Cells are not allowed.");
            return true;
        }
        if (setup.Deployment is not { } deployment) Error("setup.deployment", "Deployment is required.");
        else
        {
            // Bound arithmetic before creating settings or translating coordinates into indices.
            if (deployment.Capacity < 1 || deployment.Capacity > int.MaxValue / 5 || deployment.Capacity > (long)width * height / 2)
                Error("setup.deployment.capacity", "Capacity must be positive and fit the Board.");
            else
            {
                var settings = new HeroDeploymentSettings(deployment.Capacity);
                try { settings.ValidateDimensions(width, height); }
                catch (ArgumentException ex) { Error("setup.deployment.capacity", ex.Message); }
                if (deployment.MinimumArea != settings.MinimumArea || deployment.MinimumCore != settings.MinimumCore || deployment.ManeuverWidth != 2)
                    Error("setup.deployment", "Version 1 requires the established capacity minima and width-two maneuver core.");
            }
            Cells(deployment.Cells, "setup.deployment.cells");
            Cells(deployment.ManeuverCore, "setup.deployment.maneuverCore");
            Cells(deployment.PlacementOrder, "setup.deployment.placementOrder");
            if (!InBounds(deployment.DepartureFrom) || !InBounds(deployment.Departure)) Error("setup.deployment.departure", "Departure lies outside the Board.");
        }
        if (setup.RequiredLocations is null) Error("setup.requiredLocations", "Required locations must be initialized.");
        else foreach (var location in setup.RequiredLocations)
        {
            if (location is null) { Error("setup.requiredLocations", "Null location is invalid."); continue; }
            var path = $"setup.requiredLocations[{location.Id}]";
            if (location.Groups is null) { Error(path, "Access groups must be initialized."); continue; }
            foreach (var group in location.Groups)
            {
                if (group is null) { Error(path, "Null group is invalid."); continue; }
                Cells(group.Positions, path + $".groups[{group.Id}].positions");
            }
        }
        if (diagnostics.Count != 0) return Result();
        int Index(Cell c) => c.Y * width + c.X;
        var registry = RequiredLocationRegistry.Create(setup.RequiredLocations!.Select(l => new RequiredLocation(l.Id, l.ProducerId,
            l.SourceId, (RequiredAccessClass)l.AccessClass, l.SpecialReason, l.Groups.Select(g => new RequiredAccessGroup(g.Id,
                (RequiredAccessCoverage)g.Coverage, g.Positions.Select(Index).ToImmutableArray())).ToImmutableArray())), width, height);
        foreach (var error in registry.Errors) Error("setup.requiredLocations", error);
        if (diagnostics.Count != 0) return Result();
        var state = materialize();
        if (state.Physical.Board.Width != width || state.Physical.Board.Height != height)
        {
            Error("board", "Materialization dimensions do not match the scenario definition.");
            return Result();
        }
        var d = setup.Deployment!;
        var internalDeployment = new HeroDeployment(d.Capacity, GeneratedArea.From("setup", d.Cells.Select(Index), width),
            GeneratedArea.From("setup/core", d.ManeuverCore.Select(Index), width), d.PlacementOrder.Select(Index).ToImmutableArray(),
            Index(d.DepartureFrom), Index(d.Departure), 0);
        if (HeroDeploymentPlanner.Validate(state, internalDeployment) is { } invalid) Error("setup.deployment", invalid);
        var board = state.Physical.Board;
        var reached = MandatoryConnectivityRepair.ActualReachable(board, [Index(d.PlacementOrder[0])]);
        var geometry = AccessibilityAnalysis.Capture(board with { Edges = board.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
        void Check(RequiredLocationRegistry locations)
        {
            foreach (var failure in MandatoryConnectivityRepair.Failures(locations, reached,
                         i => geometry.CanStop(ConstructionCapabilities.OrdinaryLand, new(i % width, i / width))))
                diagnostics.Add(new("setup.requiredLocations", "Ordinary required access is unreachable.", failure.LocationId,
                    failure.GroupId, failure.Unreachable.Select(i => new Cell(i % width, i / width)).ToList()));
        }
        // Recompute interactive positions using Engine geometry, so stale maximal-board Machine
        // declarations cannot certify diagonal operation through blocking terrain.
        // Interactive identity is independent of the producer that published the declaration.
        var actual = RequiredLocations.ForInteractives(state, "interactive")
            .GroupBy(l => l.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var location in registry.Locations)
            if (actual.TryGetValue(location.Id, out var source) && location.SourceId != source.SourceId)
                diagnostics.Add(new("setup.requiredLocations", "Interactive source identity does not match its declaration.", location.Id));
        Check(registry);
        foreach (var location in registry.Locations.Where(l => l.AccessClass == RequiredAccessClass.Ordinary))
        {
            if (actual.TryGetValue(location.Id, out var refreshed))
            {
                if (location.Id == $"interactive/machine/{location.SourceId}")
                {
                    var legal = refreshed.Groups.SelectMany(g => g.Positions).ToHashSet();
                    foreach (var group in location.Groups)
                    {
                        var invalidPositions = group.Positions.Where(i => !legal.Contains(i)).ToArray();
                        if (group.Coverage == RequiredAccessCoverage.All ? invalidPositions.Length != 0
                            : !group.Positions.Any(i => legal.Contains(i) && reached.Contains(i)))
                            diagnostics.Add(new("setup.requiredLocations", "Declared Machine access has no required legal operating position.",
                                location.Id, group.Id, invalidPositions.Select(i => new Cell(i % width, i / width)).ToList()));
                    }
                }
                else Check(RequiredLocationRegistry.Create([refreshed], width, height));
                continue;
            }
            if (new[] { "machine", "resource", "loot", "ground-object" }.Any(role => location.Id == $"interactive/{role}/{location.SourceId}"))
                diagnostics.Add(new("setup.requiredLocations", "Declared interactive source no longer exists.", location.Id));
        }
        // Editing may introduce new interactives after setup publication. Omission from the
        // declarations cannot silently exempt an ordinary source from geographic access.
        var declaredIds = registry.Locations.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var interactive in actual.Values.Where(l => !declaredIds.Contains(l.Id)).OrderBy(l => l.Id, StringComparer.Ordinal))
            Check(RequiredLocationRegistry.Create([interactive], width, height));
        return Result();
    }
}
