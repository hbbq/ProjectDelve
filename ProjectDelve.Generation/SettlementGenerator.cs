using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record SettlementRequirements(int BuildingCount = 3, int MinimumRooms = 1, int MaximumRooms = 3,
    bool Furniture = true, string FloorTypeId = "stone-floor", string? ScenarioRole = null,
    SettlementFamily? Family = null, bool AllowReducedBuildingCount = false);
public sealed record SettlementRequest(string Key, CellBounds Area, PreparationPolicy Preparation,
    SettlementRequirements? Requirements = null, SitePlacementPreferences? Preferences = null);

public static class SettlementGenerator
{
    private const int BuildCandidatesPerFamily = 4;

    public static PlacementResult<SitePlan> Plan(SettlementRequest request, GenerationScope scope)
    {
        var id = scope.ObjectId(request.Key);
        var requirements = request.Requirements ?? new();
        var start = scope.Budget.Attempts;
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) =>
            new(new(id, "Settlement", code, reason, scope.Budget.Attempts - start));

        var board = scope.Draft;
        var w = board.Width;
        if (!SiteGeometry.Fits(request.Area, board.AllowedArea) ||
            requirements.BuildingCount is < 2 or > 4 || requirements.MinimumRooms < 1 || requirements.MaximumRooms > 3 ||
            requirements.MaximumRooms < requirements.MinimumRooms ||
            requirements.Family is { } family && !Enum.IsDefined(family))
            return Fail(PlacementFailureCode.InvalidRequest, "Invalid Settlement area, family, building count (2..4), or room limits (1..3).");
        try
        {
            request.Preparation.Validate();
            if (requirements.ScenarioRole is { } role) GenerationScope.ValidKey(role);
        }
        catch (ArgumentException error) { return Fail(PlacementFailureCode.InvalidRequest, error.Message); }

        if (!scope.Content.Terrain.Any(t => t.Id == requirements.FloorTypeId && t.Passable && t.LegacyKind != TerrainKind.Rail) ||
            !scope.Content.Terrain.Any(t => t.Id == "grass" && t.Passable) ||
            !scope.Content.Terrain.Any(t => t.Id == "gravel" && t.Passable) ||
            requirements.Furniture && !scope.Content.Terrain.Any(t => t.Id == "stone-floor-with-table" && !t.Passable))
            return Fail(PlacementFailureCode.InvalidContent, "Settlement materials are unavailable or require unsupported placement properties.");

        var anchors = (request.Preferences?.RouteAnchors ?? []).Where(i => i >= 0 && i < w * board.Height).Distinct().Order().ToArray();
        if (!scope.Budget.Spend((long)request.Area.Width * request.Area.Height))
            return Fail(PlacementFailureCode.BudgetExhausted, "Settlement route indexing budget exhausted.");
        var route = SiteGeometry.Cells(request.Area, w).Where(i => board.IsRouteCorridor(i) && board.Passable(i)).ToArray();
        if (requirements.Family == SettlementFamily.RoadsideHamlet && route.Length == 0)
            return Fail(PlacementFailureCode.NoFootprint, "Roadside Hamlet requires a usable protected route inside the Settlement area.");
        request = request with { Preferences = (request.Preferences ?? new()) with { RouteAnchors = anchors.Length > 0 ? anchors : route } };
        var call = scope.Child(request.Key, board);
        SettlementFamily[] families = requirements.Family is { } requested ? [requested] : Enum.GetValues<SettlementFamily>()
            .Where(f => f != SettlementFamily.RoadsideHamlet || route.Length > 0).ToArray();
        var order = families.ToList();
        call.Random("family").Shuffle(order);
        var minimumCount = requirements.AllowReducedBuildingCount ? 2 : requirements.BuildingCount;
        var firstFamily = order[0];
        var last = Fail(PlacementFailureCode.NoFootprint, "No complete Settlement layout could fit.");
        for (var count = requirements.BuildingCount; count >= minimumCount; count--)
        foreach (var familyChoice in order)
        {
            var familyScope = call.Child(familyChoice.ToString(), board).Child("lots-" + count, board);
            var candidates = SettlementLayouts.Candidates(familyChoice, count, request, familyScope, route);
            if (candidates is null)
                return Fail(PlacementFailureCode.BudgetExhausted, "Settlement geometry search budget exhausted.");
            candidates = candidates.OrderBy(candidate => candidate.Cost).Take(BuildCandidatesPerFamily).ToArray();
            foreach (var (candidate, ordinal) in candidates.Select((candidate, ordinal) => (candidate, ordinal)))
            {
                if (!scope.Budget.TryAttempt())
                    return Fail(PlacementFailureCode.BudgetExhausted, "Settlement attempt budget exhausted.");

                List<string> fallbacks = [];
                if (count < requirements.BuildingCount)
                    fallbacks.Add($"Reduced from {requirements.BuildingCount} to {count} buildings because no complete larger layout fit.");
                if (familyChoice != firstFamily)
                    fallbacks.Add($"{firstFamily} did not fit; used {familyChoice}.");
                if (familyChoice == SettlementFamily.RoadsideHamlet && candidate.Lots.Select(lot => lot.Facing).Distinct().Count() == 1)
                    fallbacks.Add("Used one-sided frontage because this layout could not fit lots on another side of the road.");
                var attemptSeed = familyScope.Random("construction", ordinal);
                var result = Build(request, requirements, candidate, scope, id,
                    ((ulong)attemptSeed.NextUInt() << 32) | attemptSeed.NextUInt(), fallbacks);
                if (result is Planned<SitePlan>) return result;
                last = (Rejected<SitePlan>)result;
                if (last.Failure.Code is PlacementFailureCode.BudgetExhausted or PlacementFailureCode.ValidationFailed)
                    return Fail(last.Failure.Code, last.Failure.Reason);
            }
        }

        return Fail(last.Failure.Code, last.Failure.Reason);
    }

    private static PlacementResult<SitePlan> Build(SettlementRequest request, SettlementRequirements requirements,
        SettlementLayout layout, GenerationScope scope, string id, ulong attemptSeed, IReadOnlyList<string> fallbacks)
    {
        Rejected<SitePlan> Fail(PlacementFailureCode code, string reason) => new(new(id, "Settlement", code, reason, 0));
        var area = layout.Bounds;
        var draft = scope.Draft.Fork(SiteGeometry.Pad(area, request.Area, 1), id);
        var localScope = new GenerationScope(attemptSeed, id, draft);
        var w = draft.Width;
        var before = draft.BuildPatch();
        var preparation = SiteGeometry.Prepare(draft, layout.Space.Cells, "gravel", request.Preparation);
        if (preparation is not null) return Fail(preparation.Code, preparation.Reason);

        List<ConstructionOperation> operations =
        [
            new(id, scope.Path, "Settlement", id, area, [], []),
            SiteGeometry.Operation(draft, before, id + "/public-space", id, "Prepared shared public space", id, area)
        ];
        List<BuildingInfo> buildings = [];
        HashSet<int> interiors = [];
        HashSet<int> aprons = [];
        foreach (var lot in layout.Lots)
        {
            var width = lot.Bounds.Width - 2;
            var height = lot.Bounds.Height - 2;
            var roomRandom = localScope.Child(lot.Key, draft).Random("room-count");
            var preferredRooms = lot.MinPreferredRooms + roomRandom.Next(lot.MaxPreferredRooms - lot.MinPreferredRooms + 1);
            var rooms = Math.Clamp(preferredRooms, requirements.MinimumRooms, requirements.MaximumRooms);
            var result = BuildingGenerator.Plan(new(lot.Key, lot.Bounds,
                new(MinWidth: width, MaxWidth: width, MinHeight: height, MaxHeight: height,
                    RoomCount: rooms, FloorTypeId: requirements.FloorTypeId, Furniture: requirements.Furniture,
                    Entrance: new(lot.Facing, lot.Entrance)), Preparation: request.Preparation), localScope);
            if (result is Rejected<BuildingPlan> rejected)
                return Fail(rejected.Failure.Code, $"{lot.Purpose}: {rejected.Failure.Reason}");

            var plan = ((Planned<BuildingPlan>)result).Plan;
            if (!draft.Accept(plan.Patch, out var error)) return Fail(PlacementFailureCode.ValidationFailed, error!);
            var building = plan.Building with { Purpose = lot.Purpose };
            buildings.Add(building);
            operations.AddRange(plan.Operations);
            var cells = building.Footprint.Indices(w).ToHashSet();
            interiors.UnionWith(cells);
            aprons.UnionWith(SiteGeometry.Cells(lot.Bounds, w).Where(i => !cells.Contains(i)));
        }

        before = draft.BuildPatch();
        var paths = layout.Lots.SelectMany(lot => lot.Approach).Concat(layout.EntryPath).ToHashSet();
        var exterior = layout.Space.Cells.Concat(aprons).Concat(paths).ToHashSet();
        if (exterior.Overlaps(interiors) || exterior.Overlaps(layout.Road))
            return Fail(PlacementFailureCode.ValidationFailed, "Settlement paths overlap a building interior or protected road.");
        paths.UnionWith(layout.Space.Cells);
        preparation = SiteGeometry.Prepare(draft, paths, "gravel", request.Preparation);
        if (preparation is not null) return Fail(preparation.Code, preparation.Reason);
        foreach (var cell in aprons.Where(i => !paths.Contains(i))) draft.ReplaceTerrain(cell, "grass");
        operations.Add(SiteGeometry.Operation(draft, before, id + "/approaches", id, "Prepared aprons and approaches", id, area));

        var entrances = Entrances(id, layout, draft);
        var footprint = exterior.Concat(interiors).ToHashSet();
        var usable = footprint.Where(draft.Passable).ToHashSet();
        var access = ConnectedAccess(draft, usable, layout.Road, entrances, buildings);
        if (access is null) return Fail(PlacementFailureCode.BudgetExhausted, "Settlement access validation budget exhausted.");
        if (access.Count == 0) return Fail(PlacementFailureCode.DisconnectedSpace,
            "Settlement rooms, public space and frontage must share usable access.");

        var summary = SiteGeometry.Summary(draft.BuildPatch());
        if (summary.ChangedTerrain > request.Preparation.MaxChangedCells || summary.Cost > request.Preparation.MaxCost)
            return Fail(PlacementFailureCode.PreparationLimit, "Complete Settlement exceeds preparation allowance.");

        draft.Reserve(exterior.Where(i => !draft.IsReserved(i)));
        var needsConnection = !exterior.Any(i => draft.Neighbors(i).Any(j => !footprint.Contains(j) && SiteGeometry.Crossable(draft, i, j)));
        List<SiteTag> tags = [SiteTag.Residential, SiteTag.Constructed];
        if (summary.WaterFilled > 0 && needsConnection) tags.Add(SiteTag.ArtificialIsland);
        var publicSpace = GeneratedArea.From(id + "/" + layout.Space.Key, layout.Space.Cells, w);
        var info = new SiteInfo(id, GeneratedSiteKind.Settlement, GeneratedArea.From(id + "/footprint", footprint, w),
            GeneratedArea.From(id + "/exterior", exterior, w), buildings, entrances,
            new(usable.Count, usable.Count, true, needsConnection, SiteGeometry.TwoByTwo(draft, usable)), tags,
            requirements.ScenarioRole is { } role ? [new(role, GeneratedObjectKind.Site, id)] : [], [], null, summary)
        {
            SettlementLayout = new(layout.Family,
                layout.Lots.Zip(buildings, (lot, building) => new SettlementLotInfo(id + "/lot-" + lot.Key, lot.Bounds, lot.Purpose, building.Id)).ToArray(),
                [new(publicSpace.Id, layout.Space.Role, publicSpace)],
                GeneratedArea.From(id + "/required-access", access, w), fallbacks),
            PopulationIntent = PopulationIntent(id, layout.Family, publicSpace, buildings, access, w)
        };
        operations.Add(new(id + "/commit", id, "Site ready for atomic commit", id, area, [], []));
        info = SiteRouteAccess.Publish(info, draft, request.Preferences);
        return new Planned<SitePlan>(new(draft.BuildPatch(), info, operations));
    }

    private static SitePortal[] Entrances(string id, SettlementLayout layout, BoardDraft draft)
    {
        IReadOnlyList<(int Inside, int Outside)> contacts = layout.Contacts.Count > 0
            ? layout.Contacts : [(layout.EntryPath[^2], layout.EntryPath[^1])];
        return contacts.Select((contact, ordinal) =>
        {
            var edge = EdgeKey.Between(contact.Inside, contact.Outside, draft.Width);
            return new SitePortal(id + "/approach-" + ordinal, id, contact.Inside, contact.Outside, edge, draft.EdgeAt(edge));
        }).ToArray();
    }

    // Verify all usable geometry, then retain only the paths needed to connect portals.
    // The remaining square, yard and rooms are available for Population.
    private static HashSet<int>? ConnectedAccess(BoardDraft draft, HashSet<int> usable, IReadOnlyList<int> road,
        IReadOnlyList<SitePortal> entrances, IReadOnlyList<BuildingInfo> buildings)
    {
        var allowed = usable.Concat(road).ToHashSet();
        var start = entrances[0].OutsideIndex;
        var parents = new Dictionary<int, int> { [start] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        {
            if (!draft.Budget.Spend(1)) return null;
            foreach (var neighbor in draft.Neighbors(cell))
                if (allowed.Contains(neighbor) && SiteGeometry.Crossable(draft, cell, neighbor) &&
                    parents.TryAdd(neighbor, cell)) queue.Enqueue(neighbor);
        }
        if (allowed.Count != parents.Count || entrances.Any(p => !parents.ContainsKey(p.InsideIndex) ||
            !parents.ContainsKey(p.OutsideIndex) || !SiteGeometry.Crossable(draft, p.InsideIndex, p.OutsideIndex))) return [];

        HashSet<int> access = [];
        var terminals = entrances.Concat(buildings.SelectMany(b => b.Portals))
            .SelectMany(p => new[] { p.InsideIndex, p.OutsideIndex }).Concat(road);
        foreach (var terminal in terminals)
            for (var cell = terminal; cell >= 0 && access.Add(cell); cell = parents[cell]) { }
        return access;
    }

    private static SitePopulationIntent PopulationIntent(string id, SettlementFamily family, GeneratedArea publicSpace,
        IReadOnlyList<BuildingInfo> buildings, HashSet<int> access, int width)
    {
        List<SitePopulationArea> areas = [];
        void Add(string areaId, string role, string cluster, IEnumerable<int> candidates)
        {
            var available = candidates.Where(i => !access.Contains(i)).Distinct().Order().ToArray();
            if (available.Length > 0)
                areas.Add(new(areaId, role, GeneratedArea.From(areaId, available, width), available[available.Length / 2], cluster));
        }

        var central = publicSpace.Indices(width).Where(i => !access.Contains(i)).ToArray();
        var sentry = central.Where(i => access.Contains(i - width) || access.Contains(i + width) ||
            i % width > 0 && access.Contains(i - 1) || i % width < width - 1 && access.Contains(i + 1))
            .Take(Math.Max(1, central.Length / 4)).ToArray();
        Add(id + "/sentry", "sentry", id, sentry);
        Add(id + "/central", "central", id, central.Except(sentry));
        foreach (var building in buildings)
        foreach (var (room, ordinal) in building.Rooms.Select((room, ordinal) => (room, ordinal)))
        {
            var role = ordinal == 0 && building.Purpose is "barn" or "storehouse" or "stable" or "shop" or "workshop"
                ? "supplies" : "resting";
            Add(room.Id + "/population", role, building.Id, room.Usable.Indices(width));
        }
        return new(family.ToString(), areas, [], 2, Math.Min(8, buildings.Count + 2), CampAlertTopology.Connected);
    }
}
