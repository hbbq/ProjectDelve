using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AmphibiousTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static GameState State(Cell? start = null, Posture posture = Posture.Upright) => GameEngine.CreateGame(new ScenarioDefinition
    {
        Board = new() { Width = 6, Height = 2, Cells = [new(new(1, 0), TerrainKind.Water), new(new(2, 0), TerrainKind.Water)] },
        UnitTypeIds = [UnitTypeIds.GiantToad, UnitTypeIds.Jackalope],
        Units = [new(UnitTypeIds.GiantToad, "blue", start ?? new(0, 0), posture, UnitId: "amphibian"),
            new(UnitTypeIds.Jackalope, "red", new(5, 0), UnitId: "land")],
        Agency = [], Loot = LootSettings.Empty,
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
    });

    [Fact]
    public void TraversalStoppingHypotheticalsPlacementAndSerializationShareWaterPermission()
    {
        var state = State();
        var paths = MovementRules.FindPaths(state, "amphibian", new(0, 0), 3);
        Assert.Contains(new Cell(1, 0), paths.Keys); Assert.Contains(new Cell(2, 0), paths.Keys);
        Assert.Equal(3, MovementRules.Cost(paths[new(3, 0)]));
        Assert.DoesNotContain(new Cell(2, 0), MovementRules.FindPaths(state, "land", new(5, 0)).Keys);
        Assert.NotEmpty(new GameplayQueries(state).MovementPathsFrom("amphibian", new(1, 0)));
        state.PlaceUnit(UnitTypeIds.GiantToad, "lying", "blue", new(1, 0), Posture.Lying);
        Assert.Throws<ArgumentException>(() => state.PlaceUnit(UnitTypeIds.Jackalope, "invalid", "blue", new(2, 0)));
        GameEngine.ValidateScenario(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!);
        Assert.True(state.Types.Single(t => t.Id == UnitTypeIds.GiantToad).Amphibious);
    }

    [Fact]
    public void InitialWaterPlacementWorksWhileLyingAndOtherBlockersAndUnknownRemainForbidden()
    {
        var waterStart = State(new(1, 0), Posture.Lying);
        GameEngine.ValidateScenario(waterStart);
        Assert.Equal(Posture.Lying, waterStart.Physical.Figures.Single(f => f.Id == "amphibian").Posture);
        var state = State();
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor));
        Assert.DoesNotContain(new Cell(1, 0), MovementRules.FindPaths(state, "amphibian", new(0, 0), 1).Keys);
        state.Physical.Board.Edges.Clear();
        state.Physical.Board.Terrain.Add(new(new(1, 1), TerrainKind.SolidRock));
        Assert.DoesNotContain(new Cell(1, 1), MovementRules.FindPaths(state, "amphibian", new(0, 0)).Keys);
        state.FogOfWarSides = ["blue"]; state.ExploredCells["blue"] = [new(0, 0)];
        Assert.DoesNotContain(new Cell(1, 0), MovementRules.FindPaths(state, "amphibian", new(0, 0)).Keys);
    }

    [Fact]
    public void SummonPlacementUsesReceivingTypeWaterPermission()
    {
        var state = GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 4, Height = 3, Cells = [new(new(1, 0), TerrainKind.Water)] },
            UnitTypeIds = [UnitTypeIds.Beastmaster], Units = [new(UnitTypeIds.Beastmaster, "blue", new(1, 1), UnitId: "master")],
            Agency = [], Loot = LootSettings.Empty, Objectives = [new("blue", ScenarioObjective.NoObjective)]
        });
        var unit = state.Units.Single(u => u.Id == "master");
        var candidates = SummoningRules.Candidates(state, unit).ToArray();
        Assert.Contains(candidates, c => c.Destination == new Cell(1, 0) &&
            c.Ability!.Definition.Mechanic is AdjacentUnitSummonMechanic { UnitTypeId: UnitTypeIds.GiantToad });
        Assert.DoesNotContain(candidates, c => c.Destination == new Cell(1, 0) &&
            c.Ability!.Definition.Mechanic is AdjacentUnitSummonMechanic { UnitTypeId: UnitTypeIds.Jackalope });
        state.Round = 1; state.ActiveToken = unit.Token; state.CurrentUnitId = unit.Id;
        state.ResolvingTokenUnitIds = [unit.Id]; state.MoveDone = true;
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var choice = state.Pending!.Candidates.Single(c => c.Destination == new Cell(1, 0) &&
            c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic { UnitTypeId: UnitTypeIds.GiantToad });
        var created = GameEngine.Advance(state, new Choice(choice.Key), new Dice(), false).State;
        Assert.Equal(UnitTypeIds.GiantToad, created.Units.Single(u =>
            u.Id == created.Physical.Figures.Single(f => f.Position == new Cell(1, 0)).Id).TypeId);
        GameEngine.ValidateScenario(created);
    }

    [Fact]
    public void ExternalRelocationAndSwapRespectEachReceivingType()
    {
        var state = State();
        Assert.Contains(new Cell(1, 0), ExternalMovementRules.Destinations(state, "land", "amphibian", 1));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(1, 0) };
        Assert.False(ExternalMovementRules.CanSwap(state, "land", "amphibian"));
        var landIndex = state.Physical.Figures.FindIndex(f => f.Id == "land");
        state.Physical.Figures[landIndex] = state.Physical.Figures[landIndex] with { Position = new(3, 0) };
        Assert.DoesNotContain(new Cell(2, 0), ExternalMovementRules.Destinations(state, "amphibian", "land", 1));
        state.Types[state.Types.FindIndex(t => t.Id == UnitTypeIds.Jackalope)] = UnitRoster.Jackalope() with { Amphibious = true };
        Assert.True(ExternalMovementRules.CanSwap(state, "land", "amphibian"));
        ExternalMovementRules.SwapPlaces(state, "land", "amphibian");
        GameEngine.ValidateScenario(state);
    }
}
