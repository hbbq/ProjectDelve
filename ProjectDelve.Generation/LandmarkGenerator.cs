using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record LandmarkRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    SiteConstructionProfile Profile, string TerrainTypeId, SitePlacementPreferences? Preferences = null);

/// <summary>Passive authored objects with a protected, ordinary traversable approach.</summary>
public static class LandmarkGenerator
{
    internal static bool IsLandmark(TerrainDefinition terrain) => terrain.Assembly is { Landmark: true } assembly &&
        !terrain.Passable && terrain.Production is null && assembly.Surface.Passable &&
        assembly.Surface.Production is null && !assembly.Surface.Categories.Contains(TerrainCategories.Water) &&
        assembly.Surface.LegacyKind != TerrainKind.Rail &&
        assembly.Components.Count(c => c.Definition.Presentation.Layer == FixedComponentLayer.Object) == 1 &&
        assembly.Components.Any(c => c.Definition.BlocksMovement);

    public static PlacementResult<SitePlan> Plan(LandmarkRequest request, GenerationScope scope)
    {
        var source = scope.Draft; var area = request.Area; var id = scope.ObjectId(request.Key);
        var start = scope.Budget.Attempts; var width = source.Width;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) =>
            new(new(id, "Landmark", code, reason, scope.Budget.Attempts - start));
        if (!SiteGeometry.Fits(area, source.AllowedArea) || area.Width < 3 || area.Height < 3 ||
            request.Profile is not (SiteConstructionProfile.RoadsideMarker or SiteConstructionProfile.IsolatedLandmark))
            return Fail(PlacementFailureCode.InvalidRequest, "Landmark requires a valid profile and room for its approach ring.");
        try { request.Preparation.Validate(); }
        catch (ArgumentException e) { return Fail(PlacementFailureCode.InvalidRequest, e.Message); }
        var terrain = scope.Content.Terrain.FirstOrDefault(t => t.Id == request.TerrainTypeId);
        if (terrain is null || !IsLandmark(terrain))
            return Fail(PlacementFailureCode.InvalidContent, "Landmark requires an explicitly eligible passive Terrain Assembly.");
        var surface = terrain.Assembly!.Surface.Id;
        var route = (request.Preferences?.RouteAnchors ?? []).Where(i => i >= 0 && i < width * source.Height && source.IsRouteCorridor(i)).Distinct().ToArray();
        int Distance(int a, int b) => Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
        var random = scope.Child(request.Key, source).Random("plots");
        var centers = new List<int>();
        // Route contacts are constructive rather than lucky samples on a large board.
        if (request.Profile == SiteConstructionProfile.RoadsideMarker)
        {
            foreach (var anchor in route)
            foreach (var offset in new[] { -2 * width, 2 * width, -2, 2 })
            {
                if (!scope.Budget.Spend(1)) return Fail(PlacementFailureCode.BudgetExhausted, "Landmark contact search budget exhausted.");
                var center = anchor + offset;
                if (center >= 0 && center < width * source.Height && Distance(anchor, center) == 2) centers.Add(center);
            }
        }
        else for (var n = 0; n < 128; n++)
        {
            if (!scope.Budget.Spend(1)) return Fail(PlacementFailureCode.BudgetExhausted, "Landmark plot search budget exhausted.");
            centers.Add((area.Y + 1 + random.Next(area.Height - 2)) * width + area.X + 1 + random.Next(area.Width - 2));
        }
        centers = centers.Distinct().ToList(); random.Shuffle(centers);
        var ranked = centers.OrderBy(i => request.Profile == SiteConstructionProfile.IsolatedLandmark && route.Length > 0 ? Math.Max(0, 6 - route.Min(r => Distance(i, r))) * 20 : 0)
            .ThenBy(i => request.Preferences?.PreferredAnchor is { } near ? Distance(i, near) * request.Preferences.AnchorWeight : 0);
        var code = PlacementFailureCode.NoFootprint; var reason = "No available Landmark footprint with an ordinary exterior approach.";
        foreach (var center in ranked.Take(128))
        {
            if (!scope.Budget.TryAttempt()) return Fail(PlacementFailureCode.BudgetExhausted, "Landmark attempt budget exhausted.");
            var plot = new CellBounds(center % width - 1, center / width - 1, 3, 3);
            if (!SiteGeometry.Fits(plot, area)) continue;
            var footprint = SiteGeometry.Cells(plot, width).ToArray(); var ring = footprint.Where(i => i != center).ToArray();
            if (footprint.Any(i => !source.CanWrite(i) || source.IsRouteCorridor(i))) { code = PlacementFailureCode.ReservationConflict; continue; }
            var contacts = ring.SelectMany(i => source.Neighbors(i).Where(j => !footprint.Contains(j) && source.Passable(j) &&
                    source.EdgeAt(EdgeKey.Between(i, j, width)).Passable() &&
                    (request.Profile != SiteConstructionProfile.RoadsideMarker || source.IsRouteCorridor(j)))
                .Select(j => (Inside: i, Outside: j))).OrderBy(p => Distance(center, p.Inside)).ThenBy(p => p.Inside).ThenBy(p => p.Outside).ToArray();
            if (contacts.Length == 0) continue;
            var draft = source.Fork(area, id); var before = draft.BuildPatch();
            var problem = SiteGeometry.Prepare(draft, footprint, surface, request.Preparation);
            if (problem is not null)
            {
                if (problem.Code == PlacementFailureCode.BudgetExhausted) return Fail(problem.Code, problem.Reason);
                code = problem.Code; reason = problem.Reason; continue;
            }
            draft.ReplaceTerrain(center, terrain.Id);
            var reached = SiteGeometry.Flood(draft, ring[0], ring);
            if (reached is null) return Fail(PlacementFailureCode.BudgetExhausted, "Landmark ring access budget exhausted.");
            if (ring.Any(i => !reached.Contains(i))) { code = PlacementFailureCode.DisconnectedSpace; continue; }
            var summary = SiteGeometry.Summary(draft.BuildPatch());
            if (summary.ChangedTerrain > request.Preparation.MaxChangedCells || summary.Cost > request.Preparation.MaxCost)
            { code = PlacementFailureCode.PreparationLimit; continue; }
            draft.Reserve(footprint);
            var contact = contacts[0]; var edge = EdgeKey.Between(contact.Inside, contact.Outside, width);
            var portal = new SitePortal(id + "/approach", id, contact.Inside, contact.Outside, edge, draft.EdgeAt(edge));
            var site = new SiteInfo(id, GeneratedSiteKind.Landmark, GeneratedArea.From(id + "/footprint", footprint, width),
                GeneratedArea.From(id + "/exterior", ring, width), [], [portal], new(8, 8, true, false, 0),
                [SiteTag.Constructed], [], [], null, summary) { ConstructionProfile = request.Profile };
            if (request.Profile == SiteConstructionProfile.RoadsideMarker) site = SiteRouteAccess.Publish(site, draft, request.Preferences);
            return new Planned<SitePlan>(new(draft.BuildPatch(), site,
                [SiteGeometry.Operation(draft, before, id, scope.Path, request.Profile.ToString(), id, plot)]));
        }
        return Fail(code, reason);
    }
}
