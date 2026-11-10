using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Bounded setup authoring. Site and encounter metadata never enter runtime rules.</summary>
public static partial class PopulationGenerator
{
    public const int MaxUnits = 256, MaxGroups = 192, MaxEncounters = 160, MaxAttempts = 1024;
    public const long MaxWork = 2_000_000;

    public static PopulationSnapshot Generate(WorldGenerationContext context, PopulationSettings settings, ContentBundle? content = null)
    {
        settings.Validate(); content ??= ContentBundle.Canonical();
        if (context.Population is not null) throw new InvalidOperationException("Population has already been authored.");
        var w = context.Window.Width; var n = context.Samples.Length;
        var target = settings.Density == PopulationDensity.Off ? 0 : Math.Min(MaxEncounters,
            n / (settings.Density switch { PopulationDensity.Low => 220, PopulationDensity.High => 55, _ => 100 }));
        var densityCeiling = settings.Density switch { PopulationDensity.Low => MaxUnits / 2, PopulationDensity.Medium => MaxUnits * 3 / 4, _ => MaxUnits };
        var unitLimit = settings.Density == PopulationDensity.Off ? 0 : Math.Min(densityCeiling,
            n / (settings.Density switch { PopulationDensity.Low => 45, PopulationDensity.High => 14, _ => 25 }));
        var budget = new PlanningBudget(MaxAttempts, MaxWork);
        List<PopulationEncounter> accepted = []; List<PopulationFailure> failures = []; List<LootMarker> loot = [];
        IReadOnlyList<PopulationAlertComponent> components = [];
        var requestedUnits = unitLimit; var quietRegions = 0; var groupCount = 0; var unitCount = 0;
        if (target == 0 || unitLimit == 0) return Snapshot(null);
        var board = context.ConstructionBoard ?? new PlanningBoard(context.Materialize(), content);
        var definition = context.Materialize();
        // One complete validation up front and one at publication. Each accepted
        // candidate uses authoritative per-Unit placement, not a full scenario rebuild.
        var state = GameEngine.CreateGame(definition, board.Content);
        state.FogOfWarSides = [];
        var authoredUnits = definition.Units.Select((u, i) => u with { UnitId = state.Units[i].Id }).ToArray();
        var types = board.Content.Units.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (var type in state.Types) types[type.Id] = type;
        var passable = Enumerable.Range(0, n).Select(board.Passable).ToArray();
        var protectedCells = context.PopulationProtectedAreas.SelectMany(a => a.Indices(w)).ToHashSet();
        foreach (var objective in definition.Objectives ?? []) if (objective.Area is { } a)
            protectedCells.UnionWith(Enumerable.Range(a.Y, a.H).SelectMany(y => Enumerable.Range(y * w + a.X, a.W)));
        foreach (var i in context.Entrances.SelectMany(e => new[] { e.InsideIndex, e.OutsideIndex })
            .Concat(context.Sites.SelectMany(s => s.AccessPorts.SelectMany(p => p.Approach.Indices(w))))
            .Concat(context.Routes.SelectMany(r => r.Crossings.SelectMany(x => x.Footprint.Indices(w))
                .Concat(r.TunnelEntrances.Select(x => x.AnchorIndex))))) protectedCells.Add(i);
        foreach (var machine in definition.Machines ?? []) protectedCells.Add(machine.Position.Y * w + machine.Position.X);
        var occupied = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).Select(c => c.Y * w + c.X).ToHashSet();
        HashSet<int> encounterCells = [.. occupied]; HashSet<int> patrolCells = [];
        var names = definition.Units.Select(u => u.UnitId).Concat(definition.Units.Select(u => u.GroupId))
            .Concat(definition.Agency.Select(a => a.GroupId)).Concat((definition.Orders ?? []).Select(o => o.GroupId))
            .Concat((definition.Machines ?? []).Select(m => m.Id)).Concat((definition.Routes ?? []).Select(r => r.Id))
            .Concat((definition.Waypoints ?? []).Select(r => r.Id)).Concat((definition.LootMarkers ?? []).Select(m => m.Id))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        names.UnionWith(state.Units.Select(u => u.Id));
        var path = WorldGenerationContext.WindowPath("population", context.Window);
        var siteCells = context.Sites.SelectMany(s => s.Footprint.Indices(w)).ToHashSet();
        List<PopulationLocation> candidates = [];
        foreach (var site in context.Sites.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (Pcg32.Stream(context.World.Seed, path + "/occupation/" + site.Id).Next(5) == 0) continue;
            if (site.PopulationIntent is { } intent)
                candidates.Add(new("StructuredCamp", site.Id, site.Exterior.Indices(w).ToArray(), site.Exterior.Bounds) { Intent = intent });
            else if (site.Kind == GeneratedSiteKind.Campsite)
                candidates.Add(new("GoblinCamp", site.Id, site.Exterior.Indices(w).ToArray(), site.Exterior.Bounds));
        }
        candidates.AddRange(ZombieHousePopulationGenerator.Locations(context)
            .Where(l => Pcg32.Stream(context.World.Seed, path + "/occupation/" + l.SourceId).Next(5) != 0)
            .Select(l => l with { Kind = "BuildingDefenders" }));
        var roads = RoadPatrolPopulationGenerator.Locations(context).ToList();
        Pcg32.Stream(context.World.Seed, path + "/road-patrols").Shuffle(roads);
        candidates.AddRange(roads.Take(settings.Density == PopulationDensity.Low ? 1 : 2));
        Pcg32.Stream(context.World.Seed, path + "/site-order").Shuffle(candidates);
        var natural = NaturalCandidates(context, board, passable, protectedCells, siteCells, budget, out quietRegions);
        foreach (var i in natural)
        {
            var random = Pcg32.Stream(context.World.Seed, path + "/natural-" + i);
            var cells = LocalGround(board, passable, i, 3, budget).Where(j => !siteCells.Contains(j)).ToArray();
            var area = GeneratedArea.From(path + "/natural-" + i, cells, w);
            candidates.Add(new(random.Next(5) < 2 ? "LoneMonster" : "WildernessGroup", area.Id, cells, area.Bounds));
        }
        foreach (var location in candidates)
        {
            if (accepted.Count >= target || unitCount >= unitLimit || groupCount >= MaxGroups) break;
            if (!budget.TryAttempt() || !budget.Spend(location.Cells.Length)) break;
            var stem = path + "/request-" + budget.Attempts + "/" + location.Kind; var id = stem;
            for (var suffix = 0; names.Any(name => name == id || name.StartsWith(id + "/", StringComparison.Ordinal)); suffix++) id = stem + "-" + suffix;
            var random = Pcg32.Stream(context.World.Seed, id + "/composition");
            var free = location.Cells.Where(Available).Distinct().Order().ToList(); random.Shuffle(free);
            var encounter = BuildEncounter(id, location, free, w, settings, unitLimit - unitCount, random, board, types,
                state, budget, Available, reason => Fail(location, reason), out var requested);
            if (encounter is null) continue;
            if (groupCount + encounter.GroupIds.Count > MaxGroups) { Fail(location, "Group allowance exhausted."); continue; }
            var beforeUnits = state.Units.Count; var beforeFigures = state.Physical.Figures.Count;
            try
            {
                foreach (var unit in encounter.Units)
                {
                    if (!budget.Spend(1)) throw new ArgumentException("Placement work allowance exhausted.");
                    if (state.Types.All(t => t.Id != unit.UnitTypeId)) state.Types.Add(types[unit.UnitTypeId]);
                    state.PlaceUnit(unit.UnitTypeId, unit.UnitId!, unit.SideId, unit.Anchor, unit.Posture, unit.GroupId, unit.Dormant, unit.WakeDistance);
                }
            }
            catch (ArgumentException error)
            {
                Rollback(); Fail(location, "Engine placement: " + error.Message); continue;
            }
            var allComponents = PopulationTopology.Components(authoredUnits.Concat(accepted.SelectMany(e => e.Units)).Concat(encounter.Units), types.Values);
            var memberIds = encounter.Units.Select(u => u.UnitId).ToHashSet(StringComparer.Ordinal);
            var componentCount = allComponents.Count(c => c.UnitIds.Any(memberIds.Contains));
            encounter = encounter with { ActualAlertComponents = componentCount };
            if (location.Intent is { } intended && (intended.AlertTopology == CampAlertTopology.Connected ? componentCount != 1 : componentCount < 2))
            { Rollback(); Fail(location, "Final occupation does not realize intended Alert connectivity."); continue; }
            accepted.Add(encounter); unitCount += encounter.Units.Count; groupCount += encounter.GroupIds.Count;
            foreach (var unit in encounter.Units) foreach (var cell in FootprintGeometry.OccupiedCells(types[unit.UnitTypeId].Footprint, unit.Anchor))
            { var index = cell.Y * w + cell.X; occupied.Add(index); encounterCells.Add(index); }
            if (location.Path is { } pathCells) patrolCells.UnionWith(pathCells);
            if (encounter.Roles.Any(r => r.Role == "patrol") && location.Intent is { } campIntent) patrolCells.UnionWith(campIntent.PatrolPath);
            foreach (var name in encounter.Units.Select(u => u.UnitId!).Concat(encounter.GroupIds)
                .Concat(encounter.Routes.Select(r => r.Id)).Concat(encounter.Waypoints.Select(p => p.Id))) names.Add(name);
            void Rollback()
            {
                state.Units.RemoveRange(beforeUnits, state.Units.Count - beforeUnits);
                state.Physical.Figures.RemoveRange(beforeFigures, state.Physical.Figures.Count - beforeFigures);
            }
        }
        if (accepted.Count < target) failures.Add(new(path, null, null, "Population target shortfall: locations, spacing, Unit/Group allowance or bounded work exhausted."));
        var lootSettings = definition.Loot ?? LootSettings.Empty;
        if (lootSettings.ResolveComposition(board.Content.LootCards, board.Content.LootPresets).Count > 0)
        {
            var lootCells = context.Sites.SelectMany(s => s.PopulationIntent?.Areas.Where(a => a.Role == "supplies").Select(a => a.AnchorIndex) ?? [])
                .Concat(accepted.SelectMany(e => e.Units.SelectMany(u => board.Neighbors(u.Anchor.Y * w + u.Anchor.X))))
                .Concat(natural.Take(32)).Distinct().ToList();
            Pcg32.Stream(context.World.Seed, path + "/loot").Shuffle(lootCells);
            var wanted = Math.Min(48, Math.Max(1, accepted.Count / 3));
            // Custom composition is explicitly authored, so only presets repeat.
            // Existing markers already reserve their cards and are never removed.
            if (lootSettings.Normalize(board.Content.LootPresets).Composition is not null)
                wanted = Math.Min(wanted, Math.Max(0, lootSettings.ResolveComposition(board.Content.LootCards, board.Content.LootPresets).Sum(r => r.Count) - (definition.LootMarkers?.Count ?? 0)));
            var largest = context.CurrentAnalysis?.Connectivity.ComponentAreas.Select((size, label) => (size, label)).OrderByDescending(p => p.size).FirstOrDefault().label;
            foreach (var i in lootCells)
            {
                if (!budget.Spend(1) || loot.Count >= wanted) break;
                if (!passable[i] || protectedCells.Contains(i) || occupied.Contains(i) ||
                    context.CurrentAnalysis is { } analysis && analysis.WalkableLabels[i] != largest) continue;
                var markerId = path + "/loot-" + i;
                if (names.Add(markerId)) loot.Add(new(markerId, new(i % w, i / w)));
            }
        }
        components = PopulationTopology.Components(authoredUnits.Concat(accepted.SelectMany(e => e.Units)), types.Values);
        var expectedDrops = (definition.Units.Concat(accepted.SelectMany(e => e.Units)).Sum(u => types[u.UnitTypeId].LootDropSuccesses) + 5) / 6;
        var provisioned = PopulationLoot.Provision(lootSettings, (definition.LootMarkers?.Count ?? 0) + loot.Count, expectedDrops, board.Content);
        var final = Combine(definition, accepted) with { LootMarkers = [.. definition.LootMarkers ?? [], .. loot], Loot = provisioned };
        _ = GameEngine.CreateGame(final, board.Content);
        return Snapshot(provisioned);

        bool Available(int i) => i >= 0 && i < n && passable[i] && !protectedCells.Contains(i) && !occupied.Contains(i) && !patrolCells.Contains(i) &&
            encounterCells.All(j => Math.Abs(i % w - j % w) + Math.Abs(i / w - j / w) > DormantRules.AlertDistance);
        void Fail(PopulationLocation location, string reason)
        { if (failures.Count < 128) failures.Add(new(path + "/request-" + budget.Attempts, location.Kind, location.SourceId, reason)); }
        PopulationSnapshot Snapshot(LootSettings? settingsLoot)
        {
            var units = accepted.SelectMany(e => e.Units).ToArray();
            return new(accepted.ToArray(), new(target, accepted.Count, units.Length, groupCount, budget.Attempts, budget.Work, Math.Max(0, target - accepted.Count))
            {
                DormantUnits = units.Count(u => u.Dormant), AwakeUnits = units.Count(u => !u.Dormant),
                InitialTokens = units.Where(u => !u.Dormant).Select(u => new ActivationToken(u.UnitTypeId, u.SideId, u.GroupId)).Distinct().Count(),
                RequestedUnits = requestedUnits, UnitShortfall = Math.Max(0, requestedUnits - units.Length), LootMarkers = loot.Count, QuietRegions = quietRegions
            }, failures.ToArray()) { LootMarkers = loot.ToArray(), Loot = settingsLoot, AlertComponents = components };
        }
    }

    internal static ScenarioDefinition Combine(ScenarioDefinition definition, IEnumerable<PopulationEncounter> source)
    {
        var encounters = source.ToArray(); var units = encounters.SelectMany(e => e.Units).ToArray();
        return definition with { Units = [.. definition.Units, .. units], UnitTypeIds = definition.UnitTypeIds.Concat(units.Select(u => u.UnitTypeId)).Distinct(StringComparer.Ordinal).ToList(),
            Agency = [.. definition.Agency, .. encounters.SelectMany(e => e.Agency)], Orders = [.. definition.Orders ?? [], .. encounters.SelectMany(e => e.Orders)],
            Routes = [.. definition.Routes ?? [], .. encounters.SelectMany(e => e.Routes)], Waypoints = [.. definition.Waypoints ?? [], .. encounters.SelectMany(e => e.Waypoints)] };
    }
    internal static void Apply(WorldGenerationContext context, WorldGenerationStep step, ContentBundle content) =>
        context.Population = Generate(context, ((PopulationStepParameters)step.Parameters).Settings, content);
}
