using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RacingIterationTests
{
    private sealed class Dice(int d6 = 4, int d8 = 3) : IRandomProvider
    {
        public int Slots { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() { Slots++; return d6; }
        public int RollD8() => d8;
    }
    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 9, Height = 5, DefaultTerrain = TerrainKind.Asphalt },
        UnitTypeIds = [UnitTypeIds.DiamondKart, "target"],
        UnitTypes = [UnitRoster.Grunt("target") with { Def = 0 }],
        Units = [new(UnitTypeIds.DiamondKart, "blue", new(1, 2), UnitId: "kart"), new("target", "red", new(4, 2), UnitId: "target")],
        Agency = [new(UnitTypeIds.DiamondKart, "blue", ControllerKind.Human), new("target", "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Waypoints = [new("shared", new(0, 0, 9, 5))],
        Routes = [new("lap", ["shared", "shared"], RouteMode.Loop)],
        Orders = [new(UnitTypeIds.DiamondKart, "blue", OrderKind.FollowRoute, Route: new("lap"))],
        StoredResources = [new("blue", UnitTypeIds.DiamondKart, ResourceTypes.Banana, 1),
            new("blue", UnitTypeIds.DiamondKart, ResourceTypes.Mushroom, 1), new("blue", UnitTypeIds.DiamondKart, ResourceTypes.Shell, 1)]
    };
    private static EngineResult Start(ScenarioDefinition? definition = null, Dice? dice = null) =>
        GameEngine.StartRound(GameEngine.CreateGame(definition ?? Fixture()), dice ?? new(), false);
    private static EngineResult Step(EngineResult result, string key, Dice? dice = null) =>
        GameEngine.Advance(result.State, new Pick(key), dice ?? new(), false);

    [Fact]
    public void BoostBeforeMoveSpendsOnceAndRequiresBothMovesBeforeActionsOrEnd()
    {
        var start = Start();
        Assert.Equal("ability:boost", new OrderAwareAutomatedProvider().Choose(start.NextInput!, new GameplayQueries(start.State)));
        var boosted = Step(start, "ability:boost");
        Assert.Equal(0, boosted.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "mushroom"));
        Assert.Equal(2, boosted.State.EffectiveMovesPerActivation);
        var spent = boosted.Events.FindIndex(e => e.Kind == "ResourceSpent");
        Assert.Equal(2, boosted.ResolutionSteps.Single(s => s.EventIndex == spent).StateAfter.EffectiveMovesPerActivation);
        Assert.DoesNotContain(boosted.NextInput!.Candidates, c => c.Kind is ActivationChoiceKind.EndTurn or ActivationChoiceKind.Action);
        var moved = Step(boosted, "stay");
        Assert.Equal(1, moved.State.MovesCompletedThisActivation);
        Assert.False(moved.State.MoveDone);
        Assert.DoesNotContain(moved.NextInput!.Candidates, c => c.Kind is ActivationChoiceKind.EndTurn or ActivationChoiceKind.Action);
        // Saving while only one of two Moves is complete must retain that count.
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(moved.State))!;
        Assert.Equal(1, restored.MovesCompletedThisActivation);
        moved = Step(moved, "stay");
        Assert.True(moved.State.MoveDone);
        Assert.Equal(0, moved.State.Orders.Single().Route!.CurrentIndex);
        Assert.Contains(moved.NextInput!.Candidates, c => c.Action == UnitAction.ShootShell);
        Assert.Contains(moved.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        Assert.DoesNotContain(moved.NextInput.Candidates, c => c.Ability?.Definition.Mechanic is Boost);
    }

    [Fact]
    public void BoostReopensMovesAfterStayButNotAfterNormalAction()
    {
        var moved = Step(Start(), "stay");
        var boosted = Step(moved, "ability:boost");
        Assert.False(boosted.State.MoveDone);
        Assert.Contains(boosted.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
        Assert.DoesNotContain(boosted.NextInput.Candidates, c => c.Kind is ActivationChoiceKind.EndTurn or ActivationChoiceKind.Action);
        var shot = Step(moved, "ability:shoot-shell:target");
        Assert.Equal(0, shot.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "shell"));
        Assert.Equal(1, shot.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "mushroom"));
        while (shot.NextInput?.Kind == DecisionKind.RollDice) shot = Step(shot, "roll-dice");
        Assert.DoesNotContain(shot.NextInput!.Candidates, c => c.Ability?.Definition.Mechanic is Boost);
        Assert.Contains(shot.NextInput.Candidates, c => c.Ability?.Definition.Mechanic is PlaceBananaPeel);
        Assert.Contains(shot.Events, e => e.Kind == "UnitDefeated");
    }

    [Fact]
    public void BananaIsFlexibleBonusAndAutomationWaitsForAllMoves()
    {
        var initial = Start();
        Assert.Contains(initial.NextInput!.Candidates, c => c.Ability?.Definition.Mechanic is PlaceBananaPeel);
        var placed = Step(initial, "ability:place-banana-peel:0,1");
        Assert.Equal(0, placed.State.ActionsUsedThisActivation);
        Assert.False(placed.State.MoveDone);
        Assert.Single(placed.State.CellMarkers);
        Assert.Equal(0, placed.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "banana"));
        var boosted = Step(Step(initial, "stay"), "ability:boost");
        var provider = new OrderAwareAutomatedProvider();
        var key = provider.Choose(boosted.NextInput!, new GameplayQueries(boosted.State));
        Assert.Contains(boosted.NextInput!.Candidates, c => c.Key == key && c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay);
        var moved = Step(boosted, "stay");
        key = provider.Choose(moved.NextInput!, new GameplayQueries(moved.State));
        Assert.Contains(moved.NextInput!.Candidates, c => c.Key == key && c.Ability?.Definition.Mechanic is PlaceBananaPeel);
    }

    [Fact]
    public void ShellUsesItsOwnRangeAndDiceDespiteCantAttackAndKeepsResourceWithoutTarget()
    {
        var moved = Step(Start(), "stay");
        Assert.DoesNotContain(moved.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        var shot = Step(moved, "ability:shoot-shell:target");
        Assert.Equal(3, shot.State.AttackInProgress!.AttackDice);
        Assert.Equal("ability:shoot-shell", shot.State.AttackInProgress.ActionId);
        shot = Step(shot, "roll-dice");
        if (shot.NextInput?.Kind == DecisionKind.RollDice) shot = Step(shot, "roll-dice");
        Assert.Equal(0, shot.State.Units.Single(u => u.Id == "target").CurrentHp);
        var far = Fixture() with { Units = [Fixture().Units[0], new("target", "red", new(8, 4), UnitId: "target")] };
        var noTarget = Step(Start(far), "stay");
        Assert.DoesNotContain(noTarget.NextInput!.Candidates, c => c.Action == UnitAction.ShootShell);
        Assert.Equal(1, noTarget.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "shell"));
        var blocked = Fixture() with { Board = Fixture().Board with { Cells = [new(new(2, 2), TerrainKind.FruitTree)] } };
        Assert.DoesNotContain(Step(Start(blocked), "stay").NextInput!.Candidates, c => c.Action == UnitAction.ShootShell);
    }

    [Theory]
    [InlineData(1, "banana")][InlineData(2, "banana")][InlineData(3, "banana")]
    [InlineData(4, "mushroom")][InlineData(5, "mushroom")][InlineData(6, "shell")]
    public void SharedSlotTableAndRewardAtEndOnlyOnce(int roll, string resource)
    {
        Assert.Equal(resource, RacingSlotTable.Table.Result(roll).Id);
        var dice = new Dice(roll);
        var kart = UnitRoster.DiamondKart();
        var fixture = Fixture() with { StoredResources = [new("blue", UnitTypeIds.DiamondKart, ResourceTypes.Mushroom, 1)],
            UnitTypes = [.. Fixture().UnitTypes!, kart.WithLocal(new("hold", "Hold", AbilityTiming.BonusAction,
                new SelfModifiersMechanic([new(Stat.Atk, 0)])))] };
        var result = Step(Start(fixture, dice), "ability:boost", dice);
        result = Step(result, "stay", dice);
        result = Step(result, "stay", dice);
        Assert.Equal(0, dice.Slots);
        Assert.Equal(0, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, resource));
        result = Step(result, "end-turn", dice);
        Assert.Equal(1, dice.Slots);
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, resource));
        Assert.Single(result.Events, e => e.Kind == "RacingSlotRolled");
    }

    [Fact]
    public void LocalWaypointRewardUsesItsAuthoredTableAndKeyAfterJsonRoundTrip()
    {
        var kart = UnitRoster.DiamondKart().Without<WaypointSlotMachine>().WithLocal(
            new("prize", "Finish Prize", AbilityTiming.EndActivation,
                new WaypointSlotMachine(new([ResourceTypes.Shell, ResourceTypes.Shell, ResourceTypes.Shell,
                    ResourceTypes.Shell, ResourceTypes.Shell, ResourceTypes.Shell]))));
        var definition = Fixture() with
        {
            UnitTypes = [.. Fixture().UnitTypes!, kart], StoredResources = []
        };
        var restored = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition));
        var dice = new Dice(1);
        var result = Step(Start(restored, dice), "stay", dice);
        Assert.Equal(1, dice.Slots);
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "shell"));
        Assert.Equal(0, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "banana"));
        Assert.Equal("ability:prize", Assert.Single(result.Events, e => e.Kind == "RacingSlotRolled").ActionId);
        Assert.Contains("Shell", kart.Ability<WaypointSlotMachine>()!.CardEntry().Description);
    }

    [Fact]
    public void FullRewardStorageDiscardsWithoutRerollAndNoOwnAdvanceGivesNoRoll()
    {
        var dice = new Dice(1);
        var moved = Step(Start(dice: dice), "stay", dice);
        var ended = Step(moved, "end-turn", dice);
        Assert.Equal(1, dice.Slots);
        Assert.Equal(1, ended.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "banana"));
        Assert.Contains(ended.Events, e => e.Kind == "RacingSlotRolled" && e.Succeeded == false);
        dice = new Dice();
        moved = Step(Start(Fixture() with { Orders = [] }, dice), "stay", dice);
        ended = Step(moved, "end-turn", dice);
        Assert.Equal(0, dice.Slots);
    }

    [Fact]
    public void SharedRouteGroupsAdvanceIndependentlyAndRepeatedWaypointReferencesSurviveTransport()
    {
        var fixture = Fixture() with {
            UnitTypeIds = [UnitTypeIds.DiamondKart], UnitTypes = [],
            Units = [Fixture().Units[0], new(UnitTypeIds.DiamondKart, "red", new(5, 2), UnitId: "other")],
            Agency = [new(UnitTypeIds.DiamondKart, "blue", ControllerKind.Human), new(UnitTypeIds.DiamondKart, "red", ControllerKind.Human)],
            Orders = [.. Fixture().Orders!, new(UnitTypeIds.DiamondKart, "red", OrderKind.FollowRoute, Route: new("lap"))]
        };
        fixture = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(fixture));
        var moved = Step(Start(fixture), "stay");
        Assert.Equal(1, moved.State.OrderFor(new(UnitTypeIds.DiamondKart, "blue")).Route!.CurrentIndex);
        Assert.Equal(0, moved.State.OrderFor(new(UnitTypeIds.DiamondKart, "red")).Route!.CurrentIndex);
        Assert.Equal(new[] { "shared", "shared" }, moved.State.Routes.Single().WaypointIds);
        var reached = Assert.Single(moved.Events, e => e.Kind == "WaypointReached");
        Assert.Equal(("shared", "lap", "kart"), (reached.WaypointId, reached.RouteId, reached.UnitId));
        foreach (var invalid in new[] {
            fixture with { Waypoints = [.. fixture.Waypoints!, fixture.Waypoints![0]] },
            fixture with { Routes = [.. fixture.Routes!, fixture.Routes![0]] },
            fixture with { Routes = [new("lap", ["missing"])] },
            fixture with { Waypoints = [] },
            fixture with { Routes = [] },
            fixture with { Routes = [new("lap", [])] } })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(invalid));
    }

    [Fact]
    public void MoveModifiersCanSubtractToZeroAndSurviveDetachedCopies()
    {
        var result = Start();
        result.State.MoveOpportunityModifierThisActivation = -3;
        result = GameEngine.RefreshChoices(result.State, new Dice(), false);
        Assert.Equal(0, result.State.EffectiveMovesPerActivation);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay);
        Assert.Contains(result.NextInput.Candidates, c => c.Action == UnitAction.ShootShell);
    }

    [Fact]
    public void ShellAutomationUsesNearestThenBoardOrderAndHumanRetainsEveryTarget()
    {
        var fixture = Fixture() with {
            Units = [Fixture().Units[0], new("target", "red", new(1, 4), UnitId: "south"),
                new("target", "red", new(3, 2), UnitId: "east"), new("target", "red", new(2, 0), UnitId: "far")],
            StoredResources = [new("blue", UnitTypeIds.DiamondKart, ResourceTypes.Shell, 1)]
        };
        var result = Step(Start(fixture), "stay");
        Assert.Equal(3, result.NextInput!.Candidates.Count(c => c.Action == UnitAction.ShootShell));
        Assert.Equal("ability:shoot-shell:east", new OrderAwareAutomatedProvider().Choose(result.NextInput, new GameplayQueries(result.State)));
    }

    [Theory]
    [InlineData("banana")][InlineData("mushroom")][InlineData("shell")]
    public void WorkersTransportAndGiveEveryRacingItemUsingExistingBehavior(string id)
    {
        var resource = RacingSlotTable.Table.Faces.First(t => t.Id == id);
        var fixture = new ScenarioDefinition {
            Board = new() { Width = 5, Height = 3, DefaultTerrain = TerrainKind.Asphalt },
            UnitTypeIds = [UnitTypeIds.Worker, UnitTypeIds.DiamondKart],
            Units = [new(UnitTypeIds.Worker, "blue", new(0, 1), UnitId: "worker"), new(UnitTypeIds.DiamondKart, "blue", new(3, 1), UnitId: "kart")],
            Agency = [new(UnitTypeIds.Worker, "blue", ControllerKind.Human), new(UnitTypeIds.DiamondKart, "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Resources = [new(resource, new(0, 1))]
        };
        var result = Step(Start(fixture), "2,1");
        Assert.Equal(new Cell(2, 1), Assert.Single(result.State.Resources).Position);
        var key = new OrderAwareAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));
        Assert.Equal("ability:give-resource:kart", key);
        result = Step(result, key!);
        Assert.Empty(result.State.Resources);
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, id));
    }

    [Fact]
    public void SlipCancelsBoostedRemainingMovesButStillAwardsOwnWaypointAtEnd()
    {
        var dice = new Dice(4);
        var result = Start(Fixture() with { CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 2))] }, dice);
        result = Step(result, "ability:boost", dice);
        result = Step(result, "2,2", dice);
        Assert.False(result.State.IsUpright("kart"));
        Assert.Single(result.Events, e => e.Kind == "WaypointReached");
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "kart");
        Assert.Equal(1, dice.Slots);
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, "mushroom"));
    }

    [Fact]
    public void FruitGenerationExemptsOnlyItsSourceTerrainAndStillRespectsWalls()
    {
        var fixture = Fixture() with { Orders = [], Board = Fixture().Board with {
            Cells = [new(new(3, 0), TerrainKind.FruitTree)], Edges = [new(new(3, 0), EdgeDirection.Right, EdgeKind.Wall)] } };
        var result = Start(fixture, new Dice(d8: 3));
        Assert.DoesNotContain(result.State.Resources, r => r.Type == ResourceTypes.Fruit);
        Assert.False(AttackRules.HasGeometricLineOfSight(result.State.Physical.Board, new(3, 0), new(4, 0)));
    }

    [Fact]
    public void FruitAndLavaUseIdenticalGenerationAttemptsAndEligibility()
    {
        var fixture = Fixture() with { Orders = [], StoredResources = [], Board = Fixture().Board with {
            Cells = [new(new(3, 0), TerrainKind.FruitTree), new(new(3, 4), TerrainKind.Lava)] } };
        var result = Start(fixture, new Dice(d8: 3));
        Assert.Equal(2, result.Events.Count(e => e.Kind == "FruitTreeRolled"));
        Assert.Equal(2, result.Events.Count(e => e.Kind == "LavaCoalRolled"));
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(4, 0) && r.Type == ResourceTypes.Fruit);
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(4, 4) && r.Type == ResourceTypes.Coal);
        Assert.Equal(2, result.State.Resources.Count); // The repeated direction cannot stack Resources.
        Assert.False(TerrainKind.FruitTree.Passable()); Assert.True(TerrainKind.FruitTree.BlocksLos());
    }

    [Theory]
    [InlineData(1, "banana")][InlineData(4, "mushroom")][InlineData(6, "shell")]
    public void SlotMachineUsesSharedTableAndAtomicOwnershipOutputRules(int roll, string output)
    {
        var fixture = new ScenarioDefinition {
            Board = new() { Width = 4, Height = 3 }, UnitTypeIds = [UnitTypeIds.Worker],
            Units = [new(UnitTypeIds.Worker, "blue", new(1, 1), UnitId: "worker")],
            Agency = [new(UnitTypeIds.Worker, "blue", ControllerKind.Human)], Objectives = [new("blue", ScenarioObjective.NoObjective)],
            Machines = [new("slot", new(2, 1), MachineTypes.SlotMachine) { OwnerSide = "blue", StoredResources = [new(ResourceTypes.Fruit, 3)] }]
        };
        fixture = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(fixture));
        var dice = new Dice(roll);
        var result = Step(Step(Start(fixture, dice), "stay", dice), "ability:operate:slot", dice);
        Assert.Equal(output, Assert.Single(result.State.Resources).Type.Id);
        Assert.Equal(new Cell(1, 1), result.State.Resources[0].Position);
        Assert.Equal(0, result.State.Physical.Board.Machines.Single().StoredQuantity("fruit"));
        Assert.Equal(1, dice.Slots);
        Assert.Contains(result.Events, e => e.Kind == "MachineOperated" && e.DieRoll == roll);
        var occupied = Step(Start(fixture with { Resources = [new(ResourceTypes.Coal, new(1, 1))] }), "stay");
        Assert.DoesNotContain(occupied.NextInput?.Candidates ?? [], c => c.Action == UnitAction.Operate);
        Assert.Equal(3, occupied.State.Physical.Board.Machines.Single().StoredQuantity("fruit"));
        var other = Step(Start(fixture with { NeutralSides = ["red"], Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Agency = [.. fixture.Agency, new(UnitTypeIds.Worker, "red", ControllerKind.Human)],
            Machines = [fixture.Machines![0] with { OwnerSide = "red" }] }), "stay");
        Assert.DoesNotContain(other.NextInput?.Candidates ?? [], c => c.Action == UnitAction.Operate);
    }
}
