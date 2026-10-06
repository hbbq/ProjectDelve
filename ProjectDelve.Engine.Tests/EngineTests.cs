using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EngineTests
{
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key ?? (request.Kind == DecisionKind.Activation
                ? request.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn).Key : null);
    }

    private sealed class ScriptedRandom(params string[] tokens) : IRandomProvider
    {
        private readonly Queue<string> _tokens = new(tokens);
        public Queue<AttackFace> AttackFaces { get; } = new();
        public Queue<DefenceFace> DefenceFaces { get; } = new();
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == _tokens.Peek()) is { } token ? Take(token) : new(_tokens.Dequeue(), "missing");
        private ActivationToken Take(ActivationToken token) { _tokens.Dequeue(); return token; }
        public AttackFace RollAttackDie() => AttackFaces.Dequeue();
        public int RollD6() => throw new InvalidOperationException("Unexpected D6 roll.");
        public DefenceFace RollDefenceDie() => DefenceFaces.Dequeue();
    }

    private static GameState State(int width = 3, int height = 3, int mov = 2,
        int rng = 1, int atk = 1, int def = 0, int hp = 2)
    {
        return new GameState
        {
            Physical = new PhysicalState(new Board(width, height, []), [new Figure("hero", new Cell(0, 0))]),
            Types = [new UnitType("hero-type", mov, rng, atk, def, hp) { Unique = true }],
            Units = [new Unit("hero", "hero-type", "blue", hp)]
        };
    }

    private static EngineResult Choose(EngineResult result, string? key, ScriptedRandom random) =>
        TestGame.Advance(result.State, new Choice(key), random);

    [Fact]
    public void InitialFigurePlacementRejectsNonPassableTerrain()
    {
        foreach (var terrain in new[] { TerrainKind.Tree, TerrainKind.StoneFloorWithTable, TerrainKind.Water })
        {
            var state = State();
            state.Physical.Board.Terrain.Add(new(new(0, 0), terrain));
            var original = JsonSerializer.Serialize(state);
            Assert.Throws<ArgumentException>(() => TestGame.StartRound(state, new ScriptedRandom("hero-type")));
            Assert.Equal(original, JsonSerializer.Serialize(state));
        }
    }

    [Theory]
    [InlineData(TerrainKind.Grass)]
    [InlineData(TerrainKind.StoneFloor)]
    public void InitialFigurePlacementAcceptsPassableTerrain(TerrainKind terrain)
    {
        var state = State();
        state.Physical.Board.Terrain.Add(new(new(0, 0), terrain));
        Assert.NotNull(TestGame.StartRound(state, new ScriptedRandom("hero-type")).NextInput);
    }

    [Fact]
    public void SuccessiveAttacksCaptureDetachedProgressiveHp_AndDeathRemovalAtItsStep()
    {
        var state = State(width: 3, height: 1, mov: 0, atk: 0, hp: 2);
        state.Types.Add(new UnitType("monster-type", 0, 1, 1, 0, 1));
        state.Units.AddRange([new("first", "monster-type", "red", 1), new("second", "monster-type", "red", 1)]);
        state.Physical.Figures.AddRange([new("first", new(1, 0)), new("second", new(2, 0))]);
        // Both Monsters can attack the Hero from their starting positions.
        state.Types[^1] = state.Types[^1] with { Rng = 2 };
        var original = JsonSerializer.Serialize(state);
        var random = new ScriptedRandom("monster-type", "hero-type");
        random.AttackFaces.Enqueue(AttackFace.Hit);
        random.AttackFaces.Enqueue(AttackFace.Hit);
        var result = TestGame.StartRound(state, random);
        result = Choose(result, "first", random);
        var first = Choose(result, "attack:hero", random);
        var firstStep = TestGame.OperationSteps(first).Single(s => TestGame.OperationEvents(first)[s.EventIndex].Kind == "AttackResolved");
        Assert.Equal(1, firstStep.StateAfter.Units.Single(u => u.Id == "hero").CurrentHp);
        // End Turn, selection of the remaining Monster, and Stay resolve automatically.
        var second = Choose(first, "attack:hero", random);
        var attack = TestGame.OperationSteps(second).Single(s => TestGame.OperationEvents(second)[s.EventIndex].Kind == "AttackResolved");
        var death = TestGame.OperationSteps(second).Single(s => TestGame.OperationEvents(second)[s.EventIndex].Kind == "UnitDied");
        Assert.Equal(0, attack.StateAfter.Units.Single(u => u.Id == "hero").CurrentHp);
        Assert.Contains(attack.StateAfter.Physical.Figures, f => f.Id == "hero");
        Assert.DoesNotContain(death.StateAfter.Physical.Figures, f => f.Id == "hero");
        Assert.Equal(1, firstStep.StateAfter.Units.Single(u => u.Id == "hero").CurrentHp);
        Assert.Equal(original, JsonSerializer.Serialize(state));
        second.State.Units.Clear();
        second.State.Physical.Figures.Clear();
        Assert.NotEmpty(death.StateAfter.Units);
        Assert.NotEmpty(death.StateAfter.Physical.Figures);
        Assert.Equal(Enumerable.Range(0, TestGame.OperationEvents(second).Count), TestGame.OperationSteps(second).Select(s => s.EventIndex));
    }

    [Fact]
    public void FullRound_MovesThenAttacksWithControlledDice_AndRemovesDeadFigure()
    {
        var state = State();
        state.Types.Add(new UnitType("monster-type", 0, 1, 1, 1, 1));
        state.Units.Add(new Unit("monster", "monster-type", "red", 1));
        state.Physical.Figures.Add(new Figure("monster", new Cell(2, 1)));
        var random = new ScriptedRandom("hero-type", "monster-type");
        random.AttackFaces.Enqueue(AttackFace.Hit);
        random.DefenceFaces.Enqueue(DefenceFace.Miss);

        var result = TestGame.StartRound(state, random);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        result = Choose(result, "1,1", random);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "MovementCompleted" &&
            e.Path!.SequenceEqual([new Cell(0, 0), new Cell(1, 0), new Cell(1, 1)]));
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        result = Choose(result, "attack:monster", random);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved" && e.Hits == 1 &&
            e.Blocks == 0 && e.Damage == 1);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "UnitDied" && e.UnitId == "monster");
        Assert.True(result.State.RoundComplete);
        Assert.Empty(result.State.Physical.Figures.Where(f => f.Id == "monster"));
        Assert.Equal(0, result.State.Units.Single(u => u.Id == "monster").CurrentHp);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "TokenDrawn" && e.TypeId == "monster-type");
    }

    [Fact]
    public void Movement_UsesTopLeftRightBottomTieBreak_AndFriendlyPassThrough()
    {
        var state = State(width: 3, height: 3, mov: 3, atk: 0);
        state.Types[0] = state.Types[0] with { Hp = 1, Unique = false };
        state.Units[0] = state.Units[0] with { CurrentHp = 1 };
        state.Units.Add(new Unit("friend", "hero-type", "blue", 1));
        state.Physical.Figures.Add(new Figure("friend", new Cell(1, 0)));
        var random = new ScriptedRandom("hero-type");
        var result = TestGame.StartRound(state, random);
        result = Choose(result, "hero", random);
        var move = result.NextInput!;
        Assert.DoesNotContain(move.Candidates, c => c.Destination == new Cell(1, 0));
        Assert.Equal([new Cell(0, 0), new Cell(1, 0), new Cell(2, 0), new Cell(2, 1)],
            move.Candidates.Single(c => c.Destination == new Cell(2, 1)).Path);
        Assert.Equal([new Cell(0, 0), new Cell(1, 0), new Cell(1, 1)],
            move.Candidates.Single(c => c.Destination == new Cell(1, 1)).Path);
    }

    [Fact]
    public void WallsAndHostileOccupancyBlockMovement_AndStayCompletesMove()
    {
        var state = State(width: 2, height: 2, mov: 2, atk: 0);
        state.Physical.Board.Edges.Add(new Edge(new Cell(0, 0), new Cell(1, 0), EdgeKind.Wall));
        state.Types.Add(new UnitType("enemy", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit("enemy", "enemy", "red", 1));
        state.Physical.Figures.Add(new Figure("enemy", new Cell(0, 1)));
        var random = new ScriptedRandom("hero-type", "enemy");
        var result = TestGame.StartRound(state, random);
        Assert.True(result.State.RoundComplete); // No legal destinations or attacks.
        Assert.Null(result.NextInput);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "MovementCompleted" && e.Path!.Count == 1);
    }

    [Fact]
    public void InvalidDecisionLeavesOriginalStateUntouched_AndPendingStateRoundTripsAsJson()
    {
        var random = new ScriptedRandom("hero-type");
        var result = TestGame.StartRound(State(), random);
        var json = JsonSerializer.Serialize(result.State);
        var restored = JsonSerializer.Deserialize<GameState>(json)!;
        Assert.Equal(result.NextInput!.Kind, restored.Pending!.Kind);
        Assert.Equal(result.NextInput.Candidates.Select(c => c.Key), restored.Pending.Candidates.Select(c => c.Key));
        Assert.Throws<ArgumentException>(() => TestGame.Advance(restored, new Choice("invented"), random));
        Assert.Equal(DecisionKind.Activation, restored.Pending!.Kind);
        result = TestGame.Advance(restored, new Choice("1,0"), random);
        Assert.True(result.State.RoundComplete);
    }

    [Fact]
    public void MeleeAllowsDiagonalTarget_AndEndTurnSkipsAttack()
    {
        var state = State(width: 2, height: 2, mov: 0);
        state.Types.Add(new UnitType("enemy", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit("enemy", "enemy", "red", 1));
        state.Physical.Figures.Add(new Figure("enemy", new Cell(1, 1)));
        var random = new ScriptedRandom("hero-type", "enemy");
        var result = TestGame.StartRound(state, random);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal("attack:enemy", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Key);
        result = Choose(result, null, random);
        Assert.Equal(1, result.State.Units.Single(u => u.Id == "enemy").CurrentHp);
    }

    [Fact]
    public void UnrelatedFeaturedEdge_DoesNotDiscardClearAttackTarget()
    {
        var state = State(width: 3, height: 2, mov: 0, rng: 2);
        state.Physical.Board.Edges.Add(new Edge(new Cell(1, 1), new Cell(2, 1), EdgeKind.Wall));
        state.Types.Add(new UnitType("enemy", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit("enemy", "enemy", "red", 1));
        state.Physical.Figures.Add(new Figure("enemy", new Cell(2, 0)));
        var random = new ScriptedRandom("hero-type", "enemy");

        var result = TestGame.StartRound(state, random);

        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal("attack:enemy", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Key);
    }

    [Fact]
    public void FeaturedEdgeEndingAtLosCorner_LeavesFreeCornerPassageLegal()
    {
        var state = State(width: 2, height: 2, mov: 0);
        state.Physical.Board.Edges.Add(new Edge(new Cell(0, 0), new Cell(1, 0), EdgeKind.Wall));
        state.Types.Add(new UnitType("enemy", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit("enemy", "enemy", "red", 1));
        state.Physical.Figures.Add(new Figure("enemy", new Cell(1, 1)));
        var random = new ScriptedRandom("hero-type", "enemy");

        var result = TestGame.StartRound(state, random);

        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal("attack:enemy", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Key);
    }

    [Fact]
    public void HostileUnitOnAnotherTargetsLos_DoesNotDiscardClearAttackTarget()
    {
        var state = State(width: 3, height: 3, mov: 0, rng: 3);
        state.Types.Add(new UnitType("enemy", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit("intervening", "enemy", "red", 1));
        state.Units.Add(new Unit("behind", "enemy", "red", 1));
        state.Units.Add(new Unit("clear", "enemy", "red", 1));
        state.Physical.Figures.Add(new Figure("intervening", new Cell(1, 0)));
        state.Physical.Figures.Add(new Figure("behind", new Cell(2, 0)));
        state.Physical.Figures.Add(new Figure("clear", new Cell(0, 2)));
        var random = new ScriptedRandom("hero-type", "enemy");

        var result = TestGame.StartRound(state, random);

        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Contains(result.NextInput.Candidates, candidate => candidate.TargetId == "clear");
        Assert.DoesNotContain(result.NextInput.Candidates, candidate => candidate.TargetId == "behind");
    }

    [Fact]
    public void SameSideIsFriendlyEvenAcrossDifferentUnitTypes()
    {
        var state = State(width: 2, height: 1, mov: 1);
        state.Types.Add(new UnitType("other", 0, 0, 0, 0, 1));
        state.Units.Add(new Unit("other", "other", "blue", 1));
        state.Physical.Figures.Add(new Figure("other", new Cell(1, 0)));
        var random = new ScriptedRandom("hero-type", "other");
        var result = TestGame.StartRound(state, random);
        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
    }

    [Fact]
    public void StayCompletesMovementWithoutChangingPosition()
    {
        var random = new ScriptedRandom("hero-type");
        var result = TestGame.StartRound(State(atk: 0), random);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.DoesNotContain(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Move && c.Destination == new Cell(0, 0));
        result = Choose(result, null, random);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "MovementCompleted" &&
            e.Path!.SequenceEqual([new Cell(0, 0)]));
        Assert.Equal(new Cell(0, 0), result.State.Physical.Figures.Single().Position);
    }

    [Fact]
    public void DamageUsesHitsMinusBlocksWhileCurrentHpStopsAtZero()
    {
        var state = State(width: 2, height: 1, mov: 0, atk: 3);
        state.Types.Add(new UnitType("enemy", 0, 0, 0, 1, 1));
        state.Units.Add(new Unit("enemy", "enemy", "red", 1));
        state.Physical.Figures.Add(new Figure("enemy", new Cell(1, 0)));
        var random = new ScriptedRandom("hero-type", "enemy");
        random.AttackFaces.Enqueue(AttackFace.Hit);
        random.AttackFaces.Enqueue(AttackFace.Hit);
        random.AttackFaces.Enqueue(AttackFace.Hit);
        random.DefenceFaces.Enqueue(DefenceFace.Miss);
        var result = TestGame.StartRound(state, random);
        result = Choose(result, "attack:enemy", random);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved" && e.Damage == 3);
        Assert.Equal(0, result.State.Units.Single(u => u.Id == "enemy").CurrentHp);
    }

    [Fact]
    public void DefenceBlockCancelsHitWithoutRemovingFigure()
    {
        var state = State(width: 2, height: 1, mov: 0);
        state.Types.Add(new UnitType("enemy", 0, 0, 0, 1, 1));
        state.Units.Add(new Unit("enemy", "enemy", "red", 1));
        state.Physical.Figures.Add(new Figure("enemy", new Cell(1, 0)));
        var random = new ScriptedRandom("hero-type", "enemy");
        random.AttackFaces.Enqueue(AttackFace.Hit);
        random.DefenceFaces.Enqueue(DefenceFace.Block);
        var result = TestGame.StartRound(state, random);
        result = Choose(result, "attack:enemy", random);
        Assert.Contains(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved" && e.Hits == 1 &&
            e.Blocks == 1 && e.Damage == 0);
        Assert.Contains(result.State.Physical.Figures, f => f.Id == "enemy");
    }
}
