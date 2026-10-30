using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DiamondKartTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture(bool diamond = true) => new()
    {
        Board = new() { Width = 7, Height = 5, DefaultTerrain = TerrainKind.Asphalt,
            Cells = [new(new(2, 1), TerrainKind.Grass), new(new(4, 2), TerrainKind.Water)] },
        UnitTypeIds = [UnitTypeIds.DiamondKart, UnitTypeIds.Worker],
        Units = [new(UnitTypeIds.DiamondKart, "blue", new(2, 2), UnitId: "kart"),
            new(UnitTypeIds.Worker, "blue", new(1, 3), UnitId: "worker")],
        Agency = [new(UnitTypeIds.DiamondKart, "blue", ControllerKind.Human), new(UnitTypeIds.Worker, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        Resources = [new(ResourceTypes.Coal, new(3, 1)), new(ResourceTypes.Banana, new(1, 3))],
        StoredResources = diamond ? [new("blue", UnitTypeIds.DiamondKart, ResourceTypes.Banana, 1)] : []
    };
    private static EngineResult Start(ScenarioDefinition definition) => GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
    private static EngineResult Step(EngineResult result, string key) => GameEngine.Advance(result.State, new Pick(key), new Dice(), false);
    private static string? Choose(EngineResult result) => new OrderAwareAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));

    [Fact]
    public void CanonicalContentAndReusableTerrainRestriction()
    {
        var type = CanonicalUnitTypes.Find(UnitTypeIds.DiamondKart)!;
        Assert.Equal((5, 0, 0, 3, 4), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.True(type.Unique);
        Assert.Equal(UnitAction.ShootShell, type.Actions);
        Assert.Equal(1, Assert.Single(type.BonusActions, a => a.PlaceBananaPeel is not null).PlaceBananaPeel!.BananaCost);
        Assert.Equal(TerrainCategories.RacingGround, Assert.Single(type.TerrainRestriction!.Categories));
        Assert.Null(type.Undying);
        Assert.Contains(type.CardEntries(), e => e.Id == "bonus:place-banana-peel" && e.Description.Contains("1 Banana"));
        var result = Start(Fixture());
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move && c.Destination == new Cell(2, 1));
        Assert.Contains(result.NextInput.Candidates, c => c.Destination == new Cell(3, 2));
        Assert.Null(new GameplayQueries(result.State).RouteDistanceToArea("kart", new(2, 2), new(2, 1, 1, 1)));
        var generic = UnitRoster.Worker("runner") with { TerrainRestrictedMovement = new(TerrainKind.Asphalt) };
        var genericState = GameEngine.CreateGame(FollowRouteTests.Fixture(type: generic));
        Assert.NotNull(new GameplayQueries(genericState).RouteDistanceToArea("a", new(0, 1), new(7, 1, 1, 1)));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Fixture() with
            { Units = [new(UnitTypeIds.DiamondKart, "blue", new(2, 1))] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Fixture() with
            { Units = [new(UnitTypeIds.DiamondKart, "blue", new(2, 2)), new(UnitTypeIds.DiamondKart, "blue", new(3, 2))] }));
    }

    [Fact]
    public void HumanCanPlaceOffAsphaltOnResourcesAndSpendAtomically()
    {
        var definition = Fixture() with
        {
            Machines = [new("press", new(3, 3), MachineTypes.DiamondPress)],
            CellMarkers = [new(CellMarkerTypes.BananaPeel, new(1, 1))]
        };
        var result = Step(Start(definition), "stay");
        var cells = result.NextInput!.Candidates.Where(c => c.Action == UnitAction.PlaceBananaPeel).Select(c => c.Destination).ToArray();
        Assert.Contains(new Cell(2, 1), cells); // Grass is a human choice.
        Assert.Contains(new Cell(3, 1), cells); // Existing loose Coal remains.
        Assert.DoesNotContain(new Cell(1, 3), cells); // Unit
        Assert.DoesNotContain(new Cell(3, 3), cells); // Machine
        Assert.DoesNotContain(new Cell(1, 1), cells); // Marker
        var invalid = Assert.Throws<ArgumentException>(() => Step(result, "place-banana-peel:3,3"));
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, ResourceTypes.Banana.Id));
        Assert.Single(result.State.CellMarkers);
        var human = Step(result, "place-banana-peel:2,1");
        Assert.Equal(0, human.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, ResourceTypes.Banana.Id));
        Assert.Contains(human.State.CellMarkers, m => m.Position == new Cell(2, 1));
        Assert.Contains(human.Events, e => e.Kind == "ResourceSpent" && e.ActionId == "place-banana-peel");
        foreach (var step in human.ResolutionSteps.SkipWhile(s => human.Events[s.EventIndex].Kind == "ActionUsed"))
        {
            Assert.Equal(0, step.StateAfter.StoredQuantity("blue", UnitTypeIds.DiamondKart, ResourceTypes.Banana.Id));
            Assert.Contains(step.StateAfter.CellMarkers, m => m.Position == new Cell(2, 1));
        }
        var onResource = Step(result, "place-banana-peel:3,1");
        Assert.Contains(onResource.State.Resources, r => r.Position == new Cell(3, 1));
        var empty = Step(Start(Fixture(false)), "stay");
        Assert.DoesNotContain(empty.NextInput!.Candidates, c => c.Action == UnitAction.PlaceBananaPeel);
    }

    [Fact]
    public void AdjacentTargetingUsesGeometricLosAndPassability()
    {
        var definition = Fixture() with { Board = Fixture().Board with
            { Edges = [new(new(2, 2), EdgeDirection.Right, EdgeKind.Wall)], Cells = [new(new(2, 1), TerrainKind.Water)] } };
        var result = Step(Start(definition), "stay");
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Action == UnitAction.PlaceBananaPeel &&
            (c.Destination == new Cell(3, 2) || c.Destination == new Cell(2, 1)));
    }

    [Fact]
    public void AutomatedPreferenceUsesAsphaltThenFarthestNextRouteAreaAndHumanKeepsChoices()
    {
        var definition = Fixture() with { Orders = [new(UnitTypeIds.DiamondKart, "blue", OrderKind.FollowRoute,
            Route: new("test-route"))], Waypoints = [new("east", new(6, 0, 1, 5)), new("west", new(0, 0, 1, 5))], Routes = [new("test-route", ["east", "west"], RouteMode.Loop)] };
        var result = Step(Start(definition), "stay");
        Assert.Equal("place-banana-peel:3,1", Choose(result)); // Grass blocks the direct west route; this target is farthest.
        Assert.Contains(result.NextInput!.Candidates, c => c.Key == "place-banana-peel:2,1");
        var noRoute = Step(Start(Fixture()), "stay");
        Assert.Equal("place-banana-peel:1,1", Choose(noRoute));
        var idle = Step(Start(definition with { Orders = [new(UnitTypeIds.DiamondKart, "blue", OrderKind.DoNothing)] }), "stay");
        Assert.Equal("end-turn", Choose(idle));
    }

    [Fact]
    public void WorkerDeliversBananaUsingSharedStorageWithoutKartPickupAction()
    {
        var result = Step(Start(Fixture(false)), "stay");
        if (result.NextInput!.UnitId == "kart") result = Step(result, "end");
        if (result.NextInput!.Kind == DecisionKind.SelectUnit) result = Step(result, "worker");
        result = Step(result, "stay");
        var give = Assert.Single(result.NextInput!.Candidates, c => c.Action == UnitAction.GiveResource && c.TargetId == "kart");
        Assert.Equal(give.Key, Choose(result));
        result = Step(result, give.Key);
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.DiamondKart, ResourceTypes.Banana.Id));
        Assert.DoesNotContain(result.State.Resources, r => r.Type == ResourceTypes.Banana);
    }

    [Fact]
    public void ForcedRelocationCanLeaveAsphaltWithoutCompletingTheRoute()
    {
        var definition = Fixture(false) with
        {
            UnitTypeIds = [UnitTypeIds.DisplacerDemon, UnitTypeIds.DiamondKart],
            Units = [new(UnitTypeIds.DisplacerDemon, "red", new(1, 2), UnitId: "demon"),
                new(UnitTypeIds.DiamondKart, "blue", new(2, 2), UnitId: "kart")],
            Agency = [new(UnitTypeIds.DisplacerDemon, "red", ControllerKind.Human),
                new(UnitTypeIds.DiamondKart, "blue", ControllerKind.Human)],
            Objectives = [new("red", ScenarioObjective.NoObjective), new("blue", ScenarioObjective.NoObjective)],
            Orders = [new(UnitTypeIds.DiamondKart, "blue", OrderKind.FollowRoute, Route: new("test-route"))],
            Waypoints = [new("grass", new(2, 1, 1, 1))], Routes = [new("test-route", ["grass"])]
        };
        var result = Start(definition);
        var displace = Assert.Single(result.NextInput!.Candidates, c => c.BonusAction?.Displace is not null && c.Destination == new Cell(2, 1));
        result = Step(result, displace.Key);
        Assert.Equal(new Cell(2, 1), result.State.Physical.Figures.Single(f => f.Id == "kart").Position);
        Assert.Single(result.State.Orders);
        Assert.DoesNotContain(result.Events, e => e.Kind is "RouteCompleted" or "RouteAdvanced");
    }

    [Fact]
    public void BananaActionAndPreferenceAreReusableWithAnAuthoredCost()
    {
        var type = UnitType.Define("thrower", "Thrower", UnitAuthoring.Stats(1, 0, 0, 1, 1),
            UnitAuthoring.CantAttack(), UnitAuthoring.ResourceStorage(ResourceTypes.Banana, 2),
            UnitAuthoring.PlaceBananaPeel(bananaCost: 2)).WithBehaviors(UnitBehavior.PreferBananaPeel);
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 3, Height = 3 }, UnitTypeIds = [type.Id], UnitTypes = [type],
            Units = [new(type.Id, "blue", new(1, 1), UnitId: "thrower")],
            Agency = [new(type.Id, "blue", ControllerKind.Human)], Objectives = [new("blue", ScenarioObjective.NoObjective)],
            StoredResources = [new("blue", type.Id, ResourceTypes.Banana, 2)]
        };
        var result = Step(Start(definition), "stay");
        Assert.Equal("place-banana-peel:0,0", Choose(result)); // No Asphalt and no route: canonical Cell order.
        result = Step(result, Choose(result)!);
        Assert.Equal(0, result.State.StoredResources.Single().Quantity);
        Assert.Single(result.State.CellMarkers);
    }
}
