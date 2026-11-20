using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

/// <summary>Opt-in diagnostic; generated content is measured, never used as a behavioral fixture.</summary>
public sealed class ExpeditionEconomyAssessment(ITestOutputHelper output)
{
    // Overrides broaden diagnostics without changing the exact Phase 7 default reproduction.
    private static ulong[] Seeds() => Environment.GetEnvironmentVariable("PROJECTDELVE_ECONOMY_SEEDS") is { Length: > 0 } seeds
        ? seeds.Split(',').Select(ulong.Parse).ToArray() : [17, 42, 889, 2026, 77341];

    [Fact]
    public void ExportServiceFrequencySample()
    {
        var path = Environment.GetEnvironmentVariable("PROJECTDELVE_ECONOMY_ASSESSMENT_OUTPUT");
        if (string.IsNullOrWhiteSpace(path)) return;
        var published = PublishedContent();
        var content = published.Bundle;
        var rows = new List<object>();
        foreach (var seed in Seeds())
        {
            var recipe = ExpeditionWorld.Configure(seed);
            var profile = ExpeditionWorldProfile.Create(recipe.NaturalTerrain);
            var plan = ExpeditionWorld.Initial(seed);
            for (var region = 0; region < 6; region++, plan = ExpeditionWorld.Next(seed, plan))
            {
                var context = ExpeditionRegionGeometry.Execute(ExpeditionWorld.RecipeFor(plan, recipe),
                    plan.EntrySeam, plan.ExitSeam, plan.ExitArea, plan.LocalSeed, content, profile: profile).Context;
                var scenario = context.Materialize();
                var population = scenario.Units.GroupBy(u => u.UnitTypeId).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => { var type = content.Units.Single(t => t.Id == g.Key); return new {
                        TypeId = g.Key, Count = g.Count(), type.LootDropSuccesses, type.CreditsDropSuccesses, type.CreditsDropAmount,
                        HypotheticalExpectedCredits = g.Count() * type.CreditsDropSuccesses * type.CreditsDropAmount / 6.0 }; }).ToArray();
                rows.Add(new { Seed = seed, RegionIndex = region, Population = population,
                    RequestedCare = ExpeditionEconomyGenerator.Requests(plan.LocalSeed, "delvecare"),
                    RequestedMeteor = ExpeditionEconomyGenerator.Requests(plan.LocalSeed, "meteor-o-mat"),
                    Placed = scenario.Machines!.Where(m => m.Type.Operation.PaidServices is not null).Select(m => m.Type.Id).ToArray(),
                    Omissions = context.Completion!.Diagnostics.Where(d => d.ProducerId == "expedition-economy" && d.Status == CompletionStatus.Omitted)
                        .Select(d => new { d.CandidateId, d.Detail }).ToArray() });
            }
        }
        var requests = new[] { "delvecare", "meteor-o-mat" }.ToDictionary(id => id,
            id => Enumerable.Range(0, 1200).Count(i => ExpeditionEconomyGenerator.Requests((ulong)i, id)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".frequency.json", JsonSerializer.Serialize(new { ContentRevision = published.Snapshot.Revision,
            LocalSeedRequestsOutOf1200 = requests, Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void ExportDeterministicEconomySample()
    {
        var path = Environment.GetEnvironmentVariable("PROJECTDELVE_ECONOMY_ASSESSMENT_OUTPUT");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var published = PublishedContent();
        var content = published.Bundle;
        var seeds = Seeds();
        var rows = new List<object>();
        foreach (var seed in seeds)
        {
            var recipe = ExpeditionWorld.Configure(seed);
            var profile = ExpeditionWorldProfile.Create(recipe.NaturalTerrain);
            var plan = ExpeditionWorld.Initial(seed);
            GameState? previous = null;
            var random = new ExpeditionRandom(seed);
            for (var region = 0; region < 3; region++)
            {
                var terrain = ExpeditionWorld.Generate(plan, content, recipe, profile);
                var scenario = ExpeditionParty.Prepare(terrain, content, plan.ExitArea, ExpeditionParty.Originals, previous);
                var state = GameEngine.CreateGame(scenario, content);
                var graph = Graph(state.Physical.Board);
                var opening = state.SideCredits.GetValueOrDefault(ExpeditionParty.SideId);
                var initialLoot = state.LootMarkers.Count;
                var expectedDropCredits = state.Units.Where(u => u.SideId != ExpeditionParty.SideId)
                    .Sum(u => { var t = state.Types.Single(t => t.Id == u.TypeId); return t.CreditsDropSuccesses * t.CreditsDropAmount / 6.0; });
                var population = state.Units.Where(u => u.SideId != ExpeditionParty.SideId).GroupBy(u => u.TypeId)
                    .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new { TypeId = g.Key, Count = g.Count() }).ToArray();
                var milestones = new Dictionary<int, int?> { [10] = null, [12] = null, [15] = null, [24] = null, [50] = null, [75] = null };
                foreach (var threshold in milestones.Keys.ToArray()) if (opening >= threshold) milestones[threshold] = 0;
                var generated = 0; var collected = 0; var otherCollected = 0; var sales = 0L; var defeats = 0;
                var decisions = 0; var peak = opening; var lootDraws = 0;
                object? firstFundedInventory = null;
                object? firstFundedBoots = null;
                var fundedStates = 0;
                var bootsQuotes = new List<object>();
                void Apply(EngineResult result)
                {
                    state = result.State;
                    generated += result.Events.Where(e => e.Kind == "DefeatDropResolved").Sum(e => e.CreditsAmount ?? 0);
                    collected += result.Events.Where(e => e.Kind == "CreditsCollected" && e.SideId == ExpeditionParty.SideId).Sum(e => e.CreditsAmount ?? 0);
                    otherCollected += result.Events.Where(e => e.Kind == "CreditsCollected" && e.SideId != ExpeditionParty.SideId).Sum(e => e.CreditsAmount ?? 0);
                    sales += result.Events.SelectMany(e => e.EquipmentSales).Sum(s => (long)s.SellValue);
                    defeats += result.Events.Count(e => e.Kind == "DefeatDropResolved");
                    lootDraws += result.Events.Count(e => e.Kind == "LootCardDrawn");
                    peak = Math.Max(peak, state.SideCredits.GetValueOrDefault(ExpeditionParty.SideId));
                    foreach (var threshold in milestones.Keys.ToArray())
                        if (milestones[threshold] is null && state.SideCredits.GetValueOrDefault(ExpeditionParty.SideId) >= threshold)
                            milestones[threshold] = state.Round;
                    if (state.SideCredits.GetValueOrDefault(ExpeditionParty.SideId) >= 12)
                    {
                        fundedStates++;
                        var living = state.Units.Where(u => u.SideId == ExpeditionParty.SideId && u.CurrentHp > 0).ToArray();
                        firstFundedInventory ??= new { state.Round, Bank = state.SideCredits[ExpeditionParty.SideId],
                            Units = living.Select(u => new { u.Id, Objects = u.Inventory.Select(o => new { o.Id, o.EquipmentTypeId, o.ItemTypeId, o.Level }).ToArray() }).ToArray() };
                        var boots = living.SelectMany(u => u.Inventory.Where(o => o.EquipmentTypeId == "lucky-boots")
                            .Select(o => new { UnitId = u.Id, ObjectId = o.Id, o.Level })).ToArray();
                        if (boots.Length > 0) firstFundedBoots ??= new { state.Round, Bank = state.SideCredits[ExpeditionParty.SideId], Boots = boots };
                    }
                }
                Apply(GameEngine.StartRound(state, random, false));
                while (!state.IsCompleted && state.Units.Any(u => u.SideId == ExpeditionParty.SideId && u.CurrentHp > 0) &&
                    decisions++ < 3200 && state.Round <= 40)
                {
                    if (state.Pending is null)
                    {
                        if (!state.RoundComplete) break;
                        Apply(GameEngine.StartRound(state, random, false));
                        continue;
                    }
                    if (state.MachineInteraction is not null)
                    {
                        var pool = state.MachineInteraction.EquipmentStation!.OriginalAllocation;
                        foreach (var assignment in pool.Assignments)
                        {
                            var obj = state.Units.SelectMany(u => u.Inventory).Single(o => o.Id == assignment.ObjectId);
                            if (obj.EquipmentTypeId != "lucky-boots" || state.SideCredits.GetValueOrDefault(ExpeditionParty.SideId) < 12) continue;
                            // Read-only quote at a naturally reached ready Station; do not submit this proposal.
                            var quote = GameEngine.PreviewEquipmentStation(state, pool with { UpgradedEquipmentIds = [obj.Id] });
                            bootsQuotes.Add(new { state.Round, ObjectId = obj.Id, obj.Level,
                                Bank = state.SideCredits[ExpeditionParty.SideId], quote.CanConfirm });
                        }
                        var sold = pool.Assignments.Where(a => Value(state, a.ObjectId) is not null).Select(a => a.ObjectId).ToImmutableArray();
                        var allocation = new EquipmentAllocation([.. pool.Assignments.Where(a => !sold.Contains(a.ObjectId))]) { SoldObjectIds = sold };
                        Apply(GameEngine.PreviewEquipmentStation(state, allocation).CanConfirm
                            ? GameEngine.ConfirmEquipmentStation(state, allocation, random, false)
                            : GameEngine.CancelMachineInteraction(state, random, false));
                        continue;
                    }
                    var request = state.Pending;
                    if (state.ControllerFor(request) == ControllerKind.Automated)
                    {
                        Apply(GameEngine.Advance(state, new OrderAwareAutomatedProvider(), random, false));
                        continue;
                    }
                    var choice = Choose(state, request, plan.ExitArea, graph);
                    Apply(GameEngine.Advance(state, new Choice(choice.Key), random, false));
                }
                var closing = state.SideCredits.GetValueOrDefault(ExpeditionParty.SideId);
                var retained = state.Units.Where(u => u.SideId == ExpeditionParty.SideId)
                    .SelectMany(u => u.Inventory).Sum(o => (long)(Value(state, o.Id) ?? 0));
                rows.Add(new { Seed = seed, RegionIndex = region, plan.Region, Opening = opening,
                    InitialMonsters = terrain.Units.Count, InitialPopulation = population, InitialLoot = initialLoot,
                    HypotheticalExpectedInitialDropCredits = expectedDropCredits, FirstAffordableRound = milestones,
                    FundedStatesObserved = fundedStates, FirstFundedLivingInventory = firstFundedInventory,
                    FirstFundedLivingBoots = firstFundedBoots, NaturallyReachedFundedBootsStationQuotes = bootsQuotes,
                    DelveCare = terrain.Machines?.Count(m => m.Type.Id == "delvecare") ?? 0,
                    MeteorOMat = terrain.Machines?.Count(m => m.Type.Id == "meteor-o-mat") ?? 0,
                    CreditsEligibleDefeatResolutions = defeats, GeneratedCredits = generated, CollectedCredits = collected, OtherSideCollectedCredits = otherCollected,
                    SaleCredits = sales, Closing = closing, PeakBank = peak, RetainedSellPotential = retained,
                    HypotheticalBankAfterLiquidation = closing + retained,
                    UncollectedCredits = state.CreditsPiles.Sum(p => p.Amount), LootDraws = lootDraws,
                    Decisions = decisions, state.Round, Completed = state.IsCompleted,
                    Outcome = state.IsCompleted ? "completed" : !state.Units.Any(u => u.SideId == ExpeditionParty.SideId && u.CurrentHp > 0)
                        ? "Party defeated" : "capped",
                    PartyAlive = state.Units.Count(u => u.SideId == ExpeditionParty.SideId && u.CurrentHp > 0) });
                File.WriteAllText(path, JsonSerializer.Serialize(new { Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
                output.WriteLine($"{seed}/{region}: drops {generated}, collected {collected}, sold {sales}, bank {closing}, retained {retained}, completed {state.IsCompleted}, round {state.Round}");
                if (!state.IsCompleted || !state.Completion!.AchievedSideIds.Contains(ExpeditionParty.SideId)) break;
                previous = state;
                plan = ExpeditionWorld.Next(seed, plan);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { ContentRevision = published.Snapshot.Revision,
            Policy = "Scavenge and engage discovered enemies until round 8; liquidate at arrival Station until round 12; then exit. Attack/heal first; prioritize board Credits; standard enemy automation. Stop on completion, Party defeat, or 40-round/3200-decision cap.", Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int? Value(GameState state, string id)
    {
        var obj = state.Units.SelectMany(u => u.Inventory).Single(o => o.Id == id);
        return obj.EquipmentTypeId is { } equipment ? state.EquipmentTypes.Single(t => t.Id == equipment).SellValue :
            obj.ItemTypeId is { } item ? state.ItemTypes.Single(t => t.Id == item).SellValue : null;
    }

    private static CanonicalContent PublishedContent() => new(Environment.GetEnvironmentVariable("PROJECTDELVE_ECONOMY_CONTENT_DIRECTORY") ?? Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web/Content/Canonical")));

    private static Candidate Choose(GameState state, DecisionRequest request, ObjectiveArea exit, Dictionary<Cell, Cell[]> graph)
    {
        if (request.Kind is DecisionKind.SelectUnit or DecisionKind.RollDice) return request.Candidates[0];
        var attack = request.Candidates.FirstOrDefault(c => c.Action == UnitAction.NormalAttack);
        if (attack is not null) return attack;
        var heal = request.Candidates.FirstOrDefault(c => c.Action == UnitAction.AdjacentUnitHeal);
        if (heal is not null) return heal;
        var pickup = request.Candidates.FirstOrDefault(c => c.ObjectId is not null);
        if (pickup is not null) return pickup;
        var hasSales = state.Units.Where(u => u.SideId == ExpeditionParty.SideId).SelectMany(u => u.Inventory)
            .Any(o => Value(state, o.Id) is > 0);
        var station = state.Physical.Board.Machines.Single(m => m.Id == ExpeditionRegionGeometry.ArrivalStationId);
        var selling = state.Round is >= 8 and <= 12 && hasSales;
        var operate = selling ? request.Candidates.FirstOrDefault(c => c.Action == UnitAction.Operate && c.MachineId == station.Id) : null;
        if (operate is not null) return operate;
        var scavenging = state.LootMarkers.Select(m => m.Position).Concat(state.Units.Where(u => u.SideId != ExpeditionParty.SideId &&
                u.CurrentHp > 0 && state.Physical.Figures.Any(f => f.Id == u.Id))
            .SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id))
            .Where(c => FogOfWar.IsExplored(state, ExpeditionParty.SideId, c))
            .SelectMany(c => graph.GetValueOrDefault(c) ?? []));
        IEnumerable<Cell> targets = selling ? MachineGeometry.PotentialOperatingPositions(state.Physical.Board, station) :
            state.CreditsPiles.Count > 0 ? state.CreditsPiles.Select(p => p.Position) :
            state.Round < 8 && scavenging.Any() ? scavenging :
            from y in Enumerable.Range(exit.Y, exit.H) from x in Enumerable.Range(exit.X, exit.W) select new Cell(x, y);
        var placed = state.Physical.Figures.Select(f => f.Id).ToHashSet();
        var occupied = state.Units.Where(u => placed.Contains(u.Id) && u.Id != request.UnitId).SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).ToHashSet();
        var blockers = state.Units.Where(u => placed.Contains(u.Id) && u.SideId != ExpeditionParty.SideId).SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).ToHashSet();
        var distance = new Dictionary<Cell, int>(); var queue = new Queue<Cell>();
        foreach (var target in targets.Where(t => !occupied.Contains(t)))
            if (distance.TryAdd(target, 0)) queue.Enqueue(target);
        while (queue.TryDequeue(out var cell))
            foreach (var next in graph.GetValueOrDefault(cell) ?? [])
                if (!blockers.Contains(next) && distance.TryAdd(next, distance[cell] + 1)) queue.Enqueue(next);
        var position = state.Physical.Figures.Single(f => f.Id == request.UnitId).Position;
        var move = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move && c.Destination is not null &&
            distance.GetValueOrDefault(c.Destination, int.MaxValue) < distance.GetValueOrDefault(position, int.MaxValue))
            .OrderBy(c => distance[c.Destination!]).ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();
        if (move is not null) return move;
        var door = request.Candidates.FirstOrDefault(c => c.Door is { } edge && (edge.A == position || edge.B == position) &&
            distance.GetValueOrDefault(edge.A == position ? edge.B : edge.A, int.MaxValue) < distance.GetValueOrDefault(position, int.MaxValue));
        return door ?? request.Candidates.FirstOrDefault(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn) ?? request.Candidates[0];
    }

    private static Dictionary<Cell, Cell[]> Graph(Board board)
    {
        var geometry = AccessibilityAnalysis.Capture(board with { Edges = board.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
        var profile = new AccessibilityProfile(Footprint.OneByOne, [MovementKinds.Land], 1);
        var result = new Dictionary<Cell, Cell[]>();
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            if (!geometry.CanStop(profile, cell)) continue;
            result[cell] = new[] { new Cell(x - 1, y), new Cell(x + 1, y), new Cell(x, y - 1), new Cell(x, y + 1) }
                .Where(n => n.X >= 0 && n.Y >= 0 && n.X < board.Width && n.Y < board.Height && geometry.CanStep(profile, cell, n)).ToArray();
        }
        return result;
    }

    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
}
