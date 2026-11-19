using System.Collections.Immutable;
using System.Text.Json;
using System.Diagnostics;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionJourneyTests(ITestOutputHelper output)
{
    [Fact]
    public void ActualSummonInExitDoesNotReplaceSecondOriginalHero()
    {
        var deployment = (from y in Enumerable.Range(1, 2) from x in Enumerable.Range(1, 5) select new Cell(x, y)).ToList();
        var core = deployment.Where(c => c.X < 5).ToList();
        var content = ContentBundle.Canonical();
        var exit = new ObjectiveArea(8, 2, 3, 3);
        var terrain = new ScenarioDefinition
        {
            Board = new() { Width = 12, Height = 8, DefaultTerrain = TerrainKind.Grass },
            UnitTypeIds = [UnitTypeIds.Jackalope], Units = [], Agency = [], Loot = LootSettings.Empty,
            Setup = new(1, new(5, deployment, core, core.Concat(deployment.Except(core)).ToList(), new(5, 1), new(6, 1), 10, 8), [])
        };
        var scenario = ExpeditionParty.Prepare(terrain, content, exit, ExpeditionParty.Originals, null);
        scenario = scenario with { Units = scenario.Units.Select(u => u.UnitTypeId switch
        {
            UnitTypeIds.Beastmaster => u with { Anchor = new(9, 3) },
            UnitTypeIds.Cleric => u with { Anchor = new(7, 3) },
            _ => u
        }).ToList() };
        var random = new SummoningRandom();
        var state = GameEngine.StartRound(GameEngine.CreateGame(scenario, content), random, false).State;
        void Choose(Candidate candidate)
        {
            Assert.Contains(candidate, state.Pending!.Candidates);
            state = GameEngine.Advance(state, new Choice(candidate.Key), random, false).State;
        }
        if (state.Pending!.Kind == DecisionKind.SelectUnit) Choose(Assert.Single(state.Pending.Candidates));
        Assert.Equal(UnitTypeIds.Beastmaster, state.Units.Single(u => u.Id == state.Pending!.UnitId).TypeId);
        Choose(state.Pending.Candidates.Single(c => c.Kind == ActivationChoiceKind.Stay));
        var summons = state.Pending!.Candidates.Where(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic m &&
            m.UnitTypeId == UnitTypeIds.Jackalope && c.Destination is { } destination && ExitCells(exit).Contains(destination)).ToArray();
        Assert.True(summons.Length > 0, string.Join(";", state.Pending.Candidates.Select(c => c.Key)) + " TYPES:" + string.Join(",", state.Types.Select(t => t.Id)));
        var summon = summons[0];
        Choose(summon);
        var child = Assert.Single(state.Units, u => u.SummonerUnitId is not null);
        Assert.Contains(state.Physical.Figures.Single(f => f.Id == child.Id).Position, ExitCells(exit));
        Assert.Equal(2, state.Physical.Figures.Count(f => ExitCells(exit).Contains(f.Position)));
        Assert.False(state.IsCompleted);
        Choose(state.Pending!.Candidates.Single(c => c.Kind == ActivationChoiceKind.EndTurn));
        Assert.False(state.IsCompleted);
        if (state.Pending!.Kind == DecisionKind.SelectUnit) Choose(Assert.Single(state.Pending.Candidates));
        Assert.Equal(UnitTypeIds.Cleric, state.Units.Single(u => u.Id == state.Pending!.UnitId).TypeId);
        Choose(state.Pending!.Candidates.First(c => c.Kind == ActivationChoiceKind.Move && c.Destination is { } destination && ExitCells(exit).Contains(destination)));
        // Reach Area is evaluated at the stable boundary; finish this original's activation if needed.
        if (!state.IsCompleted) Choose(state.Pending!.Candidates.Single(c => c.Kind == ActivationChoiceKind.EndTurn));
        Assert.True(state.IsCompleted);
        Assert.Contains(ExpeditionParty.SideId, state.Completion!.AchievedSideIds);
    }

    [Fact]
    public void PopulatedJourneyCompletesThroughLegalDecisionsAndCrossesAlignedSeamWithOwnedGear()
    {
        var root = Path.Combine(Path.GetTempPath(), "delve-journey-" + Guid.NewGuid().ToString("N"));
        try
        {
            const ulong seed = 889;
            const string gearId = "journey-picked-up-boots";
            const string itemId = "journey-picked-up-second-wind";
            const string newLootId = "new-frontier-second-wind";
            var settings = ExpeditionWorld.Configure(seed, new(new(), new(), Population: new(PopulationDensity.Low)));
            var profile = ExpeditionWorldProfile.Create(settings.NaturalTerrain);
            var store = new ExpeditionStore(root, "journey");
            string? incompatibleHeroType = null;
            var compatibleRefresh = false;
            ContentBundle Latest()
            {
                var content = ContentBundle.Canonical();
                if (incompatibleHeroType is not null)
                    return content with
                    { Units = content.Units.Select(type => type.Id == incompatibleHeroType ? type with { CanHaveEquipment = false } : type).ToImmutableArray() };
                return compatibleRefresh ? content with
                {
                    Equipment = content.Equipment.Select(item => item.Id == "lucky-boots" ? item with { Name = "Frontier Lucky Boots" } : item).ToImmutableArray(),
                    LootCards = content.LootCards.Add(new(newLootId, "Frontier Second Wind") { ItemTypeId = "second-wind" })
                } : content;
            }
            ScenarioDefinition Generate(ulong worldSeed, ExpeditionRegionPlan plan, ContentBundle content)
            {
                Assert.Equal(seed, worldSeed);
                var generated = ExpeditionWorld.Generate(plan, content, settings, profile);
                var landing = generated.Setup!.Deployment.PlacementOrder[0];
                return plan.Region == default ? generated with
                {
                    GroundObjects = [.. generated.GroundObjects ?? [], new(new OwnedObject(gearId, "lucky-boots"), landing),
                        new(new OwnedObject(itemId) { ItemTypeId = "second-wind" }, landing)]
                } : generated;
            }
            var creation = Stopwatch.StartNew();
            var runner = ExpeditionRunner.Create(store, seed, Latest, Generate, settings.ToString(), profile);
            creation.Stop();
            var initialPlan = runner.Region;
            var firstCheckpoint = store.ReadCheckpoint(runner.Head.ActiveCheckpoint);
            Assert.Equal(60, firstCheckpoint.Scenario.Board.Width);
            Assert.Equal(60, firstCheckpoint.Scenario.Board.Height);
            Assert.Contains(ExpeditionParty.SideId, firstCheckpoint.State.FogOfWarSides);
            Assert.Contains(firstCheckpoint.State.Units, unit => unit.SideId != ExpeditionParty.SideId);
            Assert.NotEmpty(firstCheckpoint.State.LootMarkers);
            var graph = OrdinaryGraph(firstCheckpoint.State.Physical.Board);
            var distances = new Dictionary<Cell, int> { [initialPlan.EntrySeam] = 0 };
            var flood = new Queue<Cell>([initialPlan.EntrySeam]);
            while (flood.TryDequeue(out var current))
                foreach (var next in graph[current])
                    if (distances.TryAdd(next, distances[current] + 1)) flood.Enqueue(next);
            var deadEnds = distances.Keys.Count(c => graph[c].Length == 1);
            var manhattan = Math.Abs(initialPlan.EntrySeam.X - initialPlan.ExitSeam.X) + Math.Abs(initialPlan.EntrySeam.Y - initialPlan.ExitSeam.Y);
            output.WriteLine($"Source graph: reachable {distances.Count}/3600, shortest seam path {distances[initialPlan.ExitSeam]}, Manhattan {manhattan}, reachable dead ends {deadEnds}.");
            Assert.True(graph.Count < 3600); // Natural obstacles survive normal repair.
            Assert.True(distances[initialPlan.ExitSeam] > manhattan); // The legal route bends around nature.
            Assert.True(deadEnds > 0); // Reaching the exit does not require erasing reachable dead ends.
            var state = runner.StartRound().State;
            var resumedPending = false;
            var automatedDecision = false;
            HashSet<string> pickedUp = [];
            var steps = 0;
            while (!state.IsCompleted && steps++ < 2400)
            {
                if (state.Pending is null)
                {
                    Assert.True(state.RoundComplete);
                    state = runner.StartRound().State;
                    continue;
                }
                var request = state.Pending;
                var automated = state.ControllerFor(request) == ControllerKind.Automated;
                if (automated && !automatedDecision)
                {
                    state = runner.DecideAutomatically().State;
                    automatedDecision = true;
                    continue;
                }
                var pickup = request.Candidates.FirstOrDefault(c => c.Key == "pickup-object:" + gearId || c.Key == "pickup-object:" + itemId);
                // Keep the remaining encounters in their initial positions so the test isolates
                // the generated geographic guarantee from the outcome of tactical combat.
                var choice = pickup ?? (automated ? Pass(request) : ChooseJourneyStep(state, request, initialPlan.ExitArea, graph));
                Assert.Contains(choice, request.Candidates);
                var result = runner.Decide(choice.Key);
                state = result.State;
                if (pickup is not null)
                {
                    pickedUp.Add(pickup.ObjectId!);
                    Assert.Contains(state.Units.Single(u => u.Id == request.UnitId).Inventory, o => o.Id == pickup.ObjectId);
                    Assert.DoesNotContain(state.GroundObjects, o => o.Object.Id == pickup.ObjectId);
                }
                if (!resumedPending && steps >= 10 && state.Pending is not null)
                {
                    runner.Save();
                    runner = ExpeditionRunner.Open(store, Latest, Generate);
                    var resumed = runner.Snapshot();
                    Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(resumed));
                    state = resumed;
                    resumedPending = true;
                }
            }
            Assert.True(state.IsCompleted, $"Journey did not complete after {steps} legal decisions, round {state.Round}. " +
                string.Join(", ", state.Physical.Figures.Where(f => ExpeditionParty.Originals.Any(hero => hero.InstanceId == f.Id))
                    .Select(f => $"{f.Id} at {f.Position}")));
            Assert.Equal(new[] { gearId, itemId }.Order(), pickedUp.Order());
            Assert.True(automatedDecision);
            Assert.True(resumedPending);
            Assert.Contains(ExpeditionParty.SideId, state.Completion!.AchievedSideIds);
            Assert.True(ExpeditionParty.Originals.Count(hero => state.Physical.Figures.Any(f => f.Id == hero.InstanceId &&
                ExitCells(initialPlan.ExitArea).Contains(f.Position))) >= 2);

            // Completion itself must already be durable before Continue is invoked.
            var committedHead = store.ReadHead();
            Assert.Equal(runner.Head.ActiveCheckpoint, committedHead.ActiveCheckpoint);
            var source = store.ReadCheckpoint(committedHead.ActiveCheckpoint);
            Assert.True(source.State.IsCompleted);
            Assert.Null(source.State.Pending);
            Assert.Empty(committedHead.Transitions);
            var originals = ExpeditionParty.Originals.Select(h => h.InstanceId).ToHashSet(StringComparer.Ordinal);
            var sourceJson = JsonSerializer.Serialize(source);

            incompatibleHeroType = Assert.Single(source.State.Units, unit => unit.Inventory.Any(obj => obj.Id == gearId)).TypeId;
            var incompatible = Assert.Throws<ExpeditionOperationException>(() => runner.Continue(initialPlan.TransitionId));
            Assert.Equal(ExpeditionFailureStage.PartySetup, incompatible.Stage);
            Assert.Contains("ownership", incompatible.Message);
            Assert.Equal(committedHead.ActiveCheckpoint, store.ReadHead().ActiveCheckpoint);
            Assert.Equal(sourceJson, JsonSerializer.Serialize(store.ReadCheckpoint(committedHead.ActiveCheckpoint)));
            incompatibleHeroType = null;
            compatibleRefresh = true;

            var transition = Stopwatch.StartNew();
            var receipt = runner.Continue(initialPlan.TransitionId);
            transition.Stop();
            output.WriteLine($"World seed {seed:X16}; region ({initialPlan.Region.X},{initialPlan.Region.Y}) entry {initialPlan.EntrySeam}, exit {initialPlan.ExitSeam}; successor ({runner.Region.Region.X},{runner.Region.Region.Y}) entry {runner.Region.EntrySeam}.");
            output.WriteLine($"Create {creation.Elapsed.TotalMilliseconds:F1} ms, successor generation and commit {transition.Elapsed.TotalMilliseconds:F1} ms, {steps} decisions, Round {state.Round}, checkpoint bytes {new FileInfo(Path.Combine(root, "expedition-journey", receipt.Source.Digest + ".json")).Length} and {new FileInfo(Path.Combine(root, "expedition-journey", receipt.Successor.Digest + ".json")).Length}.");
            Assert.Equal(committedHead.ActiveCheckpoint, receipt.Source);
            Assert.Equal(initialPlan.NextRegion, runner.Region.Region);
            Assert.Equal(SeamWorldCell(initialPlan.Region, initialPlan.ExitSeam),
                SeamWorldCell(runner.Region.Region, runner.Region.EntrySeam, initialPlan.ExitEdge));
            Assert.Equal(sourceJson, JsonSerializer.Serialize(store.ReadCheckpoint(receipt.Source)));
            Assert.Equal(receipt, runner.Continue(initialPlan.TransitionId));
            Assert.Equal(profile, runner.Head.WorldProfile);

            var successor = store.ReadCheckpoint(receipt.Successor);
            Assert.False(successor.State.IsCompleted);
            Assert.Equal(60, successor.Scenario.Board.Width);
            Assert.Equal(60, successor.Scenario.Board.Height);
            Assert.Equal(originals.Order(), successor.State.Units.Where(u => u.SideId == ExpeditionParty.SideId).Select(u => u.Id).Order());
            Assert.Contains(successor.State.Units, unit => unit.SideId != ExpeditionParty.SideId);
            Assert.NotEmpty(successor.State.LootMarkers);
            Assert.Equal("Frontier Lucky Boots", successor.State.EquipmentTypes.Single(item => item.Id == "lucky-boots").Name);
            Assert.Contains(successor.State.LootDeck!.DrawPile, card => card.LootCardId == newLootId);
            Assert.DoesNotContain(source.State.LootCardDefinitions, card => card.Id == newLootId);
            output.WriteLine($"Generated monsters {firstCheckpoint.Scenario.Units.Count - originals.Count} and {successor.Scenario.Units.Count - originals.Count}, Loot markers {firstCheckpoint.State.LootMarkers.Count} and {successor.State.LootMarkers.Count}.");
            foreach (var original in originals)
            {
                var old = source.State.Units.Single(u => u.Id == original);
                var fresh = successor.State.Units.Single(u => u.Id == original);
                Assert.Equal(old.Inventory.Select(o => o.Id), fresh.Inventory.Select(o => o.Id));
                Assert.Equal(successor.State.Types.Single(t => t.Id == fresh.TypeId).Hp, fresh.CurrentHp);
                Assert.Equal(Posture.Upright, successor.State.Physical.Figures.Single(f => f.Id == original).Posture);
                Assert.Empty(fresh.AbilityUseCounts);
                Assert.All(fresh.Inventory, obj => Assert.Empty(obj.AbilityUseCounts));
            }
            Assert.DoesNotContain(successor.State.GroundObjects, o => o.Object.Id is gearId or itemId);
            var geometry = successor.Scenario with
            {
                Units = successor.Scenario.Units.Where(u => u.SideId != ExpeditionParty.SideId).ToList(),
                Agency = successor.Scenario.Agency.Where(a => a.SideId != ExpeditionParty.SideId).ToList(),
                Machines = successor.Scenario.Machines?.Select(machine => machine.OwnerSide == ExpeditionParty.SideId
                    ? machine with { OwnerSide = null } : machine).ToList(),
                FogOfWarSides = [], Objectives = [], Party = null
            };
            var validation = PlayReadyScenarioSetup.Validate(geometry, successor.Content);
            Assert.True(validation.Valid, string.Join("; ", validation.Diagnostics.Select(d => d.Message)));
            var unoccupied = GameEngine.CreateGame(geometry, successor.Content);
            var reached = PlayReadyScenarioSetup.ReachableFromDeployment(geometry, unoccupied);
            Assert.Contains(runner.Region.EntrySeam.Y * 60 + runner.Region.EntrySeam.X, reached);
            Assert.Contains(runner.Region.ExitSeam.Y * 60 + runner.Region.ExitSeam.X, reached);
            foreach (var cell in ExitCells(runner.Region.ExitArea)) Assert.Contains(cell.Y * 60 + cell.X, reached);
            var reopened = ExpeditionRunner.Open(root, "journey");
            Assert.Equal(settings.ToString(), reopened.Head.GenerationRecipe);
            Assert.Equal(profile, reopened.Head.WorldProfile);
            Assert.Equal(runner.Head.ActiveCheckpoint, reopened.Head.ActiveCheckpoint);
            Assert.Equal(JsonSerializer.Serialize(runner.Snapshot()), JsonSerializer.Serialize(reopened.Snapshot()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // This policy sees test-owned static geography, but every submitted choice comes from the
    // Engine's current Fog-of-War-limited request, including its actual movement allowance.
    private static Candidate ChooseJourneyStep(GameState state, DecisionRequest request, ObjectiveArea exit,
        IReadOnlyDictionary<Cell, Cell[]> graph)
    {
        if (request.Kind is DecisionKind.SelectUnit or DecisionKind.RollDice) return request.Candidates[0];
        var position = state.Physical.Figures.Single(f => f.Id == request.UnitId).Position;
        var occupied = state.Units.Where(unit => unit.Id != request.UnitId)
            .SelectMany(unit => FootprintGeometry.OccupiedCells(state, unit.Id)).ToHashSet();
        var actorSide = state.Units.Single(unit => unit.Id == request.UnitId).SideId;
        // Friendly Units may be passed during a Move, but an occupied Cell
        // cannot be the destination. The Engine supplies the exact legal Moves.
        var blocking = state.Units.Where(unit => unit.SideId != actorSide)
            .SelectMany(unit => FootprintGeometry.OccupiedCells(state, unit.Id)).ToHashSet();
        var distance = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var cell in ExitCells(exit).Where(c => !occupied.Contains(c))) { distance[cell] = 0; queue.Enqueue(cell); }
        while (queue.TryDequeue(out var cell))
            foreach (var next in graph.GetValueOrDefault(cell) ?? [])
                if (!blocking.Contains(next) && distance.TryAdd(next, distance[cell] + 1)) queue.Enqueue(next);
        var best = request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move && c.Destination is not null &&
                distance.GetValueOrDefault(c.Destination, int.MaxValue) < distance.GetValueOrDefault(position, int.MaxValue))
            .OrderBy(c => distance[c.Destination!]).ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();
        if (best is not null) return best;
        var door = request.Candidates.FirstOrDefault(candidate => candidate.Door is { } edge &&
            (edge.A == position || edge.B == position) &&
            distance.GetValueOrDefault(edge.A == position ? edge.B : edge.A, int.MaxValue) < distance.GetValueOrDefault(position, int.MaxValue));
        return door ?? Pass(request);
    }

    private static Candidate Pass(DecisionRequest request) => request.Candidates.FirstOrDefault(candidate =>
        candidate.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn) ?? request.Candidates[0];

    private static Dictionary<Cell, Cell[]> OrdinaryGraph(Board board)
    {
        var analysis = AccessibilityAnalysis.Capture(board with { Edges = board.Edges.Select(edge =>
            edge.Kind == EdgeKind.ClosedDoor ? edge with { Kind = EdgeKind.OpenDoor } : edge).ToList() });
        var profile = new AccessibilityProfile(Footprint.OneByOne, [MovementKinds.Land], 1);
        var result = new Dictionary<Cell, Cell[]>();
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            if (!analysis.CanStop(profile, cell)) continue;
            result[cell] = new[] { new Cell(x - 1, y), new Cell(x + 1, y), new Cell(x, y - 1), new Cell(x, y + 1) }
                .Where(n => n.X >= 0 && n.Y >= 0 && n.X < board.Width && n.Y < board.Height && analysis.CanStep(profile, cell, n)).ToArray();
        }
        return result;
    }

    private static IEnumerable<Cell> ExitCells(ObjectiveArea area) =>
        from y in Enumerable.Range(area.Y, area.H) from x in Enumerable.Range(area.X, area.W) select new Cell(x, y);

    private static (long X, long Y) SeamWorldCell(ExpeditionRegion region, Cell seam, ExpeditionEdge? incomingFrom = null)
    {
        var x = checked(region.X * 60 + seam.X); var y = checked(region.Y * 60 + seam.Y);
        // Neighboring edge Cells border one seam; compare their shared physical boundary.
        return incomingFrom switch
        {
            ExpeditionEdge.East => (x - 1, y), ExpeditionEdge.West => (x + 1, y),
            ExpeditionEdge.South => (x, y - 1), ExpeditionEdge.North => (x, y + 1), _ => (x, y)
        };
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class SummoningRandom : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.Beastmaster) ?? bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.Cleric) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
