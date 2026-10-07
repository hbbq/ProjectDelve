using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

// These tests drive the real persistent engine one boundary at a time.
public sealed class ContinuationTests
{
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public int Calls { get; private set; }
        public string? Choose(DecisionRequest request, IGameplayQueries queries) { Calls++; return key; }
    }
    private sealed class Dice : IRandomProvider
    {
        public int Attacks, Defences, Checks;
        public int D6 { get; set; } = 2;
        public ActivationToken? Draw { get; set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => Draw is { } token && bag.Contains(token) ? token : bag[0];
        public AttackFace RollAttackDie() { Attacks++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() { Defences++; return DefenceFace.Miss; }
        public int RollD6() { Checks++; return D6; }
    }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Submit(GameState state, string? key, Dice dice, bool automatic = true) =>
        GameEngine.Advance(state, new Choice(key), dice, automatic);
    private static void Assign(GameState state, Unit unit, ControllerKind controller) =>
        state.Controllers.Add(new(new(unit.TypeId, unit.SideId), controller));
    private static GameState AttackState(int defence = 1, ControllerKind attacker = ControllerKind.Human,
        ControllerKind defender = ControllerKind.Automated)
    {
        var type = UnitType.Barbarian() with { Atk = 2, Fury = null, BonusActions = [] };
        var target = new UnitType("ordinary", 1, 1, 1, defence, 1);
        var state = new GameState
        {
            Physical = new(new(4, 3, []), [new("source", new(0, 1)), new("target", new(1, 1))]),
            Types = [type, target], Units = [type.CreateUnit("source", "red"), target.CreateUnit("target", "blue")],
            Round = 1, ActiveToken = new(type.Id, "red"), CurrentUnitId = "source", MoveDone = true
        };
        Assign(state, state.Units[0], attacker); Assign(state, state.Units[1], defender);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void RollRequiresValidExplicitInputAcrossCopySerializationAndRefresh(bool automatic)
    {
        var dice = new Dice();
        var committed = Submit(AttackState(), "attack:target", dice, automatic);
        Assert.Equal(0, dice.Attacks); Assert.Equal(0, dice.Defences);
        Assert.Equal(new[] { "ActionUsed", "AttackStarted" }, committed.Events.Select(e => e.Kind));
        Assert.True(committed.State.ActionDone);
        var pending = committed.NextInput!;
        Assert.Equal(DecisionKind.RollDice, pending.Kind);
        Assert.False(pending.AllowsNone);
        Assert.Equal(new Candidate("roll-dice", Kind: ActivationChoiceKind.RollDice), Assert.Single(pending.Candidates));
        Assert.Null(committed.State.AttackInProgress!.AttackRoll);
        Assert.Equal(ControllerKind.Human, committed.State.ControllerFor(pending));
        Assert.False(GameEngine.TryAutomaticChoice(pending, automatic, out _));
        var saved = JsonSerializer.Serialize(committed.State);
        foreach (var key in new string?[] { null, "invalid", "Hit", "2" })
        {
            var provider = new Choice(key);
            Assert.Throws<ArgumentException>(() => GameEngine.Advance(Restore(committed.State), provider, dice, automatic));
            Assert.Equal(1, provider.Calls);
        }
        Assert.Equal(0, dice.Attacks);
        var refreshed = GameEngine.RefreshChoices(Restore(committed.State), dice, automatic);
        Assert.Equal(JsonSerializer.Serialize(pending.Roll), JsonSerializer.Serialize(refreshed.NextInput!.Roll));
        Assert.Empty(refreshed.Events); Assert.Equal(0, dice.Attacks);
        var rolled = Submit(refreshed.State, "roll-dice", dice, automatic);
        Assert.Equal(2, dice.Attacks); Assert.Equal(0, dice.Defences);
        Assert.Equal(DecisionKind.RollDice, rolled.NextInput!.Kind);
        Assert.Equal("target", rolled.NextInput.UnitId);
        Assert.Equal(ControllerKind.Automated, rolled.State.ControllerFor(rolled.NextInput));
        var result = Assert.Single(rolled.Events).Dice!;
        Assert.Equal(DiceFamily.Attack, result.Pool.Family);
        Assert.Equal("source", result.Pool.OwnerUnitId);
        Assert.Equal("source", result.Pool.SourceUnitId);
        Assert.Equal("attack", result.Pool.SourceActionId);
        Assert.Equal(new[] { "target" }, result.Pool.TargetIds);
        Assert.Equal(new[] { "Hit", "Hit" }, result.Faces);
        Assert.Equal(2, result.Successes);
        Assert.Equal(result, rolled.State.AttackInProgress!.AttackRoll);
        var defended = GameEngine.Advance(Restore(rolled.State), new DefaultAutomatedProvider(), dice, automatic);
        Assert.Equal(1, dice.Defences);
        var defenceRoll = defended.Events.Single(e => e.Kind == "DiceRolled").Dice!;
        Assert.Equal(DiceFamily.Defence, defenceRoll.Pool.Family);
        Assert.Equal("target", defenceRoll.Pool.OwnerUnitId);
        Assert.Equal("source", defenceRoll.Pool.SourceUnitId);
        Assert.Equal("target", defenceRoll.Pool.TargetId);
        Assert.Equal(new[] { "Miss" }, defenceRoll.Faces);
        Assert.Equal(2, defended.Events.Single(e => e.Kind == "AttackResolved").Damage);
        Assert.Equal(saved, JsonSerializer.Serialize(committed.State));
        rolled.State.Units.Clear();
        Assert.Equal(2, rolled.ResolutionSteps[0].StateAfter.Units.Count);
    }

    [Fact]
    public void AutomatedAttackerExposesHumanDefenceThroughSameSubmissionBoundary()
    {
        var dice = new Dice();
        var attack = GameEngine.Advance(AttackState(attacker: ControllerKind.Automated, defender: ControllerKind.Human), new DefaultAutomatedProvider(), dice, false);
        Assert.Equal(ControllerKind.Automated, attack.State.ControllerFor(attack.NextInput!));
        Assert.Equal(0, dice.Attacks);
        var roll = GameEngine.Advance(attack.State, new DefaultAutomatedProvider(), dice);
        Assert.Equal(ControllerKind.Human, roll.State.ControllerFor(roll.NextInput!));
        Assert.Equal(DiceFamily.Defence, roll.NextInput!.Roll!.Family);
        Assert.Equal(0, dice.Defences);
    }

    [Fact]
    public void ZeroDefenceSkipsPoolAndFollowUpsWaitForCompleteAttack()
    {
        var dice = new Dice();
        var state = AttackState(defence: 0);
        var extra = state.Types[1].CreateUnit("extra", "blue");
        state.Units.Add(extra); state.Physical.Figures.Add(new("extra", new(1, 2)));
        var commit = Submit(state, "attack:target", dice);
        Assert.False(commit.State.CleavePending);
        var roll = Submit(commit.State, "roll-dice", dice);
        Assert.Equal(0, dice.Defences);
        Assert.Single(roll.Events, e => e.Kind == "DiceRolled");
        Assert.Equal(DecisionKind.Cleave, roll.NextInput!.Kind);
        Assert.Null(roll.State.AttackInProgress);
        Assert.Equal(2, roll.State.Units[0].CleaveUses!.RemainingUses);
    }

    [Fact]
    public void MultiTargetAttackFinishesReplacementAndDefeatBeforeAfterFollowUp()
    {
        var caster = UnitType.Wizard() with { Atk = 2, Cleave = new() };
        var troll = UnitType.Troll();
        var ordinary = UnitType.Grunt() with { Def = 1 };
        var state = new GameState
        {
            Physical = new(new(5, 4, []), [new("caster", new(0, 0)), new("troll", new(1, 1)),
                new("last", new(2, 1)), new("follow", new(0, 1))]),
            Types = [caster, troll, ordinary],
            Units = [caster.CreateUnit("caster", "blue"), troll.CreateUnit("troll", "blue"),
                ordinary.CreateUnit("last", "red"), ordinary.CreateUnit("follow", "red")],
            Round = 1, ActiveToken = new(caster.Id, "blue"), CurrentUnitId = "caster", MoveDone = true
        };
        foreach (var token in state.Units.Select(u => new ActivationToken(u.TypeId, u.SideId)).Distinct())
            state.Controllers.Add(new(token, ControllerKind.Human));
        var dice = new Dice();
        state = GameEngine.RefreshChoices(state, dice, false).State;
        var commit = Submit(state, "fireball:2,1", dice, false);
        Assert.Equal(new[] { "troll", "last" }, commit.State.AttackInProgress!.Targets.Select(t => t.TargetId));
        var attackRoll = Submit(Restore(commit.State), "roll-dice", dice, false);
        var first = Submit(Restore(attackRoll.State), "roll-dice", dice, false);
        Assert.Equal(Posture.Lying, first.State.Physical.Figures.Single(f => f.Id == "troll").Posture);
        Assert.DoesNotContain(first.Events, e => e.Kind == "UnitDefeated");
        Assert.False(first.State.CleavePending);
        Assert.Equal("last", first.NextInput!.Roll!.TargetId);
        var last = Submit(Restore(first.State), "roll-dice", dice, false);
        Assert.Equal(new[] { "DiceRolled", "AttackTargetResolved", "UnitDefeated", "AttackResolved" },
            last.Events.Select(e => e.Kind));
        Assert.Null(last.State.AttackInProgress);
        Assert.Equal(DecisionKind.Cleave, last.NextInput!.Kind);
        Assert.Equal("follow", Assert.Single(last.NextInput.Candidates).TargetId);
        Assert.DoesNotContain(last.State.Physical.Figures, f => f.Id == "last");
    }

    [Fact]
    public void FixedAttackRecipientsSkipUnitsThatHaveLeftPlayBeforeTheirApplication()
    {
        var dice = new Dice();
        var commit = Submit(AttackState(), "attack:target", dice, false);
        // Retained records do not keep a removed Figure in play.
        commit.State.Physical.Figures.RemoveAll(f => f.Id == "target");
        var result = Submit(Restore(commit.State), "roll-dice", dice, false);
        Assert.Equal(0, dice.Defences);
        Assert.Null(result.State.AttackInProgress);
        Assert.DoesNotContain(result.Events, e => e.Kind is "AttackResolved" or "UnitDefeated");
        Assert.Equal(1, result.State.Units.Single(u => u.Id == "target").CurrentHp);
        Assert.False(result.State.CleavePending);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FireballFinishesAfterCasterDefeatOrLyingWithFixedTargetsDefenceAndSpentUse(bool undying)
    {
        var caster = UnitType.Wizard() with { Atk = 2, Def = 0, Hp = 1,
            Undying = undying ? new() : null, AdjacentFriendlyUnitsDefenceBonus = new(1) };
        var a = new UnitType("a-type", 1, 1, 1, 1, 1);
        var b = a with { Id = "b-type" };
        var state = new GameState
        {
            Physical = new(new(5, 4, []), [new("caster", new(0, 1)), new("a", new(1, 1)), new("b", new(1, 2))]),
            Types = [caster, a, b], Units = [caster.CreateUnit("caster", "violet"), a.CreateUnit("a", "violet"), b.CreateUnit("b", "violet")],
            Round = 1, ActiveToken = new(caster.Id, "violet"), CurrentUnitId = "caster", MoveDone = true
        };
        Assign(state, state.Units[0], ControllerKind.Human);
        Assign(state, state.Units[1], ControllerKind.Automated);
        Assign(state, state.Units[2], ControllerKind.Human);
        var dice = new Dice();
        state = GameEngine.RefreshChoices(state, dice, false).State;
        var commit = Submit(state, "fireball:1,1", dice, false);
        var context = commit.State.AttackInProgress!;
        Assert.Equal(new[] { "caster", "a", "b" }, context.Targets.Select(t => t.TargetId));
        Assert.Equal(new[] { 0, 2, 2 }, context.Targets.Select(t => t.DefenceDice));
        Assert.Equal(new Cell(1, 1), context.SelectedCell);
        Assert.Equal(1, commit.State.Units[0].FireballUses!.RemainingUses);
        Assert.All(commit.ResolutionSteps, s => Assert.Equal(1, s.StateAfter.Units[0].FireballUses!.RemainingUses));
        var attackRoll = Submit(Restore(commit.State), "roll-dice", dice, false);
        Assert.Equal("a", attackRoll.NextInput!.UnitId);
        Assert.Equal(ControllerKind.Automated, attackRoll.State.ControllerFor(attackRoll.NextInput));
        Assert.Equal(undying ? 1 : 0, attackRoll.State.Units[0].CurrentHp);
        Assert.False(attackRoll.State.IsUpright("caster"));
        Assert.NotNull(attackRoll.State.AttackInProgress);
        Assert.False(attackRoll.State.CleavePending);
        // Change the world during the committed continuation: no re-targeting or DEF recalculation.
        attackRoll.State.Physical.Figures[attackRoll.State.Physical.Figures.FindIndex(f => f.Id == "b")] = new("b", new(4, 3));
        attackRoll.State.Units.Add(b.CreateUnit("new", "violet"));
        attackRoll.State.Physical.Figures.Add(new("new", new(1, 2)));
        var defenceA = GameEngine.Advance(Restore(attackRoll.State), new DefaultAutomatedProvider(), dice, false);
        Assert.Equal("b", defenceA.NextInput!.UnitId);
        Assert.Equal(2, defenceA.NextInput.Roll!.Count);
        Assert.Equal(ControllerKind.Human, defenceA.State.ControllerFor(defenceA.NextInput));
        var defenceB = Submit(Restore(defenceA.State), "roll-dice", dice, false);
        Assert.Equal(2, dice.Attacks); Assert.Equal(4, dice.Defences);
        Assert.Null(defenceB.State.AttackInProgress);
        var summary = defenceB.Events.Single(e => e.Kind == "AttackResolved").Attack!;
        Assert.Equal(2, summary.Hits);
        Assert.Equal(new[] { "caster", "a", "b" }, summary.Targets.Select(t => t.TargetId));
        Assert.All(summary.Targets, t => Assert.Equal(2, t.Damage));
        Assert.Equal(1, defenceB.State.Units.Single(u => u.Id == "new").CurrentHp);
        Assert.Equal(1, defenceB.State.Units[0].FireballUses!.RemainingUses);
        Assert.Null(defenceB.State.MoveAfterAttackAllowance);
        Assert.Contains(defenceB.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "caster");
    }

    [Theory]
    [InlineData(2, true)] [InlineData(3, false)]
    public void DoorCheckCommitsActionPausesAndEmitsEngineFaces(int face, bool success)
    {
        var type = UnitType.Zombie();
        var door = new Edge(new(0, 0), new(1, 0), EdgeKind.ClosedDoor);
        var state = new GameState
        {
            Physical = new(new(3, 1, [door]), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "orange")],
            Round = 1, ActiveToken = new(type.Id, "orange"), CurrentUnitId = "actor", MoveDone = true
        };
        Assign(state, state.Units[0], ControllerKind.Human);
        var dice = new Dice { D6 = face };
        state = GameEngine.RefreshChoices(state, dice, false).State;
        var commit = Submit(state, state.Pending!.Candidates.Single(c => c.TryOpenDoor is not null).Key, dice, false);
        Assert.Equal(0, dice.Checks); Assert.True(commit.State.ActionDone);
        Assert.Equal(DiceFamily.D6, commit.NextInput!.Roll!.Family);
        Assert.Equal(1, commit.NextInput.Roll.Count);
        Assert.Equal(door, commit.NextInput.Roll.Door);
        Assert.Equal(2, commit.NextInput.Roll.SuccessCount);
        Assert.Throws<ArgumentException>(() => Submit(commit.State, null, dice));
        var refresh = GameEngine.RefreshChoices(Restore(commit.State), dice);
        Assert.Equal(0, dice.Checks);
        var result = Submit(refresh.State, "roll-dice", dice, false);
        Assert.Equal(1, dice.Checks);
        Assert.Equal(face.ToString(), result.Events[0].Dice!.Faces.Single());
        Assert.Equal(success ? 1 : 0, result.Events[0].Dice!.Successes);
        Assert.Equal("try-open-door", result.Events[0].ActionId);
        Assert.Equal(success, result.Events.Single(e => e.Kind == "DoorOpeningAttemptResolved").Succeeded);
        Assert.Equal(success ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor, result.State.Physical.Board.Edges[0].Kind);
        Assert.Equal(success ? 1 : 0, result.Events.Count(e => e.Kind == "DoorOpened"));
    }

    [Fact]
    public void TokensArePairsOrderedByTypeAuthoringThenOrdinalSideAndActivateOnlyMatchingGroup()
    {
        var type = new UnitType("ordinary", 1, 0, 0, 0, 1);
        var other = type with { Id = "other" };
        var state = new GameState
        {
            Physical = new(new(6, 2, []), [new("z", new(0, 0)), new("a", new(3, 0)), new("b", new(5, 0))]),
            Types = [other, type], Units = [type.CreateUnit("z", "zebra"), type.CreateUnit("a", "amber"), other.CreateUnit("b", "amber")]
        };
        Assign(state, state.Units[0], ControllerKind.Human);
        Assign(state, state.Units[1], ControllerKind.Automated);
        Assign(state, state.Units[2], ControllerKind.Human);
        var dice = new Dice { Draw = new(type.Id, "zebra") };
        var result = GameEngine.StartRound(state, dice, false);
        Assert.Equal(new[] { new ActivationToken("other", "amber"), new("ordinary", "amber"), new("ordinary", "zebra") }, result.ResolutionSteps[0].StateAfter.Bag);
        Assert.Equal(dice.Draw, result.State.ActiveToken);
        Assert.Equal("z", result.NextInput!.UnitId);
        Assert.Equal(dice.Draw, result.NextInput.Token);
        var token = result.Events.Single(e => e.Kind == "TokenDrawn");
        Assert.Equal("zebra", token.SideId); Assert.Equal(dice.Draw, token.Token);
        var restored = Restore(result.State);
        Assert.Equal(result.State.Bag, restored.Bag); Assert.Equal(result.State.ActiveToken, restored.ActiveToken);
        Assert.Equal(result.State.Controllers, restored.Controllers);
        var finished = Submit(restored, "stay", dice, false);
        Assert.Equal("b", finished.NextInput!.UnitId);
        Assert.DoesNotContain(finished.Events, e => e.Kind == "ActivationStarted" && e.UnitId == "a");
        Assert.Equal(new[] { "RoundStarted", "TokenDrawn", "ActivationStarted" }, result.Events.Select(e => e.Kind));
        Assert.Contains(finished.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "z" && e.Token == dice.Draw);
        result.State.Bag.Clear(); result.State.Controllers.Clear();
        Assert.Equal(3, result.ResolutionSteps[0].StateAfter.Bag.Count);
        Assert.Equal(3, result.ResolutionSteps[0].StateAfter.Controllers.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SummonUsesExistingAssignmentOrInheritsSourceWithoutAddingCurrentRoundToken(bool existing)
    {
        var shaman = UnitType.Shaman(); var goblin = UnitType.Goblin();
        var state = new GameState
        {
            Physical = new(new(4, 3, []), [new("source", new(1, 1))]), Types = [shaman, goblin],
            Units = [shaman.CreateUnit("source", "gold")],
            Controllers = [new(new(shaman.Id, "gold"), ControllerKind.Human)]
        };
        if (existing) state.Controllers.Add(new(new(goblin.Id, "gold"), ControllerKind.Automated));
        // A pending token for the same Type on another Side must not activate the new Unit.
        state.Units.Add(goblin.CreateUnit("other", "silver")); state.Physical.Figures.Add(new("other", new(3, 2)));
        Assign(state, state.Units[1], ControllerKind.Automated);
        var dice = new Dice();
        var start = GameEngine.StartRound(state, dice, false);
        var action = Submit(start.State, "stay", dice, false);
        var summon = Submit(action.State, "spawn-goblin:0,0", dice, false);
        Assert.Equal(action.State.Bag, summon.ResolutionSteps.Single(s => summon.Events[s.EventIndex].Kind == "UnitCreated").StateAfter.Bag);
        Assert.DoesNotContain(new ActivationToken(goblin.Id, "gold"), summon.State.Bag);
        Assert.Equal(existing ? ControllerKind.Automated : ControllerKind.Human,
            summon.State.ControllerFor(new ActivationToken(goblin.Id, "gold")));
        var created = summon.Events.Single(e => e.Kind == "UnitCreated");
        Assert.Equal("source", created.SourceUnitId); Assert.Equal("gold", created.SideId);
        Assert.Equal(new Cell(0, 0), created.Cell); Assert.Equal(Posture.Lying, created.Posture);
        var finish = summon;
        while (!finish.State.RoundComplete)
            finish = GameEngine.Advance(finish.State, new DefaultAutomatedProvider(), dice, false);
        Assert.Equal(Posture.Lying, finish.State.Physical.Figures.Single(f => f.Id == created.UnitId).Posture);
        var next = GameEngine.StartRound(finish.State, dice, false);
        Assert.Contains(new ActivationToken(goblin.Id, "gold"), next.ResolutionSteps[0].StateAfter.Bag);
    }

    [Fact]
    public void ParticipatingGroupsRequireAuthoredAgencyWithoutIdentityDefaults()
    {
        var state = AttackState(); state.Round = 0; state.Controllers.Clear();
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Dice()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SummonParticipatesOnlyIfMatchingPairTokenHasNotResolved(bool alreadyResolved)
    {
        var shaman = UnitType.Shaman(); var goblin = UnitType.Goblin();
        var state = new GameState
        {
            Physical = new(new(7, 4, []), [new("source", new(1, 1)), new("old", new(5, 0)), new("other", new(6, 3))]),
            Types = [shaman, goblin], Units = [shaman.CreateUnit("source", "gold"), goblin.CreateUnit("old", "gold"), goblin.CreateUnit("other", "silver")],
            Controllers = [new(new(shaman.Id, "gold"), ControllerKind.Human), new(new(goblin.Id, "gold"), ControllerKind.Human), new(new(goblin.Id, "silver"), ControllerKind.Automated)]
        };
        var dice = new Dice { Draw = alreadyResolved ? new(goblin.Id, "gold") : null };
        var result = GameEngine.StartRound(state, dice, false);
        while (result.NextInput!.Token != new ActivationToken(shaman.Id, "gold"))
            result = GameEngine.Advance(result.State, new DefaultAutomatedProvider(), dice, false);
        result = Submit(result.State, "stay", dice, false);
        var bag = result.State.Bag.ToArray();
        var summon = Submit(result.State, "spawn-goblin:0,0", dice, false);
        var created = summon.Events.Single(e => e.Kind == "UnitCreated");
        Assert.Equal(bag, summon.ResolutionSteps.Single(s => summon.Events[s.EventIndex].Kind == "UnitCreated").StateAfter.Bag);
        var occurrences = new List<RulesEvent>(summon.Events);
        result = summon;
        while (!result.State.RoundComplete)
        {
            result = GameEngine.Advance(Restore(result.State), new DefaultAutomatedProvider(), dice, false);
            occurrences.AddRange(result.Events);
        }
        Assert.Equal(alreadyResolved ? Posture.Lying : Posture.Upright, result.State.Physical.Figures.Single(f => f.Id == created.UnitId).Posture);
        Assert.Equal(alreadyResolved ? 0 : 1, occurrences.Count(e => e.Kind == "ActivationStarted" && e.UnitId == created.UnitId));
        Assert.DoesNotContain(occurrences, e => e.Kind == "MovementCompleted" && e.UnitId == created.UnitId);
    }

    [Fact]
    public void HumanGroupOwnsUnitSelectionWithoutUsingTypeOrSideAsAgency()
    {
        var type = UnitType.Goblin();
        var state = new GameState
        {
            Physical = new(new(5, 3, []), [new("top", new(0, 0)), new("bottom", new(4, 2))]),
            Types = [type], Units = [type.CreateUnit("top", "red"), type.CreateUnit("bottom", "red")],
            Controllers = [new(new(type.Id, "red"), ControllerKind.Human)]
        };
        var dice = new Dice(); var start = GameEngine.StartRound(state, dice, false);
        Assert.Equal(DecisionKind.SelectUnit, start.NextInput!.Kind);
        Assert.Equal(ControllerKind.Human, start.State.ControllerFor(start.NextInput));
        Assert.Equal("top", new DefaultAutomatedProvider().Choose(start.NextInput, new GameplayQueries(start.State)));
        var selected = Submit(start.State, "bottom", dice, false);
        Assert.Equal("bottom", selected.State.CurrentUnitId);
        Assert.Equal("bottom", selected.Events.Single(e => e.Kind == "ActivationStarted").UnitId);
    }

    [Fact]
    public void CompletionSnapshotsShowCompletedActivationAndEveryOccurrenceIsDetached()
    {
        var dice = new Dice(); var start = Submit(AttackState(defence: 0), "attack:target", dice, false);
        var roll = Submit(start.State, "roll-dice", dice, false);
        Assert.Equal(Enumerable.Range(0, roll.Events.Count), roll.ResolutionSteps.Select(s => s.EventIndex));
        var completion = roll.ResolutionSteps.Single(s => roll.Events[s.EventIndex].Kind == "ActivationCompleted").StateAfter;
        Assert.Null(completion.CurrentUnitId); Assert.Contains("source", completion.CompletedUnitIds);
        var diceStep = roll.ResolutionSteps.Single(s => roll.Events[s.EventIndex].Kind == "DiceRolled").StateAfter;
        Assert.Equal(1, diceStep.Units.Single(u => u.Id == "target").CurrentHp);
        var damageStep = roll.ResolutionSteps.Single(s => roll.Events[s.EventIndex].Kind == "AttackResolved").StateAfter;
        Assert.Equal(0, damageStep.Units.Single(u => u.Id == "target").CurrentHp);
        var deathStep = roll.ResolutionSteps.Single(s => roll.Events[s.EventIndex].Kind == "UnitDefeated").StateAfter;
        Assert.DoesNotContain(deathStep.Physical.Figures, f => f.Id == "target");
        var snapshots = roll.ResolutionSteps.Select(s => JsonSerializer.Serialize(s.StateAfter)).ToArray();
        roll.State.Units.Clear(); roll.State.Controllers.Clear(); roll.State.Bag.Clear();
        Assert.Equal(snapshots, roll.ResolutionSteps.Select(s => JsonSerializer.Serialize(s.StateAfter)));
    }
}
