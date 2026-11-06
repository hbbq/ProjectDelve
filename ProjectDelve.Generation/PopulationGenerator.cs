using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Bounded authoring pass. Encounter identity never enters runtime rules.</summary>
public static class PopulationGenerator
{
    public const int MaxUnits = 96, MaxGroups = 32, MaxAttempts = 128;
    public const long MaxWork = 2000000;
    public static PopulationSnapshot Generate(WorldGenerationContext context, PopulationSettings settings, ContentBundle? content = null)
    {
        settings.Validate(); content ??= ContentBundle.Canonical();
        if (context.Population is not null) throw new InvalidOperationException("Population has already been authored.");
        var w = context.Window.Width; var n = context.Samples.Length;
        var target = settings.Density == PopulationDensity.Off ? 0 : Math.Min(MaxGroups, n / (settings.Density switch { PopulationDensity.Low => 500, PopulationDensity.High => 160, _ => 280 }));
        List<PopulationEncounter> accepted = []; List<PopulationFailure> failures = [];
        var budget = new PlanningBudget(MaxAttempts, MaxWork); int aggregateAttempts = 0; long aggregateWork = 0;
        if (target == 0) return Finish();
        var board = context.ConstructionBoard ?? new PlanningBoard(context.Materialize(), content);
        var definition = context.Materialize();
        var state = GameEngine.CreateGame(definition, board.Content);
        var protectedCells = context.PopulationProtectedAreas.SelectMany(a => a.Indices(w)).ToHashSet();
        protectedCells.UnionWith(context.CurrentAnalysis?.Chokepoints ?? []);
        var interactionCells = (definition.CellMarkers?.Select(m => m.Position) ?? [])
            .Concat(definition.Machines?.Select(m => m.Position) ?? []).Concat(definition.Resources?.Select(m => m.Position) ?? [])
            .Concat(definition.GroundObjects?.Select(m => m.Position) ?? []).Concat(definition.LootMarkers?.Select(m => m.Position) ?? []);
        foreach (var cell in interactionCells) Protect(cell.Y * w + cell.X);
        foreach (var objective in definition.Objectives ?? []) if (objective.Area is { } a)
            protectedCells.UnionWith(Enumerable.Range(a.Y, a.H).SelectMany(y => Enumerable.Range(y * w + a.X, a.W)));
        foreach (var i in context.Entrances.SelectMany(e => new[] { e.InsideIndex, e.OutsideIndex })
            .Concat(context.Sites.SelectMany(s => s.Entrances.Concat(s.Buildings.SelectMany(b => b.Portals))).SelectMany(p => new[] { p.InsideIndex, p.OutsideIndex }))
            .Concat(context.Routes.SelectMany(r => r.Crossings.SelectMany(x => x.Footprint.Indices(w)).Concat(r.TunnelEntrances.Select(x => x.AnchorIndex))))) Protect(i);
        HashSet<int> occupied = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).Select(c => c.Y * w + c.X).ToHashSet();
        var names = state.Units.Select(u => u.Id).Concat(definition.Units.Select(u => u.GroupId)).Concat(definition.Agency.Select(a => a.GroupId)).Concat((definition.Orders ?? []).Select(o => o.GroupId))
            .Concat((definition.Machines ?? []).Select(m => m.Id)).Concat((definition.Routes ?? []).Select(r => r.Id)).Concat((definition.Waypoints ?? []).Select(r => r.Id)).Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
        HashSet<string> claims = new(StringComparer.Ordinal); HashSet<int> populationCells = [];
        var path = WorldGenerationContext.WindowPath("population", context.Window);
        var locations = GoblinCampPopulationGenerator.Locations(context).Concat(ZombieHousePopulationGenerator.Locations(context))
            .Concat(RoadPatrolPopulationGenerator.Locations(context)).GroupBy(l => l.Kind).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Take(256).ToList(), StringComparer.Ordinal);
        var typeRandom = Pcg32.Stream(context.World.Seed, path + "/type");
        // Independent equal shares prevent an exhausted type starving later types.
        var budgets = locations.Keys.ToDictionary(kind => kind, kind => new PlanningBudget(MaxAttempts / locations.Count, MaxWork / locations.Count));
        for (var slot = 0; slot < target && budgets.Values.Sum(b => b.Attempts) < MaxAttempts; slot++)
        {
            var requestId = path + "/request-" + slot;
            List<(PopulationEncounter Encounter, PopulationLocation Location, int[] Cells)> viable = [];
            foreach (var (kind, original) in locations.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                budget = budgets[kind];
                var pool = original.Where(l => !claims.Contains(l.SourceId)).ToList();
                Pcg32.Stream(context.World.Seed, requestId + "/" + kind + "/locations").Shuffle(pool);
                foreach (var location in pool)
                {
                    if (!budget.TryAttempt() || !budget.Spend(location.Cells.Length)) break;
                    if (location.Path is { } patrolPath && (patrolPath.Any(i => i < 0 || i >= n || !board.Passable(i) || protectedCells.Contains(i) ||
                        occupied.Contains(i) || populationCells.Any(j => Distance(i, j) < 6)) ||
                        patrolPath.Zip(patrolPath.Skip(1)).Any(pair => !board.Neighbors(pair.First).Contains(pair.Second) ||
                            !board.EdgeAt(EdgeKey.Between(pair.First, pair.Second, w)).Passable())))
                    { Fail(kind, location.SourceId, "Patrol segment intersects protected, occupied or claimed population space."); continue; }
                    var random = Pcg32.Stream(context.World.Seed, requestId + "/" + kind + "/" + location.SourceId + "/layout");
                    var cells = location.Cells.Where(i => i >= 0 && i < n && board.Passable(i) && !protectedCells.Contains(i) && !occupied.Contains(i) &&
                        populationCells.All(j => Distance(i, j) >= 6)).Distinct().Order().ToList();
                    random.Shuffle(cells);
                    if (cells.Count < 2) { Fail(kind, location.SourceId, "Insufficient usable cells after protection, occupancy and spacing."); continue; }
                    var id = requestId + "/" + kind;
                    for (var suffix = 0; names.Any(name => name == id || name!.StartsWith(id + "/", StringComparison.Ordinal)); suffix++) id = requestId + "/" + kind + "-" + suffix;
                    var count = Math.Min(cells.Count, 2 + random.Next(Math.Min(4, 1 + settings.Difficulty / 2)));
                    count = Math.Min(count, MaxUnits - accepted.Sum(e => e.Units.Count));
                    if (count < 2) { Fail(kind, location.SourceId, "Unit allowance exhausted."); break; }
                    var chosen = cells.Take(count).ToArray();
                    var encounter = kind switch
                    {
                        "GoblinCamp" => GoblinCampPopulationGenerator.Build(id, location, chosen, w, random),
                        "ZombieHouse" => ZombieHousePopulationGenerator.Build(id, location, chosen, w, random),
                        _ => RoadPatrolPopulationGenerator.Build(id, location, chosen, w, random)
                    };
                    if (accepted.Sum(e => e.GroupIds.Count) + encounter.GroupIds.Count > MaxGroups || !Connected(occupied.Concat(chosen).ToHashSet()))
                    { Fail(kind, location.SourceId, "Group allowance or traversable connectivity conflict."); continue; }
                    if (!budget.Spend(n)) break;
                    try { _ = GameEngine.CreateGame(Combine(definition, accepted.Append(encounter)), board.Content); }
                    catch (ArgumentException error) { Fail(kind, location.SourceId, "Content validation: " + error.Message); continue; }
                    viable.Add((encounter, location, chosen)); break;
                }
            }
            if (viable.Count == 0) { Fail(null, null, "Eligible locations exhausted or planning budget exhausted."); break; }
            // Each type contributes at most one completely validated proposal, regardless of candidate abundance.
            var selected = viable[typeRandom.Next(viable.Count)];
            accepted.Add(selected.Encounter); occupied.UnionWith(selected.Cells); populationCells.UnionWith(selected.Location.Path ?? selected.Cells); claims.Add(selected.Location.SourceId);
            foreach (var name in selected.Encounter.Units.Select(u => u.UnitId).Concat(selected.Encounter.GroupIds)
                .Concat(selected.Encounter.Routes.Select(r => r.Id)).Concat(selected.Encounter.Waypoints.Select(p => p.Id))) names.Add(name);
            void Fail(string? kind, string? source, string reason) => failures.Add(new(requestId, kind, source, reason));
        }
        aggregateAttempts = budgets.Values.Sum(b => b.Attempts); aggregateWork = budgets.Values.Sum(b => b.Work);
        return Finish();

        void Protect(int i) { if (i < 0 || i >= n) return; protectedCells.Add(i); protectedCells.UnionWith(board.Neighbors(i)); }
        int Distance(int a, int b) => Math.Abs(a % w - b % w) + Math.Abs(a / w - b / w);
        bool Linked(int a, int b) => board.EdgeAt(EdgeKey.Between(a, b, w)) is EdgeKind.None or EdgeKind.OpenDoor or EdgeKind.ClosedDoor;
        bool Connected(HashSet<int> blocked)
        {
            var seen = new bool[n];
            for (var start = 0; start < n; start++)
            {
                if (seen[start] || !board.Passable(start)) continue;
                List<int> component = []; Queue<int> queue = new(); queue.Enqueue(start); seen[start] = true;
                while (queue.TryDequeue(out var i))
                {
                    if (!budget.Spend(1)) return false;
                    component.Add(i);
                    foreach (var j in board.Neighbors(i)) if (!seen[j] && board.Passable(j) && Linked(i, j)) { seen[j] = true; queue.Enqueue(j); }
                }
                var free = component.Where(i => !blocked.Contains(i)).ToHashSet();
                if (free.Count == 0) return false;
                var visited = new HashSet<int> { free.Min() }; queue.Enqueue(free.Min());
                while (queue.TryDequeue(out var i))
                {
                    if (!budget.Spend(1)) return false;
                    foreach (var j in board.Neighbors(i)) if (free.Contains(j) && Linked(i, j) && visited.Add(j)) queue.Enqueue(j);
                }
                if (visited.Count != free.Count) return false;
            }
            return true;
        }
        PopulationSnapshot Finish() => new(accepted.ToArray(), new(target, accepted.Count, accepted.Sum(e => e.Units.Count),
            accepted.Sum(e => e.GroupIds.Count), aggregateAttempts, aggregateWork, target - accepted.Count), failures.Take(MaxAttempts + 1).ToArray());
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
