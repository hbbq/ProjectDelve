using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ScenarioObjectivesTests
{
    private sealed class Dice : IRandomProvider
    {
        public int Draws { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) { Draws++; return bag[0]; }
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState Active(bool moveDone = true)
    {
        var type = new UnitType("fighter", 1, 1, 1, 0, 1);
        return new()
        {
            Physical = new(new Board(5, 3, []), [new("actor", new(0, 0)), new("enemy", new(1, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "amber"), type.CreateUnit("enemy", "violet")],
            Controllers = [new(new(type.Id, "amber"), ControllerKind.Human), new(new(type.Id, "violet"), ControllerKind.Automated)],
            Round = 1, ActiveToken = new(type.Id, "amber"), CurrentUnitId = "actor", MoveDone = moveDone,
            Pending = new(DecisionKind.Activation, type.Id, "actor", [], false)
        };
    }
    private static EngineResult Choose(GameState state, string key, Dice? dice = null) =>
        GameEngine.Advance(state, new Choice(key), dice ?? new(), false);
    private static EngineResult Attack(GameState state, Dice? dice = null)
    {
        var result = Choose(state, "attack:enemy", dice);
        while (result.NextInput?.Kind == DecisionKind.RollDice) result = Choose(result.State, "roll-dice", dice);
        return result;
    }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void DefaultObjectiveUsesSideIdentityNotUnitTypeOrAgency()
    {
        var original = Active();
        var result = Attack(original);
        Assert.Equal(new[] { "amber" }, result.State.Completion!.AchievedSideIds);
        Assert.Equal(new[] { "amber", "violet" }, result.State.Objectives.Select(o => o.SideId));
        Assert.All(result.State.Objectives, objective => Assert.Equal(ScenarioObjective.EliminateEnemies, objective.Objective));
        Assert.Null(result.NextInput); Assert.True(result.State.IsCompleted);
        Assert.Null(original.Completion); Assert.Equal(2, original.Units.Count);
        var completion = Assert.Single(result.Events, e => e.Kind == "GameCompleted");
        Assert.Equal(result.State.Completion, completion.Completion);
        Assert.True(result.Events.FindIndex(e => e.Kind == "ActivationCompleted") < result.Events.FindIndex(e => e.Kind == "GameCompleted"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "TokenDrawn");
        var restored = Restore(result.State);
        Assert.Equal(result.State.Completion.AchievedSideIds.ToArray(), restored.Completion!.AchievedSideIds.ToArray());
        Assert.Equal(result.State.Objectives, restored.Objectives);
    }

    [Fact]
    public void LastHostileDefeatDoesNotEndPartiallyCompletedActivation()
    {
        var state = Active();
        state.Types[0] = state.Types[0] with { LocalAbilities = [new("focus", "Focus", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, 0)]))] };
        var result = Attack(state);
        Assert.Single(result.State.Units.Where(u => u.CurrentHp > 0));
        Assert.Null(result.State.Completion);
        Assert.Equal("actor", result.State.CurrentUnitId);
        Assert.Contains(result.NextInput!.Candidates, c => c.Key == "ability:focus");
        Assert.All(result.ResolutionSteps, step => Assert.False(step.StateAfter.IsCompleted));
        result = Choose(Restore(result.State), "end-turn");
        Assert.Equal(new[] { "amber" }, result.State.Completion!.AchievedSideIds);
        var before = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ActivationCompleted").StateAfter;
        var after = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "GameCompleted").StateAfter;
        Assert.Null(before.Completion); Assert.True(after.IsCompleted);
        Assert.Null(after.Pending); Assert.Null(after.CurrentUnitId);
        after.Objectives.Clear(); Assert.Equal(2, result.State.Objectives.Count);
    }

    [Fact]
    public void LivingThirdSidePreventsPrematureVictoryWithSharedTypes()
    {
        var state = Active();
        state.PlaceUnit("fighter", "third", "jade", new(4, 2));
        state.Bag.Add(new("fighter", "jade"));
        state.Controllers.Add(new(new("fighter", "jade"), ControllerKind.Human));
        var result = Attack(state);
        Assert.Null(result.State.Completion);
        Assert.Equal(new[] { "amber", "jade", "violet" }, result.State.Objectives.Select(o => o.SideId));
        Assert.NotNull(result.NextInput);
    }

    [Fact]
    public void DefeatExplosionFullyResolvesThenRecordsBothSidesEvenWhenNeitherSurvives()
    {
        var state = Active(); var bomb = UnitType.BombImp(); state.Types.Add(bomb);
        state.Units[1] = bomb.CreateUnit("enemy", "violet");
        var result = Attack(state);
        Assert.All(result.State.Units, unit => Assert.Equal(0, unit.CurrentHp)); Assert.Empty(result.State.Physical.Figures);
        Assert.Equal(new[] { "amber", "violet" }, result.State.Completion!.AchievedSideIds);
        Assert.True(result.Events.FindIndex(e => e.Kind == "ExplosionDamageResolved") < result.Events.FindIndex(e => e.Kind == "GameCompleted"));
        Assert.Empty(result.State.PendingExplosions);
        Assert.All(result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind != "GameCompleted"), s => Assert.Null(s.StateAfter.Completion));
    }

    [Theory]
    [InlineData(Posture.Upright)] [InlineData(Posture.Lying)]
    public void LivingHostileOnBoardCountsRegardlessOfPosture(Posture posture)
    {
        var state = Active(); state.ActionDone = true;
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = posture };
        var result = Choose(state, "end-turn");
        Assert.False(result.State.IsCompleted);
    }

    [Fact]
    public void RemovedHostileDoesNotCountButParticipatingSideIsRetained()
    {
        var state = Active(); state.ActionDone = true;
        state.Physical.Figures.RemoveAt(1);
        var result = Choose(state, "end-turn");
        Assert.Equal(new[] { "amber" }, result.State.Completion!.AchievedSideIds);
        Assert.Equal(2, result.State.Objectives.Count);
    }

    [Fact]
    public void AllRoundStartWorldEffectsResolveBeforeSimultaneousCompletionAndNoTokenIsDrawn()
    {
        var state = Active(); state.Round = 0; state.Pending = null;
        state.Physical.Figures[1] = new("enemy", new(4, 2));
        state.WorldEffects = new(2, 1);
        state.WorldDeck = new() { DrawPile = [new("kill", WorldEffect.Loneliness), new("later", WorldEffect.Earthquake)] };
        var dice = new Dice(); List<RulesEvent> observed = [];
        var result = GameEngine.StartRound(state, dice, false, (e, step) => observed.Add(e));
        Assert.Empty(result.State.Physical.Figures);
        Assert.Equal(new[] { "amber", "violet" }, result.State.Completion!.AchievedSideIds);
        Assert.Equal(2, result.State.WorldDeck!.DiscardPile.Count);
        Assert.Null(result.State.WorldDeck.ResolvingCard); Assert.Equal(0, dice.Draws);
        Assert.Equal(result.Events, observed);
        Assert.Equal("GameCompleted", result.Events[^1].Kind);
        Assert.True(result.Events.FindLastIndex(e => e.Kind == "WorldCardDiscarded") < result.Events.FindIndex(e => e.Kind == "GameCompleted"));
        Assert.All(result.ResolutionSteps.Take(result.ResolutionSteps.Count - 1), s => Assert.Null(s.StateAfter.Completion));
    }

    [Fact]
    public void RoundStartVictoryWaitsForDefeatConsequencesAndLaterCards()
    {
        var state = Active(); var bomb = UnitType.BombImp();
        state.Types[0] = state.Types[0] with { Hp = 4, Unique = true };
        state.Types.Add(bomb); state.Units[0] = state.Types[0].CreateUnit("actor", "amber");
        state.Units[1] = bomb.CreateUnit("enemy", "violet");
        state.Controllers[1] = new(new(bomb.Id, "violet"), ControllerKind.Automated);
        state.Round = 0; state.Pending = null;
        state.WorldEffects = new(2, 1);
        state.WorldDeck = new() { DrawPile = [new("repulsion", WorldEffect.Repulsion), new("miracle", WorldEffect.Miracle)] };
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(new[] { "amber" }, result.State.Completion!.AchievedSideIds);
        Assert.Equal(4, result.State.Units.Single(u => u.Id == "actor").CurrentHp);
        Assert.Empty(result.State.PendingExplosions);
        var explosion = result.Events.FindIndex(e => e.Kind == "ExplosionDamageResolved");
        var heal = result.Events.FindIndex(e => e.Kind == "HealResolved");
        var completion = result.Events.FindIndex(e => e.Kind == "GameCompleted");
        Assert.True(explosion >= 0 && heal > explosion && completion > heal);
        Assert.All(result.ResolutionSteps.Take(result.ResolutionSteps.Count - 1), step => Assert.Null(step.StateAfter.Completion));
        Assert.DoesNotContain(result.Events, e => e.Kind is "TokenDrawn" or "ActivationStarted");
    }

    [Fact]
    public void CompletionBlocksAdvanceAndRoundStartBeforeAnyRandomnessOrProviderCall()
    {
        var finished = Restore(Attack(Active()).State); var dice = new Dice();
        // Even stale externally supplied Pending/RoundComplete flags cannot resume a finished game.
        finished.Pending = new(DecisionKind.Activation, "fighter", "actor", [], false);
        finished.RoundComplete = true;
        var before = JsonSerializer.Serialize(finished);
        Assert.Throws<InvalidOperationException>(() => GameEngine.Advance(finished, new Choice("stay"), dice));
        Assert.Throws<InvalidOperationException>(() => GameEngine.StartRound(finished, dice));
        var refreshed = GameEngine.RefreshChoices(finished, dice, false);
        Assert.Empty(refreshed.Events); Assert.Null(refreshed.NextInput); Assert.True(refreshed.State.IsCompleted);
        Assert.Equal(0, dice.Draws); Assert.Equal(before, JsonSerializer.Serialize(finished));
    }

    private static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 3, Height = 2 }, UnitTypeIds = [UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Grunt, "copper", new(0, 0)), new(UnitTypeIds.Grunt, "silver", new(2, 0))],
        Agency = [new(UnitTypeIds.Grunt, "copper", ControllerKind.Human), new(UnitTypeIds.Grunt, "silver", ControllerKind.Automated)]
    };

    [Fact]
    public void ScenarioObjectivesDefaultAndExplicitValuesRoundTripAsAuthoritativeContent()
    {
        var definition = Definition() with { Objectives = [new("silver")] };
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var state = GameEngine.CreateGame(restored);
        Assert.Equal(new[] { new SideObjective("copper"), new SideObjective("silver") }, state.Objectives);
        Assert.Null(state.Completion);
        state.Objectives.Clear(); Assert.Single(definition.Objectives!);
    }

    [Fact]
    public void InvalidObjectiveSideDuplicatesAndUnsupportedTypesFailScenarioValidation()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Definition() with { Objectives = [new("unknown")] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Definition() with { Objectives = [new("silver"), new("silver")] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Definition() with { Objectives = [new("silver", (ScenarioObjective)99)] }));
    }

    private sealed class InspectObjectives : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries)
        {
            Assert.Equal(new[] { "amber", "violet" }, queries.Objectives.Select(o => o.SideId));
            Assert.Throws<NotSupportedException>(() => ((IList<SideObjective>)queries.Objectives).Clear());
            return "end-turn";
        }
    }
    [Fact]
    public void OrdinaryProvidersReceiveDetachedReadOnlyObjectiveContent()
    {
        var state = Active(); state.ActionDone = true;
        var result = GameEngine.Advance(state, new InspectObjectives(), new Dice(), false);
        Assert.Equal(2, result.State.Objectives.Count);
    }
}
