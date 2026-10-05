using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class HolyWaveTests
{
    private sealed class Random : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Holy Wave must not roll.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Holy Wave must not roll.");
        public int RollD6() => throw new InvalidOperationException("Holy Wave must not roll.");
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState Scenario(UnitType? type = null)
    {
        type ??= UnitType.Cleric();
        return new()
        {
            Physical = new(new Board(6, 6, [new(new(2, 2), new(2, 3), EdgeKind.ClosedDoor)]),
                [new("cleric", new(2, 2)), new("a", new(3, 2)), new("b", new(1, 1))]),
            Types = [type, new("enemy", 0, 0, 0, 1, 4) { Unique = true }, new("enemy-b", 0, 0, 0, 1, 4) { Unique = true }],
            Units = [type.CreateUnit("cleric", "blue"), new("a", "enemy", "red", 4), new("b", "enemy-b", "third-side", 4)]
        };
    }
    private static EngineResult Choose(GameState state, string key) =>
        GameEngine.Advance(state, new Choice(key), new Random(), false);
    private static EngineResult Action(GameState? state = null) =>
        Choose(GameEngine.StartRound(state ?? Scenario(), new Random(), false).State, "stay");
    private static Candidate WaveChoice(EngineResult result) => result.NextInput!.Candidates.Single(c => c.Action == UnitAction.HolyWave);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void BareUnitsAndSerializationPreserveTwoUses()
    {
        var scenario = Scenario();
        scenario.Units[0] = new("cleric", scenario.Types[0].Id, "blue", 4);
        var restored = Restore(Action(scenario).State);
        Assert.Equal(new HolyWave(2), scenario.Types[0].HolyWave);
        Assert.Equal(new AbilityUses(2, 2), restored.Units[0].HolyWaveUses);
        Assert.Equal(new[] { "a", "b" }, restored.Pending!.Candidates.Single(c => c.Action == UnitAction.HolyWave).TargetIds);
        Assert.Null(scenario.Units[0].HolyWaveUses);
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 1)] [InlineData(3, 2)]
    [InlineData(3, 3)] [InlineData(2, 3)] [InlineData(1, 3)] [InlineData(1, 2)]
    public void EverySurroundingCellIsAffectedWithLos(int x, int y)
    {
        var state = Scenario();
        state.Physical.Board.Edges.Clear();
        state.Units.RemoveAt(2); state.Physical.Figures.RemoveAt(2);
        state.Physical.Figures[1] = new("a", new(x, y));
        var action = Action(state);
        Assert.Equal(new[] { "a" }, WaveChoice(action).TargetIds);
        Assert.True(WaveChoice(action).Relevant);
        Assert.Equal(Posture.Lying, Choose(action.State, "holy-wave").ResolutionSteps[0].StateAfter.Physical.Figures[1].Posture);
    }

    [Fact]
    public void CompleteTargetSetExcludesFriendlyDistantBlockedAndAlreadyLyingUnits()
    {
        var state = Scenario();
        foreach (var id in new[] { "friend", "far", "blocked", "lying" })
            state.Types.Add(new(id + "-type", 0, 0, 0, 1, 4) { Unique = true });
        state.Units.AddRange([new("friend", "friend-type", "blue", 4), new("far", "far-type", "red", 4),
            new("blocked", "blocked-type", "red", 4), new("lying", "lying-type", "red", 4)]);
        state.Physical.Figures.AddRange([new("friend", new(2, 1)), new("far", new(5, 5)),
            new("blocked", new(2, 3)), new("lying", new(1, 2), Posture.Lying)]);
        var action = Action(state);
        Assert.Equal(new[] { "a", "b" }, WaveChoice(action).TargetIds);
        var result = Choose(action.State, "holy-wave");
        Assert.Equal(new[] { "a", "b", "cleric" }, result.Events.Where(e => e.Posture == Posture.Lying).Select(e => e.UnitId));
        Assert.All(result.State.Physical.Figures.Where(f => f.Id is "friend" or "far" or "blocked"), f => Assert.Equal(Posture.Upright, f.Posture));
    }

    [Fact]
    public void PostureChangesInOrderWithActionAndUseSpentAndNoAttackOrFollowUps()
    {
        var type = UnitType.Cleric() with { Cleave = new(), MoveAfterAttack = new(1), Atk = 0, Rng = 0 };
        var action = Action(Scenario(type));
        var result = Choose(Restore(action.State), "holy-wave");
        Assert.Equal(new[] { "a", "b", "cleric" }, result.Events.Where(e => e.Posture == Posture.Lying).Select(e => e.UnitId));
        Assert.All(result.Events, e => { Assert.Null(e.Attack); Assert.Equal(0, e.Damage); Assert.Equal(0, e.Hits); Assert.Equal(0, e.Blocks); });
        Assert.DoesNotContain(result.Events, e => e.Kind.Contains("Attack") || e.Kind is "CleaveResolved" or "UnitDied" or "MovementCompleted");
        Assert.False(result.State.CleavePending); Assert.Null(result.State.MoveAfterAttackAllowance);
        Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].CleaveUses);
        Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].HealUses);
        Assert.All(result.State.Units, u => Assert.Equal(4, u.CurrentHp));
        var steps = result.ResolutionSteps.Take(3).Select(s => s.StateAfter).ToArray();
        Assert.Equal(new[] { Posture.Upright, Posture.Lying, Posture.Upright }, steps[0].Physical.Figures.Select(f => f.Posture));
        Assert.Equal(new[] { Posture.Upright, Posture.Lying, Posture.Lying }, steps[1].Physical.Figures.Select(f => f.Posture));
        Assert.All(steps[2].Physical.Figures, f => Assert.Equal(Posture.Lying, f.Posture));
        Assert.All(steps, s => { Assert.True(s.ActionDone); Assert.Equal(new AbilityUses(2, 1), s.Units[0].HolyWaveUses); });
        Assert.Null(result.State.CurrentUnitId);
        Assert.Equal(Posture.Upright, action.State.Physical.Figures[0].Posture);
        Assert.Equal(new AbilityUses(2, 2), action.State.Units[0].HolyWaveUses);
    }

    [Theory]
    [InlineData("distant")] [InlineData("friendly")] [InlineData("blocked")] [InlineData("lying")]
    public void ZeroAffectedEnemiesIsLegalIrrelevantAndStillLaysDownCleric(string condition)
    {
        var state = Scenario();
        state.Units.RemoveAt(2); state.Physical.Figures.RemoveAt(2);
        switch (condition)
        {
            case "distant": state.Physical.Figures[1] = new("a", new(4, 2)); break;
            case "friendly": state.Units[1] = state.Units[1] with { SideId = "blue" }; break;
            case "blocked": state.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.Wall)); break;
            case "lying": state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying }; break;
        }
        var action = Action(state);
        Assert.Empty(WaveChoice(action).TargetIds); Assert.False(WaveChoice(action).Relevant);
        var result = Choose(action.State, "holy-wave");
        Assert.Equal("cleric", Assert.Single(result.Events, e => e.Posture == Posture.Lying).UnitId);
        Assert.Equal(Posture.Lying, result.State.Physical.Figures[0].Posture);
        Assert.True(result.ResolutionSteps[0].StateAfter.ActionDone);
        Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].HolyWaveUses);
    }

    [Theory]
    [InlineData("uses")] [InlineData("action")] [InlineData("move")]
    public void TimingAndUsesAreRevalidatedOnSubmission(string condition)
    {
        var action = Action();
        switch (condition)
        {
            case "uses": action.State.Units[0] = action.State.Units[0] with { HolyWaveUses = new(2, 0) }; break;
            case "action": action.State.ActionDone = true; break;
            case "move": action.State.MoveDone = false; break;
        }
        Assert.Throws<ArgumentException>(() => Choose(action.State, "holy-wave"));
        Assert.DoesNotContain(GameEngine.RefreshChoices(action.State, new Random(), false).NextInput!.Candidates, c => c.Action == UnitAction.HolyWave);
    }

    [Fact]
    public void LastUseDoesNotRefillAndNextActivationOnlyStandsClericUp()
    {
        var state = Scenario(); state.Units[0] = state.Units[0] with { HolyWaveUses = new(2, 1) };
        var result = Choose(Action(state).State, "holy-wave");
        while (!result.State.RoundComplete) result = Choose(result.State, result.NextInput!.Candidates[0].Key);
        result = GameEngine.StartRound(Restore(result.State), new Random(), false);
        Assert.Equal(Posture.Upright, result.State.Physical.Figures[0].Posture);
        Assert.Equal(new AbilityUses(2, 0), result.State.Units[0].HolyWaveUses);
        Assert.DoesNotContain(result.NextInput?.Candidates ?? [], c => c.Action == UnitAction.HolyWave);
        Assert.Contains(result.Events, e => e.UnitId == "cleric" && e.Posture == Posture.Upright);
    }

    [Fact]
    public void SubmissionRebuildsTargetsRatherThanTrustingSerializedCandidate()
    {
        var action = Action(); action.State.Pending!.Candidates.Clear();
        action.State.Physical.Figures[2] = new("b", new(5, 5));
        var result = Choose(Restore(action.State), "holy-wave");
        Assert.Equal(new[] { "a", "cleric" }, result.Events.Where(e => e.Posture == Posture.Lying).Select(e => e.UnitId));
        Assert.Equal(Posture.Upright, result.State.Physical.Figures[2].Posture);
    }
}
