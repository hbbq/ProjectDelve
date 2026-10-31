using ProjectDelve.Engine;
using System.Text.Json;

// An authoring experiment, not a balance test or a claim about human play.
// Usage: library.json campaign-id [seed-count=8] [round-limit=25] [policy=all] [scenario-id]
if (args.Length < 2) throw new ArgumentException("Provide library.json and campaign ID.");
using var library = JsonDocument.Parse(File.ReadAllText(args[0]));
var campaign = library.RootElement.GetProperty("campaigns").EnumerateArray()
    .Single(c => c.GetProperty("id").GetString() == args[1]);
var seeds = args.Length > 2 ? int.Parse(args[2]) : 8;
var roundLimit = args.Length > 3 ? int.Parse(args[3]) : 25;
var policies = args.Length > 4 && args[4] != "all" ? new[] { args[4] } : new[] { "aggressive", "objective", "rush" };
if (policies.Any(p => p is not ("aggressive" or "objective" or "rush"))) throw new ArgumentException("Unknown policy.");
var runs = new List<object>();
foreach (var reference in campaign.GetProperty("scenarioIds").EnumerateArray())
{
    var id = reference.GetString()!;
    if (args.Length > 5 && id != args[5]) continue;
    var record = library.RootElement.GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);
    var definition = ScenarioDefinitionJson.FromJson(record.GetProperty("definition").GetRawText());
    foreach (var policy in policies)
    for (var sample = 0; sample < seeds; sample++)
    {
        var seed = 20261029 + sample;
        var random = new SeededRandom(seed);
        var state = GameEngine.CreateGame(definition);
        var controller = new CampaignProvider(policy, () => state);
        var initialHeroes = state.Units.Where(u => u.SideId == "side-1").Select(u => u.Id).ToHashSet();
        var decisions = 0;
        var counts = new SortedDictionary<string, int>();
        void Consume(EngineResult result)
        {
            foreach (var e in result.Events)
                if (e.Kind is "UnitDefeated" or "UnitCreated" or "DoorOpened" or "AttackStarted" or "UnitHealed")
                    counts[e.Kind] = counts.GetValueOrDefault(e.Kind) + 1;
            state = result.State;
        }
        while (state.Round < roundLimit && !state.IsCompleted && decisions < 15000)
        {
            Consume(GameEngine.StartRound(state, random));
            while (state.Pending is not null && decisions < 15000)
            {
                decisions++;
                Consume(GameEngine.Advance(state, controller, random));
            }
        }
        var result = new
        {
            scenario = id, policy, seed, rounds = state.Round, decisions,
            winners = state.Completion?.AchievedSideIds.ToArray() ?? [],
            timeout = !state.IsCompleted,
            heroDeaths = initialHeroes.Count(state.DefeatedUnitIds.Contains),
            heroHp = state.Units.Where(u => initialHeroes.Contains(u.Id)).ToDictionary(u => u.Id, u => u.CurrentHp),
            livingEnemies = state.Units.Count(u => u.SideId == "side-2" && u.CurrentHp > 0),
            counts
        };
        runs.Add(result);
        Console.Error.WriteLine($"{id} {policy} seed={seed}: {(state.IsCompleted ? string.Join(',', result.winners) : "timeout")} round={state.Round} heroes lost={result.heroDeaths}");
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { campaign = args[1], seeds, roundLimit, runs }, new JsonSerializerOptions { WriteIndented = true }));

sealed class CampaignProvider(string policy, Func<GameState> current) : IDecisionProvider
{
    private readonly OrderAwareAutomatedProvider standard = new();
    public string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        if (request.Token?.SideId != "side-1" || request.UnitId is null || request.Kind is DecisionKind.RollDice or DecisionKind.SelectUnit)
            return standard.Choose(request, queries);
        var state = current();
        var unitId = request.UnitId;
        var objective = queries.Objectives.Single(o => o.SideId == "side-1");
        var enemiesRemain = state.Units.Any(u => u.CurrentHp > 0 && state.AreHostile("side-1", u.SideId));
        var targetIds = objective.Objective == ScenarioObjective.DefeatUnits
            ? objective.Selectors.SelectMany(s => s.Kind switch
            {
                UnitSelectorKind.Unit => new[] { s.UnitId! },
                UnitSelectorKind.Group => state.Units.Where(u => u.TypeId == s.UnitTypeId && u.SideId == s.SideId).Select(u => u.Id),
                _ => state.Units.Where(u => u.SideId == s.SideId).Select(u => u.Id)
            }).Where(id => !state.DefeatedUnitIds.Contains(id)).ToArray() : [];
        bool Attack(Candidate c) => c.Action is UnitAction.NormalAttack or UnitAction.Fireball or UnitAction.ClawAttack or UnitAction.FireBreath;
        // Do not spend scarce attacks on empty cells or knowingly hurt the party.
        var attacks = request.Candidates.Where(Attack)
            .Where(c => c.TargetId is not null || c.TargetIds.Any(t => queries.IsHostileTo(unitId, t)))
            .Where(c => c.TargetIds.All(t => queries.IsHostileTo(unitId, t))).ToArray();
        request = request with { Candidates = request.Candidates.Where(c => !Attack(c) || attacks.Contains(c)).ToList() };
        var heal = request.Candidates.Where(c => c.Action == UnitAction.Heal && c.TargetId is not null)
            .OrderBy(c => state.Units.Single(u => u.Id == c.TargetId).CurrentHp)
            .FirstOrDefault(c =>
            {
                var u = state.Units.Single(u => u.Id == c.TargetId);
                return state.Types.Single(t => t.Id == u.TypeId).Hp - u.CurrentHp >= 2;
            });
        if (heal is not null) return heal.Key;
        if (attacks.Length > 0 && request.Candidates.FirstOrDefault(c => c.Ability?.Definition is
            { Timing: AbilityTiming.BonusAction, Mechanic: SelfModifiersMechanic modifier } &&
            modifier.Modifiers.Any(m => m.Stat == Stat.Atk && m.Amount > 0)) is { } power)
            return power.Key;
        var position = queries.PositionOf(unitId);
        int? Remaining(Cell cell) => objective.Objective switch
        {
            ScenarioObjective.ReachArea => queries.RouteDistanceToArea(unitId, cell, objective.Area!, true),
            ScenarioObjective.DefeatUnits when targetIds.Length > 0 => queries.DistanceToAttackPositionFrom(unitId, cell, targetIds, true),
            _ => queries.DistanceToAttackPositionFrom(unitId, cell, true)
        };
        var currentRemaining = Remaining(position);
        // Doors are ordinary legal free actions. Opening a nearby door may expose danger;
        // this deliberately simple policy does not pretend to solve that tactical choice.
        if (request.Candidates.FirstOrDefault(c => c.FreeAction == UnitFreeAction.OpenDoor) is { } door)
            return door.Key;
        bool MeetsTarget(Candidate c) => targetIds.Contains(c.TargetId!) || c.TargetIds.Any(targetIds.Contains);
        if (policy != "aggressive" && objective.Objective == ScenarioObjective.ReachArea &&
            currentRemaining > queries.EffectiveMovOf(unitId) &&
            request.Candidates.FirstOrDefault(c => c.Ability?.Definition is
                { Timing: AbilityTiming.BonusAction, Mechanic: SelfModifiersMechanic modifier } &&
                modifier.Modifiers.Any(m => m.Stat == Stat.Mov && m.Amount > 0)) is { } dash)
            return dash.Key;
        // Objective policy fights immediately when able; rush spends movement on the exit
        // first, even if an enemy could have been killed from the previous position.
        if (attacks.Length > 0 && (policy != "rush" || objective.Objective != ScenarioObjective.ReachArea ||
            !request.Candidates.Any(c => c.Kind == ActivationChoiceKind.Move)))
            return attacks.OrderByDescending(c => policy != "aggressive" && MeetsTarget(c))
                .ThenByDescending(c => queries.EffectiveAttackDice(unitId, c))
                .ThenByDescending(c => c.TargetIds.Count(t => queries.IsHostileTo(unitId, t)))
                .ThenByDescending(c => queries.IsUnlimitedAttack(unitId, c)).ThenBy(c => c.Key, StringComparer.Ordinal).First().Key;
        if ((policy != "aggressive" || !enemiesRemain) && objective.Objective is ScenarioObjective.ReachArea or ScenarioObjective.DefeatUnits)
        {
            // Once in an exit, vacate its entrance for the rest of the party. A distinct
            // standing cell prevents a greedy bot from blocking another Hero forever.
            if (objective.Objective == ScenarioObjective.ReachArea && currentRemaining == 0)
            {
                var area = objective.Area!;
                var places = Enumerable.Range(area.Y, area.H)
                    .SelectMany(y => Enumerable.Range(area.X, area.W).Select(x => new Cell(x, y)))
                    .Where(c => queries.TerrainAt(c).Passable()).ToArray();
                var party = state.Units.Where(u => u.SideId == "side-1").Select(u => u.Id).ToArray();
                if (places.Length >= party.Length)
                {
                    var destination = places[Array.IndexOf(party, unitId)];
                    var distance = queries.RouteDistanceTo(unitId, position, destination, true);
                    var settle = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move && c.Destination is not null)
                        .Where(c => Remaining(c.Destination!) == 0)
                        .Select(c => new { Candidate = c, Distance = queries.RouteDistanceTo(unitId, c.Destination!, destination, true) })
                        .Where(c => c.Distance.HasValue && c.Distance < distance)
                        .OrderBy(c => c.Distance).ThenBy(c => c.Candidate.Path?.Count).FirstOrDefault();
                    if (settle is not null) return settle.Candidate.Key;
                }
            }
            var move = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move || request.Kind == DecisionKind.Move)
                .Where(c => c.Destination is not null)
                .Select(c => new { Candidate = c, Distance = Remaining(c.Destination!), Danger = queries.CountAdjacentHostilesFrom(unitId, c.Destination!) })
                .Where(x => x.Distance.HasValue && (!currentRemaining.HasValue || x.Distance < currentRemaining))
                .OrderBy(x => x.Distance).ThenBy(x => x.Danger)
                .ThenBy(x => x.Candidate.Path?.Count).ThenBy(x => x.Candidate.Destination!.Y).ThenBy(x => x.Candidate.Destination!.X)
                .FirstOrDefault();
            if (move is not null) return move.Candidate.Key;
            if (currentRemaining == 0 && request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay) is { } stay)
                return stay.Key;
            if (currentRemaining == 0 && request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.EndTurn) is { } end)
                return end.Key;
        }
        return standard.Choose(request, queries);
    }
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
