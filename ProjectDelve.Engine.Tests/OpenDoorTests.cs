using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class OpenDoorTests
{
    private sealed class Random : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() { AttackRolls++; return AttackFace.Hit; }
        public int RollD6() => throw new InvalidOperationException("Unexpected D6 roll.");
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private sealed class Choice(Func<DecisionRequest, string?> choose) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => choose(request);
    }

    // Reverse endpoints to exercise the shared, undirected edge model.
    private static readonly Edge Door = new(new Cell(2, 1), new Cell(1, 1), EdgeKind.ClosedDoor);

    private static GameState State(bool enemies = false) => new()
    {
        Physical = new PhysicalState(new Board(3, 3, [Door]),
            enemies ? [new Figure("hero", new Cell(1, 1)), new Figure("enemy-1", new Cell(1, 0)),
                new Figure("enemy-2", new Cell(0, 1))] : [new Figure("hero", new Cell(1, 1))]),
        Types = enemies ? [UnitType.Hero("hero-type", 0, 1, 1, 0, 2), new UnitType("enemy-type", 0, 0, 0, 0, 1)]
            : [UnitType.Hero("hero-type", 0, 1, 1, 0, 2)],
        Units = enemies ? [new Unit("hero", "hero-type", "blue", 2),
            new Unit("enemy-1", "enemy-type", "red", 1), new Unit("enemy-2", "enemy-type", "red", 1)]
            : [new Unit("hero", "hero-type", "blue", 2)]
    };

    private static EngineResult Open(GameState pending, Random random) => GameEngine.Advance(pending,
        new Choice(request => request.Candidates.Single(c => c.Action == UnitAction.OpenDoor).Key), random);

    private static EngineResult FinishEnemies(EngineResult result, Random random)
    {
        var events = new List<RulesEvent>(result.Events);
        while (result.NextInput is not null)
        {
            Assert.Equal("enemy-type", result.NextInput.TypeId);
            result = GameEngine.Advance(result.State, new DefaultMonsterProvider(), random);
            events.AddRange(result.Events);
        }
        return result with { Events = events };
    }

    [Fact]
    public void HeroAdjacentToClosedDoor_CanChooseOpenDoor_AndResumeFromJson()
    {
        var state = State();
        var random = new Random();
        var pending = GameEngine.StartRound(state, random);
        Assert.Equal(DecisionKind.Act, pending.NextInput!.Kind);
        Assert.True(pending.NextInput.AllowsNone);
        var candidate = Assert.Single(pending.NextInput.Candidates);
        Assert.Equal(UnitAction.OpenDoor, candidate.Action);
        Assert.Equal(EdgeKind.ClosedDoor, candidate.Door!.Kind);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(pending.State))!;
        Assert.Equal(pending.State.Types[0].Actions, restored.Types[0].Actions);
        Assert.Equal(candidate, Assert.Single(restored.Pending!.Candidates));

        var result = Open(restored, random);

        Assert.True(result.State.RoundComplete);
        Assert.Equal(Door with { Kind = EdgeKind.OpenDoor }, Assert.Single(result.State.Physical.Board.Edges));
        var opened = Assert.Single(result.Events, e => e.Kind == "DoorOpened");
        Assert.Equal("hero", opened.UnitId);
        Assert.Equal(result.State.Physical.Board.Edges[0], opened.Door);
        Assert.Equal(0, random.AttackRolls);
        Assert.Equal(Door, Assert.Single(state.Physical.Board.Edges));
        Assert.Equal(Door, Assert.Single(restored.Physical.Board.Edges));
    }

    [Fact]
    public void MultipleAdjacentClosedDoors_ProduceSeparateSelectableCandidates()
    {
        var state = State();
        var other = new Edge(new Cell(1, 1), new Cell(1, 2), EdgeKind.ClosedDoor);
        state.Physical.Board.Edges.Add(other);
        var pending = GameEngine.StartRound(state, new Random());
        Assert.Equal(2, pending.NextInput!.Candidates.Count);
        Assert.All(pending.NextInput.Candidates, c => Assert.Equal(UnitAction.OpenDoor, c.Action));
        Assert.Equal(2, pending.NextInput.Candidates.Select(c => c.Key).Distinct().Count());

        foreach (var candidate in pending.NextInput.Candidates)
        {
            var result = GameEngine.Advance(pending.State, new Choice(_ => candidate.Key), new Random());
            Assert.Single(result.State.Physical.Board.Edges, e => e.Kind == EdgeKind.OpenDoor);
            Assert.Single(result.State.Physical.Board.Edges, e => e.Kind == EdgeKind.ClosedDoor);
            var opened = Assert.Single(result.Events, e => e.Kind == "DoorOpened").Door!;
            Assert.True(opened.A == candidate.Door!.A && opened.B == candidate.Door.B ||
                opened.A == candidate.Door.B && opened.B == candidate.Door.A);
        }
    }

    [Fact]
    public void OpenDoorConsumesAct_WithoutAlsoAttacking()
    {
        var random = new Random();
        var pending = GameEngine.StartRound(State(enemies: true), random);
        Assert.Equal(3, pending.NextInput!.Candidates.Count);
        Assert.Equal(2, pending.NextInput.Candidates.Count(c => c.Action == UnitAction.NormalAttack));

        var result = FinishEnemies(Open(pending.State, random), random);

        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Single(result.Events, e => e.Kind == "DoorOpened");
        Assert.DoesNotContain(result.Events, e => e.Kind == "AttackResolved");
        Assert.All(result.State.Units.Where(u => u.SideId == "red"), u => Assert.Equal(1, u.CurrentHp));
        Assert.Equal(0, random.AttackRolls);
        Assert.Throws<InvalidOperationException>(() => Open(result.State, random));
    }

    [Fact]
    public void HeroCanChooseAttack_WhenAttackAndOpenDoorAreLegal()
    {
        var random = new Random();
        var pending = GameEngine.StartRound(State(enemies: true), random);
        Assert.Contains(pending.NextInput!.Candidates, c => c.Action == UnitAction.OpenDoor);
        var result = GameEngine.Advance(pending.State, new Choice(request =>
            request.Candidates.Single(c => c.TargetId == "enemy-1").Key), random);

        Assert.True(result.State.RoundComplete);
        Assert.Equal("enemy-1", Assert.Single(result.Events, e => e.Kind == "AttackResolved").TargetId);
        Assert.Equal(0, result.State.Units.Single(u => u.Id == "enemy-1").CurrentHp);
        Assert.Equal(Door, Assert.Single(result.State.Physical.Board.Edges));
        Assert.DoesNotContain(result.Events, e => e.Kind == "DoorOpened");
    }

    [Fact]
    public void HeroCanChooseNone_WhenActionsAreAvailable()
    {
        var random = new Random();
        var pending = GameEngine.StartRound(State(enemies: true), random);
        Assert.Equal(3, pending.NextInput!.Candidates.Count);
        var result = FinishEnemies(GameEngine.Advance(pending.State, new Choice(_ => null), random), random);

        Assert.True(result.State.RoundComplete);
        Assert.Equal(Door, Assert.Single(result.State.Physical.Board.Edges));
        Assert.DoesNotContain(result.Events, e => e.Kind is "DoorOpened" or "AttackResolved");
        Assert.Equal(0, random.AttackRolls);
    }

    [Fact]
    public void DefaultMonsterHasOnlyAttackCandidates_EvenAdjacentToDoor()
    {
        var state = State(enemies: true);
        state.Types[0] = new UnitType("hero-type", 0, 1, 1, 0, 2);
        var random = new Random();
        var pending = GameEngine.StartRound(state, random);
        Assert.Equal(2, pending.NextInput!.Candidates.Count);
        Assert.All(pending.NextInput.Candidates, c => Assert.Equal(UnitAction.NormalAttack, c.Action));

        var result = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), random);
        Assert.Equal("enemy-1", Assert.Single(result.Events, e => e.Kind == "AttackResolved").TargetId);
        Assert.Equal(Door, Assert.Single(result.State.Physical.Board.Edges));
    }

    [Fact]
    public void MonsterCanGainOpenDoorByCapability_WithoutInventingDefaultBehavior()
    {
        var state = State();
        state.Types[0] = new UnitType("monster-type", 0, 1, 1, 0, 2,
            UnitAction.NormalAttack | UnitAction.OpenDoor);
        state.Units[0] = state.Units[0] with { TypeId = "monster-type", SideId = "red" };
        var random = new Random();
        var pending = GameEngine.StartRound(state, random);
        Assert.Equal(UnitAction.OpenDoor, Assert.Single(pending.NextInput!.Candidates).Action);
        var automatic = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), random);
        Assert.Equal(Door, Assert.Single(automatic.State.Physical.Board.Edges));
        Assert.Equal(EdgeKind.OpenDoor, Assert.Single(Open(pending.State, random).State.Physical.Board.Edges).Kind);
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.OpenDoor)]
    public void NonClosedEdges_DoNotProduceOpenDoorCandidates(EdgeKind kind)
    {
        var state = State();
        state.Physical.Board.Edges[0] = Door with { Kind = kind };
        var result = GameEngine.StartRound(state, new Random());
        Assert.True(result.State.RoundComplete);
        Assert.DoesNotContain(result.Events, e => e.Kind == "DoorOpened");
    }

    [Fact]
    public void NonAdjacentClosedDoor_DoesNotProduceCandidate()
    {
        var state = State();
        state.Physical.Figures[0] = new Figure("hero", new Cell(0, 0));
        Assert.True(GameEngine.StartRound(state, new Random()).State.RoundComplete);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void OpeningDoesNotRequireAttackCapabilityOrAttackStats(int atk, int rng)
    {
        var state = State(enemies: true);
        state.Types[0] = state.Types[0] with { Actions = UnitAction.OpenDoor, Atk = atk, Rng = rng };
        var random = new Random();
        var pending = GameEngine.StartRound(state, random);
        Assert.Equal(UnitAction.OpenDoor, Assert.Single(pending.NextInput!.Candidates).Action);
        var queries = new GameplayQueries(pending.State);
        Assert.False(queries.CanAttackHostileFrom("hero", new Cell(1, 1)));
        Assert.Null(queries.DistanceToAttackPositionFrom("hero", new Cell(1, 1)));
        Assert.Equal(EdgeKind.OpenDoor, Assert.Single(Open(pending.State, random).State.Physical.Board.Edges).Kind);
    }

    [Fact]
    public void ProviderCannotInventDoorChoices_OrChangeCanonicalActionPayload()
    {
        var random = new Random();
        var pending = GameEngine.StartRound(State(enemies: true), random);
        var forged = new Candidate("open-door:0,0:1,0", Action: UnitAction.OpenDoor,
            Door: new Edge(new Cell(0, 0), new Cell(1, 0), EdgeKind.ClosedDoor));
        pending.State.Pending!.Candidates.Add(forged);
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(pending.State, new Choice(request =>
        {
            Assert.DoesNotContain(request.Candidates, c => c.Key == forged.Key);
            request.Candidates.Add(forged);
            return forged.Key;
        }), random));

        var result = GameEngine.Advance(pending.State, new Choice(request =>
        {
            var candidate = request.Candidates.Single(c => c.Action == UnitAction.OpenDoor);
            request.Candidates[request.Candidates.IndexOf(candidate)] = candidate with
                { Action = UnitAction.NormalAttack, TargetId = "enemy-1", Door = forged.Door };
            return candidate.Key;
        }), random);
        Assert.Equal(Door with { Kind = EdgeKind.OpenDoor }, Assert.Single(result.State.Physical.Board.Edges));
        Assert.DoesNotContain(result.Events, e => e.Kind == "AttackResolved");
    }

    [Fact]
    public void OpenedDoorIsPassable_OnSubsequentMoveUsingExistingRules()
    {
        var state = State();
        state.Types[0] = UnitType.Hero("hero-type", 1, 0, 0, 0, 2);
        state.Physical = new PhysicalState(new Board(2, 1,
            [new Edge(new Cell(0, 0), new Cell(1, 0), EdgeKind.ClosedDoor)]),
            [new Figure("hero", new Cell(0, 0))]);
        var random = new Random();
        var pending = GameEngine.StartRound(state, random);
        Assert.Equal(DecisionKind.Act, pending.NextInput!.Kind); // Closed door prevents Move.
        var opened = Open(pending.State, random);
        Assert.True(opened.State.RoundComplete);
        var nextRound = GameEngine.StartRound(opened.State, random);
        Assert.Equal(DecisionKind.Move, nextRound.NextInput!.Kind);
        var destination = Assert.Single(nextRound.NextInput.Candidates);
        Assert.Equal(new Cell(1, 0), destination.Destination);
        Assert.Equal([new Cell(0, 0), new Cell(1, 0)], destination.Path);

        var moved = GameEngine.Advance(nextRound.State, new Choice(_ => destination.Key), random);
        Assert.Equal(new Cell(1, 0), Assert.Single(moved.State.Physical.Figures).Position);
        Assert.Equal(destination.Path, Assert.Single(moved.Events, e => e.Kind == "MovementCompleted").Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningDoorImmediatelyChangesMonsterMovement(bool movementOnlyProvider)
    {
        var state = new GameState
        {
            Physical = new PhysicalState(new Board(6, 5,
                [new Edge(new Cell(2, 1), new Cell(3, 1), EdgeKind.Wall),
                    new Edge(new Cell(2, 2), new Cell(3, 2), EdgeKind.ClosedDoor),
                    new Edge(new Cell(2, 3), new Cell(3, 3), EdgeKind.Wall),
                    new Edge(new Cell(2, 1), new Cell(2, 2), EdgeKind.Wall),
                    new Edge(new Cell(2, 2), new Cell(2, 3), EdgeKind.Wall)]),
                [new Figure("hero", new Cell(4, 2)), new Figure("monster-1", new Cell(1, 2)),
                    new Figure("monster-2", new Cell(1, 3))]),
            Types = [UnitType.Hero("hero-type", 2, 1, 1, 0, 2),
                new UnitType("monster-type", 2, 1, 1, 1, 1)],
            Units = [new Unit("hero", "hero-type", "blue", 2),
                new Unit("monster-1", "monster-type", "red", 1),
                new Unit("monster-2", "monster-type", "red", 1)]
        };
        var random = new Random();
        var pending = GameEngine.StartRound(state, random);
        var movedHero = GameEngine.Advance(pending.State, new Choice(_ => "3,2"), random);
        var closedQueries = new GameplayQueries(movedHero.State);
        Assert.False(closedQueries.CanAttackHostileFrom("monster-1", new Cell(2, 2)));

        var opened = Open(movedHero.State, random);
        Assert.Equal(EdgeKind.OpenDoor, Assert.Single(opened.Events, e => e.Kind == "DoorOpened").Door!.Kind);
        var monsters = new DefaultMonsterProvider();
        // Select both Bonus Actions, then monster-1 for Move.
        var result = opened;
        while (result.NextInput!.Kind == DecisionKind.SelectUnit)
            result = GameEngine.Advance(result.State, monsters, random);
        Assert.Equal("monster-1", result.NextInput.UnitId);
        Assert.Equal(DecisionKind.Move, result.NextInput.Kind);
        Assert.Equal(new[] { new Cell(1, 2), new Cell(2, 2) },
            result.NextInput.Candidates.Single(c => c.Key == "2,2").Path);

        IDecisionProvider provider = movementOnlyProvider ? new MonsterMovementProvider(monsters) : monsters;
        Assert.Equal("1,0", provider.Choose(result.NextInput, closedQueries));
        var movedMonster = GameEngine.Advance(result.State, provider, random);
        Assert.Equal(new[] { new Cell(1, 2), new Cell(2, 2) },
            Assert.Single(movedMonster.Events, e => e.Kind == "MovementCompleted").Path);
        Assert.True(new GameplayQueries(result.State).CanAttackHostileFrom("monster-1", new Cell(2, 2)));
        Assert.Equal(0, new GameplayQueries(result.State).DistanceToAttackPositionFrom("monster-1", new Cell(2, 2)));
        Assert.False(closedQueries.CanAttackHostileFrom("monster-1", new Cell(2, 2)));
    }
}
