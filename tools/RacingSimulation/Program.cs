using ProjectDelve.Engine;
using ProjectDelve.Web;
using System.Text.Json;

var rounds = args.Length > 0 ? int.Parse(args[0]) : 40;
var seed = args.Length > 1 ? int.Parse(args[1]) : 20261020;
foreach (var id in args.Length > 2 ? args[2..] : new[] { "simple-racing", "racing-and-industry" })
{
    var random = new SeededRandom(seed);
    var behavior = new OrderAwareAutomatedProvider();
    var state = PlaytestScenarios.Create(id);
    var counts = new SortedDictionary<string, int>();
    var decisions = 0;
    void Count(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
    void Consume(EngineResult result)
    {
        foreach (var e in result.Events)
        {
            if (e.Kind == "WaypointReached") Count("waypointAdvances");
            if (e.Kind == "RacingSlotRolled") { Count("waypointRolls"); Count(e.Succeeded == true ? "waypointAwards" : "discardedAwards"); }
            if (e.Kind == "ResourceStored" && e.ActionId == "waypoint-slot-machine") Count($"award:{e.ResourceType!.Id}");
            if (e.Kind == "ActionUsed" && e.ActionId is "bonus:boost" or "boost") Count("boosts");
            if (e.Kind == "AttackStarted" && e.ActionId == "shoot-shell") Count("shellAttacks");
            if (e.Kind == "CellMarkerPlaced") Count("bananaPeels");
            if (e.Kind == "CellMarkerConsumed") Count("slips");
            if (e.Kind == "ResourceCreated" && e.ResourceType == ResourceTypes.Fruit) Count("fruitProduced");
            if (e.Kind == "MachineOperated") { Count("machineOperations"); Count($"output:{e.ResourceType!.Id}"); }
            if (e.Kind == "ResourceStored" && e.ActionId == "give-resource") Count(e.MachineId is null ? $"delivery:{e.ResourceType!.Id}" : "machineFruitDeliveries");
            if (e.Kind == "UnitDefeated") Count("defeats");
        }
        state = result.State;
    }
    while (state.Round < rounds && !state.IsCompleted)
    {
        Consume(GameEngine.StartRound(state, random));
        while (state.Pending is not null)
        {
            if (++decisions > 100000) throw new InvalidOperationException("Simulation decision limit exceeded.");
            Consume(GameEngine.Advance(state, behavior, random));
        }
        Console.Error.WriteLine($"{id}: round {state.Round}, {decisions} decisions");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { scenario = id, seed, rounds = state.Round, state.RoundComplete, decisions,
        counts, livingUnits = state.Units.Count(u => u.CurrentHp > 0), completion = state.Completion }, new JsonSerializerOptions { WriteIndented = true }));
}

sealed class SeededRandom(int seed) : IRandomProvider
{
    private readonly Random random = new(seed);
    public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[random.Next(bag.Count)];
    public AttackFace RollAttackDie() => random.Next(6) < 3 ? AttackFace.Hit : AttackFace.Miss;
    public DefenceFace RollDefenceDie() => random.Next(6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
    public int RollD6() => random.Next(1, 7);
    public int RollD8() => random.Next(1, 9);
    public int WorldShuffleIndex(int exclusiveMax) => random.Next(exclusiveMax);
}
