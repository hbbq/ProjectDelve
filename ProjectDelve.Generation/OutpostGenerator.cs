using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record OutpostRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    SitePlacementPreferences? Preferences = null);

/// <summary>A small initial compound composed from ordinary Buildings, Walls and Doors.</summary>
public static class OutpostGenerator
{
    private const int MinimumPlotSize = 12;

    public static PlacementResult<SitePlan> Plan(OutpostRequest request, GenerationScope scope)
    {
        var source = scope.Draft;
        var id = scope.ObjectId(request.Key);
        var start = scope.Budget.Attempts;
        var width = source.Width;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) =>
            new(new(id, "Outpost", code, reason, scope.Budget.Attempts - start));
        if (!SiteGeometry.Fits(request.Area, source.AllowedArea) ||
            request.Area.Width < MinimumPlotSize || request.Area.Height < MinimumPlotSize ||
            request.Preferences?.PreferredApproach is { } requestedSide && !Enum.IsDefined(requestedSide))
            return Fail(PlacementFailureCode.InvalidRequest, "An outpost needs room for a defensive perimeter, courtyard and exterior apron.");
        try { request.Preparation.Validate(); }
        catch (ArgumentException error) { return Fail(PlacementFailureCode.InvalidRequest, error.Message); }
        if (!scope.Content.Terrain.Any(terrain => terrain.Id == "dirt" && terrain.Passable))
            return Fail(PlacementFailureCode.InvalidContent, "An outpost requires a passable Dirt courtyard.");

        var random = scope.Child(request.Key, source).Random("plots");
        var routes = request.Preferences?.RouteAnchors ?? [];
        var candidates = new List<(CellBounds Plot, CardinalSide Facing, int Cost)>();
        int Distance(int a, int b) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
        for (var ordinal = 0; ordinal < 48; ordinal++)
        {
            var plotWidth = Math.Min(request.Area.Width, MinimumPlotSize + random.Next(3));
            var plotHeight = Math.Min(request.Area.Height, MinimumPlotSize + random.Next(3));
            var left = request.Area.X + random.Next(request.Area.Width - plotWidth + 1);
            var top = request.Area.Y + random.Next(request.Area.Height - plotHeight + 1);
            if (request.Preferences?.PreferredAnchor is { } hub && ordinal % 2 == 0)
            {
                left = Math.Clamp(hub % width - plotWidth / 2 + random.Next(37) - 18,
                    request.Area.X, request.Area.X + request.Area.Width - plotWidth);
                top = Math.Clamp(hub / width - plotHeight / 2 + random.Next(37) - 18,
                    request.Area.Y, request.Area.Y + request.Area.Height - plotHeight);
            }
            var plot = new CellBounds(left, top, plotWidth, plotHeight);
            var cells = SiteGeometry.Cells(plot, width).ToArray();
            if (!scope.Budget.Spend(cells.Length + 4L * routes.Count))
                return Fail(PlacementFailureCode.BudgetExhausted, "Outpost plot search budget exhausted.");
            if (cells.Any(cell => !source.CanWrite(cell))) continue;
            var enclosed = new CellBounds(left + 1, top + 1, plotWidth - 2, plotHeight - 2);
            var facing = request.Preferences?.PreferredApproach ?? (CardinalSide)random.Next(4);
            if (routes.Count > 0 && request.Preferences?.PreferredApproach is null)
                facing = Enum.GetValues<CardinalSide>().MinBy(side => routes.Min(route =>
                    Distance(route, Gate(id, enclosed, side, width).OutsideIndex)));
            var preparation = cells.Sum(cell => source.TerrainAt(cell) switch
            {
                "tree" or "fruit-tree" => 2, "solid-rock" => 4, "water" => 6, _ => 0
            });
            var center = (top + plotHeight / 2) * width + left + plotWidth / 2;
            var anchorCost = request.Preferences?.PreferredAnchor is { } anchor
                ? Distance(anchor, center) * request.Preferences.AnchorWeight : 0;
            candidates.Add((plot, facing, preparation + anchorCost + SiteRouteAccess.ProximityCost(plot, routes, width)));
        }

        var failure = Fail(PlacementFailureCode.NoFootprint, "No unreserved outpost plot fits the region.");
        foreach (var (plot, facing, _) in candidates.Distinct().OrderBy(candidate => candidate.Cost).Take(16))
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Outpost construction budget exhausted.");
            var cells = SiteGeometry.Cells(plot, width).ToHashSet();
            // Existing structural geometry remains authoritative even when a caller
            // permits natural preparation with a wider policy.
            if (cells.Any(cell => source.Neighbors(cell).Any(next => cells.Contains(next) &&
                    source.EdgeAt(EdgeKey.Between(cell, next, width)) != EdgeKind.None)))
            {
                failure = Fail(PlacementFailureCode.ReservationConflict, "Outpost would overlap existing structural Edges.");
                continue;
            }
            var enclosed = new CellBounds(plot.X + 1, plot.Y + 1, plot.Width - 2, plot.Height - 2);
            var gate = Gate(id, enclosed, facing, width);
            var draft = source.Fork(request.Area, id);
            var before = draft.BuildPatch();
            var preparation = SiteGeometry.Prepare(draft, cells, "dirt", request.Preparation);
            if (preparation is not null)
            {
                failure = Fail(preparation.Code, preparation.Reason);
                if (preparation.Code == PlacementFailureCode.BudgetExhausted) return failure;
                continue;
            }
            var operations = new List<ConstructionOperation>
            {
                new(id, scope.Path, "Fortified outpost", id, plot, [], []),
                SiteGeometry.Operation(draft, before, id + "/ground", id, "Prepared courtyard and exterior apron", id, plot)
            };
            var buildingPlot = GuardhousePlot(enclosed, facing);
            var doorOutside = BuildingDoor(buildingPlot, facing, width);
            var building = BuildingGenerator.Plan(new("guardhouse", buildingPlot,
                new(4, 4, 4, 4, RoomCount: 1, Furniture: false, Entrance: new(facing, doorOutside)),
                Preparation: request.Preparation), new(scope.RootSeed, id, draft));
            if (building is Rejected<BuildingPlan> rejected)
            {
                failure = Fail(rejected.Failure.Code, rejected.Failure.Reason);
                if (rejected.Failure.Code is PlacementFailureCode.BudgetExhausted or PlacementFailureCode.InvalidContent) return failure;
                continue;
            }
            var guardhouse = ((Planned<BuildingPlan>)building).Plan;
            if (!draft.Accept(guardhouse.Patch, out var error)) return Fail(PlacementFailureCode.ValidationFailed, error!);
            operations.AddRange(guardhouse.Operations);
            before = draft.BuildPatch();
            DefensivePerimeter.Write(draft, enclosed, gate);
            operations.Add(SiteGeometry.Operation(draft, before, id + "/perimeter", id,
                "Continuous defensive perimeter and Door gate", id, enclosed));
            if (!DefensivePerimeter.Validate(draft, enclosed, gate, out error))
            {
                failure = Fail(PlacementFailureCode.EntranceConstraint, error!);
                continue;
            }
            var buildingCells = guardhouse.Building.Footprint.Indices(width).ToHashSet();
            var enclosedCells = SiteGeometry.Cells(enclosed, width).ToHashSet();
            var courtyard = enclosedCells.Except(buildingCells).ToArray();
            var apron = cells.Except(enclosedCells).ToArray();
            var exterior = courtyard.Concat(apron).ToArray();
            var reached = SiteGeometry.Flood(draft, gate.OutsideIndex, cells);
            if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Outpost access validation budget exhausted.");
            if (cells.Any(cell => !draft.Passable(cell) || !reached.Contains(cell)) || SiteGeometry.TwoByTwo(draft, courtyard) < 4)
            {
                failure = Fail(PlacementFailureCode.DisconnectedSpace, "Outpost courtyard, guardhouse and exterior approach must share ordinary Door access.");
                continue;
            }
            var summary = SiteGeometry.Summary(draft.BuildPatch());
            if (summary.ChangedTerrain > request.Preparation.MaxChangedCells || summary.Cost > request.Preparation.MaxCost)
            {
                failure = Fail(PlacementFailureCode.PreparationLimit, "Completed outpost exceeds its terrain preparation allowance.");
                continue;
            }
            draft.Reserve(exterior);
            var needsConnection = !apron.Any(cell => draft.Neighbors(cell).Any(next =>
                !cells.Contains(next) && SiteGeometry.Crossable(draft, cell, next)));
            var site = new SiteInfo(id, GeneratedSiteKind.FortifiedOutpost,
                GeneratedArea.From(id + "/footprint", cells, width), GeneratedArea.From(id + "/exterior", exterior, width),
                [guardhouse.Building with { Purpose = "Guardhouse" }], [gate],
                new(cells.Count, reached.Count, true, needsConnection, SiteGeometry.TwoByTwo(draft, courtyard)),
                [SiteTag.Constructed], [], [], null, summary)
            {
                ConstructionProfile = SiteConstructionProfile.FortifiedOutpost,
                Outpost = new(GeneratedArea.From(id + "/enclosed", enclosedCells, width),
                    GeneratedArea.From(id + "/courtyard", courtyard, width),
                    GeneratedArea.From(id + "/approach", apron, width), DefensivePerimeter.Edges(enclosed, width), gate.Id)
            };
            site = SiteRouteAccess.Publish(site, draft, request.Preferences);
            return new Planned<SitePlan>(new(draft.BuildPatch(), site, operations));
        }
        return failure;
    }

    private static SitePortal Gate(string id, CellBounds enclosed, CardinalSide facing, int width)
    {
        var inside = facing switch
        {
            CardinalSide.North => enclosed.Y * width + enclosed.X + enclosed.Width / 2,
            CardinalSide.South => (enclosed.Y + enclosed.Height - 1) * width + enclosed.X + enclosed.Width / 2,
            CardinalSide.West => (enclosed.Y + enclosed.Height / 2) * width + enclosed.X,
            _ => (enclosed.Y + enclosed.Height / 2) * width + enclosed.X + enclosed.Width - 1
        };
        var outside = inside + (facing switch
        { CardinalSide.North => -width, CardinalSide.South => width, CardinalSide.West => -1, _ => 1 });
        return new(id + "/gate", id, inside, outside, EdgeKey.Between(inside, outside, width), EdgeKind.ClosedDoor);
    }

    private static CellBounds GuardhousePlot(CellBounds enclosed, CardinalSide gateFacing) => gateFacing switch
    {
        CardinalSide.North => new(enclosed.X + (enclosed.Width - 6) / 2, enclosed.Y + enclosed.Height - 7, 6, 6),
        CardinalSide.South => new(enclosed.X + (enclosed.Width - 6) / 2, enclosed.Y + 1, 6, 6),
        CardinalSide.West => new(enclosed.X + enclosed.Width - 7, enclosed.Y + (enclosed.Height - 6) / 2, 6, 6),
        _ => new(enclosed.X + 1, enclosed.Y + (enclosed.Height - 6) / 2, 6, 6)
    };

    private static int BuildingDoor(CellBounds plot, CardinalSide facing, int width) => facing switch
    {
        CardinalSide.North => plot.Y * width + plot.X + plot.Width / 2,
        CardinalSide.South => (plot.Y + plot.Height - 1) * width + plot.X + plot.Width / 2,
        CardinalSide.West => (plot.Y + plot.Height / 2) * width + plot.X,
        _ => (plot.Y + plot.Height / 2) * width + plot.X + plot.Width - 1
    };
}

/// <summary>A rectangular shell with one designated ordinary Door opening.</summary>
internal static class DefensivePerimeter
{
    internal static IReadOnlyList<EdgeKey> Edges(CellBounds enclosed, int width)
    {
        var result = new List<EdgeKey>();
        for (var x = enclosed.X; x < enclosed.X + enclosed.Width; x++)
        {
            var north = enclosed.Y * width + x;
            var south = (enclosed.Y + enclosed.Height - 1) * width + x;
            result.Add(EdgeKey.Between(north, north - width, width));
            result.Add(EdgeKey.Between(south, south + width, width));
        }
        for (var y = enclosed.Y; y < enclosed.Y + enclosed.Height; y++)
        {
            var west = y * width + enclosed.X;
            var east = y * width + enclosed.X + enclosed.Width - 1;
            result.Add(EdgeKey.Between(west, west - 1, width));
            result.Add(EdgeKey.Between(east, east + 1, width));
        }
        return result.OrderBy(edge => edge.Index).ThenBy(edge => edge.Direction).ToArray();
    }

    internal static void Write(BoardDraft draft, CellBounds enclosed, SitePortal gate)
    {
        foreach (var edge in Edges(enclosed, draft.Width))
            draft.SetEdge(edge, edge == gate.Edge ? EdgeKind.ClosedDoor : EdgeKind.Wall);
    }

    internal static bool Validate(IPlanningBoardView board, CellBounds enclosed, SitePortal gate, out string? error)
    {
        error = null;
        var outer = new CellBounds(0, 0, board.Width, board.Height);
        if (enclosed.X < 1 || enclosed.Y < 1 || enclosed.Width < 2 || enclosed.Height < 2 ||
            !SiteGeometry.Fits(new(enclosed.X - 1, enclosed.Y - 1, enclosed.Width + 2, enclosed.Height + 2), outer))
        {
            error = "Defensive perimeter requires an in-bounds enclosed area and exterior apron.";
            return false;
        }
        var cells = SiteGeometry.Cells(enclosed, board.Width).ToHashSet();
        var perimeter = Edges(enclosed, board.Width);
        if (!cells.Contains(gate.InsideIndex) || cells.Contains(gate.OutsideIndex) ||
            !board.Neighbors(gate.InsideIndex).Contains(gate.OutsideIndex) ||
            !perimeter.Contains(gate.Edge) || EdgeKey.Between(gate.InsideIndex, gate.OutsideIndex, board.Width) != gate.Edge ||
            gate.Kind != EdgeKind.ClosedDoor || gate.Width != 1 ||
            !board.Passable(gate.InsideIndex) || !board.Passable(gate.OutsideIndex))
        {
            error = "Defensive perimeter gate must identify a passable ordinary Closed Door contact.";
            return false;
        }
        if (perimeter.Any(edge => board.EdgeAt(edge) != (edge == gate.Edge ? EdgeKind.ClosedDoor : EdgeKind.Wall)))
        {
            error = "Defensive perimeter must contain exactly its designated Door and continuous remaining Walls.";
            return false;
        }
        return true;
    }
}
