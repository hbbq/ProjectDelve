using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record CottageRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    bool Furniture = true, SitePlacementPreferences? Preferences = null);

public static class CottageGenerator
{
    public static PlacementResult<SitePlan> Plan(CottageRequest request, GenerationScope scope)
    {
        var source = scope.Draft;
        var area = request.Area;
        var id = scope.ObjectId(request.Key);
        var start = scope.Budget.Attempts;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) =>
            new(new(id, "Cottage", code, reason, scope.Budget.Attempts - start));
        if (!SiteGeometry.Fits(area, source.AllowedArea) || area.Width < 6 || area.Height < 6)
            return Fail(PlacementFailureCode.InvalidRequest, "Cottage needs space for an enclosed room and exterior apron.");
        if (!scope.Content.Terrain.Any(t => t.Id == "dirt" && t.Passable))
            return Fail(PlacementFailureCode.InvalidContent, "Cottage needs a passable Dirt apron.");
        var boardWidth = source.Width;
        var random = scope.Child(request.Key, source).Random("plots");
        var route = request.Preferences?.RouteAnchors ?? [];
        var hub = request.Preferences?.PreferredAnchor;
        var plots = new List<(CellBounds Plot, CardinalSide Facing, int Cost)>();
        int Distance(int a, int b) => Math.Abs(a % boardWidth - b % boardWidth) + Math.Abs(a / boardWidth - b / boardWidth);
        for (var i = 0; i < 20; i++)
        {
            var width = Math.Min(area.Width, 6 + random.Next(3));
            var height = Math.Min(area.Height, 6 + random.Next(2));
            var left = area.X + random.Next(area.Width - width + 1);
            var top = area.Y + random.Next(area.Height - height + 1);
            if (hub is { } anchor && i % 2 == 0)
            {
                left = Math.Clamp(anchor % boardWidth - width / 2 + random.Next(29) - 14, area.X, area.X + area.Width - width);
                top = Math.Clamp(anchor / boardWidth - height / 2 + random.Next(29) - 14, area.Y, area.Y + area.Height - height);
            }
            var plot = new CellBounds(left, top, width, height);
            if (!scope.Budget.Spend(width * height + 4L * route.Count))
                return Fail(PlacementFailureCode.BudgetExhausted, "Cottage plot search budget exhausted.");
            var cells = SiteGeometry.Cells(plot, boardWidth).ToArray();
            if (cells.Any(i => !source.CanWrite(i))) continue;
            var center = (top + height / 2) * boardWidth + left + width / 2;
            var facing = (CardinalSide)random.Next(4);
            if (route.Count > 0 && random.Next(2) == 0)
                facing = Enum.GetValues<CardinalSide>().MinBy(side =>
                {
                    var door = DoorIndex(plot, side, boardWidth);
                    return route.Min(i => Distance(door, i));
                });
            var preparation = cells.Sum(i => source.TerrainAt(i) switch
            {
                "tree" or "fruit-tree" => 2, "solid-rock" => 4, "water" => 6, _ => 0
            });
            var anchorCost = hub is { } preferred ? request.Preferences!.AnchorWeight * Distance(preferred, center) : 0;
            plots.Add((plot, facing, preparation + anchorCost + SiteRouteAccess.ProximityCost(plot, route, boardWidth, 2)));
        }

        var failure = Fail(PlacementFailureCode.NoFootprint, "No unreserved cottage plot fits the region.");
        foreach (var (plot, facing, _) in plots.Distinct().OrderBy(candidate => candidate.Cost).Take(10))
        {
            var draft = source.Fork(area, id);
            var buildingScope = new GenerationScope(scope.RootSeed, id, draft);
            var outside = DoorIndex(plot, facing, boardWidth);
            var width = plot.Width - 2;
            var height = plot.Height - 2;
            var requirements = new BuildingRequirements(width, width, height, height, RoomCount: 1,
                Furniture: request.Furniture, Entrance: new(facing, outside));
            var planned = BuildingGenerator.Plan(new("house", plot, requirements, Preparation: request.Preparation), buildingScope);
            if (planned is Rejected<BuildingPlan> rejected)
            {
                failure = Fail(rejected.Failure.Code, rejected.Failure.Reason);
                if (rejected.Failure.Code is PlacementFailureCode.BudgetExhausted or PlacementFailureCode.InvalidContent) return failure;
                continue;
            }
            var house = ((Planned<BuildingPlan>)planned).Plan;
            if (!draft.Accept(house.Patch, out var error))
                return Fail(PlacementFailureCode.ValidationFailed, error!);

            var before = draft.BuildPatch();
            var footprint = SiteGeometry.Cells(plot, boardWidth).ToArray();
            var exterior = footprint.Except(house.Building.Footprint.Indices(boardWidth)).ToArray();
            foreach (var i in exterior) draft.ReplaceTerrain(i, "dirt");
            draft.Reserve(exterior);
            var portal = house.Building.Portals[0];
            var reached = SiteGeometry.Flood(draft, portal.OutsideIndex, footprint);
            if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Cottage access budget exhausted.");
            var usable = footprint.Where(draft.Passable).ToArray();
            if (usable.Any(i => !reached.Contains(i)))
            {
                failure = Fail(PlacementFailureCode.DisconnectedSpace, "Cottage apron or room lacks a connected approach.");
                continue;
            }

            var patch = draft.BuildPatch();
            var preparation = SiteGeometry.Summary(patch);
            if (preparation.ChangedTerrain > request.Preparation.MaxChangedCells || preparation.Cost > request.Preparation.MaxCost)
            {
                failure = Fail(PlacementFailureCode.PreparationLimit, "Cottage exceeds the available terrain preparation.");
                continue;
            }
            var externalConnection = exterior.All(i => draft.Neighbors(i).All(j => reached.Contains(j) || !SiteGeometry.Crossable(draft, i, j)));
            var apron = GeneratedArea.From(id + "/apron", exterior, boardWidth);
            var building = house.Building with { Purpose = "Cottage" };
            var site = new SiteInfo(id, GeneratedSiteKind.Settlement, GeneratedArea.From(id + "/footprint", footprint, boardWidth),
                apron, [building], [portal], new(usable.Length, usable.Length, true, externalConnection, SiteGeometry.TwoByTwo(draft, usable)),
                [SiteTag.Residential, SiteTag.Constructed], [], [], null, preparation) { ConstructionProfile = SiteConstructionProfile.Cottage };
            site = SiteRouteAccess.Publish(site, draft, request.Preferences);
            return new Planned<SitePlan>(new(draft.BuildPatch(), site,
                [new(id, scope.Path, "Cottage", id, plot, [], []), .. house.Operations,
                 SiteGeometry.Operation(draft, before, id + "/apron", id, "Prepared cottage apron", id, plot)]));
        }
        return failure;
    }

    private static int DoorIndex(CellBounds plot, CardinalSide side, int boardWidth)
    {
        var x = plot.X + plot.Width / 2;
        var y = plot.Y + plot.Height / 2;
        return side switch
        {
            CardinalSide.North => plot.Y * boardWidth + x,
            CardinalSide.South => (plot.Y + plot.Height - 1) * boardWidth + x,
            CardinalSide.West => y * boardWidth + plot.X,
            _ => y * boardWidth + plot.X + plot.Width - 1
        };
    }
}
