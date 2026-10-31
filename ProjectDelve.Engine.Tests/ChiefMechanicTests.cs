using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ChiefMechanicTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "producer") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture(bool cargo = false) => new()
    {
        Board = new() { Width = 7, Height = 5, DefaultTerrain = TerrainKind.Asphalt },
        UnitTypeIds = ["producer", UnitTypeIds.Worker, UnitTypeIds.Grunt],
        // A custom identity proves the preference is independent of canonical Type ID.
        UnitTypes = [UnitRoster.ChiefMechanic("producer")],
        Units = [new("producer", "blue", new(2, 2), UnitId: "actor"),
            new(UnitTypeIds.Grunt, "red", new(6, 4))],
        Agency = [new("producer", "blue", ControllerKind.Human),
            new(UnitTypeIds.Worker, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Machines = [new("press", new(3, 2), MachineTypes.DiamondPress)
            { StoredResources = [new(ResourceTypes.Coal, cargo ? 0 : 3)] }],
        Resources = cargo ? [new(ResourceTypes.Coal, new(2, 2))] : []
    };
    private static EngineResult Start(ScenarioDefinition definition)
    {
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
        return result.NextInput?.Kind == DecisionKind.SelectUnit ? Step(result, "actor") : result;
    }
    private static EngineResult Step(EngineResult result, string key) =>
        GameEngine.Advance(result.State, new Pick(key), new Dice(), false);
    private static string? Choose(EngineResult result) =>
        new OrderAwareAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));

    [Fact]
    public void CanonicalStatsAndCapabilities()
    {
        var type = CanonicalUnitTypes.Find(UnitTypeIds.ChiefMechanic)!;
        Assert.Equal("Chief Mechanic", type.DisplayName);
        Assert.Equal((3, 0, 0, 4, 3), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.True(type.Unique);
        Assert.Equal(UnitAction.None, type.Actions);
        Assert.All(new[] { UnitAction.GiveResource, UnitAction.Operate, UnitAction.SummonWorker }, action => Assert.True(type.HasAction(action)));
        Assert.NotNull(type.Mechanic<TransportResource>());
        Assert.Null(type.ResourceStorage);
        Assert.Equal(3, type.Mechanic<SummonWorker>()!.SideLimit);
        Assert.Equal(UnitBehavior.Worker | UnitBehavior.PreferSummonWorker, type.Behaviors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SummonWinsOverOrdinaryActionsButHumanCanChooseEither(bool cargo)
    {
        var result = Step(Start(Fixture(cargo)), "stay");
        var alternative = result.NextInput!.Candidates.First(c => c.Action == (cargo ? UnitAction.GiveResource : UnitAction.Operate));
        var summon = result.NextInput.Candidates.Where(c => c.Action == UnitAction.SummonWorker)
            .OrderBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X).First();
        Assert.Equal(summon.Key, Choose(result));
        Assert.Equal(Choose(result), Choose(result));
        var human = Step(result, alternative.Key);
        Assert.DoesNotContain(human.Events, e => e.Kind == "UnitCreated");
        Assert.Contains(human.Events, e => e.Kind == (cargo ? "ResourceStored" : "MachineOperated"));
        var summoned = Step(Step(Start(Fixture(cargo)), "stay"), summon.Key);
        Assert.Contains(summoned.Events, e => e.Kind == "UnitCreated" && e.TypeId == UnitTypeIds.Worker);
        Assert.DoesNotContain(summoned.Events, e => e.Kind is "ResourceStored" or "MachineOperated");
        Assert.Contains(summoned.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "actor");
    }

    [Fact]
    public void ActionPreferenceWorksWithoutWorkerBehaviorAndForLegacyActionRequests()
    {
        var definition = Fixture() with
        { UnitTypes = [UnitRoster.ChiefMechanic("producer") with { Behaviors = UnitBehavior.PreferSummonWorker }] };
        var result = Step(Start(definition), "stay");
        var request = result.NextInput! with { Kind = DecisionKind.Act };
        var queries = new GameplayQueries(result.State);
        var expected = request.Candidates.Where(c => c.Action == UnitAction.SummonWorker)
            .OrderBy(c => c.Destination!.Y).ThenBy(c => c.Destination!.X).First();
        Assert.Equal(expected.Key, new DefaultAutomatedProvider().Choose(request, queries));
        Assert.Equal(expected.Key, Choose(result));
    }

    [Fact]
    public void MoveUsesWorkerUnchangedAndDoNothingSuppressesPreference()
    {
        var definition = Fixture(true) with { Machines = [new("press", new(5, 2), MachineTypes.DiamondPress)] };
        var normal = Start(definition);
        var workerOnly = Start(definition with { UnitTypes = [definition.UnitTypes![0] with { Behaviors = UnitBehavior.Worker }] });
        Assert.Equal(Choose(workerOnly), Choose(normal));
        Assert.NotEqual("stay", Choose(normal));
        var idle = Start(definition with { Orders = [new("producer", "blue", OrderKind.DoNothing)] });
        Assert.Equal("stay", Choose(idle));
        idle = Step(idle, Choose(idle)!);
        Assert.Equal("end-turn", Choose(idle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SideCapFallsBackToWorkerActionsAndCountsLyingWorkers(bool cargo)
    {
        var result = Start(Fixture(cargo));
        for (var i = 0; i < 3; i++)
        {
            result.State.PlaceUnit(UnitTypeIds.Worker, $"blue-{i}", "blue", new(i, 0), Posture.Lying);
            result.State.PlaceUnit(UnitTypeIds.Worker, $"red-{i}", "red", new(i, 4));
        }
        result = Step(result, "stay");
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Action == UnitAction.SummonWorker);
        Assert.Equal(cargo ? UnitAction.GiveResource : UnitAction.Operate,
            result.NextInput.Candidates.Single(c => c.Key == Choose(result)).Action);
        result.State.Units[result.State.Units.FindIndex(u => u.Id == "blue-0")] =
            result.State.Units.Single(u => u.Id == "blue-0") with { CurrentHp = 0 };
        result.State.Physical.Figures.RemoveAll(f => f.Id == "blue-0");
        Assert.Contains(SummoningRules.Candidates(result.State, result.State.Units.Single(u => u.Id == "actor")),
            c => c.Action == UnitAction.SummonWorker);
    }
}
