using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldCardMechanicsTests
{
    private sealed class Random : IRandomProvider
    {
        public List<int> Bounds { get; } = [];
        public int WorldShuffleIndex(int exclusiveMax) { Bounds.Add(exclusiveMax); return exclusiveMax - 1; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private static GameState State(params WorldCardDefinition[] cards)
    {
        var type = UnitType.Barbarian() with { Fury = null };
        var state = new GameState
        {
            Physical = new(new Board(6, 4, []), []), Types = [type], Units = [],
            WorldCardDefinitions = WorldCardCatalog.Resolve(cards),
            WorldEffects = new(cards.Length, 2),
            WorldDeck = new() { DrawPile = cards.Select((c, i) => new WorldCard($"physical-{i}", c.Id)).ToList() }
        };
        state.PlaceUnit(type.Id, "first", "blue", new(0, 0));
        state.PlaceUnit(type.Id, "second", "red", new(1, 0));
        state.Objectives.Add(new("blue", ScenarioObjective.NoObjective));
        state.Objectives.Add(new("red", ScenarioObjective.NoObjective));
        TestGame.Author(state);
        return state;
    }
    private static EngineResult Start(GameState state, Random? random = null) => GameEngine.StartRound(state, random ?? new(), false);
    private static WorldCardDefinition Immediate(WorldImmediateMechanic mechanic) =>
        new("strange-weather", "An Unfamiliar Forecast", WorldCardLifecycle.Immediate) { Immediate = mechanic };

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void DeclaredHealingChangesGameplayAndDescriptionAndPreservesPosture(int amount)
    {
        var card = Immediate(new HealDamagedUnitsMechanic(amount));
        var state = State(card);
        state.Units[0] = state.Units[0] with { CurrentHp = 1 };
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        var result = Start(state);
        var healed = Assert.Single(result.Events, e => e.Kind == "HealResolved");
        Assert.Equal(Math.Min(amount, state.Types[0].Hp - 1), healed.Healing);
        Assert.Contains($"{amount} HP", card.Description);
        var snapshot = result.ResolutionSteps.Single(s => result.Events[s.EventIndex] == healed).StateAfter;
        Assert.False(snapshot.IsUpright("first"));
        Assert.Equal("An Unfamiliar Forecast", healed.AbilityName);
    }

    [Fact]
    public void CompositeModifiersStackAndCycleAtomicallyUsingDeclaredAmounts()
    {
        var first = new WorldCardDefinition("odd-a", "Calm", WorldCardLifecycle.Continuous)
        { Modifiers = [new(WorldModifierTarget.Mov, 2), new(WorldModifierTarget.Atk, -2), new(WorldModifierTarget.Actions, 2)] };
        var second = new WorldCardDefinition("odd-b", "Miracle", WorldCardLifecycle.Continuous)
        { Modifiers = [new(WorldModifierTarget.Mov, -1), new(WorldModifierTarget.BonusActionUses, -2)] };
        var empty = new WorldCardDefinition("odd-c", "A Quiet Day", WorldCardLifecycle.Continuous);
        var result = Start(State(first, second, empty));
        var steps = result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "WorldContinuousChanged").ToArray();
        Assert.Equal(result.State.Types[0].Mov + 1, steps[1].StateAfter.EffectiveMovOf("first"));
        Assert.Equal(3, steps[1].StateAfter.EffectiveActionsPerActivation);
        Assert.Equal(0, steps[1].StateAfter.EffectiveBonusActionUsesPerActivation);
        Assert.Equal(result.State.Types[0].Mov - 1, result.State.EffectiveMovOf("first"));
        Assert.Equal(1, result.State.EffectiveActionsPerActivation);
        Assert.Equal("physical-0", Assert.Single(result.State.WorldDeck!.DiscardPile).Id);
        Assert.Contains("+2", first.Description);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ParameterizedDamageSnapshotsRecipientsAndDrainsConsequencesBeforeNextDraw(bool adjacent)
    {
        var damage = Immediate(new DamageUnitsByAdjacencyMechanic(2, adjacent));
        var state = State(damage, new("end", "End", WorldCardLifecycle.Continuous));
        if (!adjacent) state.Physical.Figures[1] = new("second", new(4, 0));
        state.Types.Add(UnitType.BombImp());
        state.PlaceUnit(UnitTypeIds.BombImp, "imp", "blue", new(0, adjacent ? 1 : 3));
        TestGame.Author(state);
        var result = Start(state);
        Assert.Equal(new[] { "first", "second", "imp" }, result.Events.Where(e => e.Kind == "WorldDamageResolved").Select(e => e.TargetId));
        Assert.All(result.Events.Where(e => e.Kind == "WorldDamageResolved"), e => Assert.Equal(2, e.Damage));
        Assert.Contains("2 damage", damage.Description);
        Assert.True(result.Events.FindLastIndex(e => e.Kind == "WorldCardDrawn") > result.Events.FindLastIndex(e => e.Kind == "UnitDefeated"));
        Assert.Empty(result.State.PendingExplosions);
    }

    [Fact]
    public void ParameterizedRenewalReplenishesLegacyAndGrantCountersUpToMaximum()
    {
        var state = State(Immediate(new ReplenishLimitedAbilitiesMechanic(2)));
        state.AbilityDefinitions.Add(new("test-wave", "Wave", AbilityTiming.Action, new HolyWaveMechanic()) { Usage = new(3) });
        state.Types[0] = state.Types[0] with { AbilityGrants = [new("grant-test", "test-wave")] };
        var accounting = state.AbilitiesOf("first").Single(a => a.GrantId == "grant-test").AccountingId;
        state.Units[0] = state.Units[0] with { CleaveUses = new(2, 0), AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add(accounting, 3) };
        var result = Start(state);
        Assert.Equal(2, result.State.Units[0].CleaveUses!.RemainingUses);
        Assert.Equal(1, result.State.Units[0].AbilityUseCounts[accounting]);
        Assert.All(result.Events.Where(e => e.Kind == "AbilityUsesReplenished"), e => Assert.Equal(2, e.ReplenishedUses));
    }

    [Fact]
    public void LocalDeckSaveReloadAndReplayKeepPhysicalCardsAndRandomConsumption()
    {
        var state = State(Immediate(new LayDownAllUnitsMechanic()),
            new("next-day", "Tomorrow", WorldCardLifecycle.Continuous) { Modifiers = [new(WorldModifierTarget.Def, 2)] });
        state.WorldEffects = new(1, 1);
        var initialRandom = new Random();
        var first = Start(state, initialRandom);
        Assert.True(first.State.RoundComplete);
        var saved = JsonSerializer.Serialize(first.State);
        var restored = JsonSerializer.Deserialize<GameState>(saved)!;
        var leftRandom = new Random(); var rightRandom = new Random();
        var left = Start(first.State, leftRandom); var right = Start(restored, rightRandom);
        Assert.Equal(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right));
        Assert.Empty(leftRandom.Bounds); Assert.Equal(leftRandom.Bounds, rightRandom.Bounds);
        Assert.Equal(new[] { 2 }, initialRandom.Bounds);
        Assert.Equal("physical-1", Assert.Single(right.State.WorldDeck!.ActiveContinuous).Id);
        Assert.Equal("physical-0", Assert.Single(right.State.WorldDeck.DiscardPile).Id);
        Assert.Equal(state.Types[0].Def + 2, right.State.EffectiveDefOf("first"));
    }
}
