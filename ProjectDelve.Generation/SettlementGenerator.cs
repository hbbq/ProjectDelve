using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record SettlementRequirements(int BuildingCount = 3, int MinimumRooms = 1, int MaximumRooms = 3,
    bool Furniture = true, string FloorTypeId = "stone-floor", string? ScenarioRole = null);
public sealed record SettlementRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    SettlementRequirements? Requirements = null, SitePlacementPreferences? Preferences = null);

public static class SettlementGenerator
{
    public static PlacementResult<SitePlan> Plan(SettlementRequest request, GenerationScope scope)
    {
        var id = scope.ObjectId(request.Key); var r = request.Requirements ?? new(); var start = scope.Budget.Attempts;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) => new(new(id, "Settlement", code, reason, scope.Budget.Attempts - start));
        if (!SiteGeometry.Fits(request.Area, scope.Draft.AllowedArea) || request.Area.Width < 21 || request.Area.Height < 21 ||
            r.BuildingCount is < 2 or > 4 || r.MinimumRooms < 1 || r.MaximumRooms > 3 || r.MaximumRooms < r.MinimumRooms)
            return Fail(PlacementFailureCode.InvalidRequest, "A small Settlement needs at least 21x21 Cells, 2..4 buildings and 1..3 rooms each.");
        try { request.Preparation.Validate(); if (r.ScenarioRole is { } role) GenerationScope.ValidKey(role); }
        catch (ArgumentException e) { return Fail(PlacementFailureCode.InvalidRequest, e.Message); }
        if (!scope.Content.Terrain.Any(t => t.Id == r.FloorTypeId && t.Passable && t.LegacyKind != TerrainKind.Rail) ||
            !scope.Content.Terrain.Any(t => t.Id == "grass" && t.Passable) || !scope.Content.Terrain.Any(t => t.Id == "gravel" && t.Passable) ||
            r.Furniture && !scope.Content.Terrain.Any(t => t.Id == "stone-floor-with-table" && !t.Passable))
            return Fail(PlacementFailureCode.InvalidContent, "Settlement materials are unavailable or require unsupported placement properties.");
        var call = scope.Child(request.Key, scope.Draft); var w = scope.Draft.Width;
        var candidates = new List<(CellBounds Bounds, int PlotWidth, int PlotHeight, int Ordinal, int Cost)>();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var random = call.Random("placement", attempt);
            var pw = 9 + random.Next(Math.Min(3, (request.Area.Width - 3) / 2 - 8));
            var ph = 9 + random.Next(Math.Min(3, (request.Area.Height - 3) / 2 - 8));
            var width = 2 * pw + 3; var height = 2 * ph + 3;
            var x = request.Area.X + random.Next(request.Area.Width - width + 1); var y = request.Area.Y + random.Next(request.Area.Height - height + 1);
            if (attempt == 0 && request.Preferences?.PreferredAnchor is { } anchor && anchor >= 0 && anchor < w * scope.Draft.Height)
            {
                x = Math.Clamp(anchor % w - width / 2, request.Area.X, request.Area.X + request.Area.Width - width);
                y = Math.Clamp(anchor / w - height / 2, request.Area.Y, request.Area.Y + request.Area.Height - height);
            }
            var bounds = new CellBounds(x, y, width, height);
            if (!scope.Budget.Spend(width * height)) return Fail(PlacementFailureCode.BudgetExhausted, "Settlement candidate search budget exhausted.");
            var cost = SiteGeometry.Cells(bounds, w).Sum(i => scope.Draft.TerrainAt(i) is "tree" or "fruit-tree" ? 2 : scope.Draft.TerrainAt(i) == "solid-rock" ? 4 : scope.Draft.TerrainAt(i) == "water" ? 6 : 0);
            candidates.Add((bounds, pw, ph, attempt, cost));
        }
        var lastCode = PlacementFailureCode.NoFootprint; var lastReason = "No complete Settlement could fit.";
        foreach (var candidate in candidates.OrderBy(c => c.Cost).ThenBy(c => c.Ordinal))
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Settlement attempt budget exhausted.");
            var area = candidate.Bounds; var draft = scope.Draft.Fork(area, id); var localScope = new GenerationScope(scope.RootSeed, id, draft);
            var pw = candidate.PlotWidth; var ph = candidate.PlotHeight; var midX = area.X + pw + 1; var midY = area.Y + ph + 1;
            var slots = new List<(string Key, CellBounds Plot, CardinalSide Facing)>
            {
                ("north-west", new(area.X,area.Y,pw,ph), CardinalSide.East),
                ("north-east", new(area.X+pw+3,area.Y,pw,ph), CardinalSide.West),
                ("south-west", new(area.X,area.Y+ph+3,pw,ph), CardinalSide.East),
                ("south-east", new(area.X+pw+3,area.Y+ph+3,pw,ph), CardinalSide.West)
            };
            call.Random("lots", candidate.Ordinal).Shuffle(slots); var chosen = slots.Take(r.BuildingCount).ToArray();
            // Prepare the street/plaza, not whole candidate plots. Children prepare only their actual buildings.
            var footprint = SiteGeometry.Cells(new(midX - 1, area.Y, 3, area.Height), w).ToHashSet();
            footprint.UnionWith(SiteGeometry.Cells(new(midX - 2, midY - 2, 5, 5), w));
            var before = draft.BuildPatch(); var preparation = SiteGeometry.Prepare(draft, footprint, "grass", request.Preparation);
            if (preparation is not null)
            {
                if (preparation.Code == PlacementFailureCode.BudgetExhausted) return Fail(preparation.Code, preparation.Reason);
                lastCode = preparation.Code; lastReason = preparation.Reason; continue;
            }
            List<ConstructionOperation> operations = [new(id, scope.Path, "Settlement", id, area, [], [])];
            operations.Add(SiteGeometry.Operation(draft, before, id + "/prepare", id, "Prepared plots and exterior access", id, area));
            before = draft.BuildPatch();
            foreach (var i in footprint) draft.ReplaceTerrain(i, "gravel");
            operations.Add(SiteGeometry.Operation(draft, before, id + "/street", id, "Shared internal access", id, area));
            var buildings = new List<BuildingInfo>(); var failed = false;
            foreach (var slot in chosen)
            {
                var countRandom = localScope.Child(slot.Key, draft).Random("requirements");
                var rooms = r.MinimumRooms + countRandom.Next(r.MaximumRooms - r.MinimumRooms + 1);
                var buildingResult = BuildingGenerator.Plan(new(slot.Key, slot.Plot,
                    new(MinWidth: 5, MaxWidth: pw - 2, MinHeight: 5, MaxHeight: ph - 2, RoomCount: rooms,
                        FloorTypeId: r.FloorTypeId, Furniture: r.Furniture, Entrance: new(slot.Facing)),
                    new(NearIndex: midY * w + midX, PreferredSide: slot.Facing), request.Preparation), localScope);
                if (buildingResult is Rejected<BuildingPlan> rejected)
                {
                    if (rejected.Failure.Code == PlacementFailureCode.BudgetExhausted) return Fail(rejected.Failure.Code, rejected.Failure.Reason);
                    lastCode = rejected.Failure.Code; lastReason = "Child building: " + rejected.Failure.Reason; failed = true; break;
                }
                var building = ((Planned<BuildingPlan>)buildingResult).Plan;
                if (!draft.Accept(building.Patch, out var error)) return Fail(PlacementFailureCode.ValidationFailed, error!);
                buildings.Add(building.Building); operations.AddRange(building.Operations);
                // Keep one ground apron around the real footprint; unused plot margins retain natural terrain.
                before = draft.BuildPatch(); var door = building.Building.Portals.Single(p => p.Id.EndsWith("/entrance", StringComparison.Ordinal));
                var b = building.Building.Footprint.Bounds;
                var actual = SiteGeometry.Cells(new(b.X - 1, b.Y - 1, b.Width + 2, b.Height + 2), w).ToArray();
                var interior = building.Building.Footprint.Indices(w).ToHashSet();
                foreach (var i in actual.Where(i => !interior.Contains(i))) draft.ReplaceTerrain(i, "grass");
                footprint.UnionWith(actual);
                // Hard-facing entrances give short connections without crossing any house interior.
                var position = door.OutsideIndex;
                var approach = new List<int>();
                while (position % w != midX)
                {
                    approach.Add(position); position += position % w < midX ? 1 : -1;
                }
                approach.Add(position);
                var pathPreparation = SiteGeometry.Prepare(draft, approach, "grass", request.Preparation);
                if (pathPreparation is not null)
                {
                    if (pathPreparation.Code == PlacementFailureCode.BudgetExhausted) return Fail(pathPreparation.Code, pathPreparation.Reason);
                    lastCode = pathPreparation.Code; lastReason = pathPreparation.Reason; failed = true; break;
                }
                foreach (var i in approach) draft.ReplaceTerrain(i, "gravel"); footprint.UnionWith(approach);
                operations.Add(SiteGeometry.Operation(draft, before, building.Building.Id + "/path", id, "Building approach path", building.Building.Id, area));
            }
            if (failed) continue;
            var usable = footprint.Where(draft.Passable).ToHashSet(); var entranceOutside = area.Y * w + midX; var entranceInside = entranceOutside + w;
            var reached = SiteGeometry.Flood(draft, entranceOutside, footprint);
            if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Settlement access validation budget exhausted.");
            if (!usable.SetEquals(reached)) { lastCode = PlacementFailureCode.DisconnectedSpace; lastReason = "Settlement interiors and exterior must share usable access."; continue; }
            var patch = draft.BuildPatch(); var summary = SiteGeometry.Summary(patch);
            if (summary.ChangedTerrain > request.Preparation.MaxChangedCells || summary.Cost > request.Preparation.MaxCost)
            { lastCode = PlacementFailureCode.PreparationLimit; lastReason = "Complete Settlement exceeds preparation allowance."; continue; }
            draft.Reserve(footprint.Where(i => !draft.IsReserved(i)));
            var interiors = buildings.SelectMany(b => b.Footprint.Indices(w)).ToHashSet();
            var exterior = usable.Where(i => !interiors.Contains(i)).ToArray();
            var needsConnection = !footprint.Any(i => draft.Neighbors(i).Any(j => !footprint.Contains(j) && SiteGeometry.Crossable(draft, i, j)));
            var tags = new List<SiteTag> { SiteTag.Residential, SiteTag.Constructed }; if (summary.WaterFilled > 0 && needsConnection) tags.Add(SiteTag.ArtificialIsland);
            var entrance = new SitePortal(id + "/approach", id, entranceInside, entranceOutside, EdgeKey.Between(entranceInside, entranceOutside, w), EdgeKind.None, 3);
            var info = new SiteInfo(id, GeneratedSiteKind.Settlement, GeneratedArea.From(id + "/footprint", footprint, w), GeneratedArea.From(id + "/exterior", exterior, w),
                buildings, [entrance], new(usable.Count, reached.Count, true, needsConnection, SiteGeometry.TwoByTwo(draft, usable)), tags,
                r.ScenarioRole is { } roleId ? [new(roleId, GeneratedObjectKind.Site, id)] : [], [], null, summary);
            operations.Add(new(id + "/commit", id, "Site ready for atomic commit", id, area, [], []));
            return new Planned<SitePlan>(new(draft.BuildPatch(), info, operations));
        }
        return Fail(lastCode, lastReason);
    }
}
