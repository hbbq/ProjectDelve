using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public enum CardinalSide { North, East, South, West }
public sealed record BuildingEntranceRequirement(CardinalSide? Side = null, int? NearIndex = null, int MaxDistance = 0);
public sealed record BuildingPreferences(int? NearIndex = null, CardinalSide? PreferredSide = null);
public sealed record BuildingRequirements(int MinWidth = 5, int MaxWidth = 9, int MinHeight = 5, int MaxHeight = 9,
    int RoomCount = 2, string FloorTypeId = "stone-floor", bool Furniture = true,
    BuildingEntranceRequirement? Entrance = null, string? ScenarioRole = null, string FurnitureTypeId = "stone-floor-with-table");
public sealed record BuildingRequest(string Key, CellBounds Plot, BuildingRequirements? Requirements = null,
    BuildingPreferences? Preferences = null, PreparationPolicy? Preparation = null);

public static class BuildingGenerator
{
    public static PlacementResult<BuildingPlan> Plan(BuildingRequest request, GenerationScope scope)
    {
        var id = scope.ObjectId(request.Key); var r = request.Requirements ?? new(); var policy = request.Preparation ?? new(); var start = scope.Budget.Attempts;
        Rejected<BuildingPlan> Fail(PlacementFailureCode code, string reason) => new(new(id, "Building", code, reason, scope.Budget.Attempts - start));
        bool ValidIndex(int? i) => i is null || i >= 0 && i < scope.Draft.Width * scope.Draft.Height;
        if (!SiteGeometry.Fits(request.Plot, scope.Draft.AllowedArea) || r.MinWidth < 2 || r.MinHeight < 2 || r.MaxWidth < r.MinWidth || r.MaxHeight < r.MinHeight ||
            r.MaxWidth > 40 || r.MaxHeight > 40 || r.RoomCount is < 1 or > 3 || r.Entrance?.MaxDistance < 0 ||
            !ValidIndex(r.Entrance?.NearIndex) || !ValidIndex(request.Preferences?.NearIndex) ||
            r.Entrance?.Side is { } side && !Enum.IsDefined(side) || request.Preferences?.PreferredSide is { } preferred && !Enum.IsDefined(preferred))
            return Fail(PlacementFailureCode.InvalidRequest, "Invalid building dimensions, Plot or entrance requirement.");
        try { policy.Validate(); if (r.ScenarioRole is { } role) GenerationScope.ValidKey(role); }
        catch (ArgumentException e) { return Fail(PlacementFailureCode.InvalidRequest, e.Message); }
        if (!scope.Content.Terrain.Any(t => t.Id == r.FloorTypeId && t.Passable && t.LegacyKind != TerrainKind.Rail) ||
            r.Furniture && !scope.Content.Terrain.Any(t => t.Id == r.FurnitureTypeId && !t.Passable))
            return Fail(PlacementFailureCode.InvalidContent, "Building requires known passable floor and obstructing furniture Types.");
        var call = scope.Child(request.Key, scope.Draft); var random = call.Random("footprints");
        var candidates = new List<(CellBounds Interior, int Inside, int Outside, CardinalSide Side)>();
        var w = scope.Draft.Width;
        int Distance(int a, int b) => Math.Abs(a % w - b % w) + Math.Abs(a / w - b / w);
        var maxWidth = Math.Min(r.MaxWidth, request.Plot.Width - 2); var maxHeight = Math.Min(r.MaxHeight, request.Plot.Height - 2);
        for (var sample = 0; sample < 256 && maxWidth >= r.MinWidth && maxHeight >= r.MinHeight; sample++)
        {
            if (!scope.Budget.Spend(1)) return Fail(PlacementFailureCode.BudgetExhausted, "Footprint search budget exhausted.");
            // The first four minimal footprints cover each orientation. Remaining bounded
            // samples vary dimensions and placement without enumerating a large Board.
            var width = sample < 4 ? r.MinWidth : r.MinWidth + random.Next(maxWidth - r.MinWidth + 1);
            var height = sample < 4 ? r.MinHeight : r.MinHeight + random.Next(maxHeight - r.MinHeight + 1);
            var s = r.Entrance?.Side ?? (CardinalSide)(sample % 4);
            var left = request.Plot.X + 1; var top = request.Plot.Y + 1;
            var right = request.Plot.X + request.Plot.Width - width - 1;
            var bottom = request.Plot.Y + request.Plot.Height - height - 1;
            var x = left + random.Next(right - left + 1); var y = top + random.Next(bottom - top + 1);
            var length = s is CardinalSide.North or CardinalSide.South ? width : height;
            var offset = random.Next(length);
            if (r.Entrance?.NearIndex is { } near)
            {
                // Project onto the feasible exterior endpoint rectangle, then choose a
                // footprint containing that endpoint. Exact entrances therefore remain
                // discoverable even when the Plot is much larger than the building.
                var nx = near % w; var ny = near / w;
                if (s is CardinalSide.North or CardinalSide.South)
                {
                    var ex = Math.Clamp(nx, left, right + width - 1);
                    var ey = s == CardinalSide.North ? Math.Clamp(ny, top - 1, bottom - 1) : Math.Clamp(ny, top + height, bottom + height);
                    if (Math.Abs(nx - ex) + Math.Abs(ny - ey) > r.Entrance.MaxDistance) continue;
                    var first = Math.Max(left, ex - width + 1); var last = Math.Min(right, ex);
                    x = first + random.Next(last - first + 1); y = s == CardinalSide.North ? ey + 1 : ey - height; offset = ex - x;
                }
                else
                {
                    var ey = Math.Clamp(ny, top, bottom + height - 1);
                    var ex = s == CardinalSide.West ? Math.Clamp(nx, left - 1, right - 1) : Math.Clamp(nx, left + width, right + width);
                    if (Math.Abs(nx - ex) + Math.Abs(ny - ey) > r.Entrance.MaxDistance) continue;
                    var first = Math.Max(top, ey - height + 1); var last = Math.Min(bottom, ey);
                    y = first + random.Next(last - first + 1); x = s == CardinalSide.West ? ex + 1 : ex - width; offset = ey - y;
                }
            }
            var inside = s switch { CardinalSide.North => y * w + x + offset, CardinalSide.South => (y + height - 1) * w + x + offset,
                CardinalSide.West => (y + offset) * w + x, _ => (y + offset) * w + x + width - 1 };
            var outside = inside + (s switch { CardinalSide.North => -w, CardinalSide.South => w, CardinalSide.West => -1, _ => 1 });
            candidates.Add((new(x, y, width, height), inside, outside, s));
        }
        candidates = candidates.Distinct().ToList();
        random.Shuffle(candidates);
        var ranked = candidates.OrderBy(c => request.Preferences?.PreferredSide is { } p && c.Side != p ? 1 : 0)
            .ThenBy(c => request.Preferences?.NearIndex is { } near ? Distance(c.Outside, near) : 0);
        var lastCode = candidates.Count == 0 && r.Entrance is not null ? PlacementFailureCode.EntranceConstraint : PlacementFailureCode.NoFootprint;
        var lastReason = "No building footprint satisfies the Plot and entrance constraints.";
        foreach (var c in ranked.Take(24))
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Building attempt budget exhausted.");
            var area = new CellBounds(c.Interior.X - 1, c.Interior.Y - 1, c.Interior.Width + 2, c.Interior.Height + 2);
            var draft = scope.Draft.Fork(request.Plot, id); var all = SiteGeometry.Cells(area, w).ToArray();
            if (all.Any(i => !draft.CanWrite(i))) { lastCode = PlacementFailureCode.ReservationConflict; lastReason = "Building or apron intersects protected space."; continue; }
            var before = draft.BuildPatch(); var preparation = SiteGeometry.Prepare(draft, all, r.FloorTypeId, policy);
            if (preparation is not null) { if (preparation.Code == PlacementFailureCode.BudgetExhausted) return Fail(preparation.Code, preparation.Reason); lastCode = preparation.Code; lastReason = preparation.Reason; continue; }
            var interior = SiteGeometry.Cells(c.Interior, w).ToHashSet();
            var conflict = false;
            foreach (var i in interior) foreach (var j in draft.Neighbors(i).Where(j => !interior.Contains(j)))
            {
                try
                {
                    var key = EdgeKey.Between(i, j, w);
                    if (policy.ReplaceExistingEdges) draft.ReplaceEdge(key, EdgeKind.Wall);
                    else draft.SetEdge(key, EdgeKind.Wall);
                }
                catch (ArgumentException) { conflict = true; }
            }
            if (conflict) { lastCode = PlacementFailureCode.NoFootprint; lastReason = "Exterior Walls conflict with an earlier construction edit."; continue; }
            List<ConstructionOperation> operations = [new(id, scope.Path, "Building", id, c.Interior, [], []),
                SiteGeometry.Operation(draft, before, id + "/shell", id, "Prepared floor and exterior Walls", id, area)];
            before = draft.BuildPatch(); var entranceEdge = EdgeKey.Between(c.Inside, c.Outside, w); draft.ReplaceEdge(entranceEdge, EdgeKind.ClosedDoor);
            var entrance = new SitePortal(id + "/entrance", id, c.Inside, c.Outside, entranceEdge, EdgeKind.ClosedDoor);
            operations.Add(SiteGeometry.Operation(draft, before, id + "/entrance", id, "Exterior Door", id, c.Interior));
            var rooms = RoomLayoutGenerator.Plan(new("rooms", c.Interior, r.RoomCount, r.Furniture, [entrance], r.FurnitureTypeId, policy.ReplaceExistingEdges), new GenerationScope(scope.RootSeed, id, draft));
            if (rooms is Rejected<RoomLayoutPlan> rejected)
            {
                if (rejected.Failure.Code == PlacementFailureCode.BudgetExhausted) return Fail(rejected.Failure.Code, rejected.Failure.Reason);
                lastCode = rejected.Failure.Code; lastReason = rejected.Failure.Reason; continue;
            }
            var layout = ((Planned<RoomLayoutPlan>)rooms).Plan;
            if (!draft.Accept(layout.Patch, out var error)) return Fail(PlacementFailureCode.ValidationFailed, error!);
            operations.AddRange(layout.Operations);
            var usable = interior.Where(draft.Passable).ToArray(); var reached = SiteGeometry.Flood(draft, c.Outside, all);
            if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Building access budget exhausted.");
            if (usable.Any(i => !reached.Contains(i))) { lastCode = PlacementFailureCode.DisconnectedSpace; lastReason = "Every room must connect to the exterior Door."; continue; }
            var summary = SiteGeometry.Summary(draft.BuildPatch());
            if (summary.ChangedTerrain > policy.MaxChangedCells || summary.Cost > policy.MaxCost)
            { lastCode = PlacementFailureCode.PreparationLimit; lastReason = "Completed building exceeds preparation allowance."; continue; }
            draft.Reserve(interior);
            var portals = new[] { entrance }.Concat(layout.Portals).ToArray();
            var building = new BuildingInfo(id, "Building", GeneratedArea.From(id + "/footprint", interior, w), layout.Rooms, portals,
                new(usable.Length, usable.Count(reached.Contains), true,
                    !all.Any(i => draft.Neighbors(i).Any(j => !all.Contains(j) && SiteGeometry.Crossable(draft, i, j))), SiteGeometry.TwoByTwo(draft, usable)),
                r.ScenarioRole is { } roleId ? [new(roleId, GeneratedObjectKind.Building, id)] : []);
            return new Planned<BuildingPlan>(new(draft.BuildPatch(), building, operations));
        }
        return Fail(lastCode, lastReason);
    }
}
