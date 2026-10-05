using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class HolyWaveTests
{
    private sealed class Dice(int hits = 2, params DefenceFace[] defence) : IRandomProvider
    {
        public int AttackRolls { get; private set; }
        public int DefenceRolls { get; private set; }
        public List<string> Order { get; } = [];
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie()
        {
            Order.Add("attack");
            return AttackRolls++ < hits ? AttackFace.Hit : AttackFace.Miss;
        }
        public DefenceFace RollDefenceDie()
        {
            Order.Add("defence");
            var index = DefenceRolls++;
            return index < defence.Length ? defence[index] : DefenceFace.Miss;
        }
        public int RollD6() => throw new InvalidOperationException();
    }

    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState Scenario(UnitType? attacker = null, int hp = 4)
    {
        attacker ??= UnitType.Cleric();
        return new()
        {
            Physical = new(new Board(6, 6, [new(new(2, 2), new(2, 3), EdgeKind.ClosedDoor)]),
                [new("cleric", new(2, 2)), new("a", new(3, 2)), new("b", new(1, 1))]),
            Types = [attacker, new("enemy", 0, 0, 0, 1, 4)],
            Units = [attacker.CreateUnit("cleric", "blue"), new("a", "enemy", "red", hp),
                new("b", "enemy", "third-side", hp)]
        };
    }

    private static EngineResult Choose(GameState state, string? key, Dice? dice = null) =>
        GameEngine.Advance(state, new Choice(key), dice ?? new(), false);
    private static EngineResult Action(GameState? state = null) =>
        Choose(GameEngine.StartRound(state ?? Scenario(), new Dice(), false).State, "stay");
    private static EngineResult Wave(GameState? state = null, Dice? dice = null) =>
        Choose(Action(state).State, "holy-wave", dice);
    private static Candidate? WaveChoice(EngineResult result) =>
        result.NextInput!.Candidates.SingleOrDefault(c => c.Action == UnitAction.HolyWave);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void ContentBareUnitsAndSerializationPreserveTwoUses()
    {
        var type = UnitType.Cleric();
        Assert.Equal(new HolyWave(2), type.HolyWave);
        Assert.Equal(new AbilityUses(2, 2), type.CreateUnit("c", "side").HolyWaveUses);
        Assert.Null(UnitType.Grunt().HolyWave);
        var scenario = Scenario();
        scenario.Units[0] = new("cleric", type.Id, "blue", 4);
        var action = Action(scenario);
        var restored = Restore(action.State);
        Assert.Equal(new AbilityUses(2, 2), restored.Units[0].HolyWaveUses);
        Assert.Equal(new[] { "a", "b" }, restored.Pending!.Candidates.Single(c => c.Action == UnitAction.HolyWave).TargetIds);
        Assert.Null(scenario.Units[0].HolyWaveUses);
    }

    [Theory]
    [InlineData("distant")]
    [InlineData("friendly")]
    [InlineData("blocked")]
    [InlineData("dead")]
    public void NoAdjacentHostileWithLosMeansUnavailable(string condition)
    {
        var scenario = Scenario();
        scenario.Units.RemoveAt(2);
        scenario.Physical.Figures.RemoveAt(2);
        switch (condition)
        {
            case "distant": scenario.Physical.Figures[1] = new("a", new(4, 2)); break;
            case "friendly": scenario.Units[1] = scenario.Units[1] with { SideId = "blue" }; break;
            case "blocked": scenario.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.Wall)); break;
            case "dead":
                scenario.Units[1] = scenario.Units[1] with { CurrentHp = 0 };
                scenario.Physical.Figures.RemoveAt(1);
                break;
        }
        var action = Action(scenario);
        Assert.Null(WaveChoice(action));
        Assert.Throws<ArgumentException>(() => Choose(action.State, "holy-wave"));
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 1)] [InlineData(3, 2)]
    [InlineData(3, 3)] [InlineData(2, 3)] [InlineData(1, 3)] [InlineData(1, 2)]
    public void OneEnemyInAnySurroundingCellMakesOneLegalAction(int x, int y)
    {
        var scenario = Scenario();
        scenario.Physical.Board.Edges.Clear();
        scenario.Units.RemoveAt(2);
        scenario.Physical.Figures.RemoveAt(2);
        scenario.Physical.Figures[1] = new("a", new(x, y));
        var candidate = WaveChoice(Action(scenario))!;
        Assert.Equal(new[] { "a" }, candidate.TargetIds);
        Assert.Equal(ActivationChoiceKind.Action, candidate.Kind);
        Assert.Null(candidate.TargetId);
    }

    [Fact]
    public void CompleteTargetSetExcludesFriendsDistantAndBlockedUnits()
    {
        var scenario = Scenario();
        scenario.Units.AddRange([new("friend", "enemy", "blue", 4), new("far", "enemy", "red", 4),
            new("blocked", "enemy", "red", 4)]);
        scenario.Physical.Figures.AddRange([new("friend", new(2, 1)), new("far", new(5, 5)), new("blocked", new(2, 3))]);
        Assert.Equal(new[] { "a", "b" }, WaveChoice(Action(scenario))!.TargetIds);
    }

    [Fact]
    public void SharedRollSeparateEffectiveDefenceAndDamageAreOneAttack()
    {
        var scenario = Scenario();
        scenario.Units[2] = scenario.Units[2] with { SideId = "red" };
        var aura = UnitType.Cleric("enemy-aura");
        scenario.Types.Add(aura);
        scenario.Units.Add(aura.CreateUnit("aura", "red"));
        scenario.Physical.Figures.Add(new("aura", new(4, 2))); // Supports a, outside wave.
        var dice = new Dice(2, DefenceFace.Block, DefenceFace.Miss, DefenceFace.Miss);
        var result = Wave(scenario, dice);
        var attack = Assert.Single(result.Events, e => e.Kind == "AttackResolved");
        Assert.Equal("cleric", attack.UnitId);
        Assert.Null(attack.TargetId);
        Assert.Equal(2, attack.Attack!.AttackDice);
        Assert.Equal(2, attack.Attack.Hits);
        Assert.Equal(new[] { new AttackTargetResult("a", 2, 1, 1), new AttackTargetResult("b", 1, 0, 2) }, attack.Attack.Targets);
        Assert.Equal(2, dice.AttackRolls);
        Assert.Equal(3, dice.DefenceRolls);
        Assert.Equal(new[] { "attack", "attack", "defence", "defence", "defence" }, dice.Order);
        Assert.Equal(3, result.State.Units[1].CurrentHp);
        Assert.Equal(2, result.State.Units[2].CurrentHp);
        Assert.Equal(4, result.State.Units[3].CurrentHp);
        var restored = JsonSerializer.Deserialize<EngineResult>(JsonSerializer.Serialize(result))!;
        Assert.Equal(attack.Attack.Targets.ToArray(), restored.Events.Last().Attack!.Targets.ToArray());
    }

    [Fact]
    public void MultipleDeathsKeepFixedMembershipFullDamageAndIntermediateSnapshots()
    {
        var action = Action(Scenario(hp: 1));
        var result = Choose(action.State, "holy-wave");
        Assert.Equal(new[] { "AttackTargetResolved", "UnitDied", "AttackTargetResolved", "UnitDied", "AttackResolved" }, result.Events.Select(e => e.Kind));
        Assert.All(result.Events.Last().Attack!.Targets, t => Assert.Equal(2, t.Damage));
        Assert.All(result.State.Units.Skip(1), u => Assert.Equal(0, u.CurrentHp));
        Assert.Single(result.State.Physical.Figures);
        var states = result.ResolutionSteps.Select(s => s.StateAfter).ToArray();
        Assert.Contains(states[0].Physical.Figures, f => f.Id == "a");
        Assert.DoesNotContain(states[1].Physical.Figures, f => f.Id == "a");
        Assert.Equal(1, states[1].Units[2].CurrentHp);
        Assert.Equal(0, states[2].Units[2].CurrentHp);
        Assert.Contains(states[2].Physical.Figures, f => f.Id == "b");
        Assert.DoesNotContain(states[3].Physical.Figures, f => f.Id == "b");
        Assert.All(states, s =>
        {
            Assert.True(s.ActionDone);
            Assert.Equal(new AbilityUses(2, 1), s.Units[0].HolyWaveUses);
            Assert.Null(s.Pending);
        });
        Assert.Equal(1, action.State.Units[1].CurrentHp);
        Assert.Equal(new AbilityUses(2, 2), action.State.Units[0].HolyWaveUses);
    }

    [Theory]
    [InlineData(-10)] [InlineData(2)]
    public void FixedAttackIgnoresTurnModifiersFuryAndBackstab(int modifier)
    {
        var type = UnitType.Cleric() with { Atk = 0, Rng = 0, Fury = new(), Backstab = new() };
        var scenario = Scenario(type);
        scenario.Units.Add(new("friend", "enemy", "blue", 4));
        scenario.Physical.Figures.Add(new("friend", new(3, 1)));
        var action = Action(scenario);
        action.State.ModifiersThisTurn.Add(new(Stat.Atk, modifier));
        Assert.Equal(modifier + 2, action.State.EffectiveAtkAgainst("cleric", "a"));
        var dice = new Dice();
        var result = Choose(action.State, "holy-wave", dice);
        Assert.Equal(2, dice.AttackRolls);
        Assert.All(result.Events.Last().Attack!.Targets, t => Assert.Equal(2, t.Damage));
    }

    [Fact]
    public void DeathOfAuraSourceUpdatesLaterDefenceWithoutChangingTargets()
    {
        var scenario = Scenario();
        var aura = UnitType.Cleric("enemy-aura") with { Def = 0 };
        scenario.Types.Add(aura);
        scenario.Units[1] = new("a", aura.Id, "red", 1);
        scenario.Units[2] = scenario.Units[2] with { SideId = "red" };
        scenario.Physical.Figures[2] = new("b", new(3, 1));
        Assert.Equal(2, scenario.EffectiveDefOf("b"));
        var dice = new Dice();
        var result = Wave(scenario, dice);
        Assert.Equal(1, dice.DefenceRolls);
        Assert.Equal(new[] { "a", "b" }, result.Events.Last().Attack!.Targets.Select(t => t.TargetId));
        Assert.Equal(1, result.Events.Last().Attack!.Targets[1].DefenceDice);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "a");
    }

    [Fact]
    public void MissedWaveStillSpendsLastUseAndUsesDoNotRefillNextRound()
    {
        var scenario = Scenario();
        scenario.Units[0] = scenario.Units[0] with { HolyWaveUses = new(2, 1) };
        var result = Wave(scenario, new Dice(0));
        Assert.True(result.State.ActionDone);
        Assert.Equal(new AbilityUses(2, 0), result.State.Units[0].HolyWaveUses);
        Assert.All(result.Events.Last().Attack!.Targets, t => Assert.Equal(0, t.Damage));
        Assert.All(result.State.Units.Skip(1), u => Assert.Equal(4, u.CurrentHp));
        while (!result.State.RoundComplete)
        {
            var next = result.NextInput!;
            var key = next.Kind == DecisionKind.SelectUnit ? next.Candidates[0].Key
                : next.Candidates.Any(c => c.Key == "stay") ? "stay" : "end-turn";
            result = Choose(result.State, key);
        }
        result = GameEngine.StartRound(result.State, new Dice(), false);
        result = Choose(result.State, "stay");
        Assert.Null(WaveChoice(result));
        Assert.Equal(new AbilityUses(2, 0), result.State.Units[0].HolyWaveUses);
    }

    [Fact]
    public void NormalAttackKeepsOneTargetTargetSpecificAtkAndExistingEventFields()
    {
        var type = UnitType.Cleric() with { Fury = new(), Backstab = new() };
        var scenario = Scenario(type);
        scenario.Units.Add(new("friend", "enemy", "blue", 4));
        scenario.Physical.Figures.Add(new("friend", new(3, 1)));
        var action = Action(scenario);
        action.State.ModifiersThisTurn.Add(new(Stat.Atk, 2));
        var dice = new Dice(7);
        var result = Choose(action.State, "attack:a", dice);
        var attack = Assert.Single(result.Events, e => e.Kind == "AttackResolved");
        Assert.Equal(7, dice.AttackRolls);
        Assert.Equal(1, dice.DefenceRolls);
        Assert.Equal("a", attack.TargetId);
        Assert.Equal(7, attack.Hits);
        Assert.Equal(0, attack.Blocks);
        Assert.Equal(7, attack.Damage);
        Assert.Equal(7, attack.Attack!.AttackDice);
        Assert.Equal(new AttackTargetResult("a", 1, 0, 7), Assert.Single(attack.Attack.Targets));
        Assert.Equal(4, result.State.Units[2].CurrentHp);
        Assert.DoesNotContain(result.Events, e => e.Kind == "AttackTargetResolved");
    }

    [Theory]
    [InlineData("uses")]
    [InlineData("action")]
    [InlineData("move")]
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
        Assert.Null(WaveChoice(GameEngine.RefreshChoices(action.State, new Dice(), false)));
    }

    [Fact]
    public void SpendsExactlyOneUseCompletesActionAndCompetesWithOtherActions()
    {
        var action = Action();
        var result = Choose(action.State, "holy-wave");
        Assert.True(result.State.ActionDone);
        Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].HolyWaveUses);
        Assert.Equal(new AbilityUses(2, 2), result.State.Units[0].HealUses);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Action);
        Assert.Throws<ArgumentException>(() => Choose(result.State, "attack:a"));
        var normal = Choose(action.State, "attack:a");
        Assert.Null(WaveChoice(normal));
        Assert.Equal(new AbilityUses(2, 2), normal.State.Units[0].HolyWaveUses);
    }

    [Fact]
    public void MoveAfterAttackOccursOnceAfterAllTargetsAndDeaths()
    {
        var goblin = UnitType.Goblin() with { Actions = UnitAction.NormalAttack | UnitAction.HolyWave, HolyWave = new() };
        goblin = goblin with { FreeActions = UnitFreeAction.OpenDoor }; // Keeps an ordinary decision after movement.
        var result = Wave(Scenario(goblin, hp: 1));
        Assert.Equal(DecisionKind.Move, result.NextInput!.Kind);
        Assert.True(result.NextInput.IsMoveAfterAttack);
        Assert.True(result.NextInput.AllowsNone);
        Assert.Single(result.Events, e => e.Kind == "AttackResolved");
        Assert.Equal(2, result.Events.Count(e => e.Kind == "UnitDied"));
        Assert.Throws<ArgumentException>(() => Choose(result.State, "end-turn"));
        result = Choose(Restore(result.State), null);
        Assert.True(Assert.Single(result.Events, e => e.Kind == "MovementCompleted").IsMoveAfterAttack);
        Assert.Null(result.State.MoveAfterAttackAllowance);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
    }

    [Theory]
    [InlineData(0, true)] // Both targets take 2.
    [InlineData(1, true)] // One takes 2, the other 1.
    [InlineData(2, false)] // Both take 1; never sum these.
    public void CleaveUsesPerTargetDamageAndCreatesAtMostOneFollowUp(int blocks, bool qualifies)
    {
        var type = UnitType.Cleric() with { Cleave = new(), MoveAfterAttack = new(1) };
        var dice = new Dice(2, blocks > 0 ? DefenceFace.Block : DefenceFace.Miss,
            blocks > 1 ? DefenceFace.Block : DefenceFace.Miss);
        var result = Wave(Scenario(type), dice);
        Assert.Equal(qualifies, result.State.CleavePending);
        Assert.Equal(qualifies ? DecisionKind.Cleave : DecisionKind.Move, result.NextInput!.Kind);
        Assert.Throws<ArgumentException>(() => Choose(result.State, "open-door:2,2:2,3"));
        Assert.Throws<ArgumentException>(() => Choose(result.State, "end-turn"));
        if (qualifies)
        {
            Assert.Equal(2, result.NextInput.Candidates.Count);
            result = Choose(Restore(result.State), "cleave:a");
            Assert.Single(result.Events, e => e.Kind == "CleaveResolved");
            Assert.False(result.State.CleavePending);
            Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].CleaveUses);
            Assert.Equal(DecisionKind.Move, result.NextInput!.Kind);
        }
        result = Choose(result.State, null);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.False(result.State.CleavePending);
        Assert.Null(result.State.MoveAfterAttackAllowance);
    }

    [Fact]
    public void SubmissionRebuildsTargetsRatherThanTrustingSerializedCandidate()
    {
        var action = Action();
        action.State.Pending!.Candidates.Clear();
        action.State.Physical.Figures[2] = new("b", new(5, 5));
        var result = Choose(Restore(action.State), "holy-wave");
        Assert.Equal("a", Assert.Single(result.Events.Last().Attack!.Targets).TargetId);
        Assert.Equal(4, result.State.Units[2].CurrentHp);
    }
}
