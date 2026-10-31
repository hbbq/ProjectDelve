using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LabyrinthShaperTests
{
    private const string Key = "ability:reshape-room";
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
    private static GameState World(Cell? center = null)
    {
        var boss = UnitRoster.LabyrinthShaper(); var enemy = UnitRoster.Grunt();
        return new()
        {
            Physical = new(new Board(7, 7, []), [new("boss", center ?? new(3, 3)), new("enemy", center == new Cell(6, 6) ? new(0, 0) : new(6, 6))]),
            Types = [boss, enemy], Units = [boss.CreateUnit("boss", "red"), enemy.CreateUnit("enemy", "blue")]
        };
    }
    private static EngineResult Start(GameState world) => TestGame.StartRound(world, new Dice(), false);
    private static EngineResult Pick(EngineResult result, string key) => TestGame.Advance(result.State, new Choice(key), new Dice(), false);

    [Fact]
    public void CanonicalContentAndSerializationExposeUnlimitedConcreteBonusAction()
    {
        var type = UnitType.LabyrinthShaper();
        Assert.Equal(type.Id, CanonicalUnitTypes.Find(UnitTypeIds.LabyrinthShaper)!.Id);
        Assert.Equal((2, 1, 2, 2, 5), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.True(type.Unique); Assert.Equal(Footprint.OneByOne, type.Footprint);
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(UnitBehavior.PreferReshapeRoom, type.Behaviors);
        var ability = Assert.Single(type.LocalAbilities); Assert.IsType<ReshapeRoom>(ability.Mechanic); Assert.Null(ability.Usage.MaxUses);
        Assert.Empty(type.CreateUnit("boss", "red").AbilityUseCounts);
        var card = Assert.Single(type.CardEntries(), c => c.Id == "ability:reshape-room");
        Assert.Equal("Bonus Action", card.Category);
        Assert.Equal("Replace Edges in the 3 \u00D7 3 area centered on this Unit to form a room with an opening at the middle of each of its four sides.", card.Description);
        Assert.NotNull(JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type))!.Mechanic<ReshapeRoom>());
    }

    [Theory]
    [InlineData(3, 3, 8)]
    [InlineData(0, 0, 2)]
    [InlineData(6, 0, 2)]
    [InlineData(0, 6, 2)]
    [InlineData(6, 6, 2)]
    [InlineData(0, 3, 4)]
    [InlineData(3, 0, 4)]
    [InlineData(6, 3, 4)]
    [InlineData(3, 6, 4)]
    public void ExactRoomPatternClearsAllInteriorFeaturesAndClipsWithoutBoundaryFeatures(int x, int y, int walls)
    {
        var world = World(new(x, y));
        // Seed every existing Edge with a Door: the pattern must explicitly clear all gaps and interior Edges.
        for (var row = 0; row < 7; row++)
        for (var col = 0; col < 7; col++)
        {
            if (col < 6) world.Physical.Board.Edges.Add(new(new(col, row), new(col + 1, row), EdgeKind.ClosedDoor));
            if (row < 6) world.Physical.Board.Edges.Add(new(new(col, row), new(col, row + 1), EdgeKind.OpenDoor));
        }
        var original = world.Physical.Board.Edges.ToArray();
        var result = Pick(Start(world), Key);
        var board = result.State.Physical.Board;
        foreach (var edge in original)
        {
            var horizontal = edge.A.Y == edge.B.Y;
            var cross = horizontal ? edge.A.X - x : edge.A.Y - y;
            var offset = horizontal ? edge.A.Y - y : edge.A.X - x;
            var affected = offset >= -1 && offset <= 1 && cross >= -2 && cross <= 1;
            var expected = affected ? (cross is -2 or 1 && offset != 0 ? EdgeKind.Wall : EdgeKind.None) : edge.Kind;
            Assert.Equal(expected, board.EdgeBetween(edge.A, edge.B));
        }
        Assert.Equal(walls, board.Edges.Count(e => e.Kind == EdgeKind.Wall));
        Assert.All(board.Edges, e => Assert.True(EdgeReplacementRules.CanReplace(result.State, e)));
        Assert.Equal(world.Physical.Figures, result.State.Physical.Figures);
        Assert.Empty(board.Terrain); Assert.False(result.State.MoveDone); Assert.False(result.State.ActionDone);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Key == Key);
    }

    [Fact]
    public void PatternKeepsForbiddenWaterWallDoorWhileOtherReplacementsProceed()
    {
        var world = World(); var door = new Edge(new(1, 2), new(2, 2), EdgeKind.OpenDoor);
        world.Physical.Board.Edges.Add(door);
        world.Physical.Board.Terrain.AddRange([new(door.A, TerrainKind.Water), new(door.B, TerrainKind.Water)]);
        var result = Pick(Start(world), Key);
        Assert.Contains(door, result.State.Physical.Board.Edges);
        Assert.Equal(7, result.State.Physical.Board.Edges.Count(e => e.Kind == EdgeKind.Wall));
    }

    [Fact]
    public void PatternDoesNotCutAcrossLargeLyingFigureAndCanClearItsInternalOpenDoor()
    {
        var world = World(); var large = UnitRoster.RedDragon(); world.Types.Add(large);
        world.Units.Add(large.CreateUnit("large", "red")); world.Physical.Figures.Add(new("large", new(1, 2), Posture.Lying));
        var preserved = new Edge(new(1, 2), new(2, 2), EdgeKind.OpenDoor);
        var cleared = new Edge(new(2, 2), new(2, 3), EdgeKind.OpenDoor);
        world.Physical.Board.Edges.AddRange([preserved, cleared]);
        var result = Pick(Start(world), Key);
        Assert.Contains(preserved, result.State.Physical.Board.Edges);
        Assert.Equal(EdgeKind.None, result.State.Physical.Board.EdgeBetween(cleared.A, cleared.B));
        Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single(f => f.Id == "large").Posture);
    }

    [Fact]
    public void HumanCanReshapeAfterMovingAndAutomatedBehaviorPrefersItBeforeMovement()
    {
        var start = Start(World());
        Assert.Equal(Key, new DefaultAutomatedProvider().Choose(start.NextInput!, new GameplayQueries(start.State)));
        var move = start.NextInput!.Candidates.First(c => c.Kind == ActivationChoiceKind.Move);
        var moved = Pick(start, move.Key);
        Assert.Contains(moved.NextInput!.Candidates, c => c.Key == Key);
        var reshaped = Pick(moved, Key);
        Assert.Contains(reshaped.Events, e => e.AbilityName == "Reshape Room");
        Assert.Equal(move.Destination, reshaped.State.Physical.Figures.Single(f => f.Id == "boss").Position);
    }

    [Theory]
    [InlineData(WorldEffect.Surge, 2)]
    [InlineData(WorldEffect.Hesitation, 0)]
    public void WorldBonusModifiersUseOrdinaryPerActivationCounters(WorldEffect effect, int opportunities)
    {
        var world = World(); world.WorldEffects = new(1, 1);
        world.WorldDeck = new() { DrawPile = [new("world", effect)] };
        var result = Start(world);
        for (var used = 0; used < opportunities; used++)
        {
            Assert.Contains(result.NextInput!.Candidates, c => c.Key == Key);
            result = Pick(result, Key);
        }
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Key == Key);
        Assert.Equal(opportunities, result.State.BonusActionUsesThisActivation("unit:reshape-room"));
        Assert.Empty(result.State.Units[0].AbilityUseCounts);
    }

    [Fact]
    public void FullyClippedNoOpConsumesBonusOpportunityAndNoOpIsAvailableNextRound()
    {
        var world = World(new(0, 0));
        var clipped = World(new(0, 0));
        clipped.Physical = clipped.Physical with { Board = new Board(1, 1, []) };
        clipped.Units.RemoveAt(1); clipped.Physical.Figures.RemoveAt(1);
        var completed = Pick(Start(clipped), Key);
        Assert.Empty(completed.State.Physical.Board.Edges);
        Assert.Equal(1, completed.ResolutionSteps.Single(step => completed.Events[step.EventIndex].Kind == "ActionUsed")
            .StateAfter.BonusActionUsesThisActivation("unit:reshape-room"));
        Assert.True(completed.State.IsCompleted);

        // Keep a living hostile beyond impassable Cells so a no-op can also be exercised next Round.
        world.Physical = world.Physical with { Board = new Board(1, 4, [])
            { Terrain = [new(new(0, 1), TerrainKind.Water), new(new(0, 2), TerrainKind.Water)] } };
        world.Types[1] = new("idle-enemy", 0, 0, 0, 0, 1);
        world.Units[1] = new("enemy", "idle-enemy", "blue", 1);
        world.Physical.Figures[1] = new("enemy", new(0, 3));
        var result = Pick(Start(world), Key);
        Assert.Empty(result.State.Physical.Board.Edges);
        var used = result.ResolutionSteps.Single(step => result.Events[step.EventIndex].Kind == "ActionUsed").StateAfter;
        Assert.Equal(1, used.BonusActionUsesThisActivation("unit:reshape-room"));
        Assert.True(result.State.RoundComplete);
        result = TestGame.StartRound(result.State, new Dice(), false);
        Assert.Contains(result.NextInput!.Candidates, c => c.Key == Key);
    }

    [Fact]
    public void AutomatedActivationReshapesThenMovesThenUsesNormalAttack()
    {
        var world = World(); world.Physical.Figures[1] = new("enemy", new(3, 5));
        TestGame.Author(world);
        world.Controllers[0] = world.Controllers[0] with { Controller = ControllerKind.Automated };
        var result = GameEngine.StartRound(world, new Dice(), false);
        List<RulesEvent> events = [.. result.Events];
        for (var step = 0; step < 20 && result.NextInput?.UnitId == "boss"; step++)
        {
            result = GameEngine.Advance(result.State, new DefaultAutomatedProvider(), new Dice(), false);
            events.AddRange(result.Events);
        }
        var reshape = events.FindIndex(e => e.Kind == "ActionUsed" && e.AbilityName == "Reshape Room");
        var move = events.FindIndex(e => e.Kind == "MovementCompleted");
        var attack = events.FindIndex(e => e.Kind == "AttackStarted" && e.UnitId == "boss");
        Assert.True(reshape >= 0); Assert.True(move > reshape); Assert.True(attack > move);
    }

    [Fact]
    public void LyingBossOnlyStandsWithoutReshaping()
    {
        var world = World(); world.Physical.Figures[0] = world.Physical.Figures[0] with { Posture = Posture.Lying };
        var result = Start(world);
        Assert.Empty(result.State.Physical.Board.Edges);
        Assert.DoesNotContain(result.Events, e => e.AbilityName == "Reshape Room");
        Assert.Equal(Posture.Upright, result.State.Physical.Figures[0].Posture);
    }
}
