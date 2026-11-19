using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record RuinRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    SiteConstructionProfile Profile, SitePlacementPreferences? Preferences = null);

/// <summary>Constructs initial, ordinary geometry for abandoned buildings and small hamlets.</summary>
public static class RuinGenerator
{
    public static PlacementResult<SitePlan> Plan(RuinRequest request, GenerationScope scope)
    {
        var id = scope.ObjectId(request.Key);
        var start = scope.Budget.Attempts;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) =>
            new(new(id, "Ruin", code, reason, scope.Budget.Attempts - start));
        if (!SiteGeometry.Fits(request.Area, scope.Draft.AllowedArea) || request.Profile is not
            (SiteConstructionProfile.RuinedCottage or SiteConstructionProfile.CollapsedBuilding or SiteConstructionProfile.RuinedHamlet))
            return Fail(PlacementFailureCode.InvalidRequest, "Ruins require a valid area and a ruined construction profile.");
        try { request.Preparation.Validate(); }
        catch (ArgumentException error) { return Fail(PlacementFailureCode.InvalidRequest, error.Message); }
        if (new[] { "stone-floor", "grass", "gravel" }.Any(type =>
            !scope.Content.Terrain.Any(t => t.Id == type && t.Passable && t.LegacyKind != TerrainKind.Rail)))
            return Fail(PlacementFailureCode.InvalidContent, "Ruins require ordinary passable floor, grass and gravel.");
        var cottageRemnant = request.Profile == SiteConstructionProfile.RuinedCottage &&
            scope.Child(request.Key, scope.Draft).Random("furnishing-remnant").Next(3) == 0 &&
            scope.Content.Terrain.Any(t => t.Id == "stone-floor-with-table" && !t.Passable);

        var source = scope.Draft;
        var w = source.Width;
        var random = scope.Child(request.Key, source).Random("plots");
        var preferredCount = request.Profile == SiteConstructionProfile.RuinedHamlet ? 2 + random.Next(2) : 1;
        var candidates = new List<(CellBounds[] Lots, int Cost)>();
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var count = request.Profile == SiteConstructionProfile.RuinedHamlet ? 2 + (preferredCount + attempt) % 2 : 1;
            var width = request.Profile == SiteConstructionProfile.CollapsedBuilding ? 8 + random.Next(2) : 6 + random.Next(2);
            var height = 6 + random.Next(2);
            CellBounds[] offsets = count switch
            {
                3 => [new(0, 0, 7, 6), new(8, 2, 6, 7), new(2, 10, 6, 6)],
                2 => [new(0, 0, 6, 6), new(7, 2, 6, 7)],
                _ => [new(0, 0, width, height)]
            };
            // Transposition varies frontage without forcing an artificial settlement grid.
            if (random.Next(2) == 0) offsets = offsets.Select(b => new CellBounds(b.Y, b.X, b.Height, b.Width)).ToArray();
            var extentWidth = offsets.Max(b => b.X + b.Width);
            var extentHeight = offsets.Max(b => b.Y + b.Height);
            if (extentWidth > request.Area.Width || extentHeight > request.Area.Height) continue;
            var x = request.Area.X + random.Next(request.Area.Width - extentWidth + 1);
            var y = request.Area.Y + random.Next(request.Area.Height - extentHeight + 1);
            var lots = offsets.Select(b => b with { X = b.X + x, Y = b.Y + y }).ToArray();
            var cells = lots.SelectMany(b => SiteGeometry.Cells(b, w)).ToArray();
            if (!scope.Budget.Spend(cells.Length)) return Fail(PlacementFailureCode.BudgetExhausted, "Ruin plot search budget exhausted.");
            if (cells.Any(i => source.IsRouteCorridor(i) || !source.CanWrite(i))) continue;
            var cost = cells.Sum(i => source.TerrainAt(i) switch { "tree" or "fruit-tree" => 2, "solid-rock" => 4, "water" => 6, _ => 0 });
            var bounds = new CellBounds(x, y, extentWidth, extentHeight);
            cost += SiteRouteAccess.ProximityCost(bounds, request.Preferences?.RouteAnchors ?? [], w, 2);
            if (request.Preferences?.PreferredAnchor is { } anchor)
                cost += request.Preferences.AnchorWeight * (Math.Abs(x + extentWidth / 2 - anchor % w) + Math.Abs(y + extentHeight / 2 - anchor / w));
            candidates.Add((lots, cost));
        }

        var failure = Fail(PlacementFailureCode.NoFootprint, "No complete ruin fits the available region.");
        foreach (var (lots, _) in candidates.OrderBy(c => c.Cost).Take(12))
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Ruin construction budget exhausted.");
            var draft = source.Fork(request.Area, id);
            var buildingScope = new GenerationScope(scope.RootSeed, id, draft);
            List<BuildingInfo> buildings = [];
            List<ConstructionOperation> operations = [new(id, scope.Path, "Ruin", id, request.Area, [], [])];
            HashSet<int> interiors = [];
            HashSet<int> exterior = [];
            var failed = false;
            foreach (var (lot, ordinal) in lots.Select((lot, ordinal) => (lot, ordinal)))
            {
                var facing = request.Preferences?.PreferredApproach ?? (ordinal % 2 == 0 ? CardinalSide.South : CardinalSide.West);
                var outside = DoorIndex(lot, facing, w);
                var requirements = new BuildingRequirements(lot.Width - 2, lot.Width - 2, lot.Height - 2, lot.Height - 2,
                    RoomCount: request.Profile == SiteConstructionProfile.CollapsedBuilding ? 2 : 1,
                    FloorTypeId: request.Profile == SiteConstructionProfile.RuinedCottage ? "stone-floor" : "gravel",
                    Furniture: cottageRemnant, Entrance: new(facing, outside)) { RuinedShell = true };
                var result = BuildingGenerator.Plan(new("building-" + ordinal, lot, requirements, Preparation: request.Preparation), buildingScope);
                if (result is Rejected<BuildingPlan> rejected)
                {
                    failure = Fail(rejected.Failure.Code, rejected.Failure.Reason);
                    if (rejected.Failure.Code is PlacementFailureCode.BudgetExhausted or PlacementFailureCode.InvalidContent) return failure;
                    failed = true;
                    break;
                }
                var plan = ((Planned<BuildingPlan>)result).Plan;
                if (!draft.Accept(plan.Patch, out var error)) return Fail(PlacementFailureCode.ValidationFailed, error!);
                buildings.Add(plan.Building with { Purpose = request.Profile.ToString() });
                operations.AddRange(plan.Operations);
                var interior = plan.Building.Footprint.Indices(w).ToHashSet();
                interiors.UnionWith(interior);
                exterior.UnionWith(SiteGeometry.Cells(lot, w).Where(i => !interior.Contains(i)));
            }
            if (failed) continue;

            // Join the aprons before publication. Routes occupy only actual public
            // approach Cells, never the untouched rectangle between the buildings.
            HashSet<int> paths = [];
            var root = buildings[0].Portals[0].OutsideIndex;
            var approachBounds = GeneratedArea.From(id + "/approach-bounds", lots.SelectMany(lot => SiteGeometry.Cells(lot, w)), w).Bounds;
            foreach (var building in buildings.Skip(1))
            {
                var path = Approach(draft, root, building.Portals[0].OutsideIndex, interiors, approachBounds);
                if (path is null) return Fail(PlacementFailureCode.BudgetExhausted, "Ruin approach search budget exhausted.");
                if (path.Count == 0) { failed = true; break; }
                paths.UnionWith(path);
            }
            if (failed) { failure = Fail(PlacementFailureCode.DisconnectedSpace, "Ruin aprons cannot share a public approach."); continue; }
            exterior.UnionWith(paths);
            var before = draft.BuildPatch();
            var preparation = SiteGeometry.Prepare(draft, exterior, "grass", request.Preparation);
            if (preparation is not null)
            {
                failure = Fail(preparation.Code, preparation.Reason);
                if (preparation.Code == PlacementFailureCode.BudgetExhausted) return failure;
                continue;
            }
            // A broken gravel trace through overgrown, still passable ground.
            foreach (var cell in exterior.Order().Where(i => (i % w + 2 * (i / w)) % 4 == 0 || paths.Contains(i) && i % 3 != 0))
                draft.ReplaceTerrain(cell, "gravel");
            var footprint = interiors.Concat(exterior).ToHashSet();
            var access = SiteGeometry.Flood(draft, root, footprint);
            if (access is null) return Fail(PlacementFailureCode.BudgetExhausted, "Ruin access validation budget exhausted.");
            var usable = footprint.Where(draft.Passable).ToHashSet();
            if (access.Count != usable.Count || exterior.Any(i => !access.Contains(i)))
            { failure = Fail(PlacementFailureCode.DisconnectedSpace, "Every ruin room, gap and approach must share ordinary access."); continue; }
            var summary = SiteGeometry.Summary(draft.BuildPatch());
            if (summary.ChangedTerrain > request.Preparation.MaxChangedCells || summary.Cost > request.Preparation.MaxCost)
            { failure = Fail(PlacementFailureCode.PreparationLimit, "Completed ruin exceeds preparation allowance."); continue; }
            draft.Reserve(exterior);
            operations.Add(SiteGeometry.Operation(draft, before, id + "/approaches", id, "Overgrown aprons and disrupted paths", id,
                GeneratedArea.From(id + "/bounds", footprint, w).Bounds));
            var entrances = buildings.SelectMany(b => b.Portals).Where(p => exterior.Contains(p.OutsideIndex)).ToArray();
            var needsConnection = !exterior.Any(i => draft.Neighbors(i).Any(j => !footprint.Contains(j) && SiteGeometry.Crossable(draft, i, j)));
            var site = new SiteInfo(id, GeneratedSiteKind.Ruin, GeneratedArea.From(id + "/footprint", footprint, w),
                GeneratedArea.From(id + "/exterior", exterior, w), buildings, entrances,
                new(usable.Count, access.Count, true, needsConnection, SiteGeometry.TwoByTwo(draft, usable)),
                [SiteTag.Residential, SiteTag.Constructed], [], [], null, summary) { ConstructionProfile = request.Profile };
            site = SiteRouteAccess.Publish(site, draft, request.Preferences);
            return new Planned<SitePlan>(new(draft.BuildPatch(), site, operations));
        }
        return failure;
    }

    private static List<int>? Approach(BoardDraft draft, int start, int goal, HashSet<int> interiors, CellBounds bounds)
    {
        var previous = new Dictionary<int, int> { [start] = start };
        Queue<int> queue = new();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        {
            if (!draft.Budget.Spend(1)) return null;
            if (cell == goal)
            {
                List<int> result = [cell];
                while (previous[cell] != cell) { cell = previous[cell]; result.Add(cell); }
                return result;
            }
            foreach (var next in draft.Neighbors(cell).Order())
            {
                var x = next % draft.Width;
                var y = next / draft.Width;
                if (x < bounds.X || y < bounds.Y || x >= bounds.X + bounds.Width || y >= bounds.Y + bounds.Height ||
                    interiors.Contains(next) || draft.IsRouteCorridor(next) || !draft.CanWrite(next)) continue;
                var edge = draft.EdgeAt(EdgeKey.Between(cell, next, draft.Width));
                if (!edge.Passable() && edge != EdgeKind.ClosedDoor) continue;
                if (previous.TryAdd(next, cell)) queue.Enqueue(next);
            }
        }
        return [];
    }

    private static int DoorIndex(CellBounds lot, CardinalSide side, int width) => side switch
    {
        CardinalSide.North => lot.Y * width + lot.X + lot.Width / 2,
        CardinalSide.South => (lot.Y + lot.Height - 1) * width + lot.X + lot.Width / 2,
        CardinalSide.West => (lot.Y + lot.Height / 2) * width + lot.X,
        _ => (lot.Y + lot.Height / 2) * width + lot.X + lot.Width - 1
    };
}
