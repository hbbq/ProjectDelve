using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BeaconTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.SideId == "blue") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    internal static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 40, Height = 5 },
        UnitTypeIds = ["operator"],
        UnitTypes = [UnitType.Define("operator", "Operator", UnitAuthoring.Stats(3, 0, 0, 1, 1),
            UnitAuthoring.CantAttack(), UnitAuthoring.Operate())],
        Units = [new("operator", "blue", new(2, 2), UnitId: "blue"), new("operator", "red", new(3, 3), UnitId: "red")],
        Agency = [new("operator", "blue", ControllerKind.Human), new("operator", "red", ControllerKind.Human)],
        Machines = [new("beacon", new(3, 2), MachineTypes.Beacon)], FogOfWarSides = ["blue", "red"],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
    };
    private static Candidate Operation(GameState state, string side = "blue", string machine = "beacon") =>
        MachineRules.Candidates(state, state.Units.Single(u => u.Id == side)).Single(c => c.MachineId == machine);
    private static RulesEvent? Operate(GameState state, string side = "blue", string machine = "beacon") =>
        MachineRules.Resolve(state, side, Operation(state, side, machine), new Dice());

    [Fact]
    public void RevealIsNeutralSideSpecificPermanentAndIndependentlyLimited()
    {
        var state = GameEngine.CreateGame(Fixture());
        var before = state.DetachedCopy();
        Assert.False(FogOfWar.IsExplored(state, "blue", new(17, 2)));
        var red = state.ExploredCells["red"].ToHashSet();
        Assert.NotNull(Operate(state));
        Assert.True(FogOfWar.IsExplored(state, "blue", new(18, 2)));
        Assert.False(FogOfWar.IsExplored(state, "blue", new(19, 2)));
        Assert.True(red.SetEquals(state.ExploredCells["red"]));
        Assert.Empty(MachineRules.Candidates(state, state.Units.Single(u => u.Id == "blue")));
        Assert.NotNull(Operate(state, "red"));
        Assert.Equal(2L, state.Physical.Board.Machines[0].TotalUses);
        Assert.Equal(0L, before.Physical.Board.Machines[0].TotalUses);
        FogOfWar.Refresh(state);
        Assert.True(FogOfWar.IsExplored(state, "blue", new(18, 2)));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(restored));
    }

    [Theory]
    [InlineData(1, null, false)]
    [InlineData(2, 1, true)]
    [InlineData(null, 1, true)]
    [InlineData(0, null, false)]
    public void TotalAndPerSideLimitsApplyTogether(int? total, int? perSide, bool redAllowed)
    {
        var definition = Fixture();
        var machine = definition.Machines![0];
        machine = machine with { Type = machine.Type with { Operation = machine.Type.Operation with {
            MaxUsesTotal = total, MaxUsesPerSide = perSide } } };
        var state = GameEngine.CreateGame(definition with { Machines = [machine] });
        if (total != 0) Operate(state);
        Assert.Equal(redAllowed, MachineRules.Candidates(state, state.Units.Single(u => u.Id == "red")).Any());
    }

    [Fact]
    public void SeparateInstancesDoNotShareCountersAndResourcesDoNotPreventReveal()
    {
        var definition = Fixture();
        var state = GameEngine.CreateGame(definition with {
            Machines = [.. definition.Machines!, new("second", new(2, 1), MachineTypes.Beacon)],
            Resources = [new(ResourceTypes.Coal, new(2, 2))] });
        Operate(state);
        Assert.Equal(0L, state.Physical.Board.Machines[1].TotalUses);
        Assert.Equal(1L, state.Physical.Board.Machines[0].TotalUses);
        Assert.Single(state.Resources);
    }

    [Fact]
    public void NoDiscoveryDisabledFogAndStaleChoicesSpendNothing()
    {
        var state = GameEngine.CreateGame(Fixture());
        var stale = Operation(state);
        state.ExploredCells["blue"].UnionWith(FogOfWar.VisionArea(state.Physical.Board, [new(3, 2)], 15, excludedMachineId: "beacon"));
        Assert.Empty(MachineRules.Candidates(state, state.Units.Single(u => u.Id == "blue")));
        Assert.Null(MachineRules.Resolve(state, "blue", stale, new Dice()));
        Assert.Equal(0L, state.Physical.Board.Machines[0].TotalUses);
        state.FogOfWarSides.Clear();
        Assert.Empty(MachineRules.Candidates(state, state.Units.Single(u => u.Id == "red")));
    }

    [Fact]
    public void OnlySourceMachineIsExcludedAndOperatorPhaseDoesNotPassOtherBlockers()
    {
        var definition = Fixture();
        var type = definition.UnitTypes![0].WithLocal(new("phase", "Phase", AbilityTiming.Passive, new Phase()));
        var state = GameEngine.CreateGame(definition with { UnitTypes = [type],
            Machines = [.. definition.Machines!, new("blocker", new(7, 2), MachineTypes.Beacon)] });
        state.ExploredCells["blue"].Clear();
        Operate(state);
        Assert.True(FogOfWar.IsExplored(state, "blue", new(7, 2)));
        Assert.False(FogOfWar.IsExplored(state, "blue", new(8, 2)));
        Assert.True(FogOfWar.IsExplored(state, "blue", new(6, 2)));
    }

    [Fact]
    public void MalformedCountsLimitsAndWrongFloorAreRejected()
    {
        var definition = Fixture(); var machine = definition.Machines![0];
        foreach (var invalid in new[] {
            machine with { UseCounts = ImmutableDictionary<string, int>.Empty.Add("missing", 1) },
            machine with { UseCounts = ImmutableDictionary<string, int>.Empty.Add("blue", -1) },
            machine with { UseCounts = ImmutableDictionary<string, int>.Empty.Add("blue", 2) },
            machine with { Type = machine.Type with { Operation = machine.Type.Operation with { MaxUsesTotal = -1 } } },
            machine with { Type = machine.Type with { Operation = machine.Type.Operation with { MaxUsesPerSide = -1 } } },
            machine with { Type = machine.Type with { Operation = machine.Type.Operation with { Reveal = new(-1) } } }
        }) Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { Machines = [invalid] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with {
            Board = definition.Board with { Cells = [new(new(3, 2), TerrainKind.Grass)] } }));
    }

    [Fact]
    public void ActionSnapshotsAndRestoredReplayCaptureSuccessfulDiscoveryAndUse()
    {
        var start = GameEngine.StartRound(GameEngine.CreateGame(Fixture()), new Dice(), false);
        if (start.NextInput!.Candidates.Any(c => c.Kind == ActivationChoiceKind.Stay))
            start = GameEngine.Advance(start.State, new Choice("stay"), new Dice(), false);
        var key = start.NextInput!.Candidates.Single(c => c.Action == UnitAction.Operate).Key;
        var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(start.State))!;
        var result = GameEngine.Advance(start.State, new Choice(key), new Dice(), false);
        var replay = GameEngine.Advance(saved, new Choice(key), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        var index = result.Events.FindIndex(e => e.Kind == "MachineOperated");
        Assert.True(index >= 0);
        var snapshot = result.ResolutionSteps.Single(s => s.EventIndex == index).StateAfter;
        Assert.Equal(1L, snapshot.Physical.Board.Machines[0].TotalUses);
        Assert.True(FogOfWar.IsExplored(snapshot, "blue", new(18, 2)));
        Assert.Equal(0L, start.State.Physical.Board.Machines[0].TotalUses);
        Assert.Equal(ScenarioDefinitionJson.ToJson(Fixture()), ScenarioDefinitionJson.ToJson(
            ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Fixture()))));
    }
}

