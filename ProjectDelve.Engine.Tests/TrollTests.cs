using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TrollTests
{
    private sealed class Dice(int hits = 2, int doorRoll = 1) : IRandomProvider
    {
        private int rolls;
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => rolls++ < hits ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => doorRoll;
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static EngineResult Choose(GameState state, string? key, Dice? dice = null) =>
        GameEngine.Advance(state, new Choice(key), dice ?? new(), false);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static GameState Scenario(UnitType? target = null, Posture posture = Posture.Upright, bool cleave = false)
    {
        target ??= UnitType.Troll();
        var attacker = new UnitType("attacker-type", 2, 1, 2, 0, 5, FreeActions: UnitFreeAction.OpenDoor)
            { Cleave = cleave ? new() : null };
        return new()
        {
            Physical = new(new Board(4, 2, [new(new(0, 0), new(0, 1), EdgeKind.ClosedDoor)]),
                [new("attacker", new(0, 0)), new("target", new(1, 0), posture)]),
            Types = [attacker, target],
            Units = [attacker.CreateUnit("attacker", "blue"), target.CreateUnit("target", "red")]
        };
    }
    private static EngineResult Attack(GameState state, Dice? dice = null) =>
        Choose(Choose(GameEngine.StartRound(state, new Dice(), false).State, "stay").State, "attack:target", dice);
    private static void AssertSaved(GameState state)
    {
        Assert.Equal(1, state.Units.Single(u => u.Id == "target").CurrentHp);
        Assert.Equal(new Figure("target", new(1, 0), Posture.Lying), state.Physical.Figures.Single(f => f.Id == "target"));
    }

    [Fact]
    public void TrollComposesExactStatsSharedActionsBehaviorAndUnlimitedCapability()
    {
        var type = UnitType.Troll();
        Assert.Equal((2, 1, 4, 4, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(new TryOpenDoor(4), type.TryOpenDoor);
        Assert.Equal(UnitBehavior.ApproachThroughClosedDoors, type.Behaviors);
        Assert.NotNull(type.Undying);
        Assert.Equal("Troll", type.DisplayName);
        Assert.Equal(new TryOpenDoor(2), UnitType.Zombie().TryOpenDoor);
        var entry = Assert.Single(type.CardEntries(), e => e.Id == "undying");
        Assert.Equal("Capability", entry.Category);
        Assert.Contains("1 HP", entry.Description);
        Assert.Contains("While lying, die normally", entry.Description);
        Assert.Null(entry.MaxUses);
        Assert.Null(type.UsesFor(type.CreateUnit("t", "red"), entry.Id));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void TrollApproachesAndUsesSharedFourOfSixDoorAction(int roll)
    {
        var troll = UnitType.Troll();
        var hero = new UnitType("hero", 0, 0, 0, 0, 5);
        var state = new GameState
        {
            Physical = new(new Board(5, 1, [new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor)]),
                [new("troll", new(0, 0)), new("hero", new(4, 0))]),
            Types = [troll, hero], Units = [troll.CreateUnit("troll", "red"), hero.CreateUnit("hero", "blue")]
        };
        var dice = new Dice(doorRoll: roll);
        var start = GameEngine.StartRound(state, dice, false);
        var moved = GameEngine.Advance(start.State, new DefaultMonsterProvider(), dice, false);
        Assert.Equal(new Cell(1, 0), moved.State.Physical.Figures[0].Position);
        Assert.Equal(new TryOpenDoor(4), Assert.Single(moved.NextInput!.Candidates, c => c.TryOpenDoor is not null).TryOpenDoor);
        var result = GameEngine.Advance(Restore(moved.State), new DefaultMonsterProvider(), dice, false);
        var attempt = Assert.Single(result.Events, e => e.Kind == "DoorOpeningAttemptResolved");
        Assert.Equal(4, attempt.SuccessCount);
        Assert.Equal(roll <= 4, attempt.Succeeded);
        Assert.Equal(roll <= 4 ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor, result.State.Physical.Board.Edges[0].Kind);
    }

    [Fact]
    public void LethalOutcomeAndEverySnapshotExposeLivingLyingUnitInOriginalCell()
    {
        var scenario = Scenario();
        var result = Attack(scenario);
        AssertSaved(result.State);
        Assert.Equal(new[] { "AttackResolved", "PostureChanged" }, result.Events.Select(e => e.Kind));
        Assert.Equal(2, result.Events[0].Damage); // Damage is not capped by HP or the replacement.
        Assert.Equal(Posture.Lying, result.Events[1].Posture);
        Assert.All(result.ResolutionSteps, s => AssertSaved(s.StateAfter));
        Assert.Equal(new[] { 0, 1 }, result.ResolutionSteps.Select(s => s.EventIndex));
        AssertSaved(Restore(result.State));
        Assert.Equal(Posture.Upright, scenario.Physical.Figures[1].Posture);
    }

    [Theory]
    [InlineData(true, Posture.Lying)]
    [InlineData(false, Posture.Upright)]
    public void LyingCapabilityAndUprightUnitWithoutCapabilityDieNormally(bool undying, Posture posture)
    {
        var type = UnitType.Troll() with { Undying = undying ? new() : null };
        var result = Attack(Scenario(type, posture));
        Assert.Equal(0, result.State.Units[1].CurrentHp);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "target");
        Assert.Single(result.Events, e => e.Kind == "UnitDied" && e.UnitId == "target");
        Assert.DoesNotContain(result.Events, e => e.Kind == "PostureChanged");
    }

    [Theory]
    [InlineData(1, 0, 1)] [InlineData(3, 1, 2)]
    public void NonlethalDamageIsOrdinaryAndCapabilityWorksWithoutTrollIdentity(int hp, int hits, int expected)
    {
        var type = new UnitType("unrelated", 2, 1, 4, 4, hp) { Undying = new() };
        var result = Attack(Scenario(type), new Dice(hits));
        Assert.Equal(expected, result.State.Units[1].CurrentHp);
        Assert.Equal(Posture.Upright, result.State.Physical.Figures[1].Posture);
        Assert.Single(result.Events);
        if (hp == 1) AssertSaved(Attack(Scenario(type)).State);
    }

    [Fact]
    public void LyingActivationOnlyStandsAndEndsThenUndyingCanSaveAgain()
    {
        var saved = Attack(Scenario());
        var completed = Choose(Restore(saved.State), "end-turn");
        Assert.True(completed.State.RoundComplete);
        Assert.Equal(Posture.Upright, completed.State.Physical.Figures[1].Posture);
        Assert.Equal(new[] { "PostureChanged" }, completed.Events.Where(e => e.UnitId == "target").Select(e => e.Kind));
        AssertSaved(Attack(Restore(completed.State)).State);
    }

    [Fact]
    public void SavedTargetRemainsLegalForImmediateCleaveWhichKillsItWhileLying()
    {
        var attack = Attack(Scenario(cleave: true));
        AssertSaved(attack.State);
        Assert.Equal(DecisionKind.Cleave, attack.NextInput!.Kind);
        Assert.Contains(attack.NextInput.Candidates, c => c.Key == "cleave:target");
        var result = Choose(Restore(attack.State), "cleave:target");
        Assert.Equal(0, result.State.Units[1].CurrentHp);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "target");
        Assert.Equal(new[] { "CleaveResolved", "UnitDied" }, result.Events.Select(e => e.Kind));
    }

    [Fact]
    public void DirectCleaveDamageAlsoUsesUndyingReplacement()
    {
        var type = new UnitType("other", 2, 1, 4, 4, 3) { Undying = new() };
        var attack = Attack(Scenario(type, cleave: true));
        Assert.Equal(Posture.Upright, attack.State.Physical.Figures[1].Posture);
        var result = Choose(attack.State, "cleave:target");
        AssertSaved(result.State);
        Assert.All(result.ResolutionSteps, s => AssertSaved(s.StateAfter));
        Assert.Equal(new[] { "CleaveResolved", "PostureChanged" }, result.Events.Select(e => e.Kind));
    }

    [Fact]
    public void FireballUsesSameReplacementBeforeTargetAndSummarySnapshots()
    {
        var state = Scenario();
        var wizard = UnitType.Wizard();
        state.Types[0] = wizard;
        state.Units[0] = wizard.CreateUnit("attacker", "blue");
        var action = Choose(GameEngine.StartRound(state, new Dice(), false).State, "stay");
        var result = Choose(action.State, "fireball:1,0");
        AssertSaved(result.State);
        var targetIndex = result.Events.FindIndex(e => e.Kind == "AttackTargetResolved" && e.TargetId == "target");
        Assert.True(targetIndex >= 0);
        Assert.All(result.ResolutionSteps.Where(s => s.EventIndex >= targetIndex), s => AssertSaved(s.StateAfter));
        Assert.DoesNotContain(result.Events, e => e.Kind == "UnitDied");
        Assert.Contains(result.Events.Last().Attack!.Targets, t => t.TargetId == "target" && t.Damage == 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OrdinaryTelekinesisOrHolyWaveMakesTrollVulnerableToNextAttack(bool holyWave)
    {
        var state = Scenario();
        var caster = holyWave ? UnitType.Cleric() : UnitType.Wizard();
        state.Types.Insert(0, caster);
        state.Units.Insert(0, caster.CreateUnit("caster", "blue"));
        state.Physical.Figures.Insert(0, new("caster", new(2, 0)));
        var start = GameEngine.StartRound(state, new Dice(), false);
        var action = Choose(start.State, "stay");
        var laidDown = Choose(action.State, holyWave ? "holy-wave" : "telekinesis:target");
        AssertSaved(laidDown.State);
        Assert.DoesNotContain(laidDown.Events, e => e.Kind is "AttackResolved" or "UnitDied");
        if (!holyWave) laidDown = Choose(laidDown.State, "end-turn");
        Assert.Equal("attacker", laidDown.NextInput!.UnitId);
        var killed = Choose(Choose(Restore(laidDown.State), "stay").State, "attack:target");
        Assert.Equal(0, killed.State.Units.Single(u => u.Id == "target").CurrentHp);
        Assert.DoesNotContain(killed.State.Physical.Figures, f => f.Id == "target");
        Assert.Single(killed.Events, e => e.Kind == "UnitDied");
    }
}
