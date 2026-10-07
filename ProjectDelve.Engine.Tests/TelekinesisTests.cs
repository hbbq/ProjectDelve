using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TelekinesisTests
{
    private sealed class NoDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Telekinesis must not roll.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Telekinesis must not roll.");
        public int RollD6() => throw new InvalidOperationException("Telekinesis must not roll.");
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState Scenario(UnitType? type = null, Cell? target = null)
    {
        type ??= UnitType.Wizard();
        return new()
        {
            Physical = new(new Board(8, 5, []), [new("wizard", new(0, 0)), new("enemy", target ?? new(4, 0))]),
            Types = [type, new("enemy-type", 0, 0, 0, 2, 8) { Unique = true }],
            Units = [type.CreateUnit("wizard", "blue"), new("enemy", "enemy-type", "red", 8)]
        };
    }
    private static EngineResult Choose(GameState state, string key) =>
        TestGame.Advance(state, new Choice(key), new NoDice(), false);
    private static EngineResult Action(GameState? state = null) =>
        Choose(TestGame.StartRound(state ?? Scenario(), new NoDice(), false).State, "stay");
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void UnlimitedActionHasDomainCardAndNoUsageStateAcrossGamesRoundsOrSerialization()
    {
        var type = UnitType.Wizard();
        var entry = Assert.Single(type.CardEntries(), e => e.Id == "telekinesis");
        Assert.Equal(new CardEntryDescription("telekinesis", "Telekinesis", "Action",
            "Choose an upright enemy within RNG and LOS. Lay it down."), entry);
        Assert.Null(entry.MaxUses);
        Assert.Null(entry.UseLimitText);
        Assert.Null(type.UsesFor(type.CreateUnit("wizard", "blue"), entry.Id));
        Assert.DoesNotContain(typeof(Unit).GetProperties(), p => p.Name.Contains("Telekinesis"));
        var state = Scenario();
        state.Units[0] = new("wizard", type.Id, "blue", 4);
        for (var round = 0; round < 4; round++)
        {
            var action = Action(Restore(state));
            var candidate = Assert.Single(action.NextInput!.Candidates, c => c.Action == UnitAction.Telekinesis);
            Assert.Equal(ActivationChoiceKind.Action, candidate.Kind);
            Assert.Equal("enemy", candidate.TargetId);
            Assert.True(candidate.Relevant);
            var result = Choose(Restore(action.State), candidate.Key);
            state = Choose(result.State, "end-turn").State;
            Assert.True(state.RoundComplete);
            Assert.Equal(Posture.Upright, state.Physical.Figures[1].Posture);
            Assert.Equal(new AbilityUses(2, 2), state.Units[0].FireballUses);
        }
    }

    [Theory]
    [InlineData(4, 4, 0, true)]
    [InlineData(4, 2, 2, true)]
    [InlineData(4, 3, 2, false)]
    [InlineData(4, 5, 0, false)]
    [InlineData(1, 1, 1, true)]
    [InlineData(1, 2, 0, false)]
    [InlineData(0, 1, 0, false)]
    public void UsesAuthoritativeRangeIncludingManhattanAndMelee(int range, int x, int y, bool legal)
    {
        var action = Action(Scenario(UnitType.Wizard() with { Rng = range }, new(x, y)));
        Assert.Equal(legal, action.NextInput!.Candidates.Any(c => c.Action == UnitAction.Telekinesis));
    }

    [Theory]
    [InlineData("friendly")]
    [InlineData("lying")]
    [InlineData("dead")]
    [InlineData("wall")]
    [InlineData("door")]
    [InlineData("tree")]
    [InlineData("intervening-hostile")]
    public void RejectsIllegalTargetsAndRevalidatesPreviouslySuppliedChoices(string condition)
    {
        var action = Action();
        var state = action.State;
        switch (condition)
        {
            case "friendly": state.Units[1] = state.Units[1] with { SideId = "blue" }; break;
            case "lying": state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying }; break;
            case "dead": state.Units[1] = state.Units[1] with { CurrentHp = 0 }; state.Physical.Figures.RemoveAt(1); break;
            case "wall": state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall)); break;
            case "door": state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.ClosedDoor)); break;
            case "tree": state.Physical.Board.Terrain.Add(new(new(2, 0), TerrainKind.Tree)); break;
            case "intervening-hostile":
                state.Types.Add(state.Types.Single(t => t.Id == "enemy-type") with { Id = "blocker-type" });
                state.Units.Add(new("blocker", "blocker-type", "red", 8));
                state.Physical.Figures.Add(new("blocker", new(2, 0)));
                break;
        }
        Assert.Throws<ArgumentException>(() => Choose(Restore(state), "telekinesis:enemy"));
        Assert.DoesNotContain(GameEngine.RefreshChoices(state, new NoDice(), false).NextInput!.Candidates,
            c => c.Key == "telekinesis:enemy");
    }

    [Fact]
    public void OpenDoorAndInterveningFriendlyPreserveNormalLos()
    {
        var state = Scenario();
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.OpenDoor));
        state.Types.Add(state.Types.Single(t => t.Id == "enemy-type") with { Id = "friend-type" });
        state.Units.Add(new("friend", "friend-type", "blue", 8));
        state.Physical.Figures.Add(new("friend", new(2, 0)));
        Assert.Contains(Action(state).NextInput!.Candidates, c => c.Key == "telekinesis:enemy");
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(-1, false)]
    public void EffectiveRangeFromGameStateDeterminesLegality(int modifier, bool legal)
    {
        var action = Action(Scenario(target: new(modifier > 0 ? 5 : 4, 0)));
        Assert.Equal(modifier < 0, action.NextInput!.Candidates.Any(c => c.Action == UnitAction.Telekinesis));
        action.State.ModifiersThisTurn.Add(new(Stat.Rng, modifier));
        var refreshed = GameEngine.RefreshChoices(Restore(action.State), new NoDice(), false);
        Assert.Equal(4 + modifier, refreshed.State.EffectiveRngOf("wizard"));
        Assert.Equal(legal, refreshed.NextInput!.Candidates.Any(c => c.Action == UnitAction.Telekinesis));
        if (legal) Assert.Equal(Posture.Lying, Choose(refreshed.State, "telekinesis:enemy").State.Physical.Figures[1].Posture);
        else Assert.Throws<ArgumentException>(() => Choose(refreshed.State, "telekinesis:enemy"));
    }

    [Fact]
    public void LaysDownWithoutMovementDamageDiceAttackOrFollowUpsAndConsumesOnlyAction()
    {
        var type = UnitType.Wizard() with { Atk = 0, Actions = UnitAction.Telekinesis, Cleave = new(), MoveAfterAttack = new(2) };
        var action = Action(Scenario(type));
        var result = Choose(Restore(action.State), "telekinesis:enemy");
        var changed = Assert.Single(TestGame.OperationEvents(result));
        Assert.Equal(new RulesEvent("PostureChanged", "enemy", Posture: Posture.Lying) { SourceUnitId = "wizard", ActionId = "telekinesis" }, changed);
        Assert.True(result.State.ActionDone);
        Assert.False(result.State.CleavePending);
        Assert.Null(result.State.MoveAfterAttackAllowance);
        Assert.Equal(action.State.Units.Select(u => u.CurrentHp), result.State.Units.Select(u => u.CurrentHp));
        Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].CleaveUses);
        Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].FireballUses);
        Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].BonusActionUses["Focus"]);
        Assert.Empty(result.State.BonusActionsUsedThisActivation);
        Assert.Equal(new Cell(4, 0), result.State.Physical.Figures[1].Position);
        Assert.Equal(Posture.Lying, result.State.Physical.Figures[1].Posture);
        Assert.Equal(Posture.Upright, result.State.Physical.Figures[0].Posture);
        Assert.Equal(Posture.Upright, action.State.Physical.Figures[1].Posture);
        var step = Assert.Single(TestGame.OperationSteps(result)).StateAfter;
        Assert.True(step.ActionDone);
        Assert.Equal(Posture.Lying, Restore(step).Physical.Figures[1].Posture);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Action);
        Assert.Throws<ArgumentException>(() => Choose(result.State, "telekinesis:enemy"));
    }

    [Theory]
    [InlineData("telekinesis:enemy")]
    [InlineData("attack:enemy")]
    [InlineData("fireball:4,0")]
    public void CompetesWithNormalAttackAndFireballForSingleAction(string key)
    {
        var action = Action();
        Assert.Contains(action.NextInput!.Candidates, c => c.Key == "attack:enemy");
        Assert.Contains(action.NextInput.Candidates, c => c.Key == "fireball:4,0");
        Assert.Contains(action.NextInput.Candidates, c => c.Key == "telekinesis:enemy");
        // Attack choices need a deterministic dice provider; reuse the existing physical faces.
        var result = TestGame.Advance(action.State, new Choice(key), new Dice(), false);
        Assert.True(result.State.ActionDone);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Action);
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException();
    }
}
