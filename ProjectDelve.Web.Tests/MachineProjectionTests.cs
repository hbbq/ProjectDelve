using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class MachineProjectionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture(int coal = 3) => new()
    {
        Board = new() { Width = 5, Height = 4, Edges = [new(new(2, 1), EdgeDirection.Right, EdgeKind.Wall)] },
        UnitTypeIds = ["operator", UnitTypeIds.Grunt],
        UnitTypes = [new("operator", 2, 0, 0, 1, 1, UnitAction.Operate | UnitAction.GiveResource)],
        Units = [new("operator", "blue", new(1, 1), UnitId: "actor"), new(UnitTypeIds.Grunt, "red", new(4, 3))],
        Agency = [new("operator", "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Machines = [new("press", new(2, 1), MachineTypes.DiamondPress) { StoredResources = [new(ResourceTypes.Coal, coal)] }]
    };
    private static EngineResult Ready(ScenarioDefinition definition)
    {
        var start = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
        return GameEngine.Advance(start.State, new Choice("stay"), new Dice(), false);
    }

    [Fact]
    public void OperateTargetsMachineCellAndProjectsItsOwnStorageAndAtomicConversion()
    {
        var ready = Ready(Fixture());
        var presentation = BrowserProjection.Create(ready);
        var machine = Assert.Single(presentation.Machines);
        Assert.Null(machine.OwnerSide);
        Assert.Equal("Diamond Press", machine.Name);
        Assert.Equal(MachineTypes.DiamondPress.Id, machine.TypeId);
        Assert.Contains("3 Coal", machine.Operation);
        Assert.Contains("1 Diamond", machine.Operation);
        Assert.Equal(new StoredResourceDisplay("coal", "Coal", 3, 3), Assert.Single(machine.ResourceStorage));
        var choice = presentation.Decision!.Candidates.Single(c => c.Key == "operate:press");
        Assert.Equal("operate", choice.EntryId);
        Assert.Equal(InteractionKind.Position, choice.Interaction.Kind);
        Assert.Equal(new Cell(2, 1), choice.Interaction.Position);
        Assert.Empty(choice.AffectedUnitIds);
        Assert.Contains("Diamond Press", choice.Label);
        Assert.Equal("press", GambitProjection.Decision(ready.NextInput!).Candidates.Single(c => c.Key == choice.Key).MachineId);
        var result = GameEngine.Advance(ready.State, new Choice(choice.Key!), new Dice(), false);
        var converted = BrowserProjection.Create(result);
        Assert.Equal(0, converted.Machines.Single().ResourceStorage.Single().Quantity);
        Assert.Contains(converted.Events, e => e.Text.Contains("press") && e.Text.Contains("Diamond"));
        var ev = result.Events.Single(e => e.Kind == "MachineOperated");
        var projected = GambitProjection.Event(ev);
        Assert.Equal("press", projected.MachineId);
        Assert.Equal(ResourceTypes.Diamond, projected.ResourceType);
        Assert.Equal(new MachineResource(ResourceTypes.Coal, 3), Assert.Single(projected.ConsumedResources!));
        var step = converted.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "MachineOperated");
        Assert.Equal(machine.TypeId, step.Machines.Single().TypeId);
        Assert.Equal(0, step.Machines.Single().ResourceStorage.Single().Quantity);
        Assert.Equal(ResourceTypes.Diamond, result.ResolutionSteps[step.EventIndex].StateAfter.Resources.Single().Type);
        Assert.Equal(3, presentation.Machines.Single().ResourceStorage.Single().Quantity);
        var observation = GambitProjection.Observation(result.State);
        Assert.Contains("M", observation.AsciiBoard);
        Assert.Equal(0, observation.Board.Machines.Single().StoredQuantity("coal"));
        result.State.Physical.Board.Machines.Clear();
        Assert.Single(observation.Board.Machines);
        Assert.Single(step.Machines);
    }

    [Fact]
    public void OwnershipIsProjectedInStateObservationAndEveryResolutionSnapshot()
    {
        var ready = Ready(Fixture() with { Machines = [Fixture().Machines![0] with { OwnerSide = "blue" }] });
        Assert.Equal("blue", BrowserProjection.Create(ready).Machines.Single().OwnerSide);
        Assert.Equal("blue", GambitProjection.Observation(ready.State).Board.Machines.Single().OwnerSide);
        var operated = GameEngine.Advance(ready.State, new Choice("operate:press"), new Dice(), false);
        Assert.Equal("blue", BrowserProjection.Create(operated).Machines.Single().OwnerSide);
        Assert.All(BrowserProjection.Create(operated).ResolutionSteps,
            step => Assert.Equal("blue", step.Machines.Single().OwnerSide));
    }

    [Fact]
    public void GiveTargetsMachineWithoutPretendingItIsAUnitAndProjectsResult()
    {
        var ready = Ready(Fixture(2) with { Resources = [new(ResourceTypes.Coal, new(1, 1))] });
        var choice = BrowserProjection.Create(ready).Decision!.Candidates.Single(c => c.EntryId == "give-resource");
        Assert.Equal(InteractionKind.Position, choice.Interaction.Kind);
        Assert.Equal(new Cell(2, 1), choice.Interaction.Position);
        Assert.Null(choice.Interaction.UnitId);
        Assert.Empty(choice.AffectedUnitIds);
        var result = GameEngine.Advance(ready.State, new Choice(choice.Key!), new Dice(), false);
        Assert.Equal(3, BrowserProjection.Create(result).Machines.Single().ResourceStorage.Single().Quantity);
        Assert.Contains(BrowserProjection.Create(result).Events, e => e.Text.Contains("press") && e.Text.Contains("3 stored"));
        Assert.Equal("press", GambitProjection.Event(result.Events.Single(e => e.Kind == "ResourceStored")).MachineId);
    }

    [Fact]
    public void WebImportAndRestartPreserveMachineSetupAndDetachItsStorage()
    {
        var definition = Fixture() with { Machines = [Fixture().Machines![0] with { OwnerSide = "blue" }] };
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(definition));
        var imported = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal("blue", imported.Presentation.Machines.Single().OwnerSide);
        Assert.Equal("blue", imported.Result.State.Physical.Board.Machines.Single().OwnerSide);
        Assert.Equal(3, imported.Presentation.Machines.Single().ResourceStorage.Single().Quantity);
        var moved = game.StartRound(imported.Revision);
        var ready = game.Decide(moved.Revision, "stay");
        var operated = game.Decide(ready.Revision, "operate:press");
        Assert.Equal(0, operated.Presentation.Machines.Single().ResourceStorage.Single().Quantity);
        Assert.Equal(ResourceTypes.Diamond, operated.Result.State.Resources.Single().Type);
        var restarted = game.Restart(operated.Revision);
        Assert.Equal("blue", restarted.Presentation.Machines.Single().OwnerSide);
        Assert.Equal(3, restarted.Presentation.Machines.Single().ResourceStorage.Single().Quantity);
        Assert.Empty(restarted.Result.State.Resources);
    }
}
