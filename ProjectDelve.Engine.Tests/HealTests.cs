using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class HealTests
{
    private sealed class Dice : IRandomProvider
    {
        public int Rolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag.Contains("cleric-type") ? "cleric-type" : bag[0];
        public AttackFace RollAttackDie() { Rolls++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() { Rolls++; return DefenceFace.Miss; }
        public int RollD6() => throw new InvalidOperationException("Heal must not roll dice.");
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState Scenario(int hp = 2) => new()
    {
        Physical = new(new Board(5, 5, [new(new(1, 1), new(1, 2), EdgeKind.ClosedDoor)]),
            [new("cleric", new(1, 1)), new("friend", new(2, 1)), new("enemy", new(0, 1))]),
        Types = [UnitType.Cleric(), UnitType.Hero("friend-type", 0, 1, 1, 2, 4), UnitType.Grunt()],
        Units = [UnitType.Cleric().CreateUnit("cleric", "blue"), new("friend", "friend-type", "blue", hp),
            UnitType.Grunt().CreateUnit("enemy", "red")]
    };

    private static EngineResult Choose(GameState state, string key, Dice? dice = null) =>
        GameEngine.Advance(state, new Choice(key), dice ?? new Dice(), false);

    private static EngineResult Action(GameState? scenario = null) =>
        Choose(GameEngine.StartRound(scenario ?? Scenario(), new Dice(), false).State, "stay");

    private static bool OffersHeal(EngineResult result, string id = "friend") =>
        result.NextInput!.Candidates.Any(c => c.Action == UnitAction.Heal && c.TargetId == id);

    [Fact]
    public void ContentAndBareUnitsInitializeTwoUsesAndSerialize()
    {
        var type = UnitType.Cleric();
        Assert.Equal(new Heal(2), type.Heal);
        Assert.Equal(new AbilityUses(2, 2), type.CreateUnit("c", "blue").HealUses);
        Assert.Null(UnitType.Grunt().Heal);
        var scenario = Scenario();
        scenario.Units[0] = new("cleric", type.Id, "blue", 4);
        var started = GameEngine.StartRound(scenario, new Dice(), false);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(started.State))!;
        Assert.Equal(new AbilityUses(2, 2), restored.Units[0].HealUses);
        Assert.Equal(type.Heal, restored.Types[0].Heal);
        Assert.Null(scenario.Units[0].HealUses);
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)]
    [InlineData(1, 2)] [InlineData(0, 2)] [InlineData(0, 1)] [InlineData(0, 0)]
    public void DamagedFriendlyUnitsInAllEightCellsAreLegal(int x, int y)
    {
        var scenario = Scenario();
        scenario.Physical.Board.Edges.Clear();
        scenario.Physical.Figures.RemoveAll(f => f.Id == "enemy");
        scenario.Units.RemoveAll(u => u.Id == "enemy");
        scenario.Physical.Figures[1] = new("friend", new(x, y));
        var action = Action(scenario);
        var candidate = Assert.Single(action.NextInput!.Candidates, c => c.Action == UnitAction.Heal);
        Assert.Equal("friend", candidate.TargetId);
        Assert.Equal(ActivationChoiceKind.Action, candidate.Kind);
    }

    [Theory]
    [InlineData(3, 4, 1)]
    [InlineData(2, 4, 2)]
    [InlineData(1, 3, 2)]
    public void HealRestoresOnlyMissingHpSpendsUseAndCompletesActionWithoutDice(int hp, int expectedHp, int healing)
    {
        var action = Action(Scenario(hp));
        Assert.True(OffersHeal(action));
        var dice = new Dice();
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(action.State))!;
        var healed = Choose(restored, "heal:friend", dice);
        Assert.Equal(expectedHp, healed.State.Units[1].CurrentHp);
        Assert.InRange(healed.State.Units[1].CurrentHp, 1, healed.State.Types[1].Hp);
        Assert.Equal(new AbilityUses(2, 1), healed.State.Units[0].HealUses);
        Assert.True(healed.State.ActionDone);
        Assert.True(healed.State.MoveDone);
        Assert.Equal(0, dice.Rolls);
        var resolved = Assert.Single(healed.Events);
        Assert.Equal("HealResolved", resolved.Kind);
        Assert.Equal(healing, resolved.Healing);
        Assert.Equal("cleric", resolved.UnitId);
        Assert.Equal("friend", resolved.TargetId);
        Assert.True(healed.ResolutionSteps[0].StateAfter.ActionDone);
        Assert.Equal(expectedHp, healed.ResolutionSteps[0].StateAfter.Units[1].CurrentHp);
        Assert.Equal(hp, action.State.Units[1].CurrentHp);
        Assert.Equal(new AbilityUses(2, 2), action.State.Units[0].HealUses);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("hostile")]
    [InlineData("distant")]
    [InlineData("dead")]
    public void IllegalTargetsAreNotOfferedOrAccepted(string condition)
    {
        var scenario = Scenario();
        switch (condition)
        {
            case "full": scenario.Units[1] = scenario.Units[1] with { CurrentHp = 4 }; break;
            case "hostile": scenario.Units[1] = scenario.Units[1] with { SideId = "red" }; break;
            case "distant": scenario.Physical.Figures[1] = new("friend", new(3, 1)); break;
            case "dead":
                scenario.Units[1] = scenario.Units[1] with { CurrentHp = 0 };
                scenario.Physical.Figures.RemoveAll(f => f.Id == "friend");
                break;
        }
        var action = Action(scenario);
        Assert.False(OffersHeal(action));
        Assert.Throws<ArgumentException>(() => Choose(action.State, "heal:friend"));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.WallWithWindow, true)]
    public void AdjacentTargetsUseNormalLos(EdgeKind kind, bool legal)
    {
        var scenario = Scenario();
        scenario.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), kind));
        Assert.Equal(legal, OffersHeal(Action(scenario)));
    }

    [Fact]
    public void DiagonalTargetUsesSharedCornerLos()
    {
        var scenario = Scenario();
        scenario.Physical.Figures[1] = new("friend", new(2, 2));
        Assert.True(OffersHeal(Action(scenario))); // One passage is still open.
        scenario.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        Assert.False(OffersHeal(Action(scenario)));
    }

    [Fact]
    public void DamagedClericCannotHealItself()
    {
        var scenario = Scenario();
        scenario.Units[0] = scenario.Units[0] with { CurrentHp = 1 };
        var action = Action(scenario);
        Assert.False(OffersHeal(action, "cleric"));
        Assert.Throws<ArgumentException>(() => Choose(action.State, "heal:cleric"));
    }

    [Fact]
    public void FriendshipDependsOnSideEvenForMonsterContent()
    {
        var scenario = Scenario();
        var goblin = UnitType.Goblin() with { Hp = 4 };
        scenario.Types[1] = goblin;
        scenario.Units[1] = new("friend", goblin.Id, "blue", 2);
        Assert.True(OffersHeal(Action(scenario)));
    }

    [Fact]
    public void ZeroUsesOrCompletedActionPreventHealAndSubmissionRechecksState()
    {
        var action = Action();
        action.State.Units[0] = action.State.Units[0] with { HealUses = new(2, 0) };
        Assert.Throws<ArgumentException>(() => Choose(action.State, "heal:friend"));
        Assert.False(OffersHeal(GameEngine.RefreshChoices(action.State, new Dice(), false)));
        action = Action();
        action.State.ActionDone = true;
        Assert.Throws<ArgumentException>(() => Choose(action.State, "heal:friend"));
        Assert.False(OffersHeal(GameEngine.RefreshChoices(action.State, new Dice(), false)));
    }

    [Theory]
    [InlineData("heal:friend", "attack:enemy")]
    [InlineData("attack:enemy", "heal:friend")]
    public void AttackAndHealCompeteAndNormalChoicesRegenerate(string chosen, string rejected)
    {
        var action = Action();
        Assert.True(OffersHeal(action));
        Assert.Contains(action.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        var result = Choose(action.State, chosen);
        Assert.True(result.State.ActionDone);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal(new[] { "open-door:1,1:1,2", "end-turn" }, result.NextInput.Candidates.Select(c => c.Key));
        Assert.Throws<ArgumentException>(() => Choose(result.State, rejected));
        Assert.Equal(3, result.State.EffectiveDefOf("friend")); // Aura remains derived.
        if (chosen == "attack:enemy")
        {
            Assert.Equal(3, Assert.Single(result.Events, e => e.Kind == "AttackResolved").Hits);
            Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].HealUses);
        }
    }
}
