using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ShamanTests
{
    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException("Shaman does not roll dice.");
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState State(Cell? start = null, Cell? enemy = null, int width = 5, int height = 5) => new()
    {
        Physical = new(new Board(width, height, []), enemy is null
            ? [new("shaman", start ?? new(2, 2))]
            : [new("shaman", start ?? new(2, 2)), new("enemy", enemy)]),
        Types = [UnitType.Shaman(), new("enemy-type", 0, 0, 0, 0, 10, Actions: UnitAction.None) { Unique = true }],
        Units = enemy is null ? [UnitType.Shaman().CreateUnit("shaman", "red")]
            : [UnitType.Shaman().CreateUnit("shaman", "red"), new("enemy", "enemy-type", "blue", 10)]
    };
    private static EngineResult Choose(GameState state, string key) =>
        TestGame.Advance(state, new Choice(key), new Random(), false);
    private static EngineResult Action(GameState state) =>
        Choose(TestGame.StartRound(state, new Random(), false).State, "stay");
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void ContentExplicitlyOmitsAttackAndHasOnlySpawnCard()
    {
        var type = UnitType.Shaman();
        Assert.Equal((2, 0, 0, 3, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.SummonAdjacent, type.Actions);
        Assert.Equal(UnitBehavior.Flee | UnitBehavior.UseSummon, type.Behaviors);
        Assert.Null(type.TryOpenDoor);
        Assert.Equal("Summon Goblin", Assert.Single(type.CardEntries()).Name);
        Assert.Equal(type, JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type)));
        var state = State(enemy: new(3, 2));
        state.Types[0] = type with { Atk = 10, Rng = 10 };
        Assert.DoesNotContain(Action(state).NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
    }

    [Theory]
    [InlineData(false, Posture.Upright)]
    [InlineData(false, Posture.Lying)]
    [InlineData(true, Posture.Upright)]
    [InlineData(true, Posture.Lying)]
    public void SummonUsesConfiguredTypePostureAndExistingContentWithoutChangingCurrentBag(bool existing, Posture posture)
    {
        var summoner = UnitType.Define("summoner", "Summoner", UnitAuthoring.Stats(2, 0, 0, 3, 1),
            UnitAuthoring.CantAttack(),
            UnitAuthoring.Ability("Call Ally", UnitAuthoring.Unlimited(),
                UnitAuthoring.SummonAdjacent(UnitTypeIds.Wizard, posture)))
            .WithBehaviors(UnitBehavior.UseSummon);
        var initial = State();
        initial.Types[0] = summoner;
        initial.Units[0] = summoner.CreateUnit("shaman", "red");
        var expectedType = existing ? UnitType.Wizard() with { Hp = 7, DisplayName = "Learned Wizard" } : UnitType.Wizard();
        if (existing) initial.Types.Add(expectedType);
        var action = Action(Restore(initial));
        var entry = Assert.Single(action.State.Types[0].CardEntries(action.State.Types));
        Assert.Equal("spawn-goblin", entry.Id); // Existing opaque presentation identity is retained.
        Assert.Equal("Call Ally", entry.Name);
        Assert.Null(entry.MaxUses);
        Assert.Equal($"Choose an adjacent empty Cell. Place one {posture} {expectedType.DisplayName} there.", entry.Description);
        var restored = Restore(action.State);
        Assert.Equal(new SummonAdjacent(UnitTypeIds.Wizard, posture), restored.Types[0].SummonAdjacent);
        var result = TestGame.Advance(restored, new DefaultAutomatedProvider(), new Random(), false);
        var step = Assert.Single(TestGame.OperationSteps(result), s => TestGame.OperationEvents(result)[s.EventIndex].Kind == "UnitCreated");
        var created = Assert.Single(step.StateAfter.Units, u => u.TypeId == UnitTypeIds.Wizard);
        Assert.Equal(expectedType.Hp, created.CurrentHp);
        Assert.Equal("red", created.SideId);
        Assert.Equal(new AbilityUses(2, 2), created.FireballUses);
        Assert.Equal(new AbilityUses(2, 2), created.BonusActionUses["Focus"]);
        Assert.Equal(new Figure(created.Id, new(1, 1), posture), step.StateAfter.Physical.Figures.Single(f => f.Id == created.Id));
        Assert.Equal(JsonSerializer.Serialize(expectedType), JsonSerializer.Serialize(
            Assert.Single(step.StateAfter.Types, t => t.Id == UnitTypeIds.Wizard)));
        Assert.Equal("Call Ally", TestGame.OperationEvents(result)[step.EventIndex].AbilityName);
        Assert.Equal(action.State.Bag, step.StateAfter.Bag);
        Assert.DoesNotContain(TestGame.OperationEvents(result), e => e.Kind == "TokenDrawn" && e.TypeId == UnitTypeIds.Wizard);
        Assert.DoesNotContain(result.State.Types, t => t.Id == UnitTypeIds.Goblin);
        Assert.True(result.State.RoundComplete);
        Assert.Contains(UnitTypeIds.Wizard, TestGame.StartRound(Restore(result.State), new Random(), false).State.Bag.Select(t => t.TypeId));
    }

    [Theory]
    [InlineData("missing-type", Posture.Upright)]
    [InlineData("goblin-type", (Posture)99)]
    public void InvalidSummonConfigurationIsRejectedBeforeResolution(string typeId, Posture posture)
    {
        var state = State();
        state.Types[0] = state.Types[0] with { SummonAdjacent = new(typeId, posture) };
        Assert.Throws<ArgumentException>(() => TestGame.StartRound(state, new Random(), false));
        Assert.Single(state.Units);
    }

    [Fact]
    public void SpawnIncludesAllEightAdjacentCellsAndNoOthers()
    {
        var result = Action(State());
        var spawns = result.NextInput!.Candidates.Where(c => c.Action == UnitAction.SummonAdjacent).ToArray();
        Assert.Equal(8, spawns.Length);
        Assert.All(spawns, c => Assert.True(SpatialRules.AreAdjacent(result.State.Physical.Board, new(2, 2), c.Destination!)));
        Assert.Throws<ArgumentException>(() => Choose(result.State, "spawn-goblin:0,0"));
        Assert.Throws<ArgumentException>(() => Choose(result.State, "spawn-goblin:2,2"));
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("water")]
    [InlineData("tree")]
    [InlineData("table")]
    [InlineData("wall")]
    [InlineData("closed-door")]
    [InlineData("corner")]
    public void SpawnRevalidatesPlacementAndLos(string blocker)
    {
        var result = Action(State());
        var cell = blocker == "corner" ? new Cell(1, 1) : new Cell(2, 1);
        var state = Restore(result.State);
        switch (blocker)
        {
            case "occupied":
                state.Types.Add(state.Types.Single(t => t.Id == "enemy-type") with { Id = "friend-type" });
                state.Units.Add(new("friend", "friend-type", "red", 10));
                state.Physical.Figures.Add(new("friend", cell, Posture.Lying)); break;
            case "water": state.Physical.Board.Terrain.Add(new(cell, TerrainKind.Water)); break;
            case "tree": state.Physical.Board.Terrain.Add(new(cell, TerrainKind.Tree)); break;
            case "table": state.Physical.Board.Terrain.Add(new(cell, TerrainKind.StoneFloorWithTable)); break;
            case "corner":
                state.Physical.Board.Edges.Add(new(new(2, 2), new(1, 2), EdgeKind.Wall));
                state.Physical.Board.Edges.Add(new(new(2, 2), new(2, 1), EdgeKind.Wall)); break;
            default: state.Physical.Board.Edges.Add(new(new(2, 2), cell,
                blocker == "wall" ? EdgeKind.Wall : EdgeKind.ClosedDoor)); break;
        }
        var key = $"spawn-goblin:{cell.X},{cell.Y}";
        Assert.DoesNotContain(GameEngine.RefreshChoices(state, new Random(), false).NextInput!.Candidates, c => c.Key == key);
        Assert.Throws<ArgumentException>(() => Choose(state, key));
    }

    [Fact]
    public void SpawnCreatesOrdinaryLyingGoblinConsumesActionAndPreservesBagSnapshot()
    {
        var initial = State();
        var action = Action(initial);
        Assert.DoesNotContain("goblin-type", action.State.Bag.Select(t => t.TypeId));
        var result = TestGame.Advance(Restore(action.State), new DefaultAutomatedProvider(), new Random(), false);
        var step = Assert.Single(TestGame.OperationSteps(result), s => TestGame.OperationEvents(result)[s.EventIndex].Kind == "UnitCreated").StateAfter;
        Assert.True(step.ActionDone);
        Assert.Equal(action.State.Bag, step.Bag);
        var goblin = Assert.Single(step.Units, u => u.TypeId == "goblin-type");
        Assert.Equal(UnitType.Goblin(), step.Types.Single(t => t.Id == goblin.TypeId));
        Assert.Equal(UnitType.Goblin().CreateUnit(goblin.Id, "red"), goblin);
        Assert.Equal(new Figure(goblin.Id, new(1, 1), Posture.Lying), step.Physical.Figures.Single(f => f.Id == goblin.Id));
        Assert.Single(initial.Units);
        Assert.True(result.State.RoundComplete);
        Assert.DoesNotContain(TestGame.OperationEvents(result), e => e.TypeId == "goblin-type" && e.Kind == "TokenDrawn");

        var next = TestGame.StartRound(Restore(result.State), new Random(), false);
        Assert.Contains("goblin-type", next.State.Bag.Select(t => t.TypeId));
        Assert.Equal(Posture.Lying, next.State.Physical.Figures.Single(f => f.Id == goblin.Id).Posture);
        next = Choose(next.State, "stay");
        next = Choose(next.State, "end-turn");
        Assert.Contains(TestGame.OperationEvents(next), e => e.Kind == "TokenDrawn" && e.TypeId == "goblin-type");
        Assert.Equal(new RulesEvent("PostureChanged", goblin.Id, Posture: Posture.Upright),
            Assert.Single(TestGame.OperationEvents(next), e => e.UnitId == goblin.Id));
        Assert.True(next.State.RoundComplete);
        var later = TestGame.StartRound(next.State, new Random(), false);
        later = Choose(later.State, "stay");
        later = Choose(later.State, "end-turn");
        Assert.Equal(goblin.Id, later.NextInput!.UnitId);
        Assert.Contains(later.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Move);
        Assert.Equal(UnitBehavior.BackAwayAfterAttack, new GameplayQueries(later.State).BehaviorsOf(goblin.Id));
    }

    [Fact]
    public void AutomatedSpawnSelectsTopLeftLegalChoiceAndNoneWhenSurrounded()
    {
        var state = State();
        state.Physical.Board.Terrain.Add(new(new(1, 1), TerrainKind.Water));
        var action = Action(state);
        var provider = new DefaultAutomatedProvider();
        Assert.Equal("spawn-goblin:2,1", provider.Choose(action.NextInput!, new GameplayQueries(action.State)));
        for (var y = 1; y <= 3; y++)
            for (var x = 1; x <= 3; x++)
                if ((x != 2 || y != 2) && (x != 1 || y != 1))
                    state.Physical.Board.Terrain.Add(new(new(x, y), TerrainKind.Water));
        Assert.True(TestGame.StartRound(state, new Random(), false).State.RoundComplete);
        Assert.Null(provider.Choose(new(DecisionKind.Act, "shaman-type", "shaman", [], true), new GameplayQueries(state)));
    }

    private static Cell Flee(GameState state)
    {
        var move = TestGame.StartRound(state, new Random(), false);
        var result = TestGame.Advance(move.State, new DefaultAutomatedProvider(), new Random(), false);
        return TestGame.OperationEvents(result).Single(e => e.Kind == "MovementCompleted").Path![^1];
    }

    [Fact]
    public void FleeMaximizesNearestReachableEnemyDistanceThenTopLeft()
    {
        Assert.Equal(new Cell(0, 2), Flee(State(enemy: new(2, 0))));
        var state = State(enemy: new(2, 0));
        state.Types.Add(state.Types.Single(t => t.Id == "enemy-type") with { Id = "second-enemy-type" });
        state.Units.Add(new("second-enemy", "second-enemy-type", "blue", 10));
        state.Physical.Figures.Add(new("second-enemy", new(0, 2)));
        Assert.Equal(new Cell(4, 2), Flee(state));
    }

    [Fact]
    public void FleeUsesTerrainPathsRatherThanManhattanAndShortestMoveTieBreak()
    {
        var state = State(new(1, 0), new(3, 0), 4, 5);
        for (var y = 0; y < 4; y++)
            state.Physical.Board.Edges.Add(new(new(1, y), new(2, y), EdgeKind.Wall));
        Assert.Equal(10, new GameplayQueries(state).DistanceToNearestHostileFrom("shaman", new(1, 0)));
        // (0,1) is farther geometrically, but (0,0) is farther along the terrain route.
        Assert.Equal(new Cell(0, 0), Flee(state));

        var corridor = State(new(2, 0), new(0, 0), 5, 1);
        corridor.Types.Add(corridor.Types.Single(t => t.Id == "enemy-type") with { Id = "second-enemy-type" });
        corridor.Units.Add(new("second-enemy", "second-enemy-type", "blue", 10));
        corridor.Physical.Figures.Add(new("second-enemy", new(4, 0)));
        Assert.Equal(new Cell(2, 0), Flee(corridor));
    }

    [Fact]
    public void NoReachableHostileStaysAndOpeningDoorMakesHostileRelevantWithoutChangingMovement()
    {
        Assert.Equal(new Cell(2, 2), Flee(State()));
        var state = State(new(2, 0), new(0, 0), 5, 1);
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor));
        Assert.Null(new GameplayQueries(state).DistanceToNearestHostileFrom("shaman", new(2, 0)));
        Assert.Equal(new Cell(2, 0), Flee(state));
        var withFlee = TestGame.StartRound(state, new Random(), false).NextInput!;
        state.Types[0] = state.Types[0] with { Behaviors = UnitBehavior.None };
        Assert.Equal(withFlee.Candidates, TestGame.StartRound(state, new Random(), false).NextInput!.Candidates,
            new CandidateKeyComparer());
        state.Types[0] = UnitType.Shaman();
        state.Physical.Board.Edges[0] = state.Physical.Board.Edges[0] with { Kind = EdgeKind.OpenDoor };
        Assert.Equal(new Cell(4, 0), Flee(state));
    }
    private sealed class CandidateKeyComparer : IEqualityComparer<Candidate>
    {
        public bool Equals(Candidate? a, Candidate? b) => a?.Key == b?.Key && a?.Destination == b?.Destination;
        public int GetHashCode(Candidate c) => c.Key.GetHashCode();
    }

    [Fact]
    public void FleeDistanceTiesPreferShortestActualMovementThenTopLeftIncludingStay()
    {
        var state = State(new(2, 2), new(2, 0));
        var real = new GameplayQueries(state);
        var distances = new Dictionary<Cell, int?> { [new(2, 2)] = 2, [new(0, 2)] = 4,
            [new(1, 2)] = 4, [new(2, 3)] = 4 };
        var queries = new FleeRankingQueries(real, distances);
        var request = new DecisionRequest(DecisionKind.Move, "shaman-type", "shaman", [
            new("long", new(0, 2), [new(2, 2), new(1, 2), new(0, 2)]),
            new("bottom", new(2, 3), [new(2, 2), new(2, 3)]),
            new("left", new(1, 2), [new(2, 2), new(1, 2)])], true);
        Assert.Equal("left", new DefaultAutomatedProvider().Choose(request, queries));
        distances[new(2, 2)] = 4;
        Assert.Null(new DefaultAutomatedProvider().Choose(request, queries));
    }
    private sealed class FleeRankingQueries(IGameplayQueries real, Dictionary<Cell, int?> distances) : IGameplayQueries
    {
        public IReadOnlyList<Cell> OccupiedCellsOf(string unitId) => real.OccupiedCellsOf(unitId);
        public Cell PositionOf(string id) => real.PositionOf(id);
        public UnitBehavior BehaviorsOf(string id) => real.BehaviorsOf(id);
        public int? DistanceToNearestHostileFrom(string id, Cell cell, bool closedDoorsTraversable = false) => distances[cell];
        public int ManhattanDistanceBetweenUnits(string first, string second) => throw new InvalidOperationException();
        public bool CanAttackHostileFrom(string id, Cell cell) => throw new InvalidOperationException();
        public int? DistanceToNearestAttackableHostileFrom(string id, Cell cell) => throw new InvalidOperationException();
        public bool HasNearbyHostileThreatFrom(string id, Cell cell) => throw new InvalidOperationException();
        public int? DistanceToAttackPositionFrom(string id, Cell cell, bool closedDoorsTraversable = false) => throw new InvalidOperationException();
    }

    [Fact]
    public void NearestHostileApproachIgnoresTemporaryOccupantsAndDoesNotRequireAttackOrLos()
    {
        var state = State(new(0, 0), new(4, 0), 5, 1);
        state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.WallWithWindow));
        state.Types.Add(state.Types.Single(t => t.Id == "enemy-type") with { Id = "blocker-type" });
        state.Units.Add(new("blocker", "blocker-type", "red", 10));
        state.Physical.Figures.Add(new("blocker", new(2, 0), Posture.Lying));
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying };
        Assert.Null(new GameplayQueries(state).DistanceToNearestHostileFrom("shaman", new(0, 0)));
        state.Physical.Board.Edges.Clear();
        Assert.Equal(4, new GameplayQueries(state).DistanceToNearestHostileFrom("shaman", new(0, 0)));
        Assert.DoesNotContain(MovementRules.FindPaths(state, "shaman", new(0, 0), 2).Keys, c => c == new Cell(2, 0));
    }
}
