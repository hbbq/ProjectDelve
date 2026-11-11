using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;

internal static class PopulationInvestigation
{
    private const int Warmups = 1, Samples = 3, MaxDecisions = 1000;

    internal static void Run(string? directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var canonical = ContentBundle.Canonical();
        List<object> records = [];
        foreach (var (width, height) in new[] { (30, 24), (60, 50), (120, 100), (120, 120) })
        foreach (var density in new[] { PopulationDensity.Low, PopulationDensity.Medium, PopulationDensity.High })
        {
            try
            {
                var recipe = new WorldGenerationRecipe(new(-17, -13, width, height), new(10, 4, 5), 12345,
                    new(), new(Campsites: ContentDensity.VeryHigh, Settlements: ContentDensity.Low), new(ThroughRoutes: ContentDensity.Medium, Profile: RouteProfileKind.Road),
                    new(density, Difficulty: 5), LootPreset: "Classic");
                _ = WorldGenerator.Generate(recipe);
                List<GeneratedWorld> runs = []; List<double> total = [];
                for (var i = 0; i < Samples; i++)
                {
                    var clock = Stopwatch.StartNew(); runs.Add(WorldGenerator.Generate(recipe)); total.Add(clock.Elapsed.TotalMilliseconds);
                }
                var world = runs[^1]; var population = world.Context.Population!; var stats = population.Statistics;
                if (runs.Any(w => w.Fingerprint != world.Fingerprint)) throw new InvalidOperationException("Population benchmark is not deterministic.");
                if (stats.Work > PopulationGenerator.MaxWork || stats.Attempts > PopulationGenerator.MaxAttempts ||
                    stats.Units > PopulationGenerator.MaxUnits || stats.Groups > PopulationGenerator.MaxGroups)
                    throw new InvalidOperationException("Population exceeded its hard limits.");

                var setup = Measure(() => GameEngine.CreateGame(world.Definition, canonical));
                var generatedState = GameEngine.CreateGame(world.Definition, canonical);
                var generatedBoundary = Boundaries(generatedState, null);
                var temporary = WorldQuickPlay.Prepare(world.Definition, canonical);
                var quickPlaySetup = Measure(() => GameEngine.CreateGame(temporary, canonical));
                var quickPlayState = GameEngine.CreateGame(temporary, canonical);
                var partyBoundary = Boundaries(quickPlayState, null);
                var wake = WakeSetup(temporary, quickPlayState, population, canonical);
                object representative;
                if (wake is null) representative = new { skipped = "No dormant Unit with a legal scouting Cell and positive Wake Distance." };
                else
                {
                    var wakeState = GameEngine.CreateGame(wake.Definition, canonical);
                    representative = new
                    {
                        wake.TargetUnitId, wake.ComponentIndex, wake.ScoutId, wake.ScoutCell,
                        boundary = Boundaries(wakeState, wake.Token), activation = Activations(wakeState, wake.Token)
                    };
                }
                var scenarioJson = ScenarioDefinitionJson.ToJson(world.Definition);
                var transport = ScenarioDefinitionTransport.Encode(world.Definition);
                var quickPlayJson = ScenarioDefinitionJson.ToJson(temporary);
                var quickPlayTransport = ScenarioDefinitionTransport.Encode(temporary);
                var record = new
                {
                    width, height, density, warmups = Warmups, samples = Samples, world.Code,
                    generationMs = Median(total),
                    populationMs = Median(runs.Select(w => w.Metrics.Stages.Single(s => s.Stage == WorldStepKind.Population).Milliseconds)),
                    populationAllocatedBytes = Median(runs.Select(w => w.Metrics.Stages.Single(s => s.Stage == WorldStepKind.Population).AllocatedBytes)),
                    statistics = stats, kinds = population.Encounters.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count()),
                    alertComponentSizes = population.AlertComponents.Select(c => c.UnitIds.Count).OrderDescending().ToArray(),
                    setupMs = setup, generatedRoundStartBoundary = generatedBoundary,
                    quickPlaySetupMs = quickPlaySetup, quickPlayRoundStartBoundary = partyBoundary,
                    representativeWake = representative,
                    previewBytes = JsonSerializer.SerializeToUtf8Bytes(world.Preview, json).Length,
                    previewRawEstimateBytes = world.Preview.EstimatedRetainedBytes,
                    scenarioBytes = Encoding.UTF8.GetByteCount(scenarioJson), transportBytes = Encoding.UTF8.GetByteCount(transport),
                    quickPlayScenarioBytes = Encoding.UTF8.GetByteCount(quickPlayJson), quickPlayTransportBytes = Encoding.UTF8.GetByteCount(quickPlayTransport),
                    jsonEncodeMs = Measure(() => ScenarioDefinitionJson.ToJson(world.Definition)),
                    transportEncodeMs = Measure(() => ScenarioDefinitionTransport.Encode(world.Definition)),
                    world.Fingerprint, errors = Array.Empty<string>()
                };
                records.Add(record); Console.WriteLine(JsonSerializer.Serialize(record, json));
                if (directory is not null) WriteFixture(directory, world, canonical, json, $"{width}x{height}-{density}");
            }
            catch (Exception error)
            {
                var record = new { width, height, density, errors = new[] { error.GetType().Name + ": " + error.Message } };
                records.Add(record); Console.WriteLine(JsonSerializer.Serialize(record, json));
            }
        }
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "population-performance.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions(json) { WriteIndented = true }));
    }

    // This complete public proximity-check boundary includes normal validation, copying,
    // FoW, bag construction and the first decision, rather than timing a private Alert proxy.
    private static object Boundaries(GameState initial, ActivationToken? preferred)
    {
        BoundarySample Run()
        {
            var clock = Stopwatch.StartNew();
            var result = GameEngine.StartRound(initial, new BenchmarkDice(preferred), autoChooseSingleRelevantChoice: false);
            var elapsed = clock.Elapsed.TotalMilliseconds;
            return new(elapsed, result.Events.Count(e => e.Kind == "UnitWoken"), result.State.Units.Count(u => u.Dormant),
                result.State.Units.Count(u => !u.Dormant), result.State.Bag.Count + (result.State.ActiveToken is null ? 0 : 1), result.Events.Count);
        }
        _ = Run(); var runs = Enumerable.Range(0, Samples).Select(_ => Run()).ToArray(); var last = runs[^1];
        return new { boundary = "Initial Round Start", medianMs = Median(runs.Select(r => r.Milliseconds)),
            last.Woken, last.Dormant, last.Awake, tokensAtFirstDecision = last.InitialTokens, last.Events, heroFogOfWar = initial.FogOfWarSides.Count > 0 };
    }

    // One complete selected Monster Group, bounded by decisions. This deliberately
    // does not simulate an all-awake world or retain event snapshots between steps.
    private static object Activations(GameState initial, ActivationToken token)
    {
        ActivationSample Run()
        {
            var random = new BenchmarkDice(token); var provider = new OrderAwareAutomatedProvider();
            var boundary = GameEngine.StartRound(initial, random, autoChooseSingleRelevantChoice: false);
            var completed = boundary.Events.Count(e => e.Kind == "ActivationCompleted" && e.Token == token);
            var events = boundary.Events.Count; var decisions = 0;
            var active = boundary; var clock = Stopwatch.StartNew();
            while (decisions < MaxDecisions && !active.State.RoundComplete && !active.State.IsCompleted &&
                !(completed > 0 && active.State.ActiveToken != token))
            {
                if (active.State.Pending is null) break;
                active = GameEngine.Advance(active.State, provider, random, autoChooseSingleRelevantChoice: false);
                decisions++; events += active.Events.Count;
                completed += active.Events.Count(e => e.Kind == "ActivationCompleted" && e.Token == token);
            }
            return new(clock.Elapsed.TotalMilliseconds, decisions, events, completed,
                completed > 0 && active.State.ActiveToken != token, active.State.Bag.Count,
                active.State.Units.Count(u => u.Dormant), active.State.Units.Count(u => !u.Dormant));
        }
        // Gameplay is intentionally a single observed complete Group sample: it
        // can be substantially slower than generation on large boards. Generation
        // and public proximity boundaries retain warm median measurements.
        var runs = new[] { Run() }; var last = runs[^1];
        return new { token, samples = 1, advanceMs = last.Milliseconds,
            last.Decisions, last.Events, last.CompletedUnits, last.GroupCompleted, last.RemainingBag, last.Dormant, last.Awake,
            decisionLimit = MaxDecisions, reachedDecisionLimit = last.Decisions == MaxDecisions,
            scope = "One representative Monster Group after proximity wake-up; Round Start timed separately" };
    }

    private static WakeFixture? WakeSetup(ScenarioDefinition definition, GameState state, PopulationSnapshot population, ContentBundle content)
    {
        var scout = state.Units.First(u => u.SideId == HeroesVsMonsters.Heroes);
        var occupied = state.Physical.Figures.Where(f => f.Id != scout.Id).SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id))
            .Concat(state.Physical.Board.Machines.Select(m => m.Position)).ToHashSet();
        var board = state.Physical.Board; var tiles = board.Terrain.ToDictionary(t => t.Position);
        foreach (var component in population.AlertComponents.OrderByDescending(c => c.UnitIds.Count).ThenBy(c => c.Index))
        foreach (var id in component.UnitIds.OrderBy(id => state.Physical.Figures.Single(f => f.Id == id).Posture == Posture.Upright ? 0 : 1).ThenBy(id => id, StringComparer.Ordinal))
        {
            var target = state.Units.Single(u => u.Id == id);
            if (!target.Dormant || target.WakeDistance <= 0) continue;
            var cells = FootprintGeometry.OccupiedCells(state, id).ToArray();
            var positions = Enumerable.Range(0, board.Height).SelectMany(y => Enumerable.Range(0, board.Width).Select(x => new Cell(x, y)))
                .Select(cell => (Cell: cell, Distance: cells.Min(c => Math.Abs(c.X - cell.X) + Math.Abs(c.Y - cell.Y))))
                .Where(p => p.Distance <= target.WakeDistance && !occupied.Contains(p.Cell) && board.TerrainDefinition(tiles.GetValueOrDefault(p.Cell)).Passable)
                .OrderBy(p => p.Distance).ThenBy(p => p.Cell.Y).ThenBy(p => p.Cell.X);
            foreach (var position in positions.Take(8))
            {
                var candidate = definition with { Units = definition.Units.Select(u => u.UnitId == scout.Id ? u with { Anchor = position.Cell } : u).ToList() };
                try { _ = GameEngine.CreateGame(candidate, content); }
                catch (ArgumentException) { continue; }
                return new(candidate, id, component.Index, scout.Id, position.Cell, target.Token);
            }
        }
        return null;
    }

    private static void WriteFixture(string directory, GeneratedWorld world, ContentBundle canonical, JsonSerializerOptions json, string stem)
    {
        var catalog = ScenarioContentReferences.ScenarioCatalog(canonical, world.Definition).Definitions;
        var response = new
        {
            definition = JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(world.Definition)),
            terrainDefinitions = catalog.Terrain,
            unitDefinitions = catalog.Units.Where(type => world.Definition.UnitTypeIds.Contains(type.Id))
                .Select(type => new { type.Id, displayName = type.DisplayName ?? type.Id, cellSpan = type.Footprint == Footprint.TwoByTwo ? 2 : 1 }),
            world.Code, title = "Generated populated world", transport = ScenarioDefinitionTransport.Encode(world.Definition),
            world.VisualSeed, world.Window, world.Fingerprint, world.Diagnostics, world.Preview, world.Metrics
        };
        File.WriteAllText(Path.Combine(directory, stem + ".response.json"), JsonSerializer.Serialize(response, json));
    }

    private static double Measure<T>(Func<T> operation)
    {
        for (var i = 0; i < Warmups; i++) _ = operation();
        List<double> samples = [];
        for (var i = 0; i < Samples; i++) { var clock = Stopwatch.StartNew(); _ = operation(); samples.Add(clock.Elapsed.TotalMilliseconds); }
        return Median(samples);
    }
    private static T Median<T>(IEnumerable<T> source) where T : IComparable<T> { var values = source.Order().ToArray(); return values[values.Length / 2]; }
    private sealed record BoundarySample(double Milliseconds, int Woken, int Dormant, int Awake, int InitialTokens, int Events);
    private sealed record ActivationSample(double Milliseconds, int Decisions, int Events, int CompletedUnits, bool GroupCompleted, int RemainingBag, int Dormant, int Awake);
    private sealed record WakeFixture(ScenarioDefinition Definition, string TargetUnitId, int ComponentIndex, string ScoutId, Cell ScoutCell, ActivationToken Token);
    private sealed class BenchmarkDice(ActivationToken? preferred) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => preferred is not null && bag.Contains(preferred) ? preferred :
            bag.FirstOrDefault(t => t.SideId == HeroesVsMonsters.Monsters) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
        public int RollD8() => 1;
        public int WorldShuffleIndex(int exclusiveMax) => 0;
    }
}
