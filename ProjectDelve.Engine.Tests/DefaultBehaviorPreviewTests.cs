using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DefaultBehaviorPreviewTests
{
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class CountingRandom : IRandomProvider
    {
        public int Draws, Shuffles, Attacks, Defences, D6s;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) { Draws++; return bag[0]; }
        public int WorldShuffleIndex(int exclusiveMax) { Shuffles++; return 0; }
        public AttackFace RollAttackDie() { Attacks++; return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { Defences++; return DefenceFace.Miss; }
        public int RollD6() { D6s++; return 1; }
    }

    private static (GameState State, CountingRandom Random) TwoTrollDoor()
    {
        var troll = UnitType.Troll();
        var hero = new UnitType("hero", 0, 0, 0, 0, 5) { Unique = true };
        var state = new GameState
        {
            Physical = new(new Board(5, 2,
            [
                new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor),
                new(new(1, 1), new(2, 1), EdgeKind.ClosedDoor)
            ]), [new("troll-1", new(0, 0)), new("troll-2", new(0, 1)), new("hero", new(4, 0))]),
            Types = [troll, hero],
            Units = [troll.CreateUnit("troll-1", "red"), troll.CreateUnit("troll-2", "red"), hero.CreateUnit("hero", "blue")],
            Controllers = [new(new(troll.Id, "red"), ControllerKind.Automated), new(new(hero.Id, "blue"), ControllerKind.Human)],
            Objectives = [new("red"), new("blue")]
        };
        var random = new CountingRandom();
        return (GameEngine.StartRound(state, random, false).State, random);
    }

    [Fact]
    public void TwoTrollDoorPreviewIsDetachedDeterministicAndStopsBeforeDice()
    {
        var (state, random) = TwoTrollDoor();
        var before = JsonSerializer.Serialize(state);

        var first = GameEngine.PreviewDefaultBehaviorActivation(state, "troll-1");
        var second = GameEngine.PreviewDefaultBehaviorActivation(state, "troll-1");

        Assert.Equal(first.UnitId, second.UnitId);
        Assert.Equal(first.Advisory, second.Advisory);
        Assert.Equal(first.StopReason, second.StopReason);
        Assert.Equal(first.Steps, second.Steps);
        Assert.True(first.Advisory);
        Assert.Equal(DefaultBehaviorPreviewStopReason.DiceRequired, first.StopReason);
        Assert.Equal(new[] { DecisionKind.Activation, DecisionKind.Activation }, first.Steps.Select(step => step.DecisionKind));
        Assert.Equal(ActivationChoiceKind.SelectUnit, state.Pending!.Candidates.Single(candidate => candidate.Key == "troll-1").Kind);
        Assert.Contains(first.Steps, step => step.IntendedAction.Contains("Move", StringComparison.Ordinal));
        Assert.Contains(first.Steps, step => step.IntendedAction.Contains("door", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.Equal((1, 0, 0, 0, 0), (random.Draws, random.Shuffles, random.Attacks, random.Defences, random.D6s));
    }

    [Fact]
    public void PreviewStopsAtBoundWithoutInventingLaterChoices()
    {
        var (state, _) = TwoTrollDoor();
        var preview = GameEngine.PreviewDefaultBehaviorActivation(state, "troll-2", 1);
        Assert.Equal(DefaultBehaviorPreviewStopReason.StepLimit, preview.StopReason);
        Assert.Single(preview.Steps);
    }

    [Fact]
    public void ActivePreviewRebuildsCanonicalLegalityAndAppliesItsRecordedChoice()
    {
        var (selection, random) = TwoTrollDoor();
        var active = GameEngine.Advance(selection, new Choice("troll-1"), random, false).State;
        var token = active.Pending!.Token;
        active.Pending = new(DecisionKind.Activation, UnitTypeIds.Troll, "troll-1",
            [new("stale-end", Kind: ActivationChoiceKind.EndTurn)], false) { Token = token };
        var before = JsonSerializer.Serialize(active);

        var preview = GameEngine.PreviewDefaultBehaviorActivation(active, "troll-1");

        Assert.Equal(DefaultBehaviorPreviewStopReason.DiceRequired, preview.StopReason);
        Assert.Equal("1,0", preview.Steps[0].CandidateKey);
        Assert.DoesNotContain(preview.Steps, step => step.CandidateKey == "stale-end");
        Assert.Contains(preview.Steps, step => step.IntendedAction.Contains("door", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, JsonSerializer.Serialize(active));
        Assert.Equal((1, 0, 0, 0, 0), (random.Draws, random.Shuffles, random.Attacks, random.Defences, random.D6s));
    }}
