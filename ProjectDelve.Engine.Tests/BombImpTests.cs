using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BombImpTests
{
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice : IRandomProvider
    {
        public int Attacks, Defences;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() { Attacks++; return AttackFace.Hit; }
        public DefenceFace RollDefenceDie() { Defences++; return DefenceFace.Miss; }
        public int RollD6() => throw new InvalidOperationException();
    }
    private static GameState World(UnitType? actor = null)
    {
        actor ??= new("actor-type", 0, 6, 2, 0, 5) { Unique = true };
        return new()
        {
            Physical = new(new Board(9, 6, []), [new("actor", new(0, 2))]),
            Types = [actor], Units = [actor.CreateUnit("actor", "blue")],
            Round = 1, ActiveToken = new(actor.Id, "blue"), CurrentUnitId = "actor", MoveDone = true,
            Pending = new(DecisionKind.Activation, actor.Id, "actor", [], false)
        };
    }
    private static void Add(GameState state, string id, Cell cell, UnitType? type = null,
        string side = "red", Posture posture = Posture.Upright)
    {
        type ??= UnitType.BombImp();
        if (!state.Types.Any(t => t.Id == type.Id)) state.Types.Add(type);
        state.Units.Add(type.CreateUnit(id, side));
        state.Physical.Figures.Add(new(id, cell, posture));
    }
    private static EngineResult Act(GameState state, string key = "attack:imp", Dice? dice = null) =>
        TestGame.Advance(state, new Choice(key), dice ?? new(), false);
    private static RulesEvent[] Damage(EngineResult result) =>
        result.Events.Where(e => e.Kind == "ExplosionDamageResolved").ToArray();

    [Fact]
    public void CanonicalContentHasExactStatsCardAndHumanScenarioAgency()
    {
        var type = CanonicalUnitTypes.Find(UnitTypeIds.BombImp)!;
        Assert.Equal((3, 1, 2, 2, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.False(type.Unique);
        Assert.Equal(1, type.Explosion!.Damage);
        Assert.NotNull(UnitType.BombImp("custom").Explosion);
        var entry = Assert.Single(type.CardEntries(), e => e.Id == "explosion");
        Assert.Equal("Explosion", entry.Name);
        Assert.Equal("When this Unit is defeated, deal 1 damage to every adjacent Unit.", entry.Description);
        Assert.Null(entry.MaxUses);
        var definition = Scenario.Define(Scenario.Map(4, 4),
            [Scenario.Group(type.Id, "red", ControllerKind.Human, Scenario.At(1, 1))]);
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(ControllerKind.Human, result.State.ControllerFor(result.NextInput!));
        Assert.Contains(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
    }

    [Fact]
    public void AuthoredExplosionDamageSurvivesSerializationAndControlsCardAndResolution()
    {
        var type = UnitType.Define("custom-imp", "Custom Imp", UnitAuthoring.Stats(3, 1, 2, 2, 1),
            UnitAuthoring.Ability("Blast", UnitAuthoring.Unlimited(), UnitAuthoring.Explosion(damage: 3)));
        type = JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type))!;
        Assert.Equal(3, type.Explosion!.Damage);
        var entry = Assert.Single(type.CardEntries(), e => e.Id == "explosion");
        Assert.Equal("Blast", entry.Name);
        Assert.Equal("When this Unit is defeated, deal 3 damage to every adjacent Unit.", entry.Description);
        var state = World();
        Add(state, "imp", new(2, 2), type);
        Add(state, "durable", new(3, 2), new("durable", 0, 0, 0, 9, 5) { Unique = true });
        Add(state, "fragile", new(2, 1), UnitType.Grunt());
        var result = Act(state);
        Assert.Equal(2, result.State.Units.Single(u => u.Id == "durable").CurrentHp);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "fragile");
        Assert.Equal(2, Damage(result).Length);
        Assert.All(Damage(result), e => { Assert.Equal(3, e.Damage); Assert.Equal("Blast", e.AbilityName); });
    }

    [Theory]
    [InlineData(Posture.Upright)]
    [InlineData(Posture.Lying)]
    public void ExplosionIsMandatoryDirectDamageWithFriendlyFireAndRemovedSource(Posture posture)
    {
        var state = World();
        Add(state, "imp", new(2, 2), posture: posture);
        Add(state, "friendly", new(2, 1), UnitType.Grunt());
        Add(state, "enemy", new(3, 3), UnitType.Grunt(), "blue");
        Add(state, "far", new(5, 2), UnitType.Grunt());
        var dice = new Dice();
        var result = Act(state, dice: dice);
        Assert.Equal(new[] { "friendly", "enemy" }, Damage(result).Select(e => e.TargetId));
        Assert.All(Damage(result), e => { Assert.Equal(1, e.Damage); Assert.Null(e.Attack); });
        Assert.Equal(2, dice.Attacks); Assert.Equal(2, dice.Defences);
        Assert.Equal(1, result.State.Units.Single(u => u.Id == "far").CurrentHp);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id is "imp" or "friendly" or "enemy");
        var defeat = Assert.Single(result.Events, e => e.Kind == "UnitDefeated" && e.UnitId == "imp");
        Assert.Equal(posture, defeat.DefeatContext!.Figure.Posture);
        Assert.Equal(new Cell(2, 2), defeat.DefeatContext.Figure.Position);
        Assert.All(result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "ExplosionDamageResolved"),
            s => Assert.DoesNotContain(s.StateAfter.Physical.Figures, f => f.Id == "imp"));
        Assert.Empty(result.State.PendingExplosions);
        Assert.Empty(state.PendingExplosions); // Input state remains detached.
    }

    [Theory]
    [InlineData(Posture.Upright, false)]
    [InlineData(Posture.Lying, true)]
    public void ExplosionUsesUndyingAndOnlyActualDefeatQueuesTrollConsequence(Posture posture, bool defeated)
    {
        var state = World();
        Add(state, "imp", new(2, 2));
        // Attach the same concrete trigger to Troll to prove replacement prevents trigger creation.
        Add(state, "troll", new(3, 2), UnitType.Troll() with { Explosion = new() }, posture: posture);
        Add(state, "beyond", new(4, 2), UnitType.Grunt());
        var result = Act(state);
        Assert.Equal(defeated, result.Events.Any(e => e.Kind == "UnitDefeated" && e.UnitId == "troll"));
        Assert.Equal(defeated, Damage(result).Any(e => e.UnitId == "troll"));
        Assert.Equal(defeated ? 0 : 1, result.State.Units.Single(u => u.Id == "beyond").CurrentHp);
        if (!defeated)
        {
            Assert.Equal(1, result.State.Units.Single(u => u.Id == "troll").CurrentHp);
            Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single(f => f.Id == "troll").Posture);
            Assert.Contains(result.Events, e => e.Kind == "PostureChanged" && e.UnitId == "troll");
        }
        Assert.Empty(result.State.PendingExplosions);
    }

    [Fact]
    public void ChainedExplosionsFinishCurrentRecipientsBeforeNewConsequence()
    {
        var state = World();
        Add(state, "imp", new(2, 2));
        Add(state, "second", new(3, 1)); // First recipient, queues a new Explosion.
        Add(state, "last", new(3, 3), UnitType.Grunt());
        Add(state, "beyond", new(4, 1), UnitType.Grunt());
        var result = Act(state);
        Assert.Equal(new[] { "imp:second", "imp:last", "second:beyond" },
            Damage(result).Select(e => $"{e.UnitId}:{e.TargetId}"));
        Assert.Empty(result.State.PendingExplosions);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id != "actor");
    }

    [Fact]
    public void PendingAttackFinishesAcrossSaveBeforeExplosionsDrainInFifoOrder()
    {
        var actor = new UnitType("breather", 0, 6, 2, 0, 5, Actions: UnitAction.FireBreath) { Unique = true };
        var state = World(actor);
        Add(state, "imp", new(2, 1));
        Add(state, "sibling", new(2, 3));
        Add(state, "child", new(3, 1), side: "blue");
        Add(state, "shared", new(3, 2), new("durable", 0, 0, 0, 8, 5) { Unique = true }, "blue");
        TestGame.Author(state);
        var dice = new Dice();
        var key = GameEngine.GameplayCandidates(state, state.Units[0]).Single(c => c.Action == UnitAction.FireBreath).Key;
        var committed = GameEngine.Advance(state, new Choice(key), dice, false);
        var rolled = GameEngine.Advance(committed.State, new Choice("roll-dice"), dice, false);
        var first = GameEngine.Advance(rolled.State, new Choice("roll-dice"), dice, false);
        Assert.Single(first.State.PendingExplosions);
        Assert.Empty(Damage(first));
        Assert.Equal("sibling", first.NextInput!.Roll!.TargetId);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(first.State))!;
        var result = GameEngine.Advance(restored, new Choice("roll-dice"), dice, false);
        Assert.Equal(new[] { "imp:child", "imp:shared", "sibling:shared", "child:shared" },
            Damage(result).Select(e => $"{e.UnitId}:{e.TargetId}"));
        var summary = result.Events.FindIndex(e => e.Kind == "AttackResolved");
        Assert.True(summary < result.Events.FindIndex(e => e.Kind == "ExplosionDamageResolved"));
        Assert.Equal(2, result.State.Units.Single(u => u.Id == "shared").CurrentHp);
        Assert.Equal(2, dice.Attacks); Assert.Equal(4, dice.Defences);
        Assert.Single(first.State.PendingExplosions); // Continuation copied, not consumed in place.
        Assert.Empty(result.State.PendingExplosions);
    }

    [Fact]
    public void AdjacencyUsesGeometricLosAndAffectsLargeRecipientOnce()
    {
        var state = World();
        Add(state, "imp", new(2, 2));
        Add(state, "blocked", new(2, 1), UnitType.Grunt());
        state.Physical.Board.Edges.Add(new(new(2, 1), new(2, 2), EdgeKind.Wall));
        Add(state, "large", new(3, 2), new("large", 0, 0, 0, 9, 5)
            { Unique = true, Footprint = Footprint.TwoByTwo });
        var result = Act(state);
        Assert.Equal("large", Assert.Single(Damage(result)).TargetId);
        Assert.Equal(4, result.State.Units.Single(u => u.Id == "large").CurrentHp);
        Assert.Equal(1, result.State.Units.Single(u => u.Id == "blocked").CurrentHp);
    }

    [Fact]
    public void ExplosionsResolveBeforeAfterAttackCleaveEligibility()
    {
        var state = World(UnitType.Barbarian() with { Atk = 2, Fury = null });
        state.Physical.Figures[0] = new("actor", new(1, 2));
        Add(state, "imp", new(2, 2));
        Add(state, "cleave-target", new(2, 3), UnitType.Grunt());
        var result = Act(state);
        Assert.False(result.State.CleavePending);
        Assert.NotEqual(DecisionKind.Cleave, result.NextInput!.Kind);
        Assert.Equal(4, result.State.Units.Single(u => u.Id == "actor").CurrentHp);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "cleave-target");
    }
}
