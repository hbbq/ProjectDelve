using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RailwayTests
{
    private sealed class Dice : IRandomProvider
    {
        public List<ActivationToken> Drawn { get; } = [];
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) { Drawn.Add(bag[0]); return bag[0]; }
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Rails roll no combat dice.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Rails roll no combat dice.");
        public int RollD6() => throw new InvalidOperationException("Rails roll no dice.");
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static ScenarioDefinition Fixture(params UnitPlacement[] obstacles) => new()
    {
        Board = new() { Width = 8, Height = 5,
            Cells = Enumerable.Range(0, 7).Select(x => new CellDefinition(new(x, 1), TerrainKind.Rail, RailDirection.East))
                .Concat([new(new(7, 1), TerrainKind.Rail, RailDirection.South),
                    new(new(7, 2), TerrainKind.Rail, RailDirection.South),
                    new(new(7, 3), TerrainKind.Rail, RailDirection.West),
                    new(new(6, 3), TerrainKind.Rail, RailDirection.North),
                    new(new(6, 2), TerrainKind.Rail, RailDirection.North)]).ToList() },
        UnitTypeIds = [UnitTypeIds.Locomotive, UnitTypeIds.Grunt, UnitTypeIds.Troll, UnitTypeIds.BombImp, UnitTypeIds.RedDragon],
        Units = [new(UnitTypeIds.Locomotive, "rail", new(0, 1), UnitId: "train"),
            new(UnitTypeIds.Grunt, "blue", new(0, 4), UnitId: "hero"),
            new(UnitTypeIds.Grunt, "red", new(7, 4), UnitId: "enemy"), .. obstacles],
        Agency = [new(UnitTypeIds.Locomotive, "rail", ControllerKind.Human),
            new(UnitTypeIds.Grunt, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human),
            .. obstacles.Select(u => new AgencyAssignment(u.UnitTypeId, u.SideId, ControllerKind.Human)).Distinct()
                .Where(a => a.UnitTypeId != UnitTypeIds.Grunt || a.SideId is not ("red" or "blue"))],
        NeutralSides = ["rail"],
        Objectives = [new("rail", ScenarioObjective.NoObjective), new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
    };
    private static EngineResult Run(ScenarioDefinition definition, Dice? dice = null) =>
        GameEngine.StartRound(GameEngine.CreateGame(definition), dice ?? new(), false);
    private static Cell Position(GameState state, string id = "train") => state.Physical.Figures.Single(f => f.Id == id).Position;

    [Fact]
    public void CanonicalLocomotiveAndFourIndependentStepsAreAutomaticAndRepeatEachRound()
    {
        var type = CanonicalUnitTypes.Find(UnitTypeIds.Locomotive)!;
        Assert.Equal(0, type.Mov); Assert.Equal(Footprint.OneByOne, type.Footprint);
        Assert.Equal(UnitAction.None, type.Actions); Assert.Equal(4, type.RunRails!.Steps);
        var dice = new Dice(); var result = Run(Fixture(), dice);
        Assert.Equal(new(4, 1), Position(result.State));
        Assert.Equal(4, result.Events.Count(e => e.Kind == "UnitRepositioned"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "MovementCompleted");
        Assert.Equal(new[] { new Cell(1, 1), new(2, 1), new(3, 1), new(4, 1) },
            result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "UnitRepositioned").Select(s => Position(s.StateAfter)));
        Assert.NotEqual("train", result.NextInput?.UnitId);
        while (!result.State.RoundComplete)
            result = GameEngine.Advance(result.State, new Choice(result.NextInput!.Candidates.Any(c => c.Key == "stay") ? "stay" : "end-turn"), dice, false);
        result = GameEngine.StartRound(result.State, dice, false);
        Assert.Equal(new(7, 2), Position(result.State));
        Assert.Equal(2, dice.Drawn.Count(t => t == new ActivationToken(UnitTypeIds.Locomotive, "rail")));
    }

    [Theory]
    [InlineData(RailDirection.North, 7, 2, 7, 2)]
    [InlineData(RailDirection.South, 7, 1, 6, 2)]
    [InlineData(RailDirection.West, 7, 3, 7, 1)]
    public void DirectionsAreReadAgainAtEveryStep(RailDirection direction, int x, int y, int endX, int endY)
    {
        var definition = Fixture();
        var cells = definition.Board.Cells.Where(c => c.Position != new Cell(x, y)).ToList();
        cells.Add(new(new(x, y), TerrainKind.Rail, direction));
        definition = definition with { Board = definition.Board with { Cells = cells },
            Units = definition.Units.Select(u => u.UnitId == "train" ? u with { Anchor = new(x, y) } : u).ToList() };
        Assert.Equal(new(endX, endY), Position(Run(definition).State));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    [InlineData(EdgeKind.WallWithWindow)]
    public void BlockingEdgesPreventEvenCollisions(EdgeKind edge)
    {
        var definition = Fixture(new UnitPlacement(UnitTypeIds.Grunt, "red", new(1, 1), UnitId: "struck"));
        definition = definition with { Board = definition.Board with { Edges = [new(new(0, 1), EdgeDirection.Right, edge)] } };
        var result = Run(definition);
        Assert.Equal(new(0, 1), Position(result.State)); Assert.True(result.State.IsUpright("struck"));
        Assert.DoesNotContain(result.Events, e => e.Kind is "PlacesSwapped" or "UnitDefeated");
    }

    [Theory]
    [InlineData("boundary")]
    [InlineData("off-track")]
    [InlineData("not-rail")]
    public void InvalidStepsEndTheWholeSequence(string reason)
    {
        var d = Fixture();
        var cells = d.Board.Cells.ToList();
        if (reason == "boundary") cells[0] = cells[0] with { Direction = RailDirection.West };
        if (reason == "off-track") cells[1] = cells[1] with { Terrain = TerrainKind.StoneFloor, Direction = null };
        if (reason == "not-rail") cells[0] = cells[0] with { Terrain = TerrainKind.StoneFloor, Direction = null };
        var result = Run(d with { Board = d.Board with { Cells = cells } });
        Assert.Equal(new(0, 1), Position(result.State));
    }

    [Theory]
    [InlineData(Posture.Upright, "red")]
    [InlineData(Posture.Upright, "rail")]
    [InlineData(Posture.Lying, "red")]
    [InlineData(Posture.Lying, "rail")]
    public void CollisionsIgnoreSideAndNeverTransportResources(Posture posture, string side)
    {
        var d = Fixture(new UnitPlacement(UnitTypeIds.Grunt, side, new(1, 1), posture, UnitId: "struck"));
        d = d with { Resources = [new(ResourceTypes.Coal, new(0, 1)), new(ResourceTypes.Diamond, new(1, 1))],
            UnitTypes = [UnitRoster.Locomotive() with { TransportResource = new() }, UnitRoster.Grunt() with { TransportResource = new() }] };
        var result = Run(d);
        Assert.Equal(new(4, 1), Position(result.State));
        Assert.Equal(d.Resources, result.State.Resources);
        if (posture == Posture.Upright)
        {
            Assert.Equal(new(0, 1), Position(result.State, "struck")); Assert.False(result.State.IsUpright("struck"));
            Assert.Contains(result.Events, e => e.Kind == "PlacesSwapped" && e.TargetId == "struck");
        }
        else
        {
            Assert.Contains("struck", result.State.DefeatedUnitIds);
            Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "struck");
        }
    }

    [Fact]
    public void LyingBombImpDefeatDrainsExplosionBeforeAnotherRailStep()
    {
        var result = Run(Fixture(new UnitPlacement(UnitTypeIds.BombImp, "red", new(1, 1), Posture.Lying, UnitId: "bomb"),
            new(UnitTypeIds.Grunt, "red", new(2, 2), UnitId: "bystander")));
        var explosion = result.Events.FindIndex(e => e.Kind == "ExplosionDamageResolved" && e.TargetId == "bystander");
        Assert.True(explosion > 0);
        Assert.Contains("bystander", result.State.DefeatedUnitIds);
        Assert.Equal(new Cell(1, 1), Position(result.ResolutionSteps[explosion].StateAfter));
        Assert.Equal(new Cell(4, 1), Position(result.State));
    }

    [Fact]
    public void LyingUndyingUnitIsDefeatedAndLargeFootprintBlocks()
    {
        var result = Run(Fixture(new UnitPlacement(UnitTypeIds.Troll, "red", new(1, 1), Posture.Lying, UnitId: "troll"),
            new(UnitTypeIds.RedDragon, "red", new(3, 0), UnitId: "large")));
        Assert.Contains("troll", result.State.DefeatedUnitIds);
        Assert.Equal(new(2, 1), Position(result.State)); Assert.Equal(new(3, 0), Position(result.State, "large"));
    }

    [Fact]
    public void LyingLocomotiveOnlyStandsUp()
    {
        var d = Fixture(); d.Units[0] = d.Units[0] with { Posture = Posture.Lying };
        var result = Run(d);
        Assert.True(result.State.IsUpright("train")); Assert.Equal(new(0, 1), Position(result.State));
        Assert.DoesNotContain(result.Events, e => e.Kind == "UnitRepositioned");
    }

    [Fact]
    public void DefeatedCollisionGroupDoesNotActivateAndDisappearsFromNextRoundsBag()
    {
        var d = Fixture(new UnitPlacement(UnitTypeIds.Troll, "red", new(1, 1), Posture.Lying, UnitId: "troll"));
        var dice = new Dice(); var result = Run(d, dice);
        var events = new List<RulesEvent>(result.Events);
        Assert.Contains(result.ResolutionSteps[0].StateAfter.Bag, t => t.TypeId == UnitTypeIds.Troll);
        while (!result.State.RoundComplete)
        {
            result = GameEngine.Advance(result.State, new Choice(result.NextInput!.Candidates.Any(c => c.Key == "stay") ? "stay" : "end-turn"), dice, false);
            events.AddRange(result.Events);
        }
        Assert.DoesNotContain(events, e => e.Kind == "ActivationStarted" && e.UnitId == "troll");
        result = GameEngine.StartRound(result.State, dice, false);
        Assert.DoesNotContain(result.ResolutionSteps[0].StateAfter.Bag, t => t.TypeId == UnitTypeIds.Troll);
        Assert.Contains(result.ResolutionSteps[0].StateAfter.Bag, t => t.TypeId == UnitTypeIds.Locomotive && t.SideId == "rail");
    }

    [Fact]
    public void FixedMachineBlocksRailPlacementAndOpenDoorPermitsTraversal()
    {
        var d = Fixture();
        d = d with { Machines = [new("press", new(2, 1), MachineTypes.DiamondPress)],
            Board = d.Board with { Edges = [new(new(0, 1), EdgeDirection.Right, EdgeKind.OpenDoor)] } };
        Assert.Equal(new(1, 1), Position(Run(d).State));
    }

    [Fact]
    public void OrdinaryMovementCrossesRailInEitherDirectionAndRailDoesNotBlockLos()
    {
        var state = GameEngine.CreateGame(Fixture());
        Assert.Contains(new Cell(1, 1), MovementRules.FindPaths(state, "hero", new(0, 4), 4).Keys);
        Assert.True(AttackRules.HasGeometricLineOfSight(state.Physical.Board, new(1, 0), new(1, 2)));
        Assert.True(MovementRules.CanStep(state.Physical.Board, Footprint.OneByOne, new(2, 1), new(1, 1), default));
    }

    [Fact]
    public void ScenarioTransportAndStatePlaybackAreDeterministic()
    {
        var d = Fixture(new UnitPlacement(UnitTypeIds.Grunt, "red", new(1, 1), UnitId: "struck"));
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(d));
        Assert.Equal(ScenarioDefinitionJson.ToJson(d), ScenarioDefinitionJson.ToJson(decoded));
        var original = GameEngine.CreateGame(decoded);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(original))!;
        var first = GameEngine.StartRound(original, new Dice(), false);
        var second = GameEngine.StartRound(restored, new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(new(0, 1), Position(original));
    }

    [Fact]
    public void NeutralityAndNoObjectiveAreIndependentAndHostilityIsSymmetric()
    {
        var d = Fixture(); var state = GameEngine.CreateGame(d);
        Assert.True(state.AreHostile("red", "blue")); Assert.True(state.AreHostile("blue", "red"));
        Assert.False(state.AreHostile("rail", "blue")); Assert.False(state.AreHostile("blue", "rail"));
        Assert.False(state.AreHostile("rail", "rail"));
        Assert.Equal(ScenarioObjective.NoObjective, state.Objectives.Single(o => o.SideId == "red").Objective);
        var defaults = GameEngine.CreateGame(d with { Objectives = null });
        Assert.All(defaults.Objectives, o => Assert.Equal(ScenarioObjective.EliminateEnemies, o.Objective));
        Assert.Equal(new[] { "rail" }, GameEngine.StartRound(defaults, new Dice()).State.Completion!.AchievedSideIds);
        Assert.False(Run(d).State.IsCompleted);
    }

    [Fact]
    public void LivingNeutralUnitsDoNotPreventEliminateEnemies()
    {
        var d = Fixture();
        d = d with { Units = d.Units.Where(u => u.SideId != "red").ToList(),
            Objectives = [new("rail", ScenarioObjective.NoObjective), new("blue"), new("red", ScenarioObjective.NoObjective)] };
        var result = Run(d);
        Assert.Equal(new[] { "blue" }, result.State.Completion!.AchievedSideIds);
        Assert.Contains(result.State.Physical.Figures, f => f.Id == "train");
    }

    [Fact]
    public void NeutralUnitsCannotBeAttackedOrBlockHostileLineOfSight()
    {
        var d = Fixture(); var state = GameEngine.CreateGame(d);
        state.Physical = state.Physical with { Figures = [new("hero", new(0, 0)), new("train", new(1, 0)), new("enemy", new(2, 0))] };
        Assert.Equal(UnitTargetEvaluation.NotPossible, AttackRules.EvaluateFrom(state, "hero", new(0, 0), "train"));
        Assert.True(AttackRules.HasUnitLineOfSight(state, "hero", "enemy"));
        Assert.False(OrderValidation.IsValidAssignment(new(new(UnitTypeIds.Grunt, "blue"), OrderKind.Attack,
            Target: OrderTarget.ForSide("rail")), state));
    }

    [Fact]
    public void ValidationRejectsUnknownNeutralSidesAndMissingOrMisplacedDirections()
    {
        var d = Fixture();
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(d with { NeutralSides = ["missing"] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(d with { NeutralSides = ["rail", "rail"] }));
        foreach (var tile in new[] { new CellDefinition(new(0, 1), TerrainKind.Rail),
                     new(new(0, 1), TerrainKind.StoneFloor, RailDirection.East), new(new(0, 1), TerrainKind.Rail, (RailDirection)99) })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(d with { Board = d.Board with { Cells = [tile] } }));
    }
}
