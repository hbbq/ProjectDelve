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
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
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
        Assert.Equal(UnitBehavior.RetreatAfterAttack, type.Behaviors);
        Assert.Equal(new MoveAfterAttack(1), type.MoveAfterAttack);
        Assert.Equal(type, JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type)));
    }

    [Fact]
    public void AttackCompletesActAndImmediatelyOffersOneStepMoveThenResumesActivation()
    {
        var state = State();
        state.Units.Add(new("second", "goblin-type", "red", 1));
        state.Physical.Figures.Add(new("second", new(0, 2)));
        var result = GameEngine.StartRound(state, new Random());
        // Select and finish both ordinary Moves before selecting the first Act.
        result = GameEngine.Advance(result.State, new Choice("goblin"), new Random()); // Bonus
        result = GameEngine.Advance(result.State, new Choice("goblin"), new Random()); // Move selection
        result = GameEngine.Advance(result.State, new Choice(null), new Random());
        result = GameEngine.Advance(result.State, new Choice(null), new Random()); // second Move
        result = GameEngine.Advance(result.State, new Choice("goblin"), new Random()); // Act selection
        result = GameEngine.Advance(result.State, new Choice("attack:hero"), new Random());
        Assert.Equal("AttackResolved", Assert.Single(result.Events).Kind);
        Assert.Equal(Phase.Act, result.State.Phase);
        Assert.Contains("goblin", result.State.CompletedUnitIds);
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
        Assert.Equal(DecisionKind.Act, moved.NextInput!.Kind);
        Assert.Null(moved.State.MoveAfterAttackAllowance);
        Assert.True(GameEngine.Advance(moved.State, new Choice(null), new Random()).State.RoundComplete);
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
        // Reach the attack through the normal group selection flow.
        state.Round = 1;
        state.ActiveTypeId = "goblin-type";
        state.Phase = Phase.Act;
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
    public void RetreatMaximizesNearestHostileOrdinaryApproachAndBreaksTiesByBoardOrder()
    {
        var extra = Extra(State());
        var provider = new DefaultMonsterProvider();
        // Up, right and down are all distance two. Top-left selects up.
        Assert.Equal("2,0", provider.Choose(extra.NextInput!, new GameplayQueries(extra.State)));
        extra.NextInput!.Candidates.Reverse();
        Assert.Equal("2,0", provider.Choose(extra.NextInput, new GameplayQueries(extra.State)));
        var moved = GameEngine.Advance(extra.State, provider, new Random());
        Assert.Equal(new Cell(2, 0), moved.State.Physical.Figures[0].Position);
    }

    [Fact]
    public void StayWinsWhenMovementReducesNearestHostileDistance()
    {
        var state = State(start: new(3, 0), target: new(0, 0), width: 7, height: 1);
        state.Units.Add(new("other", "hero-type", "blue", 4));
        state.Physical.Figures.Add(new("other", new(6, 0)));
        var move = GameEngine.StartRound(state, new Random());
        var request = move.NextInput! with { IsMoveAfterAttack = true };
        Assert.Null(new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
        Assert.Equal("2,0", new DefaultMonsterProvider().Choose(request with { AllowsNone = false }, new GameplayQueries(state)));
    }

    [Fact]
    public void RetreatUsesTerrainRouteIgnoresFiguresAndDoesNotOpenClosedDoorsInAnalysis()
    {
        var state = State(start: new(2, 1), target: new(0, 1));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.ClosedDoor));
        state.Physical.Board.Edges.Add(new(new(1, 2), new(2, 2), EdgeKind.Wall));
        state.Units.Add(new("friend", "goblin-type", "red", 1));
        state.Physical.Figures.Add(new("friend", new(1, 0)));
        var queries = new GameplayQueries(state);
        Assert.Equal(4, queries.DistanceToNearestHostileFrom("goblin", new(2, 1)));
        Assert.Equal(3, queries.DistanceToNearestHostileFrom("goblin", new(2, 0)));
        Assert.Equal(5, queries.DistanceToNearestHostileFrom("goblin", new(2, 2)));
        var request = new DecisionRequest(DecisionKind.Move, "goblin-type", "goblin",
            [new("2,0", new(2, 0), [new(2, 1), new(2, 0)]),
             new("2,2", new(2, 2), [new(2, 1), new(2, 2)])], true, true);
        Assert.Equal("2,2", new DefaultMonsterProvider().Choose(request, queries));
    }

    [Fact]
    public void OrdinaryGoblinMoveApproachesAndStaysWhenAlreadyAbleToAttack()
    {
        var provider = new DefaultMonsterProvider();
        var adjacent = GameEngine.StartRound(State(), new Random());
        Assert.False(adjacent.NextInput!.IsMoveAfterAttack);
        Assert.Null(provider.Choose(adjacent.NextInput, new GameplayQueries(adjacent.State)));
        var distant = GameEngine.StartRound(State(start: new(0, 0), target: new(6, 0), width: 7, height: 1), new Random());
        Assert.Equal("4,0", provider.Choose(distant.NextInput!, new GameplayQueries(distant.State)));
    }

    [Fact]
    public void StayWinsEqualDistanceBeforeBoardOrder()
    {
        var state = State(start: new(3, 0), target: new(0, 0), width: 6, height: 1);
        state.Units.Add(new("other", "hero-type", "blue", 4));
        state.Physical.Figures.Add(new("other", new(5, 0)));
        var move = GameEngine.StartRound(state, new Random());
        var request = move.NextInput! with { IsMoveAfterAttack = true };
        var queries = new GameplayQueries(state);
        Assert.Equal(2, queries.DistanceToNearestHostileFrom("goblin", new(3, 0)));
        Assert.Equal(2, queries.DistanceToNearestHostileFrom("goblin", new(2, 0)));
        Assert.Null(new DefaultMonsterProvider().Choose(request, queries));
    }

    [Fact]
    public void ShorterCanonicalMovementPathWinsBeforeBoardOrder()
    {
        var state = State(start: new(2, 2), target: new(1, 1), width: 5, height: 5);
        state.Types[0] = state.Types[0] with { MoveAfterAttack = new(3) };
        var extra = Extra(state);
        // Both positions are ordinary approach distance three from the hostile.
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
    public void NoCalculableHostileDistanceChoosesStayAndCompletesExtraMoveNormally(bool noHostiles)
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
        Assert.Null(queries.DistanceToNearestHostileFrom("goblin", new(2, 1)));
        Assert.True(extra.NextInput!.IsMoveAfterAttack);
        Assert.NotEmpty(extra.NextInput.Candidates);
        Assert.All(extra.NextInput.Candidates, c =>
            Assert.Null(queries.DistanceToNearestHostileFrom("goblin", c.Destination!)));
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
