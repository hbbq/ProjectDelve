using System.Diagnostics;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

internal static class VisionBenchmark
{
    public static void WriteFixtures()
    {
        Directory.CreateDirectory(".playwright-cli/vision-fixtures");
        foreach (var type in new[] { UnitTypeIds.Barbarian, UnitTypeIds.Jackalope, UnitTypeIds.GiantToad })
        {
            var hero = type == UnitTypeIds.Barbarian;
            var definition = new ScenarioDefinition {
                Board = new() { Width = 24, Height = 9, Cells = hero ? [new(new(4, 4), TerrainKind.Water)] : [] },
                UnitTypeIds = [type],
                Units = [new(type, "blue", new(hero ? 4 : 1, 4), UnitId: "actor") {
                    Inventory = hero ? [new("binoculars-instance", "binoculars"), new("swim-tube-instance", "swim-tube")] : null }],
                Agency = [new(type, "blue", hero ? ControllerKind.Human : ControllerKind.Automated)],
                Objectives = [new("blue", ScenarioObjective.NoObjective)], FogOfWarSides = ["blue"],
                Machines = [new("beacon", new(5, 4), MachineTypes.Beacon), new("hidden-beacon", new(22, 4), MachineTypes.Beacon)]
            };
            GameEngine.CreateGame(definition);
            File.WriteAllText($".playwright-cli/vision-fixtures/{type}.txt", ScenarioDefinitionTransport.Encode(definition));
        }
        var display = new ScenarioDefinition {
            Board = new() { Width = 12, Height = 9 }, UnitTypeIds = [UnitTypeIds.Barbarian],
            Units = [new(UnitTypeIds.Barbarian, "blue", new(5, 4), UnitId: "actor")],
            Agency = [new(UnitTypeIds.Barbarian, "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)],
            Machines = [new("nw", new(3, 2), MachineTypes.Beacon), new("ne", new(7, 2), MachineTypes.Beacon),
                new("sw", new(3, 6), MachineTypes.Beacon), new("se", new(7, 6), MachineTypes.Beacon)] };
        GameEngine.CreateGame(display);
        File.WriteAllText(".playwright-cli/vision-fixtures/four-beacons.txt", ScenarioDefinitionTransport.Encode(display));
    }
    public static void Run()
    {
        foreach (var range in new[] { 5, 8, 15 })
        {
            var state = Many(range);
            Measure($"refresh-cached-100-units-r{range}", () => { FogOfWar.Refresh(state); return state.ExploredCells["blue"].Count; });
            // Moving a source forces a full geometry refresh, without serialization in the timed region.
            var left = false;
            Measure($"refresh-changing-100-units-r{range}", () => {
                left = !left; state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(left ? 2 : 3, 2) };
                FogOfWar.Refresh(state); return state.ExploredCells["blue"].Count;
            });
        }
        var board = Many(5).Physical.Board;
        Measure("reveal-area-r15-120x120", () => FogOfWar.VisionArea(board, [new(30, 30)], 15, excludedMachineId: "source").Count);
        var actor = GameEngine.CreateGame(new() {
            Board = new() { Width = 120, Height = 120 }, UnitTypeIds = [UnitTypeIds.Jackalope],
            Units = [new(UnitTypeIds.Jackalope, "blue", new(2, 2), UnitId: "actor")],
            Agency = [new(UnitTypeIds.Jackalope, "blue", ControllerKind.Automated)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], FogOfWarSides = ["blue"],
            Machines = [new("beacon", new(6, 2), MachineTypes.Beacon)] });
        var start = GameEngine.StartRound(actor, new Dice(), false);
        var provider = new TimedProvider();
        Measure("beacon-move-decision-with-engine-120x120", () => {
            var result = GameEngine.Advance(start.State, provider, new Dice(), false); return result.Events.Count;
        });
        Console.WriteLine(JsonSerializer.Serialize(new { name = "beacon-provider-only", provider.Milliseconds, provider.Bytes }));
        var recipe = new WorldGenerationRecipe(new(0, 0, 120, 120), new(20, 5, 5), 12345,
            new(Enabled: false), new(Campsites: ContentDensity.None, Settlements: ContentDensity.None));
        Measure("civilization-beacons-120x120", () => WorldGenerator.Generate(recipe.ToString()).Definition.Machines?.Count ?? 0);
    }
    private static GameState Many(int range)
    {
        var type = new UnitType("scout", 3, 0, 0, 1, 1) { SightDistance = range };
        var state = new GameState { Types = [type], Units = [], Physical = new(new Board(120, 120, []), []), FogOfWarSides = ["blue"] };
        for (var y = 0; y < 10; y++) for (var x = 0; x < 10; x++) {
            var id = $"{x}-{y}";state.Units.Add(type.CreateUnit(id, "blue"));state.Physical.Figures.Add(new(id, new(2 + x * 11, 2 + y * 11)));
        }
        for (var y = 0; y < 120; y++) for (var x = 0; x < 120; x++)
            if ((x * 17 + y * 7) % 19 == 0 && !state.Physical.Figures.Any(f => f.Position == new Cell(x, y)) &&
                new Cell(x, y) != new Cell(3, 2)) state.Physical.Board.Terrain.Add(new(new(x, y), TerrainKind.SolidRock));
        return state;
    }
    private static void Measure(string name, Func<int> action)
    {
        for (var i = 0; i < 3; i++) action();
        List<double> times = []; List<long> allocations = []; var result = 0;
        for (var i = 0; i < 7; i++) {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
            result = action();times.Add(clock.Elapsed.TotalMilliseconds);allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        times.Sort();allocations.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new { name, medianMs = times[3], p95Ms = times[6], allocatedBytes = allocations[3], result }));
    }
    private sealed class TimedProvider : IDecisionProvider
    {
        public double Milliseconds { get; private set; }
        public long Bytes { get; private set; }
        public string? Choose(DecisionRequest request, IGameplayQueries queries) {
            var bytes=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
            var choice=new OrderAwareAutomatedProvider().Choose(request, queries);
            Milliseconds=timer.Elapsed.TotalMilliseconds;Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;return choice;
        }
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
