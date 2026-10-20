using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BananaPeelPlanningTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }

    private static ScenarioDefinition Corridor(int movement = 4, bool peel = true) => new()
    {
        Board = new() { Width = 1, Height = 8 },
        UnitTypeIds = ["porter", UnitTypeIds.Grunt],
        UnitTypes = [UnitRoster.Worker("porter") with { Mov = movement }],
        Units = [new("porter", "blue", new(0, 0), UnitId: "actor"),
            new(UnitTypeIds.Grunt, "red", new(0, 7), UnitId: "enemy")],
        Agency = [new("porter", "blue", ControllerKind.Automated),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Resources = [new(ResourceTypes.Coal, new(0, 4))],
        Machines = [new("press", new(0, 5), MachineTypes.DiamondPress)],
        CellMarkers = peel ? [new(CellMarkerTypes.BananaPeel, new(0, 2))] : []
    };

    private static EngineResult Start(ScenarioDefinition definition) =>
        GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);

    private static Candidate SelectedMove(EngineResult result)
    {
        var key = new OrderAwareAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));
        return result.NextInput!.Candidates.Single(c => c.Key == key);
    }

    [Fact]
    public void WorkerRecognizesCoalBeyondPeelAndLegallyArrivesOnPeelAndSlips()
    {
        var start = Start(Corridor());
        Assert.Equal(new Cell(0, 2), SelectedMove(start).Destination);
        Assert.DoesNotContain(start.NextInput!.Candidates, c => c.Destination is { Y: > 2 });
        var end = GameEngine.Advance(start.State, new OrderAwareAutomatedProvider(), new Dice(), false);
        Assert.Equal(new Cell(0, 2), end.State.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.Empty(end.State.CellMarkers);
        Assert.False(end.State.IsUpright("actor"));
        Assert.Equal(new[] { "MovementCompleted", "CellMarkerConsumed", "PostureChanged", "ActivationCompleted" },
            end.Events.Take(4).Select(e => e.Kind));
        Assert.Equal(new[] { new Cell(0, 0), new(0, 1), new(0, 2) }, end.Events[0].Path);
        Assert.Equal(new Cell(0, 4), Assert.Single(end.State.Resources).Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerMakesProgressBeforePeelEvenWhenCoalIsBeyondMovementRange(bool peel)
    {
        var start = Start(Corridor(movement: 1, peel: peel));
        Assert.Equal(new Cell(0, 1), SelectedMove(start).Destination);
        var next = GameEngine.Advance(start.State, new OrderAwareAutomatedProvider(), new Dice(), false);
        Assert.True(next.State.IsUpright("actor"));
        Assert.Equal(peel ? 1 : 0, next.State.CellMarkers.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerDeliveryAndReadyMachinePlanningAlsoIgnorePeels(bool delivery)
    {
        var definition = Corridor() with
        {
            Resources = delivery ? [new(ResourceTypes.Coal, new(0, 0))] : [],
            Machines = [new("press", new(0, 5), MachineTypes.DiamondPress)
                { StoredResources = [new(ResourceTypes.Coal, delivery ? 0 : 3)] }]
        };
        Assert.Equal(new Cell(0, 2), SelectedMove(Start(definition)).Destination);
    }

    [Fact]
    public void WorkerSeeksCoalWhenItsOnlyRecipientIsBeyondPeel()
    {
        var start = Start(Corridor() with { Resources = [new(ResourceTypes.Coal, new(0, 1))] });
        Assert.Equal(new Cell(0, 1), SelectedMove(start).Destination);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPlanningPathsIgnorePeelsWhileActualMovementCannotCrossThem(bool peel)
    {
        var state = GameEngine.CreateGame(Corridor(peel: peel));
        var queries = new GameplayQueries(state);
        Assert.Equal(5, queries.PlanningPathsFrom("actor", new(0, 0))[new(0, 4)].Count);
        Assert.Equal(4, queries.RouteDistanceTo("actor", new(0, 0), new(0, 4)));
        Assert.Equal(!peel, queries.MovementPathsFrom("actor", new(0, 0)).ContainsKey(new(0, 4)));
        Assert.Contains(new Cell(0, 2), queries.MovementPathsFrom("actor", new(0, 0)).Keys);
        // Planning still respects the Machine blocking the corridor.
        Assert.DoesNotContain(new Cell(0, 6), queries.PlanningPathsFrom("actor", new(0, 0)).Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherBehaviorsKeepSharedDistancesAndChooseOnlyLegalProgress(bool ordered)
    {
        var definition = Corridor() with
        {
            UnitTypeIds = [UnitTypeIds.Grunt], UnitTypes = [],
            Units = [new(UnitTypeIds.Grunt, "blue", new(0, 0), UnitId: "actor"),
                new(UnitTypeIds.Grunt, "red", new(0, 7), UnitId: "enemy")],
            Agency = [new(UnitTypeIds.Grunt, "blue", ControllerKind.Automated),
                new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
            Machines = [], Resources = [],
            Orders = ordered ? [new(UnitTypeIds.Grunt, "blue", OrderKind.MoveTo, new(0, 4))] : []
        };
        foreach (var peel in new[] { false, true })
        {
            var start = Start(definition with { CellMarkers = peel ? definition.CellMarkers : [] });
            var queries = new GameplayQueries(start.State);
            Assert.Equal(4, queries.RouteDistanceTo("actor", new(0, 0), new(0, 4)));
            Assert.Equal(6, queries.DistanceToAttackPositionFrom("actor", new(0, 0)));
            Assert.Equal(6, queries.DistanceToAdjacentPositionFrom("actor", new(0, 0), "enemy"));
            Assert.Equal(7, queries.DistanceToNearestHostileFrom("actor", new(0, 0)));
            Assert.Equal(new Cell(0, peel ? 2 : 3), SelectedMove(start).Destination);
        }
    }
}
