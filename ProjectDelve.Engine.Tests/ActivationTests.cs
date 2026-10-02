using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ActivationTests
{
    private sealed class Random : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class UnexpectedChoice : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) =>
            throw new InvalidOperationException("The dead Unit must not choose again.");
    }

    private static GameState State() => new()
    {
        Physical = new(new Board(5, 2, [new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor)]),
            [new("first", new(1, 0)), new("second", new(0, 0)), new("hero", new(4, 0))]),
        Types = [UnitType.Zombie(), new("hero-type", 0, 0, 0, 0, 4)],
        Units = [new("first", "zombie-type", "red", 1), new("second", "zombie-type", "red", 1),
            new("hero", "hero-type", "blue", 4)]
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoorOpenedByFirstUnitChangesSecondUnitsMoveDuringSameToken(bool relevanceAutoChoice)
    {
        var random = new Random();
        var result = GameEngine.StartRound(State(), random, relevanceAutoChoice);
        result = GameEngine.Advance(result.State, new Choice("first"), random, relevanceAutoChoice);
        Assert.False(result.NextInput!.AllowsNone);
        Assert.DoesNotContain(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(result.State, new Choice("end-turn"), random, relevanceAutoChoice));
        result = GameEngine.Advance(result.State, new Choice("stay"), random, relevanceAutoChoice);
        Assert.True(result.State.MoveDone);
        Assert.False(result.State.ActionDone);
        Assert.Empty(result.State.BonusActionsUsedThisActivation);
        Assert.Contains(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.EndTurn);
        result = GameEngine.Advance(result.State, new Choice("try-open-door:1,0:2,0"), random, relevanceAutoChoice);
        Assert.Contains(result.Events, e => e.Kind == "DoorOpened");
        Assert.Equal("second", result.State.CurrentUnitId);
        Assert.Contains("first", result.State.CompletedUnitIds);
        Assert.False(result.State.MoveDone);
        Assert.Contains(result.NextInput!.Candidates, c => c.Destination == new Cell(2, 0));
        Assert.DoesNotContain(result.NextInput.Candidates, c => c.Key == "first");
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        result = GameEngine.Advance(restored, new Choice("2,0"), random, relevanceAutoChoice);
        Assert.Equal(new Cell(2, 0), result.State.Physical.Figures.Single(f => f.Id == "second").Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadCurrentUnitCannotResumeActivationOrPostAttackMove(bool continuation)
    {
        var state = State();
        state.Round = 1;
        state.ActiveTypeId = "zombie-type";
        state.CurrentUnitId = "first";
        state.MoveDone = true;
        state.ActionDone = continuation;
        state.MoveAfterAttackAllowance = continuation ? 1 : null;
        state.Pending = new(DecisionKind.Activation, "zombie-type", "first", [], false);
        state.Units[0] = state.Units[0] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "first");
        var result = GameEngine.Advance(state, new UnexpectedChoice(), new Random());
        Assert.DoesNotContain(result.Events, e => e.UnitId == "first");
        Assert.Null(result.State.MoveAfterAttackAllowance);
        Assert.Equal("second", result.State.CurrentUnitId);
        Assert.False(result.State.MoveDone);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
    }

    [Fact]
    public void OldPhaseBasedSaveIsIncompatible()
    {
        var json = JsonSerializer.Serialize(State());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameState>(json.Insert(1, "\"Phase\":1,")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyStayThenOnlyEndTurnAutoResolveWithoutProvider(bool relevanceAutoChoice)
    {
        var state = State();
        state.Types = [new("idle", 0, 0, 0, 0, 1)];
        state.Units = [new("first", "idle", "red", 1)];
        state.Physical.Figures.RemoveAll(f => f.Id != "first");
        var result = GameEngine.StartRound(state, new Random(), relevanceAutoChoice);
        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Equal(new[] { new Cell(1, 0) }, Assert.Single(result.Events, e => e.Kind == "MovementCompleted").Path);
    }
}
