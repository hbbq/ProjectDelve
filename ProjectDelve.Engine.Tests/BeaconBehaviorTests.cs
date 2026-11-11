using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BeaconBehaviorTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private static GameState State(string type, int beaconX = 5, int actorX = 1) => GameEngine.CreateGame(new()
    {
        Board = new() { Width = 40, Height = 5 }, UnitTypeIds = [type],
        Units = [new(type, "blue", new(actorX, 2), UnitId: "actor")],
        Agency = [new(type, "blue", ControllerKind.Automated)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], FogOfWarSides = ["blue"],
        Machines = [new("beacon", new(beaconX, 2), MachineTypes.Beacon)]
    });
    private static string? Prefer(EngineResult result) => result.NextInput is { } request ? new GameplayQueries(result.State).PreferredBeaconChoice(request) : null;
    private static EngineResult Start(GameState state) => GameEngine.StartRound(state, new Dice(), false);
    private static EngineResult Advance(EngineResult result) => GameEngine.Advance(result.State, new OrderAwareAutomatedProvider(), new Dice(), false);

    [Theory]
    [InlineData(UnitTypeIds.Jackalope)]
    [InlineData(UnitTypeIds.GiantToad)]
    public void CompanionsMoveThenOperateAndReplayDeterministically(string type)
    {
        var start = Start(State(type));
        var choice = Prefer(start);
        var move = start.NextInput!.Candidates.Single(c => c.Key == choice);
        Assert.Equal(new Cell(4, 2), move.Destination);
        var moved = Advance(start);
        Assert.Equal(new Cell(4, 2), moved.State.Physical.Figures.Single().Position);
        Assert.Equal(UnitAction.Operate, moved.NextInput!.Candidates.Single(c => c.Key == Prefer(moved)).Action);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(moved.State))!;
        var operated = Advance(moved);
        var replay = GameEngine.Advance(restored, new OrderAwareAutomatedProvider(), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(operated), JsonSerializer.Serialize(replay));
        Assert.Equal(1L, operated.State.Physical.Board.Machines[0].TotalUses);
        Assert.True(FogOfWar.IsExplored(operated.State, "blue", new(19, 2)));
    }

    [Theory]
    [InlineData(UnitTypeIds.Jackalope)]
    [InlineData(UnitTypeIds.GiantToad)]
    public void AdjacentBeaconUsesStayThenNormalAction(string type)
    {
        var start = Start(State(type, actorX: 4));
        Assert.Equal("stay", Prefer(start));
        var stayed = Advance(start);
        Assert.Contains("operate", Prefer(stayed)!);
        Assert.Equal(1L, Advance(stayed).State.Physical.Board.Machines[0].TotalUses);
    }

    [Fact]
    public void ImmediateBeaconsTieByMachineIdentityThenCandidateKeyInsteadOfMachinePosition()
    {
        var state = State(UnitTypeIds.Jackalope, actorX: 4);
        // The alphabetically first Machine is below the other one. Both use
        // the actor's same operating Cell, independent of Machine position.
        state.Physical.Board.Machines.Add(new("a-beacon", new(4, 3), MachineTypes.Beacon));
        state.Types[0] = state.Types[0] with { CanHaveEquipment = true };
        state.EquipmentTypes.Add(new("beacon-permit", "Beacon Permit")
        { LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate())] });
        state.Units[0] = state.Units[0] with { Inventory = [new("permit-1", "beacon-permit")] };
        var stayed = Advance(Start(state));
        var operations = stayed.NextInput!.Candidates.Where(c => c.Action == UnitAction.Operate).ToArray();
        Assert.Contains(operations, c => c.MachineId == "beacon");
        var expected = operations.Where(c => c.MachineId == "a-beacon")
            .OrderBy(c => c.Key, StringComparer.Ordinal).First();
        Assert.Equal(2, operations.Count(c => c.MachineId == "a-beacon"));
        Assert.Equal(expected.Key, Prefer(stayed));

        // Source-bound choices for the same Machine use the final key tie-break,
        // regardless of their order in the supplied legal decision list.
        var request = stayed.NextInput! with { Candidates = stayed.NextInput.Candidates.AsEnumerable().Reverse().ToList() };
        Assert.Equal(expected.Key, new GameplayQueries(stayed.State).PreferredBeaconChoice(request));
    }

    [Fact]
    public void HiddenObstaclesCannotChangeBeaconMovementPreference()
    {
        var open = State(UnitTypeIds.Jackalope);
        var obstructed = open.Copy();
        for (var y = 0; y < obstructed.Physical.Board.Height; y++)
            obstructed.Physical.Board.Terrain.Add(new(new(7, y), TerrainKind.SolidRock));
        obstructed.Physical.Board.Machines.Add(new("hidden-machine", new(9, 2), MachineTypes.DiamondPress));
        obstructed.Physical.Board.Edges.Add(new(new(8, 1), new(8, 2), EdgeKind.Wall));
        Assert.False(FogOfWar.IsExplored(open, "blue", new(7, 2)));
        Assert.False(FogOfWar.IsExplored(obstructed, "blue", new(7, 2)));
        Assert.NotEqual(FogOfWar.VisionArea(open.Physical.Board, [new(5, 2)], 15, excludedMachineId: "beacon").Count,
            FogOfWar.VisionArea(obstructed.Physical.Board, [new(5, 2)], 15, excludedMachineId: "beacon").Count);

        var openStart = Start(open);
        var obstructedStart = Start(obstructed);
        Assert.Equal(openStart.NextInput!.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).Select(c => c.Key).Order(),
            obstructedStart.NextInput!.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).Select(c => c.Key).Order());
        var expected = openStart.NextInput.Candidates.Single(c => c.Key == Prefer(openStart));
        Assert.Equal(new Cell(4, 2), expected.Destination);
        Assert.Equal(expected.Key, Prefer(obstructedStart));
        Assert.Equal(expected.Key, new OrderAwareAutomatedProvider().Choose(obstructedStart.NextInput, new GameplayQueries(obstructedStart.State)));
    }

    [Fact]
    public void HiddenDistantExhaustedDisabledAndIneffectiveAreIgnored()
    {
        var hidden = State(UnitTypeIds.Jackalope, 20);
        Assert.Null(Prefer(Start(hidden)));
        hidden.ExploredCells["blue"].Add(new(20, 2));
        Assert.Null(Prefer(Start(hidden)));
        var exhausted = State(UnitTypeIds.Jackalope);
        exhausted.Physical.Board.Machines[0] = exhausted.Physical.Board.Machines[0] with {
            UseCounts = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.Add("blue", 1) };
        Assert.Null(Prefer(Start(exhausted)));
        var disabled = State(UnitTypeIds.Jackalope); disabled.FogOfWarSides.Clear();
        Assert.Null(Prefer(Start(disabled)));
        var allKnown = State(UnitTypeIds.Jackalope);
        allKnown.ExploredCells["blue"].UnionWith(FogOfWar.VisionArea(allKnown.Physical.Board, [new(5, 2)], 15, excludedMachineId: "beacon"));
        Assert.Null(Prefer(Start(allKnown)));
    }

    [Theory]
    [InlineData(OrderKind.HoldPosition)]
    [InlineData(OrderKind.DoNothing)]
    public void StandingOrdersAreNotOverridden(OrderKind kind)
    {
        var state = State(UnitTypeIds.GiantToad);
        state.AssignOrder(state.Units[0].Token, kind);
        var start = Start(state);
        Assert.Null(Prefer(start));
        Assert.Equal("stay", new OrderAwareAutomatedProvider().Choose(start.NextInput!, new GameplayQueries(start.State)));
    }

    [Fact]
    public void NoActionBudgetAndBananaDestinationDoNotCreatePlans()
    {
        var state = State(UnitTypeIds.GiantToad);
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(4, 2));
        Assert.Null(Prefer(Start(state)));
        var start = Start(State(UnitTypeIds.Jackalope));
        start.State.ActionsUsedThisActivation = start.State.EffectiveActionsPerActivation;
        Assert.Null(Prefer(start));
    }

    [Fact]
    public void DiscoveryAfterMoveIsRecheckedWithoutSpendingMachineUse()
    {
        var moved = Advance(Start(State(UnitTypeIds.GiantToad)));
        moved.State.ExploredCells["blue"].UnionWith(FogOfWar.VisionArea(moved.State.Physical.Board, [new(5, 2)], 15, excludedMachineId: "beacon"));
        var refreshed = GameEngine.RefreshChoices(moved.State, new Dice(), false);
        Assert.Null(Prefer(refreshed));
        Assert.DoesNotContain(refreshed.NextInput?.Candidates ?? [], c => c.Action == UnitAction.Operate);
        Assert.Equal(0L, refreshed.State.Physical.Board.Machines[0].TotalUses);
    }
}
