using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record SitePlacementPreferences(int? PreferredAnchor = null, IReadOnlyList<int>? OpportunityAnchors = null,
    IReadOnlyList<int>? RouteAnchors = null, CardinalSide? PreferredApproach = null)
{
    internal int AnchorWeight { get; init; }
}
public sealed record CampsiteRequirements(int MinimumUsableArea = 18, int Radius = 3, string? ScenarioRole = null)
{
    public SiteConstructionProfile? ConstructionProfile { get; init; }
}
public sealed record CampsiteRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    CampsiteRequirements? Requirements = null, SitePlacementPreferences? Preferences = null,
    IReadOnlyList<string>? SourceFormationIds = null, string? SourceOpportunityId = null);

public static class CampsiteGenerator
{
    public static PlacementResult<SitePlan> Plan(CampsiteRequest request, GenerationScope scope)
    {
        var id = scope.ObjectId(request.Key); var requirements = request.Requirements ?? new(); var startAttempts = scope.Budget.Attempts;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) => new(new(id, "Campsite", code, reason, scope.Budget.Attempts - startAttempts));
        if (requirements.ConstructionProfile is { } profile && profile is not (SiteConstructionProfile.OpenCamp or SiteConstructionProfile.ShelteredCamp or SiteConstructionProfile.PrimitiveHut))
            return Fail(PlacementFailureCode.InvalidRequest, "Campsites support OpenCamp, ShelteredCamp or PrimitiveHut construction.");
        if (!SiteGeometry.Fits(request.Area, scope.Draft.AllowedArea) || requirements.Radius is < 2 or > 5 || requirements.MinimumUsableArea < 1 ||
            requirements.MinimumUsableArea > (2 * requirements.Radius + 1) * (2 * requirements.Radius + 1) - 1 ||
            request.Area.Width < 2 * requirements.Radius + 5 || request.Area.Height < 2 * requirements.Radius + 5)
            return Fail(PlacementFailureCode.InvalidRequest, "Campsite needs room for a clearing, fire and access apron.");
        try { request.Preparation.Validate(); if (requirements.ScenarioRole is { } role) GenerationScope.ValidKey(role); }
        catch (ArgumentException e) { return Fail(PlacementFailureCode.InvalidRequest, e.Message); }
        if (!scope.Content.Terrain.Any(t => t.Id == "campfire" && !t.Passable) || !scope.Content.Terrain.Any(t => t.Id == "grass" && t.Passable) ||
            (requirements.ConstructionProfile is null
                ? !scope.Content.Terrain.Any(t => t.Id == "stone-floor-with-table" && !t.Passable) || !scope.Content.Terrain.Any(t => t.Id == "stone-floor" && t.Passable)
                : !scope.Content.Terrain.Any(t => t.Id == "dirt" && t.Passable)))
            return Fail(PlacementFailureCode.InvalidContent, "Campsite requires Grass, canonical Campfire, and the construction profile's floor and furniture terrain.");
        var call = scope.Child(request.Key, scope.Draft); var r = requirements.Radius;
        var candidates = new List<int>();
        if (request.Preferences?.PreferredAnchor is { } preferred) candidates.Add(preferred);
        candidates.AddRange(request.Preferences?.OpportunityAnchors ?? []);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var random = call.Random("placement", attempt);
            var x = request.Area.X + r + 2 + random.Next(request.Area.Width - 2 * r - 4);
            var y = request.Area.Y + r + 2 + random.Next(request.Area.Height - 2 * r - 4);
            candidates.Add(y * scope.Draft.Width + x);
        }
        var possible = candidates.Distinct().Where(i => i >= 0 && i < scope.Draft.Width * scope.Draft.Height &&
            SiteGeometry.Fits(new(i % scope.Draft.Width - r - 2, i / scope.Draft.Width - r - 2, 2 * r + 5, 2 * r + 5), request.Area))
            .Take(24).ToArray();
        // Existing opportunities/preferences lead; otherwise score only a small sample of prepared ground.
        var ranked = possible.Select((i, order) => (Index: i, Order: order,
            Cost: SiteGeometry.Cells(new(i % scope.Draft.Width - r, i / scope.Draft.Width - r, 2 * r + 1, 2 * r + 1), scope.Draft.Width)
                .Sum(j => scope.Draft.TerrainAt(j) is "tree" or "fruit-tree" ? 2 : scope.Draft.TerrainAt(j) == "solid-rock" ? 4 : TerrainCatalog.Find(scope.Content.Terrain, scope.Draft.TerrainAt(j)).Categories.Contains(TerrainCategories.Water) ? 6 : 0)))
            .OrderBy(c => request.Preferences?.PreferredAnchor == c.Index ? -10000 : c.Cost + SiteRouteAccess.ProximityCost(new(c.Index % scope.Draft.Width - r - 2, c.Index / scope.Draft.Width - r - 2, 2 * r + 5, 2 * r + 5), request.Preferences?.RouteAnchors, scope.Draft.Width)).ThenBy(c => c.Order);
        PlacementFailureCode lastCode = PlacementFailureCode.NoFootprint; var lastReason = "No campsite footprint fitted.";
        foreach (var candidate in ranked)
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Shared candidate budget exhausted.");
            var center = candidate.Index; var random = call.Random("clearing", candidate.Order);
            var cx = center % scope.Draft.Width; var cy = center / scope.Draft.Width;
            var area = new CellBounds(cx - r - 2, cy - r - 2, 2 * r + 5, 2 * r + 5);
            var draft = scope.Draft.Fork(SiteGeometry.Pad(area, request.Area, 1), id);
            var direction = random.Next(4);
            if (request.Preferences?.RouteAnchors is { Count: > 0 } routeAnchors)
            {
                var nearest = routeAnchors.OrderBy(i => Math.Abs(i % draft.Width - cx) + Math.Abs(i / draft.Width - cy)).ThenBy(i => i).First();
                var tx = nearest % draft.Width - cx; var ty = nearest / draft.Width - cy;
                direction = Math.Abs(tx) >= Math.Abs(ty) ? tx >= 0 ? 0 : 1 : ty >= 0 ? 2 : 3;
            }
            if (request.Preferences?.PreferredApproach is { } facing) direction = facing == CardinalSide.East ? 0 : facing == CardinalSide.West ? 1 : facing == CardinalSide.South ? 2 : 3;
            var dx = direction == 0 ? 1 : direction == 1 ? -1 : 0; var dy = direction == 2 ? 1 : direction == 3 ? -1 : 0;
            // Local u runs along the camp; v points towards its approach. Roadside camps
            // spread along the road rather than filling or obstructing the through corridor.
            var roadside = request.Preferences?.RouteAnchors is { Count: > 0 } && random.Next(2) == 0;
            var extent = r + 1; var depth = roadside ? 2 : r + 1;
            int Cell(int u, int v) => (cy + dx * u + dy * v) * draft.Width + cx - dy * u + dx * v;
            var cells = new HashSet<int>();
            for (var u = -extent; u <= extent; u++)
            for (var v = -depth; v <= depth; v++)
                if (roadside || Math.Abs(u) + Math.Abs(v) <= extent + depth - 2) cells.Add(Cell(u, v));
            for (var distance = 1; distance <= r + 2; distance++) cells.Add((cy + dy * distance) * draft.Width + cx + dx * distance);
            if (requirements.ConstructionProfile == SiteConstructionProfile.ShelteredCamp)
            {
                // Keep the ground behind the shelter connected around its side Walls in small clearings.
                cells.Add(Cell(-r - 1, -2));
                cells.Add(Cell(-r - 1, 2));
            }
            // A hut has a real shell and its own complete apron, including the corners
            // omitted by the irregular clearing. It shares the camp's private draft.
            CellBounds? hutPlot = null;
            if (requirements.ConstructionProfile == SiteConstructionProfile.PrimitiveHut)
            {
                var plotCells = Enumerable.Range(-r - 1, 4).SelectMany(u => Enumerable.Range(-2, 5).Select(v => Cell(u, v))).ToArray();
                hutPlot = GeneratedArea.From(id + "/hut-plot", plotCells, draft.Width).Bounds;
                cells.UnionWith(plotCells);
            }
            var before = draft.BuildPatch();
            var preparation = SiteGeometry.Prepare(draft, cells, "grass", request.Preparation);
            if (preparation is not null) { if (preparation.Code == PlacementFailureCode.BudgetExhausted) return Fail(preparation.Code, preparation.Reason); lastCode = preparation.Code; lastReason = preparation.Reason; continue; }
            List<ConstructionOperation> operations = [new(id, scope.Path, "Campsite", id, area, [], [])];
            operations.Add(SiteGeometry.Operation(draft, before, id + "/prepare", id, "Clearing and access apron", id, area));
            before = draft.BuildPatch();
            List<BuildingInfo> buildings = [];
            if (requirements.ConstructionProfile is null)
            {
                // Preserve legacy furnishing and random draws for unprofiled requests.
                foreach (var u in new[] { -r, r })
                foreach (var v in new[] { -1, 1 })
                {
                    if (v == -1 || random.Next(3) != 0) draft.ReplaceTerrain(Cell(u, v), "stone-floor-with-table");
                    draft.ReplaceTerrain(Cell(u, 0), "stone-floor");
                }
            }
            else
            {
                foreach (var u in new[] { -r, r })
                foreach (var v in new[] { -1, 0, 1 }) draft.ReplaceTerrain(Cell(u, v), "dirt");
                foreach (var v in new[] { -1, 1 }) draft.ReplaceTerrain(Cell(0, v), "dirt");
                if (requirements.ConstructionProfile == SiteConstructionProfile.ShelteredCamp)
                {
                    // Three Wall sides leave a full open frontage toward the hearth.
                    for (var v = -1; v <= 1; v++)
                    {
                        draft.ReplaceTerrain(Cell(-r + 1, v), "dirt");
                        draft.ReplaceEdge(EdgeKey.Between(Cell(-r, v), Cell(-r - 1, v), draft.Width), EdgeKind.Wall);
                    }
                    foreach (var u in new[] { -r, -r + 1 })
                    foreach (var v in new[] { -1, 1 })
                        draft.ReplaceEdge(EdgeKey.Between(Cell(u, v), Cell(u, v + Math.Sign(v)), draft.Width), EdgeKind.Wall);
                }
            }
            if (hutPlot is null) draft.ReplaceTerrain(center, "campfire");
            operations.Add(SiteGeometry.Operation(draft, before, id + "/furnish", id, "Shared hearth and supply clusters", id, area));
            if (hutPlot is { } plot)
            {
                var entranceSide = dy > 0 ? CardinalSide.West : dy < 0 ? CardinalSide.East : dx > 0 ? CardinalSide.South : CardinalSide.North;
                var entranceOutside = Cell(-r + 2, 1);
                var hut = BuildingGenerator.Plan(new("hut", plot,
                    new(MinWidth: plot.Width - 2, MaxWidth: plot.Width - 2,
                        MinHeight: plot.Height - 2, MaxHeight: plot.Height - 2,
                        RoomCount: 1, FloorTypeId: "dirt", Furniture: false,
                        Entrance: new(entranceSide, entranceOutside)), Preparation: request.Preparation),
                    new GenerationScope(scope.RootSeed, id, draft));
                if (hut is Rejected<BuildingPlan> rejected)
                {
                    if (rejected.Failure.Code == PlacementFailureCode.BudgetExhausted) return Fail(rejected.Failure.Code, rejected.Failure.Reason);
                    lastCode = rejected.Failure.Code; lastReason = rejected.Failure.Reason; continue;
                }
                var plan = ((Planned<BuildingPlan>)hut).Plan;
                if (!draft.Accept(plan.Patch, out var error)) return Fail(PlacementFailureCode.ValidationFailed, error!);
                buildings.Add(plan.Building with { Purpose = "Primitive hut" });
                operations.AddRange(plan.Operations);
                before = draft.BuildPatch();
                draft.ReplaceTerrain(center, "campfire");
                operations.Add(SiteGeometry.Operation(draft, before, id + "/hearth", id, "Camp hearth", id, area));
            }
            var usable = cells.Where(draft.Passable).ToHashSet();
            var outside = (cy + dy * (r + 2)) * draft.Width + cx + dx * (r + 2);
            var inside = outside - dy * draft.Width - dx;
            var reached = SiteGeometry.Flood(draft, outside, cells);
            if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Shared traversal budget exhausted.");
            if (usable.Count < requirements.MinimumUsableArea || !usable.SetEquals(reached))
            { lastCode = PlacementFailureCode.DisconnectedSpace; lastReason = "Clearing lacks connected usable ground."; continue; }
            var needsConnection = !cells.Any(i => draft.Neighbors(i).Any(j => !cells.Contains(j) && SiteGeometry.Crossable(draft, i, j)));
            var portal = new SitePortal(id + "/approach", id, inside, outside, EdgeKey.Between(inside, outside, draft.Width), EdgeKind.None);
            var patch = draft.BuildPatch(); var summary = SiteGeometry.Summary(patch);
            if (summary.Cost > request.Preparation.MaxCost || summary.ChangedTerrain > request.Preparation.MaxChangedCells)
            { lastCode = PlacementFailureCode.PreparationLimit; lastReason = "Complete campsite exceeds preparation allowance."; continue; }
            draft.Reserve(cells.Where(i => !draft.IsReserved(i)));
            operations.Add(new(id + "/commit", id, "Site ready for atomic commit", id, area, [], []));
            var tags = new List<SiteTag> { SiteTag.Camp, SiteTag.Constructed }; if (summary.WaterFilled > 0 && needsConnection) tags.Add(SiteTag.ArtificialIsland);
            var site = new SiteInfo(id, GeneratedSiteKind.Campsite, GeneratedArea.From(id + "/footprint", cells, draft.Width),
                GeneratedArea.From(id + "/exterior", usable.Except(buildings.SelectMany(b => b.Footprint.Indices(draft.Width))), draft.Width), buildings,
                new[] { portal }.Concat(buildings.SelectMany(b => b.Portals)).ToArray(),
                new(usable.Count, reached.Count, true, needsConnection, SiteGeometry.TwoByTwo(draft, usable)), tags,
                requirements.ScenarioRole is { } roleId ? [new(roleId, GeneratedObjectKind.Site, id)] : [], request.SourceFormationIds ?? [], request.SourceOpportunityId, summary)
            {
                ConstructionProfile = requirements.ConstructionProfile,
                PopulationIntent = PopulationIntent(id, draft, cells, center, outside, r, roadside, Cell,
                    call.Random("alert-topology").Next(2) == 0 ? CampAlertTopology.Connected : CampAlertTopology.Separated)
            };
            site = SiteRouteAccess.Publish(site, draft, request.Preferences);
            return new Planned<SitePlan>(new(draft.BuildPatch(), site, operations));
        }
        return Fail(lastCode, lastReason);
    }

    private static SitePopulationIntent PopulationIntent(string id, BoardDraft draft, HashSet<int> cells, int center,
        int entrance, int radius, bool roadside, Func<int, int, int> cell, CampAlertTopology topology)
    {
        var areas = new List<SitePopulationArea>();
        void Add(string key, string role, IEnumerable<int> candidates, int preferred, string cluster)
        {
            var usable = candidates.Where(i => cells.Contains(i) && draft.Passable(i)).Distinct().Order().ToArray();
            if (usable.Length == 0) return;
            var anchor = usable.OrderBy(i => Math.Abs(i % draft.Width - preferred % draft.Width) + Math.Abs(i / draft.Width - preferred / draft.Width)).ThenBy(i => i).First();
            areas.Add(new(id + "/population/" + key, role, GeneratedArea.From(id + "/population/" + key, usable, draft.Width), anchor, id + "/" + cluster));
        }
        Add("hearth", "central", cells.Where(i => Distance(i, center) <= 2), cell(0, -1), "shared");
        foreach (var sign in new[] { -1, 1 })
        {
            var key = sign < 0 ? "west" : "east";
            var wing = Enumerable.Range(radius, 2).SelectMany(u => Enumerable.Range(-2, 5).Select(v => cell(sign * u, v))).ToArray();
            Add(key + "-rest", "resting", wing, cell(sign * (radius + 1), 0), key);
            Add(key + "-supply", "supplies", wing.Where(i => Distance(i, cell(sign * radius, 0)) <= 1), cell(sign * radius, 0), key);
            Add(key + "-sentry", "sentry", Enumerable.Range(-1, 3).Select(v => cell(sign * (radius + 1), v)), cell(sign * (radius + 1), 0), key);
        }
        Add("approach", "sentry", [entrance], entrance, "approach");
        // A shortest path is published only after checking every physical Cell and Edge.
        // It is open: no implied connection from its last Cell back to its first.
        var start = cell(-radius - 1, 0); var goal = cell(radius + 1, 0);
        var parents = new Dictionary<int, int> { [start] = -1 }; var queue = new Queue<int>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var i))
        {
            if (!draft.Budget.Spend(1)) return new(roadside ? "roadside" : "clearing-centered", areas, [], 6, 16, topology);
            if (i == goal) break;
            foreach (var j in draft.Neighbors(i))
                if (cells.Contains(j) && draft.Passable(j) && draft.EdgeAt(EdgeKey.Between(i, j, draft.Width)).Passable() && parents.TryAdd(j, i)) queue.Enqueue(j);
        }
        var path = new List<int>();
        if (parents.ContainsKey(goal)) { for (var i = goal; i >= 0; i = parents[i]) path.Add(i); path.Reverse(); }
        return new(roadside ? "roadside" : "clearing-centered", areas, path, 6, 16, topology);

        int Distance(int a, int b) => Math.Abs(a % draft.Width - b % draft.Width) + Math.Abs(a / draft.Width - b / draft.Width);
    }
}
