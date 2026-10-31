using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class GeomancerTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.Geomancer) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 7, Height = 5 },
        UnitTypeIds = [UnitTypeIds.Geomancer, UnitTypeIds.Worker, UnitTypeIds.Troll, UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Geomancer, "blue", new(2, 2), UnitId: "geo"),
            new(UnitTypeIds.Grunt, "red", new(6, 4), UnitId: "enemy")],
        Agency = [new(UnitTypeIds.Geomancer, "blue", ControllerKind.Human),
            new(UnitTypeIds.Worker, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Troll, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Human)]
    };
    private static EngineResult Start(ScenarioDefinition? definition = null) =>
        Ready(GameEngine.StartRound(GameEngine.CreateGame(definition ?? Fixture()), new Dice(), false));
    private static EngineResult Ready(EngineResult result) => result.NextInput?.Kind == DecisionKind.SelectUnit
        ? Step(result, "geo") : result;
    private static EngineResult Step(EngineResult result, string key) =>
        GameEngine.Advance(result.State, new Pick(key), new Dice(), false);
    private static EngineResult Actions(ScenarioDefinition? definition = null) => Step(Start(definition), "stay");
    private static string? Automated(EngineResult result) =>
        new DefaultAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static ScenarioDefinition Stored(int quantity = 1) => Fixture() with
    {
        StoredResources = [new("blue", UnitTypeIds.Geomancer, ResourceTypes.Diamond, quantity)]
    };

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void WorkerLimitCountsLivingCanonicalWorkersAcrossTheSide(int count, bool available)
    {
        var state = Start().State;
        for (var i = 0; i < count; i++) state.PlaceUnit(UnitTypeIds.Worker, $"w{i}", "blue", new(i, 0),
            i == 0 ? Posture.Lying : Posture.Upright);
        // Another Geomancer does not grant another allowance.
        state.PlaceUnit(UnitTypeIds.Geomancer, "geo2", "blue", new(5, 2));
        Assert.Equal(available, SummoningRules.Candidates(state, state.Units.Single(u => u.Id == "geo"))
            .Any(c => c.Action == UnitAction.SummonWorker));
        Assert.Equal(available, SummoningRules.Candidates(state, state.Units.Single(u => u.Id == "geo2"))
            .Any(c => c.Action == UnitAction.SummonWorker));
        if (count >= 3)
        {
            // A defeated record is no longer a living Worker.
            var index = state.Units.FindIndex(u => u.Id == "w0");
            state.Units[index] = state.Units[index] with { CurrentHp = 0 };
            state.Physical.Figures.RemoveAll(f => f.Id == "w0");
            Assert.Equal(count == 3, SummoningRules.Candidates(state, state.Units[0])
                .Any(c => c.Action == UnitAction.SummonWorker));
        }
    }

    [Fact]
    public void WorkerLimitsAreIndependentAcrossSidesAndDoNotCountOtherTypes()
    {
        var state = Start().State;
        state.Types.Add(UnitRoster.Worker("other-worker"));
        for (var i = 0; i < 3; i++)
        {
            state.PlaceUnit(UnitTypeIds.Worker, $"red{i}", "red", new(i, 0));
            state.PlaceUnit("other-worker", $"other{i}", "blue", new(i, 4));
        }
        state.PlaceUnit(UnitTypeIds.Geomancer, "red-geo", "red", new(5, 2));
        Assert.Contains(SummoningRules.Candidates(state, state.Units[0]), c => c.Action == UnitAction.SummonWorker);
        Assert.DoesNotContain(SummoningRules.Candidates(state, state.Units.Single(u => u.Id == "red-geo")),
            c => c.Action == UnitAction.SummonWorker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SummonsUprightOnOwnSideAndDoesNotAddCurrentRoundTokens(bool troll)
    {
        var ready = Actions(troll ? Stored() : Fixture());
        var originalBag = ready.State.Bag.ToArray();
        var key = troll ? "ability:summon-troll:1,1" : "ability:summon-worker:1,1";
        var result = Step(ready, key);
        var typeId = troll ? UnitTypeIds.Troll : UnitTypeIds.Worker;
        var created = Assert.Single(result.State.Units, u => u.TypeId == typeId);
        Assert.Equal("blue", created.SideId);
        Assert.Equal(new Figure(created.Id, new(1, 1), Posture.Upright),
            result.State.Physical.Figures.Single(f => f.Id == created.Id));
        var creation = Assert.Single(result.ResolutionSteps, s => result.Events[s.EventIndex].Kind == "UnitCreated");
        Assert.Equal(originalBag, creation.StateAfter.Bag);
        Assert.Equal(0, result.State.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "geo");
        Assert.Equal(ControllerKind.Automated, result.State.ControllerFor(new ActivationToken(typeId, "blue")));
        Assert.Equal(JsonSerializer.Serialize(result.State.Types[0]), JsonSerializer.Serialize(Restore(result.State).Types[0]));
        if (troll)
        {
            // Playback must never expose spent storage without the corresponding Troll.
            var spent = Assert.Single(result.ResolutionSteps, s => result.Events[s.EventIndex].Kind == "ResourceSpent");
            Assert.Contains(spent.StateAfter.Units, u => u.Id == created.Id);
            Assert.Equal(0, spent.StateAfter.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
        }
    }

    [Fact]
    public void SharedStorageIsConsumedByOneGeomancerAndRecheckedForTheNext()
    {
        var definition = Stored() with { Units = [Fixture().Units[0],
            new(UnitTypeIds.Geomancer, "blue", new(4, 2), UnitId: "geo2"), Fixture().Units[1]] };
        var ready = Actions(definition);
        Assert.Contains(ready.NextInput!.Candidates, c => c.Action == UnitAction.SummonTroll);
        var result = Step(ready, "ability:summon-troll:1,1");
        Assert.Equal(0, new GameplayQueries(result.State).StoredQuantityFor("geo2", ResourceTypes.Diamond));
        Assert.DoesNotContain(SummoningRules.Candidates(result.State, result.State.Units.Single(u => u.Id == "geo2")),
            c => c.Action == UnitAction.SummonTroll);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnavailableSummonNeverConsumesDiamond(int quantity)
    {
        var ready = Actions(Stored(quantity));
        var state = ready.State;
        for (var y = 1; y <= 3; y++)
            for (var x = 1; x <= 3; x++)
                if (x != 2 || y != 2) state.Physical.Board.Terrain.Add(new(new(x, y), TerrainKind.Water));
        var refreshed = GameEngine.RefreshChoices(state, new Dice(), false);
        Assert.DoesNotContain(refreshed.NextInput!.Candidates, c => c.Action is UnitAction.SummonTroll or UnitAction.SummonWorker);
        Assert.Throws<ArgumentException>(() => Step(refreshed, "ability:summon-troll:1,1"));
        Assert.Equal(quantity, refreshed.State.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
    }

    [Fact]
    public void TrollRequiresDiamondAndPlacementUsesExistingLosAndOccupancyRules()
    {
        Assert.DoesNotContain(Actions().NextInput!.Candidates, c => c.Action == UnitAction.SummonTroll);
        var ready = Actions(Stored());
        ready.State.Physical.Board.Edges.Add(new(new(2, 2), new(2, 1), EdgeKind.Wall));
        ready.State.PlaceUnit(UnitTypeIds.Worker, "blocker", "blue", new(1, 1));
        var refreshed = GameEngine.RefreshChoices(ready.State, new Dice(), false);
        Assert.DoesNotContain(refreshed.NextInput!.Candidates, c => c.Action.HasValue &&
            c.Destination is { } cell && (cell == new Cell(2, 1) || cell == new Cell(1, 1)));
        Assert.Equal("ability:summon-troll:3,1", Automated(refreshed));
    }

    [Fact]
    public void StandingOnDiamondChoosesStayThenPickupBeforeSummoningWorker()
    {
        var start = Start(Fixture() with { Resources = [new(ResourceTypes.Diamond, new(2, 2))] });
        Assert.Equal("stay", Automated(start));
        var ready = Step(start, "stay");
        Assert.Equal("ability:pickup-resource", Automated(ready));
        Assert.Contains(ready.NextInput!.Candidates, c => c.Action == UnitAction.SummonWorker);
        var result = Step(ready, "ability:pickup-resource");
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
    }

    [Fact]
    public void SeeksNearestReachableDiamondWithTopLeftTies()
    {
        var start = Start(Fixture() with { Resources = [new(ResourceTypes.Diamond, new(4, 2)),
            new(ResourceTypes.Diamond, new(2, 0)), new(ResourceTypes.Diamond, new(1, 2))] });
        Assert.Equal("1,2", Automated(start));
        start.State.Resources.RemoveAll(r => r.Position == new Cell(1, 2));
        start = GameEngine.RefreshChoices(start.State, new Dice(), false);
        Assert.Equal("2,0", Automated(start));
    }

    [Fact]
    public void DiamondRankingUsesActualPathRatherThanManhattanDistance()
    {
        var start = Start(Fixture() with
        {
            Board = new() { Width = 7, Height = 5, Edges = [new(new(2, 2), EdgeDirection.Right, EdgeKind.Wall)] },
            Resources = [new(ResourceTypes.Diamond, new(3, 2)), new(ResourceTypes.Diamond, new(2, 4))]
        });
        Assert.Equal("2,4", Automated(start));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FleeFallbackWhenDiamondIsBeyondMoveOrStorageIsOccupied(bool occupied)
    {
        var definition = (occupied ? Stored() : Fixture()) with
        { Resources = [new(ResourceTypes.Diamond, occupied ? new(3, 2) : new(5, 2))] };
        var start = Start(definition);
        var fleeOnly = start.State.Copy();
        fleeOnly.Types[0] = fleeOnly.Types[0] with { Behaviors = UnitBehavior.Flee };
        Assert.Equal(new DefaultAutomatedProvider().Choose(start.NextInput!, new GameplayQueries(fleeOnly)), Automated(start));
    }

    [Fact]
    public void ActionPriorityAndHumanChoicesAreIndependent()
    {
        var ready = Actions(Stored() with { Resources = [new(ResourceTypes.Diamond, new(2, 2))] });
        Assert.Equal("ability:summon-troll:1,1", Automated(ready));
        Assert.Contains(ready.NextInput!.Candidates, c => c.Action == UnitAction.SummonWorker);
        // Human can choose Worker while Troll is preferred, leaving the Diamond unspent.
        Assert.Equal(1, Step(ready, "ability:summon-worker:1,1").State.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
        ready = Actions();
        Assert.Equal("ability:summon-worker:1,1", Automated(ready));
        for (var i = 0; i < 3; i++) ready.State.PlaceUnit(UnitTypeIds.Worker, $"worker{i}", "blue", new(i, 0));
        var request = ready.NextInput! with { Kind = DecisionKind.Act, Candidates = [], AllowsNone = true };
        Assert.Null(new DefaultAutomatedProvider().Choose(request, new GameplayQueries(ready.State)));
    }

    [Fact]
    public void PreferencesCanBeUsedByAnotherUnitType()
    {
        var definition = Fixture() with
        {
            UnitTypeIds = ["artisan", UnitTypeIds.Grunt],
            UnitTypes = [UnitRoster.Geomancer("artisan")],
            Units = [Fixture().Units[0] with { UnitTypeId = "artisan" }, Fixture().Units[1]],
            Agency = [new("artisan", "blue", ControllerKind.Human), Fixture().Agency[3]],
            Resources = [new(ResourceTypes.Diamond, new(2, 2))]
        };
        Assert.Equal("stay", Automated(Start(definition)));
        Assert.Equal("ability:pickup-resource", Automated(Actions(definition)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlySummonedWorkerUsesOnlyAnExistingUnresolvedMatchingToken(bool resolved)
    {
        var definition = Fixture() with { Units = [Fixture().Units[0],
            new(UnitTypeIds.Worker, "blue", new(0, 0), UnitId: "initial-worker"), Fixture().Units[1]] };
        var ready = Actions(definition);
        if (resolved) ready.State.Bag.RemoveAll(t => t == new ActivationToken(UnitTypeIds.Worker, "blue"));
        var created = Step(ready, "ability:summon-worker:1,1");
        List<RulesEvent> events = [.. created.Events];
        var result = created;
        for (var i = 0; i < 80 && !result.State.RoundComplete; i++)
        {
            result = GameEngine.Advance(result.State, new DefaultAutomatedProvider(), new Dice(), false);
            events.AddRange(result.Events);
        }
        Assert.True(result.State.RoundComplete);
        var id = Assert.Single(created.Events, e => e.Kind == "UnitCreated").UnitId;
        Assert.Equal(!resolved, events.Any(e => e.Kind == "ActivationCompleted" && e.UnitId == id));
        var next = GameEngine.StartRound(Restore(result.State), new Dice(), false);
        Assert.Contains(next.State.Bag, t => t == new ActivationToken(UnitTypeIds.Worker, "blue"));
    }

    [Fact]
    public void GroupedGeomancersCompleteIndividuallyAndReevaluateWorkerCap()
    {
        var definition = Fixture() with
        {
            Units = [Fixture().Units[0], new(UnitTypeIds.Geomancer, "blue", new(4, 2), UnitId: "geo2"),
                new(UnitTypeIds.Worker, "blue", new(0, 0), UnitId: "w1"),
                new(UnitTypeIds.Worker, "blue", new(1, 0), UnitId: "w2"), Fixture().Units[1]]
        };
        var result = Start(definition);
        List<RulesEvent> events = [];
        for (var i = 0; i < 30 && !events.Any(e => e.Kind == "ActivationCompleted" && e.UnitId == "geo2"); i++)
        {
            result = GameEngine.Advance(result.State, new DefaultAutomatedProvider(), new Dice(), false);
            events.AddRange(result.Events);
        }
        Assert.Single(events, e => e.Kind == "UnitCreated" && e.TypeId == UnitTypeIds.Worker);
        Assert.Equal(3, result.State.Units.Count(u => u.SideId == "blue" && u.TypeId == UnitTypeIds.Worker));
        Assert.True(events.FindIndex(e => e.Kind == "ActivationCompleted" && e.UnitId == "geo") <
            events.FindIndex(e => e.Kind == "ActivationStarted" && e.UnitId == "geo2"));
    }

    [Fact]
    public void WorkerProductionAndGeomancerSummoningFormAnAutonomousChain()
    {
        var definition = Fixture() with
        {
            Board = new() { Width = 8, Height = 7 },
            Units = [Fixture().Units[0] with { Anchor = new(0, 0) },
                new(UnitTypeIds.Worker, "blue", new(2, 3), UnitId: "worker"),
                Fixture().Units[1] with { Anchor = new(7, 6) }],
            Agency = Fixture().Agency.Select(a => a with { Controller = ControllerKind.Automated }).ToList(),
            Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.DoNothing)],
            Machines = [new("press", new(3, 3), MachineTypes.DiamondPress)],
            Resources = [new(ResourceTypes.Coal, new(2, 3)), new(ResourceTypes.Coal, new(3, 2)),
                new(ResourceTypes.Coal, new(3, 4)), new(ResourceTypes.Coal, new(4, 3)),
                new(ResourceTypes.Coal, new(2, 5)), new(ResourceTypes.Coal, new(4, 5))]
        };
        var state = GameEngine.CreateGame(JsonSerializer.Deserialize<ScenarioDefinition>(JsonSerializer.Serialize(definition))!);
        List<RulesEvent> events = [];
        for (var round = 0; round < 30 && !events.Any(e => e.Kind == "UnitCreated" && e.TypeId == UnitTypeIds.Troll); round++)
        {
            var result = GameEngine.StartRound(state, new Dice(), false);
            for (var decision = 0; decision < 200 && !result.State.RoundComplete; decision++)
            {
                result = GameEngine.Advance(result.State, new OrderAwareAutomatedProvider(), new Dice(), false);
                events.AddRange(result.Events);
            }
            Assert.True(result.State.RoundComplete);
            state = Restore(result.State);
        }
        Assert.Equal(3, state.Units.Count(u => u.TypeId == UnitTypeIds.Worker && u.SideId == "blue"));
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.MachineId == "press");
        Assert.Contains(events, e => e.Kind == "MachineOperated" && e.ResourceType == ResourceTypes.Diamond);
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.ResourceType == ResourceTypes.Diamond && e.TypeId == UnitTypeIds.Geomancer);
        Assert.Contains(events, e => e.Kind == "UnitCreated" && e.TypeId == UnitTypeIds.Troll);
    }
}
