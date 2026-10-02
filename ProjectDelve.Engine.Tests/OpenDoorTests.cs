using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class OpenDoorTests
{
    private sealed class Random : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException("Open Door does not roll dice.");
    }
    private sealed class Choice(Func<DecisionRequest, string?> choose) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => choose(request);
    }
    private static readonly Edge Door = new(new(2, 1), new(1, 1), EdgeKind.ClosedDoor);
    private static GameState State(bool enemy = false) => new()
    {
        Physical = new(new Board(3, 3, [Door]), enemy
            ? [new("hero", new(1, 1)), new("enemy", new(2, 1))] : [new("hero", new(1, 1))]),
        Types = [UnitType.Barbarian() with { Mov = 1 }, new("enemy-type", 0, 0, 0, 0, 1)],
        Units = enemy ? [new("hero", "barbarian-type", "blue", 5), new("enemy", "enemy-type", "red", 1)]
            : [new("hero", "barbarian-type", "blue", 5)]
    };
    private static EngineResult Choose(GameState state, string key, bool relevanceAutoChoice = true) =>
        GameEngine.Advance(state, new Choice(_ => key), new Random(), relevanceAutoChoice);
    private static Candidate DoorChoice(EngineResult result) =>
        Assert.Single(result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);

    [Fact]
    public void ContentGrantsOpenDoorExplicitly_IndependentOfSideOrHeroHelper()
    {
        Assert.Equal(UnitFreeAction.OpenDoor, UnitType.Barbarian().FreeActions);
        Assert.Equal(UnitFreeAction.OpenDoor, UnitType.Rogue().FreeActions);
        Assert.Equal(UnitFreeAction.None, UnitType.Hero("hero", 1, 1, 1, 1, 1).FreeActions);
        Assert.All(new[] { UnitType.Grunt(), UnitType.Zombie(), UnitType.SkeletonArcher(), UnitType.Goblin() },
            type => Assert.Equal(UnitFreeAction.None, type.FreeActions));
        var state = State();
        state.Types[0] = state.Types[0] with { Actions = UnitAction.None, Atk = 0, Rng = 0 };
        state.Units[0] = state.Units[0] with { SideId = "red" };
        Assert.NotNull(DoorChoice(GameEngine.StartRound(state, new Random())));
        state.Types[0] = state.Types[0] with { FreeActions = UnitFreeAction.None };
        Assert.DoesNotContain(GameEngine.StartRound(state, new Random()).NextInput!.Candidates, c => c.FreeAction is not null);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void OpeningPreservesAllOpportunities_BeforeMoveAfterMoveAndAfterAttack(bool moveDone, bool actionDone, bool relevanceAutoChoice)
    {
        var state = State(enemy: true);
        state.Physical.Figures[1] = new("enemy", new(1, 0));
        // Keep another optional choice after opening, including after the Attack.
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 2), EdgeKind.ClosedDoor));
        var pending = GameEngine.StartRound(state, new Random(), relevanceAutoChoice);
        if (moveDone) pending = Choose(pending.State, "stay", relevanceAutoChoice);
        if (actionDone) pending = Choose(pending.State, "attack:enemy", relevanceAutoChoice);
        pending.State.BonusActionUsed = relevanceAutoChoice;
        var candidate = pending.NextInput!.Candidates.Single(c => c.Key == "open-door:1,1:2,1");
        Assert.Equal(ActivationChoiceKind.FreeAction, candidate.Kind);
        Assert.Null(candidate.Action);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(pending.State))!;
        Assert.Equal(UnitFreeAction.OpenDoor, restored.Types[0].FreeActions);
        Assert.Equal(candidate, restored.Pending!.Candidates.Single(c => c.Key == candidate.Key));
        var opened = Choose(restored, candidate.Key, relevanceAutoChoice);
        Assert.Equal(moveDone, opened.State.MoveDone);
        Assert.Equal(actionDone, opened.State.ActionDone);
        Assert.Equal(relevanceAutoChoice, opened.State.BonusActionUsed);
        Assert.Equal("hero", opened.State.CurrentUnitId);
        Assert.False(opened.State.RoundComplete);
        var evt = Assert.Single(opened.Events);
        Assert.Equal("DoorOpened", evt.Kind);
        Assert.Equal("hero", evt.UnitId);
        Assert.Equal(Door with { Kind = EdgeKind.OpenDoor }, evt.Door);
        Assert.Equal(evt.Door, opened.State.Physical.Board.Edges[0]);
        Assert.Equal(Door, restored.Physical.Board.Edges[0]);
        Assert.DoesNotContain(opened.NextInput!.Candidates, c => c.Key == candidate.Key);
        Assert.Equal(!moveDone, opened.NextInput.Candidates.Any(c => c.Kind == ActivationChoiceKind.Stay));
        Assert.Equal(moveDone, opened.NextInput.Candidates.Any(c => c.Kind == ActivationChoiceKind.EndTurn));
        Assert.Equal(moveDone && !actionDone, opened.NextInput.Candidates.Any(c => c.Action == UnitAction.NormalAttack));
        if (moveDone) Assert.True(Choose(opened.State, "end-turn", relevanceAutoChoice).State.RoundComplete);
    }

    [Fact]
    public void EachAdjacentClosedDoorIsAChoice_AndMultipleDoorsCanOpenInOneActivation()
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 2), EdgeKind.ClosedDoor));
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(0, 1), EdgeKind.OpenDoor));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(1, 0), EdgeKind.Wall));
        var pending = GameEngine.StartRound(state, new Random());
        var doors = pending.NextInput!.Candidates.Where(c => c.FreeAction is not null).ToArray();
        Assert.Equal(2, doors.Length);
        Assert.Equal(2, doors.Select(c => c.Key).Distinct().Count());
        foreach (var candidate in doors)
        {
            var first = Choose(pending.State, candidate.Key);
            Assert.Equal(doors.Single(c => c.Key != candidate.Key), DoorChoice(first));
            var second = Choose(first.State, DoorChoice(first).Key);
            Assert.Equal(3, second.State.Physical.Board.Edges.Count(e => e.Kind == EdgeKind.OpenDoor));
            Assert.DoesNotContain(second.NextInput!.Candidates, c => c.FreeAction is not null);
            Assert.False(second.State.MoveDone);
            Assert.False(second.State.ActionDone);
            Assert.False(second.State.BonusActionUsed);
        }
    }

    [Fact]
    public void OpeningBeforeMoveRegeneratesPaths_AndMovingRegeneratesAdjacentDoors()
    {
        var state = State();
        state.Physical = new(new Board(3, 1,
            [new(new(1, 0), new(0, 0), EdgeKind.ClosedDoor), new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor)]),
            [new("hero", new(0, 0))]);
        var pending = GameEngine.StartRound(state, new Random());
        Assert.DoesNotContain(pending.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
        var opened = Choose(pending.State, DoorChoice(pending).Key);
        var move = Assert.Single(opened.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
        Assert.Equal(new Cell(1, 0), move.Destination);
        Assert.Equal([new Cell(0, 0), new Cell(1, 0)], move.Path);
        var moved = Choose(opened.State, move.Key);
        var second = Choose(moved.State, DoorChoice(moved).Key);
        Assert.Equal(2, second.State.Physical.Board.Edges.Count(e => e.Kind == EdgeKind.OpenDoor));
        Assert.True(second.State.RoundComplete); // Only End Turn remains; resolves normally.
    }

    [Fact]
    public void OpeningAfterMoveRegeneratesAttackLegality_AndDoesNotConsumeAttack()
    {
        var pending = GameEngine.StartRound(State(enemy: true), new Random());
        var stayed = Choose(pending.State, "stay");
        Assert.DoesNotContain(stayed.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        var opened = Choose(stayed.State, DoorChoice(stayed).Key);
        Assert.Contains(opened.NextInput!.Candidates, c => c.Key == "attack:enemy");
        Assert.False(opened.State.ActionDone);
        Assert.Equal(0, new GameplayQueries(opened.State).DistanceToAttackPositionFrom("hero", new(1, 1)));
        var attacked = Choose(opened.State, "attack:enemy");
        Assert.Contains(attacked.Events, e => e.Kind == "AttackResolved");
        Assert.True(attacked.State.RoundComplete);
    }

    [Fact]
    public void EndTurnCanDeclineFreeActions_AndDefaultMonsterDoesNotInventDoorBehavior()
    {
        var pending = GameEngine.StartRound(State(), new Random());
        Assert.DoesNotContain(pending.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        var stayed = Choose(pending.State, "stay");
        var ended = Choose(stayed.State, "end-turn");
        Assert.True(ended.State.RoundComplete);
        Assert.Equal(Door, Assert.Single(ended.State.Physical.Board.Edges));
        Assert.DoesNotContain(ended.Events, e => e.Kind == "DoorOpened");
        Assert.Throws<InvalidOperationException>(() => Choose(ended.State, DoorChoice(stayed).Key));
        var automatic = GameEngine.Advance(stayed.State, new DefaultMonsterProvider(), new Random());
        Assert.True(automatic.State.RoundComplete);
        Assert.DoesNotContain(automatic.Events, e => e.Kind == "DoorOpened");
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.OpenDoor)]
    [InlineData(EdgeKind.None)]
    public void NonClosedEdgesDoNotProduceFreeActions(EdgeKind kind)
    {
        var state = State();
        state.Physical.Board.Edges[0] = Door with { Kind = kind };
        Assert.DoesNotContain(GameEngine.StartRound(state, new Random()).NextInput!.Candidates, c => c.FreeAction is not null);
    }

    [Fact]
    public void ProviderCannotForgeChoicesOrReplaceCanonicalFreeActionPayload()
    {
        var pending = GameEngine.StartRound(State(enemy: true), new Random());
        var candidate = DoorChoice(pending);
        var forged = candidate with { Key = "open-door:0,0:1,0", Door = new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor) };
        pending.State.Pending!.Candidates.Add(forged);
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(pending.State, new Choice(request =>
        {
            Assert.DoesNotContain(request.Candidates, c => c.Key == forged.Key);
            request.Candidates.Add(forged);
            return forged.Key;
        }), new Random()));
        var result = GameEngine.Advance(pending.State, new Choice(request =>
        {
            request.Candidates[request.Candidates.FindIndex(c => c.Key == candidate.Key)] = candidate with
                { FreeAction = null, Action = UnitAction.NormalAttack, TargetId = "enemy", Door = forged.Door };
            return candidate.Key;
        }), new Random());
        Assert.Equal(Door with { Kind = EdgeKind.OpenDoor }, Assert.Single(result.State.Physical.Board.Edges));
        Assert.DoesNotContain(result.Events, e => e.Kind == "AttackResolved");
        Assert.False(result.State.MoveDone);
        Assert.False(result.State.ActionDone);
    }
}
