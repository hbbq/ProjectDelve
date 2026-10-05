using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PostureTests
{
    private sealed class Random : IRandomProvider
    {
        public int DefenceRolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() { DefenceRolls++; return DefenceFace.Block; }
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class NoOrdinaryChoices : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) =>
            throw new InvalidOperationException("A Lying activation must not consult Behavior.");
    }
    private static GameState State(UnitType? type = null, Posture posture = Posture.Upright)
    {
        type ??= UnitType.Barbarian();
        var target = new UnitType("target-type", 1, 1, 2, 2, 10) { Unique = true };
        return new()
        {
            Physical = new(new Board(5, 3, [new(new(1, 1), new(1, 0), EdgeKind.ClosedDoor)]),
                [new("actor", new(1, 1), posture), new("target", new(2, 1))]),
            Types = [type, target],
            Units = [type.CreateUnit("actor", "blue"), target.CreateUnit("target", "red")]
        };
    }
    private static EngineResult Choose(GameState state, string key, Random? random = null) =>
        GameEngine.Advance(state, new Choice(key), random ?? new(), false);
    private static void Lie(GameState state, string id)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Posture = Posture.Lying };
    }

    [Fact]
    public void UprightActivatesNormallyWithMoveBonusFreeActionAndAttack()
    {
        var start = GameEngine.StartRound(State(), new Random(), false);
        Assert.Equal("actor", start.NextInput!.UnitId);
        Assert.Contains(start.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Move);
        Assert.Contains(start.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
        Assert.Contains(start.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.FreeAction);
        var action = Choose(start.State, "stay");
        Assert.Contains(action.NextInput!.Candidates, c => c.Key == "attack:target");
        Assert.DoesNotContain(start.Events, e => e.Kind == "PostureChanged");
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void LyingActivationOnlyStandsAndCompletesWithoutBehavior(bool automatic)
    {
        var state = State(UnitType.Goblin() with { FreeActions = UnitFreeAction.OpenDoor }, Posture.Lying);
        var result = GameEngine.StartRound(state, new Random(), automatic);
        Assert.Equal("target", result.NextInput!.UnitId);
        Assert.Equal(Posture.Upright, result.State.Physical.Figures[0].Posture);
        Assert.Equal(Posture.Lying, state.Physical.Figures[0].Posture);
        Assert.Equal(Posture.Upright, Assert.Single(result.Events, e => e.Kind == "PostureChanged").Posture);
        Assert.DoesNotContain(result.Events, e => e.Kind is "MovementCompleted" or "AttackResolved" or "AbilityUsed" or "DoorOpened");
        Assert.Null(result.State.MoveAfterAttackAllowance);
    }

    [Fact]
    public void SameTypeUnitsHaveIndependentPostureAndIndividualActivationsAfterSerialization()
    {
        var state = State();
        state.Types[0] = state.Types[0] with { Hp = 1, Unique = false };
        state.Units[0] = state.Units[0] with { CurrentHp = 1 };
        state.Units.Add(state.Types[0].CreateUnit("second", "blue"));
        state.Units.Add(state.Types[0].CreateUnit("third", "blue"));
        state.Physical.Figures.Add(new("second", new(0, 2), Posture.Lying));
        state.Physical.Figures.Add(new("third", new(1, 2), Posture.Lying));
        var result = GameEngine.StartRound(state, new Random(), false);
        Assert.Equal(DecisionKind.SelectUnit, result.NextInput!.Kind);
        Assert.Equal(3, result.NextInput.Candidates.Count);
        result = Choose(result.State, "second");
        Assert.Contains("second", result.State.CompletedUnitIds);
        Assert.Equal(new[] { Posture.Upright, Posture.Upright, Posture.Upright, Posture.Lying }, result.State.Physical.Figures.Select(f => f.Posture));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        result = Choose(restored, "third");
        Assert.Equal("actor", result.NextInput!.UnitId);
        Assert.Contains("second", result.State.CompletedUnitIds);
        Assert.Contains("third", result.State.CompletedUnitIds);
        Assert.All(result.NextInput.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move), c => Assert.NotNull(c.Path));
        Assert.Equal("third", Assert.Single(result.Events, e => e.Kind == "PostureChanged").UnitId);
    }

    [Fact]
    public void LyingTargetUsesNormalDefenceAndRemainsOccupiedAndHostile()
    {
        var state = State(); Lie(state, "target");
        var random = new Random();
        var started = GameEngine.StartRound(state, random, false);
        Assert.DoesNotContain(started.NextInput!.Candidates, c => c.Destination == new Cell(2, 1));
        Assert.True(new GameplayQueries(started.State).HasNearbyHostileThreatFrom("actor", new(1, 1)));
        var action = Choose(started.State, "stay");
        Assert.Contains(action.NextInput!.Candidates, c => c.Key == "attack:target");
        var result = Choose(action.State, "attack:target", random);
        Assert.Equal(2, random.DefenceRolls);
        Assert.Equal(2, result.Events.Single(e => e.Kind == "AttackResolved").Attack!.Targets[0].DefenceDice);
        Assert.Equal(8, result.State.Units[1].CurrentHp);
        Assert.Contains(result.State.Physical.Figures, f => f.Id == "target" && f.Position == new Cell(2, 1) && f.Posture == Posture.Lying);
    }

    [Fact]
    public void FriendlyLyingOccupantCanBeTraversedButCannotBeDestination()
    {
        var state = State(); Lie(state, "target");
        state.Units[1] = state.Units[1] with { SideId = "blue" };
        var paths = MovementRules.FindPaths(state, "actor", new(1, 1), 2);
        Assert.DoesNotContain(new Cell(2, 1), paths.Keys);
        Assert.Equal(new[] { new Cell(1, 1), new Cell(2, 1), new Cell(3, 1) }, paths[new(3, 1)]);
    }

    [Fact]
    public void LyingPassiveSourceIsInactiveButUprightAuraAndModifiersStillAffectLyingRecipient()
    {
        var state = State(UnitType.Cleric());
        state.Units[1] = state.Units[1] with { SideId = "blue" };
        Lie(state, "target");
        state.CurrentUnitId = "target";
        state.ModifiersThisTurn.Add(new(Stat.Def, 2));
        Assert.Equal(5, state.EffectiveDefOf("target"));
        Lie(state, "actor");
        Assert.Equal(4, state.EffectiveDefOf("target"));
        Assert.Equal(10, state.Units[1].CurrentHp);
        Assert.Equal(1, state.EffectiveMovOf("target"));
        Assert.Equal(1, state.EffectiveRngOf("target"));
        Assert.Equal(2, state.EffectiveAtkOf("target"));
    }

    [Fact]
    public void FuryAndBackstabAreInactiveOnLyingSourceButLyingUnitsCountForOthersConditions()
    {
        var type = UnitType.Barbarian() with { Backstab = new() };
        var state = State(type);
        state.Units.Add(state.Types[1].CreateUnit("enemy", "red"));
        state.Physical.Figures.Add(new("enemy", new(0, 1), Posture.Lying));
        state.Units.Add(state.Types[1].CreateUnit("friend", "blue"));
        state.Physical.Figures.Add(new("friend", new(3, 1), Posture.Lying));
        Assert.Equal(5, state.EffectiveAtkOf("actor"));
        Assert.Equal(6, state.EffectiveAtkAgainst("actor", "target"));
        Lie(state, "actor");
        Assert.Equal(4, state.EffectiveAtkOf("actor"));
        Assert.Equal(4, state.EffectiveAtkAgainst("actor", "target"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LyingCurrentUnitCannotResumeOrdinaryChoicesOrCapabilities(bool cleave)
    {
        var state = State(UnitType.Barbarian() with { MoveAfterAttack = new(1), Behaviors = UnitBehavior.BackAwayAfterAttack });
        var result = GameEngine.StartRound(state, new Random(), false);
        Lie(result.State, "actor");
        result.State.CleavePending = cleave;
        result.State.MoveAfterAttackAllowance = 1;
        Assert.Empty(GameEngine.GameplayCandidates(result.State, result.State.Units[0]));
        Assert.Equal(UnitBehavior.None, new GameplayQueries(result.State).BehaviorsOf("actor"));
        Assert.False(new GameplayQueries(result.State).CanAttackHostileFrom("actor", new(1, 1)));
        result = GameEngine.Advance(result.State, new NoOrdinaryChoices(), new Random(), false);
        Assert.Equal("target", result.NextInput!.UnitId);
        Assert.False(result.State.CleavePending);
        Assert.Null(result.State.MoveAfterAttackAllowance);
        Assert.Equal(Posture.Lying, result.State.Physical.Figures[0].Posture);
        Assert.Empty(result.Events.Where(e => e.UnitId == "actor"));
    }
}
