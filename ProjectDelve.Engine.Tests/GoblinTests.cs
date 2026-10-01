using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class GoblinTests
{
    private sealed class Random(bool hit = false) : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => hit ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key ?? (request.Kind == DecisionKind.Activation
                ? request.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn).Key : null);
    }

    private static GameState State(Cell? start = null, Cell? target = null, int width = 5, int height = 3) => new()
    {
        Physical = new(new Board(width, height, []),
            [new("goblin", start ?? new(2, 1)), new("hero", target ?? new(1, 1))]),
        Types = [UnitType.Goblin(), new("hero-type", 0, 0, 0, 0, 4)],
        Units = [new("goblin", "goblin-type", "red", 1), new("hero", "hero-type", "blue", 4)]
    };

    private static EngineResult Act(GameState state)
    {
        var move = GameEngine.StartRound(state, new Random());
        return GameEngine.Advance(move.State, new Choice(null), new Random());
    }

    private static EngineResult Extra(GameState state, bool hit = false) =>
        GameEngine.Advance(Act(state).State, new Choice("attack:hero"), new Random(hit));

    [Fact]
    public void GoblinContentHasSpecifiedStatsAndSerializableComponents()
    {
        var type = UnitType.Goblin();
        Assert.Equal((4, 1, 2, 2, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(UnitBehavior.BackAwayAfterAttack, type.Behaviors);
        Assert.Equal(new MoveAfterAttack(1), type.MoveAfterAttack);
        Assert.Equal(type, JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type)));
    }

    [Fact]
    public void AttackConsumesActionAndResolvesExtraMoveBeforeNextUnitActivates()
    {
        var state = State();
        state.Units.Add(new("second", "goblin-type", "red", 1));
        state.Physical.Figures.Add(new("second", new(0, 2)));
        var result = GameEngine.StartRound(state, new Random());
        result = GameEngine.Advance(result.State, new Choice("goblin"), new Random());
        result = GameEngine.Advance(result.State, new Choice("stay"), new Random());
        result = GameEngine.Advance(result.State, new Choice("attack:hero"), new Random());
        Assert.Equal("AttackResolved", Assert.Single(result.Events).Kind);
        Assert.True(result.State.MoveDone);
        Assert.DoesNotContain("goblin", result.State.CompletedUnitIds);
        Assert.True(result.State.ActionDone);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        Assert.Equal("goblin", result.NextInput!.UnitId);
        Assert.Equal(DecisionKind.Move, result.NextInput.Kind);
        Assert.True(result.NextInput.IsMoveAfterAttack);
        Assert.Equal(1, result.State.MoveAfterAttackAllowance);
        Assert.All(result.NextInput.Candidates, c => Assert.Equal(2, c.Path!.Count));
        Assert.DoesNotContain(result.NextInput.Candidates, c => c.Key == "4,1");
        // Restore a decision boundary; edited client candidates must not alter legality.
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        restored.Pending!.Candidates.Clear();
        var moved = GameEngine.Advance(restored, new Choice("3,1"), new Random());
        var movement = Assert.Single(moved.Events, e => e.Kind == "MovementCompleted");
        Assert.True(movement.IsMoveAfterAttack);
        Assert.Equal(new[] { new Cell(2, 1), new Cell(3, 1) }, movement.Path);
        Assert.Equal("second", moved.State.CurrentUnitId);
        Assert.Equal(DecisionKind.Activation, moved.NextInput!.Kind);
        Assert.Null(moved.State.MoveAfterAttackAllowance);
        var stayed = GameEngine.Advance(moved.State, new Choice("stay"), new Random());
        Assert.True(GameEngine.Advance(stayed.State, new Choice("end-turn"), new Random()).State.RoundComplete);
    }

    [Fact]
    public void DamageAndDeathResolveBeforeMoveCandidatesAreGenerated()
    {
        var state = State();
        state.Units[1] = state.Units[1] with { CurrentHp = 1 };
        var result = Extra(state, hit: true);
        Assert.Equal(new[] { "AttackResolved", "UnitDied" }, result.Events.Select(e => e.Kind));
        Assert.Equal(2, result.Events[0].Damage);
        Assert.Equal(0, result.State.Units[1].CurrentHp);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "hero");
        Assert.Contains(result.NextInput!.Candidates, c => c.Key == "1,1");
        var done = GameEngine.Advance(result.State, new Choice(null), new Random());
        Assert.True(done.State.RoundComplete);
        Assert.True(done.Events[0].IsMoveAfterAttack);
        Assert.Single(done.Events[0].Path!);
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    [InlineData(EdgeKind.WallWithWindow)]
    public void ExtraMoveUsesNormalTerrainEdgesAndOccupancy(EdgeKind edge)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(2, 1), new(2, 0), edge));
        state.Physical.Board.Terrain.Add(new(new(3, 1), TerrainKind.Water));
        state.Units.Add(new("friend", "goblin-type", "red", 1));
        state.Physical.Figures.Add(new("friend", new(2, 2)));
        // Resume this Unit after its normal Move.
        state.Round = 1;
        state.ActiveTypeId = "goblin-type";
        state.MoveDone = true;
        state.CurrentUnitId = "goblin";
        state.Pending = new(DecisionKind.Act, "goblin-type", "goblin", [], true);
        var result = GameEngine.Advance(state, new Choice("attack:hero"), new Random());
        // Every adjacent destination is blocked; the extra Move automatically stays.
        Assert.Equal(new[] { "AttackResolved", "MovementCompleted" }, result.Events.Select(e => e.Kind));
        Assert.True(result.Events[1].IsMoveAfterAttack);
        Assert.Single(result.Events[1].Path!);
    }

    [Fact]
    public void NoActionNoLegalAttackAndDoorActionDoNotGrantExtraMove()
    {
        var skipped = GameEngine.Advance(Act(State()).State, new Choice(null), new Random());
        Assert.True(skipped.State.RoundComplete);
        Assert.DoesNotContain(skipped.Events, e => e.IsMoveAfterAttack);
        var distant = Act(State(target: new(0, 0), start: new(4, 2)));
        Assert.True(distant.State.RoundComplete);
        var doorState = State();
        doorState.Types[0] = doorState.Types[0] with { Actions = UnitAction.OpenDoor };
        doorState.Physical.Board.Edges.Add(new(new(2, 1), new(3, 1), EdgeKind.ClosedDoor));
        var door = Act(doorState);
        var opened = GameEngine.Advance(door.State, new Choice(door.NextInput!.Candidates[0].Key), new Random());
        Assert.True(opened.State.RoundComplete);
        Assert.DoesNotContain(opened.Events, e => e.IsMoveAfterAttack);
    }

    [Fact]
    public void CapabilityAppliesToHumanHeroAndUsesItsOwnAllowance()
    {
        var state = State();
        state.Types[0] = UnitType.Hero("hero-with-capability", 0, 1, 2, 2, 1) with
            { MoveAfterAttack = new(2) };
        state.Units[0] = state.Units[0] with { TypeId = state.Types[0].Id, SideId = "blue" };
        state.Units[1] = state.Units[1] with { SideId = "red" };
        var act = GameEngine.StartRound(state, new Random());
        var extra = GameEngine.Advance(act.State, new Choice("attack:hero"), new Random());
        Assert.True(extra.NextInput!.IsMoveAfterAttack);
        Assert.Contains(extra.NextInput.Candidates, c => c.Key == "4,1" && c.Path!.Count == 3);
        Assert.DoesNotContain(extra.NextInput.Candidates, c => c.Path!.Count > 3);
        var moved = GameEngine.Advance(extra.State, new Choice("4,1"), new Random());
        Assert.Equal(new Cell(4, 1), moved.State.Physical.Figures[0].Position);
        Assert.True(moved.State.RoundComplete);
    }

    [Fact]
    public void OrdinaryGoblinMoveApproachesAndStaysWhenAlreadyAbleToAttack()
    {
        var provider = new DefaultMonsterProvider();
        var adjacent = GameEngine.StartRound(State(), new Random());
        Assert.False(adjacent.NextInput!.IsMoveAfterAttack);
        Assert.Equal("stay", provider.Choose(adjacent.NextInput, new GameplayQueries(adjacent.State)));
        var distant = GameEngine.StartRound(State(start: new(0, 0), target: new(6, 0), width: 7, height: 1), new Random());
        Assert.Equal("4,0", provider.Choose(distant.NextInput!, new GameplayQueries(distant.State)));
    }

    [Theory]
    [InlineData(1, 1)] // Orthogonal.
    [InlineData(1, 0)] // Diagonal.
    public void ClearLosAdjacencyCountsRegardlessOfHostileAttackStatsOrContent(int x, int y)
    {
        var state = State(target: new(x, y));
        state.Types[1] = state.Types[1] with
        {
            Rng = 0, Atk = 0, Actions = UnitAction.None, MoveAfterAttack = new(5),
            Behaviors = UnitBehavior.MaximizeAttackDistance
        };
        var queries = new GameplayQueries(state);
        Assert.Equal(NormalAttackEvaluation.NotPossible,
            AttackRules.EvaluateFrom(state, "hero", new(x, y), "goblin"));
        Assert.True(queries.HasNearbyHostileThreatFrom("goblin", new(2, 1)));
        var extra = Extra(state);
        var moved = GameEngine.Advance(extra.State, new DefaultMonsterProvider(), new Random());
        Assert.Equal(new Cell(3, 1), moved.State.Physical.Figures[0].Position);
        Assert.False(new GameplayQueries(moved.State).HasNearbyHostileThreatFrom("goblin", new(3, 1)));
        Assert.True(moved.Events[0].IsMoveAfterAttack);
        Assert.True(moved.State.RoundComplete);
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.WallWithWindow, true)]
    public void NearbyThreatUsesOrdinaryEdgeLos(EdgeKind edge, bool threatened)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), edge));
        var queries = new GameplayQueries(state);
        Assert.Equal(threatened, queries.HasNearbyHostileThreatFrom("goblin", new(2, 1)));
        var move = GameEngine.StartRound(state, new Random());
        var request = move.NextInput! with { IsMoveAfterAttack = true };
        Assert.Equal(threatened ? "3,1" : "stay", new DefaultMonsterProvider().Choose(request, queries));
    }

    [Fact]
    public void DiagonalThreatUsesSharedCornerLosRule()
    {
        var state = State(start: new(2, 2), target: new(1, 1));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        // One corner passage remains open.
        Assert.True(new GameplayQueries(state).HasNearbyHostileThreatFrom("goblin", new(2, 2)));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 2), EdgeKind.ClosedDoor));
        Assert.False(new GameplayQueries(state).HasNearbyHostileThreatFrom("goblin", new(2, 2)));
    }

    [Fact]
    public void CannotEscapeOneHostileIntoAnother()
    {
        var state = State();
        state.Units.Add(new("other", "hero-type", "blue", 4));
        state.Physical.Figures.Add(new("other", new(4, 1)));
        var extra = Extra(state);
        var queries = new GameplayQueries(extra.State);
        Assert.Contains(extra.NextInput!.Candidates, c => c.Key == "3,1");
        // Right escapes the attacked Hero but enters the other Hero's nearby area.
        Assert.True(queries.HasNearbyHostileThreatFrom("goblin", new(3, 1)));
        Assert.All(extra.NextInput.Candidates, c => Assert.True(queries.HasNearbyHostileThreatFrom("goblin", c.Destination!)));
        var stayed = GameEngine.Advance(extra.State, new DefaultMonsterProvider(), new Random());
        Assert.Equal(new Cell(2, 1), stayed.State.Physical.Figures[0].Position);
        Assert.Single(stayed.Events[0].Path!);
        Assert.True(stayed.Events[0].IsMoveAfterAttack);
    }

    [Fact]
    public void StaysWhenEveryLegalDestinationRemainsThreatened()
    {
        var extra = Extra(State(start: new(1, 0), target: new(0, 0), width: 2, height: 2));
        Assert.NotEmpty(extra.NextInput!.Candidates);
        Assert.Null(new DefaultMonsterProvider().Choose(extra.NextInput, new GameplayQueries(extra.State)));
        var done = GameEngine.Advance(extra.State, new DefaultMonsterProvider(), new Random());
        Assert.True(done.State.RoundComplete);
        Assert.Equal(new Cell(1, 0), done.State.Physical.Figures[0].Position);
    }

    [Fact]
    public void AlreadyOutsideNearbyThreatsStaysEvenWithLegalMovesFartherAway()
    {
        var state = State(start: new(2, 1), target: new(0, 1));
        var move = GameEngine.StartRound(state, new Random());
        var request = move.NextInput! with { IsMoveAfterAttack = true };
        Assert.Contains(request.Candidates, c => c.Key == "3,1");
        Assert.False(new GameplayQueries(state).HasNearbyHostileThreatFrom("goblin", new(2, 1)));
        Assert.Equal("stay", new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void KillingOneOfTwoNearbyHostilesBacksAwayFromSurvivor()
    {
        var state = State();
        state.Units[1] = state.Units[1] with { CurrentHp = 1 };
        state.Units.Add(new("survivor", "hero-type", "blue", 4));
        state.Physical.Figures.Add(new("survivor", new(1, 0)));
        var extra = Extra(state, hit: true);
        Assert.Equal(new[] { "AttackResolved", "UnitDied" }, extra.Events.Select(e => e.Kind));
        Assert.Equal(0, extra.State.Units.Single(u => u.Id == "hero").CurrentHp);
        Assert.True(new GameplayQueries(extra.State).HasNearbyHostileThreatFrom("goblin", new(2, 1)));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(extra.State))!;
        var done = GameEngine.Advance(restored, new DefaultMonsterProvider(), new Random());
        Assert.Equal(new Cell(3, 1), done.State.Physical.Figures[0].Position);
        Assert.False(new GameplayQueries(done.State).HasNearbyHostileThreatFrom("goblin", new(3, 1)));
        Assert.True(done.Events[0].IsMoveAfterAttack);
        Assert.True(done.State.RoundComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualLengthEscapesUseTopLeftOrderRegardlessOfCandidateOrder(bool reverse)
    {
        var extra = Extra(State(start: new(2, 2), target: new(1, 1), height: 5));
        if (reverse) extra.NextInput!.Candidates.Reverse();
        // Right (3,2) and down (2,3) escape. Right is first by row.
        Assert.Equal("3,2", new DefaultMonsterProvider().Choose(extra.NextInput!, new GameplayQueries(extra.State)));
        var state = State(start: new(2, 2), target: new(2, 1), height: 5);
        state.Types[0] = state.Types[0] with { MoveAfterAttack = new(3) };
        extra = Extra(state);
        var request = extra.NextInput! with
        {
            Candidates = extra.NextInput!.Candidates.Where(c => c.Key is "0,1" or "4,1" or "1,4").ToList()
        };
        if (reverse) request.Candidates.Reverse();
        Assert.All(request.Candidates, c => Assert.Equal(4, c.Path!.Count));
        Assert.Equal("0,1", new DefaultMonsterProvider().Choose(request, new GameplayQueries(extra.State)));
    }

    [Fact]
    public void FriendlyAndDeadUnitsDoNotCountAsNearbyThreats()
    {
        var state = State();
        state.Units[1] = state.Units[1] with { SideId = "red" };
        Assert.False(new GameplayQueries(state).HasNearbyHostileThreatFrom("goblin", new(2, 1)));
        state.Units[1] = state.Units[1] with { SideId = "blue", CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "hero");
        Assert.False(new GameplayQueries(state).HasNearbyHostileThreatFrom("goblin", new(2, 1)));
    }

    [Fact]
    public void ShorterCanonicalMovementPathWinsBeforeBoardOrder()
    {
        var state = State(start: new(2, 2), target: new(1, 1), width: 5, height: 5);
        state.Types[0] = state.Types[0] with { MoveAfterAttack = new(3) };
        var extra = Extra(state);
        // Both destinations escape; the one-step path wins over the earlier board position.
        var request = extra.NextInput! with
        {
            Candidates = extra.NextInput!.Candidates.Where(c => c.Key is "0,3" or "3,2").ToList(),
            AllowsNone = false
        };
        Assert.Equal(4, request.Candidates.Single(c => c.Key == "0,3").Path!.Count);
        Assert.Equal(2, request.Candidates.Single(c => c.Key == "3,2").Path!.Count);
        Assert.Equal("3,2", new DefaultMonsterProvider().Choose(request, new GameplayQueries(extra.State)));
    }

    [Fact]
    public void ChoosesShortestEscapeWithoutMaximizingDistanceBeyondNearbyThreats()
    {
        var state = State(start: new(1, 0), target: new(0, 0), width: 5, height: 1);
        state.Types[0] = state.Types[0] with { MoveAfterAttack = new(2) };
        var extra = Extra(state);
        Assert.Contains(extra.NextInput!.Candidates, c => c.Key == "3,0" && c.Path!.Count == 3);
        var done = GameEngine.Advance(extra.State, new DefaultMonsterProvider(), new Random());
        Assert.Equal(new Cell(2, 0), done.State.Physical.Figures[0].Position);
        Assert.Equal(2, done.Events[0].Path!.Count);
        Assert.True(done.Events[0].IsMoveAfterAttack);
    }

    [Fact]
    public void ClientCannotChangeExtraMoveAllowanceOrDecisionContextThroughPendingCandidates()
    {
        var extra = Extra(State());
        extra.State.Pending = extra.State.Pending! with
        {
            IsMoveAfterAttack = false,
            Candidates = [new("4,1", new(4, 1), [new(2, 1), new(3, 1), new(4, 1)])]
        };
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(extra.State, new Choice("4,1"), new Random()));
        var result = GameEngine.Advance(extra.State, new Choice("3,1"), new Random());
        Assert.True(result.Events[0].IsMoveAfterAttack);
    }

    [Fact]
    public void ZeroAllowanceAutomaticallyCompletesExtraMoveAndNegativeAllowanceIsRejected()
    {
        var state = State();
        state.Types[0] = state.Types[0] with { MoveAfterAttack = new(0) };
        var result = Extra(state);
        Assert.True(result.State.RoundComplete);
        Assert.True(result.Events[1].IsMoveAfterAttack);
        Assert.Single(result.Events[1].Path!);
        state.Types[0] = state.Types[0] with { MoveAfterAttack = new(-1) };
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Random()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoNearbyHostileChoosesStayAndCompletesExtraMoveNormally(bool noHostiles)
    {
        var state = State();
        state.Units[1] = state.Units[1] with { CurrentHp = 1 };
        var extra = Extra(state, hit: true);
        if (!noHostiles)
        {
            extra.State.Units.Add(new("isolated", "hero-type", "blue", 4));
            extra.State.Physical.Figures.Add(new("isolated", new(0, 0)));
            extra.State.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor));
            extra.State.Physical.Board.Edges.Add(new(new(0, 0), new(0, 1), EdgeKind.Wall));
        }
        var queries = new GameplayQueries(extra.State);
        Assert.False(queries.HasNearbyHostileThreatFrom("goblin", new(2, 1)));
        Assert.True(extra.NextInput!.IsMoveAfterAttack);
        Assert.NotEmpty(extra.NextInput.Candidates);
        Assert.All(extra.NextInput.Candidates, c =>
            Assert.False(queries.HasNearbyHostileThreatFrom("goblin", c.Destination!)));
        var provider = new DefaultMonsterProvider();
        Assert.Null(provider.Choose(extra.NextInput, queries));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(extra.State))!;
        var stayed = GameEngine.Advance(restored, provider, new Random());
        var movement = Assert.Single(stayed.Events, e => e.IsMoveAfterAttack);
        Assert.Equal("MovementCompleted", movement.Kind);
        Assert.Equal(new[] { new Cell(2, 1) }, movement.Path);
        Assert.Equal(new Cell(2, 1), stayed.State.Physical.Figures[0].Position);
        Assert.Null(stayed.State.MoveAfterAttackAllowance);
        Assert.Null(stayed.State.CurrentUnitId);
        Assert.Null(stayed.NextInput);
        Assert.True(stayed.State.RoundComplete);
        // The fallback is only a provider preference; other providers can still move.
        var moved = GameEngine.Advance(extra.State, new Choice("3,1"), new Random());
        Assert.Equal(new Cell(3, 1), moved.State.Physical.Figures[0].Position);
        Assert.True(moved.State.RoundComplete);
    }
}
