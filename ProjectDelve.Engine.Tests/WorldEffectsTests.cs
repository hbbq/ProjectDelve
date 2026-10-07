using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldEffectsTests
{
    private sealed class Random : IRandomProvider
    {
        public List<int> ShuffleBounds { get; } = [];
        public bool InvalidShuffle { get; init; }
        public int WorldShuffleIndex(int exclusiveMax)
        {
            ShuffleBounds.Add(exclusiveMax);
            return InvalidShuffle ? exclusiveMax : exclusiveMax - 1;
        }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState State(params WorldEffect[] effects)
    {
        var hero = UnitType.Barbarian() with { Fury = null };
        var grunt = UnitType.Grunt();
        var state = new GameState
        {
            Physical = new(new Board(6, 6, []), []), Types = [hero, grunt], Units = [],
            WorldEffects = new(effects.Length, 1),
            WorldDeck = new() { DrawPile = effects.Select((effect, i) => new WorldCard($"card-{i}", effect)).ToList() }
        };
        state.PlaceUnit(hero.Id, "hero", "red", new(0, 0));
        state.PlaceUnit(grunt.Id, "grunt", "blue", new(3, 0));
        TestGame.Author(state);
        return state;
    }
    private static GameState After(EngineResult result, string kind, string? target = null) =>
        result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == kind &&
            (target is null || result.Events[s.EventIndex].TargetId == target)).StateAfter;
    private static EngineResult Start(GameState state, Random? random = null) => GameEngine.StartRound(state, random ?? new(), false);

    [Fact]
    public void InitialDeckContainsFortyDistinctPhysicalCardsWithExactComposition()
    {
        var deck = WorldCards.CreateDeck();
        Assert.Equal(40, deck.DrawPile.Count);
        Assert.Equal(40, deck.DrawPile.Select(c => c.Id).Distinct().Count());
        Assert.Empty(deck.DiscardPile); Assert.Empty(deck.ActiveContinuous);
        foreach (var content in WorldCards.All)
            Assert.Equal(content.Copies, deck.DrawPile.Count(c => c.Effect == content.Effect));
        Assert.Equal(8, deck.DrawPile.Count(c => c.Effect == WorldEffect.Calm));
        Assert.Equal(19, WorldCards.All.Count);
    }

    [Fact]
    public void CalmCyclesAtomicallyAndSnapshotsPreserveEveryCardLocation()
    {
        var previous = State(WorldEffect.Bloodlust, WorldEffect.Calm);
        var result = Start(previous);
        Assert.Empty(previous.WorldDeck!.ActiveContinuous);
        Assert.Equal(2, previous.WorldDeck.DrawPile.Count);
        var changes = result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "WorldContinuousChanged").ToArray();
        Assert.Equal(2, changes.Length);
        Assert.Equal(WorldEffect.Bloodlust, Assert.Single(changes[0].StateAfter.WorldDeck!.ActiveContinuous).Effect);
        var final = changes[1].StateAfter;
        Assert.Equal(WorldEffect.Calm, Assert.Single(final.WorldDeck!.ActiveContinuous).Effect);
        Assert.Equal(WorldEffect.Bloodlust, Assert.Single(final.WorldDeck.DiscardPile).Effect);
        Assert.Equal(final.Types[0].Atk, final.EffectiveAtkOf("hero"));
        Assert.Equal("card-0", result.Events[changes[1].EventIndex].CycledWorldCard!.Id);
        foreach (var step in result.ResolutionSteps)
        {
            var deck = step.StateAfter.WorldDeck!;
            var cards = deck.DrawPile.Concat(deck.DiscardPile).Concat(deck.ActiveContinuous)
                .Concat(deck.ResolvingCard is { } card ? [card] : Array.Empty<WorldCard>()).ToArray();
            Assert.Equal(2, cards.Length); Assert.Equal(2, cards.Select(c => c.Id).Distinct().Count());
            Assert.InRange(deck.ActiveContinuous.Count, 0, 1);
        }
        final.WorldDeck.ActiveContinuous.Clear();
        Assert.Single(result.State.WorldDeck!.ActiveContinuous);
        Assert.Single(changes[0].StateAfter.WorldDeck!.ActiveContinuous);
    }

    [Fact]
    public void OpposingCardsCancelNumericallyButKeepTheirDistinctCyclingPositions()
    {
        var state = State(WorldEffect.Bloodlust, WorldEffect.Weakness, WorldEffect.Calm);
        state.WorldEffects = new(3, 2);
        var result = Start(state);
        var changes = result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "WorldContinuousChanged").ToArray();
        Assert.Equal(state.Types[0].Atk, changes[1].StateAfter.EffectiveAtkOf("hero"));
        Assert.Equal(new[] { WorldEffect.Weakness, WorldEffect.Calm }, result.State.WorldDeck!.ActiveContinuous.Select(c => c.Effect));
        Assert.Equal(state.Types[0].Atk - 1, result.State.EffectiveAtkOf("hero"));
    }

    [Theory]
    [InlineData(WorldEffect.Bloodlust, Stat.Atk, 1)]
    [InlineData(WorldEffect.Weakness, Stat.Atk, -1)]
    [InlineData(WorldEffect.IronSkin, Stat.Def, 1)]
    [InlineData(WorldEffect.Vulnerability, Stat.Def, -1)]
    [InlineData(WorldEffect.Haste, Stat.Mov, 1)]
    [InlineData(WorldEffect.Sluggishness, Stat.Mov, -1)]
    public void ActiveStatModifiersStackAndClampForUprightAndLyingUnits(WorldEffect effect, Stat stat, int amount)
    {
        var state = State(Enumerable.Repeat(effect, 5).ToArray());
        state.WorldEffects = new(5, 5);
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying };
        var result = Start(state);
        var snapshot = result.ResolutionSteps.Last(s => result.Events[s.EventIndex].Kind == "WorldContinuousChanged").StateAfter;
        foreach (var unit in snapshot.Units)
        {
            var type = snapshot.Types.Single(t => t.Id == unit.TypeId);
            var baseValue = stat switch { Stat.Atk => type.Atk, Stat.Def => type.Def, _ => type.Mov };
            var effective = stat switch { Stat.Atk => snapshot.EffectiveAtkOf(unit.Id), Stat.Def => snapshot.EffectiveDefOf(unit.Id), _ => snapshot.EffectiveMovOf(unit.Id) };
            Assert.Equal(Math.Max(0, baseValue + amount * 5), effective);
        }
    }

    [Fact]
    public void ImmediateResolutionKeepsContinuousCardsAndDrainsFifoConsequencesBeforeNextDrawAndBag()
    {
        var state = State(WorldEffect.Repulsion, WorldEffect.SecondWind);
        state.Types.Add(UnitType.BombImp());
        state.Physical.Figures[1] = new("grunt", new(0, 1));
        state.PlaceUnit(UnitTypeIds.BombImp, "imp-a", "blue", new(1, 0));
        state.PlaceUnit(UnitTypeIds.BombImp, "imp-b", "blue", new(1, 1));
        state.WorldDeck!.ActiveContinuous.Add(new("iron", WorldEffect.IronSkin));
        TestGame.Author(state);
        var result = Start(state);
        var damage = result.Events.Where(e => e.Kind == "WorldDamageResolved").ToArray();
        Assert.Equal(new[] { "hero", "imp-a", "grunt", "imp-b" }, damage.Select(e => e.TargetId));
        var firstExplosion = result.Events.FindIndex(e => e.Kind == "ExplosionDamageResolved");
        Assert.True(firstExplosion > result.Events.FindLastIndex(e => e.Kind == "WorldDamageResolved"));
        Assert.Equal(new[] { "imp-a", "imp-b" }, result.Events.Where(e => e.Kind == "ExplosionDamageResolved").Select(e => e.UnitId));
        var secondDraw = result.Events.FindLastIndex(e => e.Kind == "WorldCardDrawn");
        Assert.True(secondDraw > result.Events.FindLastIndex(e => e.Kind == "ExplosionDamageResolved"));
        Assert.All(result.Events.Where(e => e.Kind == "WorldDamageResolved" || e.Kind == "ExplosionDamageResolved"), e => Assert.Equal("card-0", e.WorldCard!.Id));
        Assert.Single(result.State.WorldDeck!.ActiveContinuous);
        var healed = After(result, "HealResolved", "hero");
        Assert.Equal(state.Types[0].Hp - 2, healed.Units.Single(u => u.Id == "hero").CurrentHp);
        var token = Assert.Single(result.Events, e => e.Kind == "TokenDrawn");
        Assert.Equal(UnitTypeIds.Barbarian, token.Token!.TypeId);
        Assert.DoesNotContain(result.State.Bag, t => t.TypeId is UnitTypeIds.BombImp or UnitTypeIds.Grunt);
        Assert.Empty(result.State.PendingExplosions);
    }

    [Fact]
    public void RepulsionSnapshotsMembershipBeforeFirstDefeatChangesAdjacency()
    {
        var state = State(WorldEffect.Repulsion);
        state.Units.RemoveAt(0); state.Physical.Figures.RemoveAt(0);
        state.Physical.Figures[0] = new("grunt", new(0, 0));
        state.PlaceUnit(UnitTypeIds.Grunt, "other", "blue", new(1, 0));
        var result = Start(state);
        Assert.Equal(new[] { "grunt", "other" }, result.Events.Where(e => e.Kind == "WorldDamageResolved").Select(e => e.TargetId));
        Assert.Empty(result.State.Physical.Figures);
        Assert.Empty(result.State.Bag); Assert.True(result.State.RoundComplete);
        Assert.DoesNotContain(result.Events, e => e.Kind == "TokenDrawn");
    }

    [Fact]
    public void LonelinessUsesOrdinaryAdjacencyIncludingLosAndIgnoresSideAndPosture()
    {
        var state = State(WorldEffect.Loneliness);
        state.Physical.Figures[1] = new("grunt", new(1, 0), Posture.Lying);
        state.Units[1] = state.Units[1] with { SideId = "red" };
        TestGame.Author(state);
        Assert.DoesNotContain(Start(state).Events, e => e.Kind == "WorldDamageResolved");
        state.Physical.Board.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall));
        Assert.Equal(new[] { "hero", "grunt" }, Start(state).Events.Where(e => e.Kind == "WorldDamageResolved").Select(e => e.TargetId));
    }

    [Theory]
    [InlineData(Posture.Upright, true)] [InlineData(Posture.Lying, false)]
    public void WorldDamageUsesOrdinaryUndyingReplacement(Posture posture, bool survives)
    {
        var state = State(WorldEffect.Loneliness);
        state.Types.Add(UnitType.Troll());
        state.Units.Clear(); state.Physical.Figures.Clear();
        state.PlaceUnit(UnitTypeIds.Troll, "zombie", "blue", new(0, 0), posture);
        TestGame.Author(state);
        var result = Start(state);
        var resolved = After(result, "WorldDamageResolved");
        Assert.Equal(survives ? 1 : 0, resolved.Units[0].CurrentHp);
        Assert.Equal(survives ? 1 : 0, resolved.Physical.Figures.Count);
        if (survives) Assert.Equal(Posture.Lying, resolved.Physical.Figures[0].Posture);
        Assert.Equal(!survives, result.Events.Any(e => e.Kind == "UnitDefeated"));
    }

    [Theory]
    [InlineData(WorldEffect.Miracle)] [InlineData(WorldEffect.SecondWind)]
    public void HealingIsCappedAndDoesNotChangePosture(WorldEffect effect)
    {
        var state = State(effect);
        var maxHp = state.Types[0].Hp;
        state.Units[0] = state.Units[0] with { CurrentHp = maxHp - 2 };
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        var result = Start(state);
        var healed = After(result, "HealResolved", "hero");
        Assert.Equal(effect == WorldEffect.Miracle ? maxHp : maxHp - 1, healed.Units[0].CurrentHp);
        Assert.Equal(Posture.Lying, healed.Physical.Figures[0].Posture);
        Assert.Equal(1, healed.Units[1].CurrentHp);
        if (effect == WorldEffect.SecondWind) Assert.Single(result.Events, e => e.Kind == "HealResolved");
    }

    [Fact]
    public void EarthquakeLaysDownAllUnitsAndOrdinaryActivationsThenOnlyStandUp()
    {
        var result = Start(State(WorldEffect.Earthquake));
        var discarded = After(result, "WorldCardDiscarded");
        Assert.All(discarded.Physical.Figures, f => Assert.Equal(Posture.Lying, f.Posture));
        Assert.True(result.State.RoundComplete);
        Assert.All(result.State.Physical.Figures, f => Assert.Equal(Posture.Upright, f.Posture));
        Assert.DoesNotContain(result.Events, e => e.Kind is "ActionUsed" or "MovementCompleted");
    }

    [Fact]
    public void DoorEffectsChangeOnlyDoorsWithoutSpendingAnAction()
    {
        var state = State(WorldEffect.OpenSesame, WorldEffect.Lockdown);
        state.Physical.Board.Edges.AddRange([
            new(new(0, 1), new(1, 1), EdgeKind.ClosedDoor),
            new(new(1, 1), new(2, 1), EdgeKind.OpenDoor),
            new(new(2, 1), new(3, 1), EdgeKind.Wall)]);
        var result = Start(state);
        Assert.Single(result.Events, e => e.Kind == "DoorOpened");
        Assert.Equal(2, result.Events.Count(e => e.Kind == "DoorClosed"));
        Assert.Equal(new[] { EdgeKind.ClosedDoor, EdgeKind.ClosedDoor, EdgeKind.Wall }, result.State.Physical.Board.Edges.Select(e => e.Kind));
        Assert.DoesNotContain(result.Events, e => e.Kind == "ActionUsed");
    }

    [Fact]
    public void RenewalRestoresAllConcreteLimitedAbilitiesUpToMaximum()
    {
        var state = State(WorldEffect.Renewal, WorldEffect.Renewal);
        state.Units[0] = state.Units[0] with { CleaveUses = new(2, 0), BonusActionUses = ImmutableDictionary<string, AbilityUses>.Empty.Add("Rage", new(2, 1)) };
        state.Types.Add(UnitType.Cleric()); state.Types.Add(UnitType.Wizard()); state.Types.Add(UnitType.DisplacerDemon());
        state.PlaceUnit(UnitTypeIds.Cleric, "cleric", "red", new(0, 2));
        state.PlaceUnit(UnitTypeIds.Wizard, "wizard", "red", new(0, 4));
        state.PlaceUnit(UnitTypeIds.DisplacerDemon, "demon", "blue", new(5, 5));
        state.Units[2] = state.Units[2] with { HealUses = new(2, 0), HolyWaveUses = new(2, 0) };
        state.Units[3] = state.Units[3] with { FireballUses = new(2, 0) };
        TestGame.Author(state);
        var result = Start(state);
        Assert.Equal(2, result.State.Units[0].CleaveUses!.RemainingUses);
        Assert.Equal(2, result.State.Units[0].BonusActionUses["Rage"].RemainingUses);
        Assert.Equal(2, result.State.Units[2].HealUses!.RemainingUses);
        Assert.Equal(2, result.State.Units[2].HolyWaveUses!.RemainingUses);
        Assert.Equal(2, result.State.Units[3].FireballUses!.RemainingUses);
        Assert.Empty(result.State.Units[4].BonusActionUses);
    }

    [Fact]
    public void ReshuffleExcludesActiveCardsAndOccursOnlyWhenAnotherDrawIsNeeded()
    {
        var state = State(WorldEffect.Calm, WorldEffect.SecondWind);
        state.WorldEffects = new(3, 1);
        var random = new Random();
        var result = Start(state, random);
        Assert.Equal(new[] { "card-0", "card-1", "card-1" }, result.Events.Where(e => e.Kind == "WorldCardDrawn").Select(e => e.WorldCard!.Id));
        Assert.Single(result.Events, e => e.Kind == "WorldDeckReshuffled");
        Assert.Equal("card-0", Assert.Single(result.State.WorldDeck!.ActiveContinuous).Id);
        Assert.Equal("card-1", Assert.Single(result.State.WorldDeck.DiscardPile).Id);
        Assert.Null(result.State.WorldDeck.ResolvingCard);
        Assert.Equal(new[] { 2 }, random.ShuffleBounds);
    }

    [Fact]
    public void EmptyDrawAndDiscardSkipsDrawWithoutCyclingActiveCards()
    {
        var state = State(WorldEffect.Calm);
        state.WorldEffects = new(2, 1);
        var result = Start(state);
        Assert.Single(result.Events, e => e.Kind == "WorldCardDrawn");
        Assert.Single(result.Events, e => e.Kind == "WorldDrawSkipped");
        Assert.Single(result.State.WorldDeck!.ActiveContinuous);
    }

    [Fact]
    public void FrenzyAllowsRepeatedActionsAndCountersSurviveSerializationAndReset()
    {
        var state = State(WorldEffect.Frenzy);
        state.Types[0] = state.Types[0] with { Atk = 1 };
        state.Types.Add(UnitType.Cleric());
        state.Units.RemoveAt(1); state.Physical.Figures.RemoveAt(1);
        state.PlaceUnit(UnitTypeIds.Cleric, "target", "blue", new(1, 0));
        TestGame.Author(state);
        var started = Start(state);
        started.State.MoveDone = true;
        var request = GameEngine.RefreshChoices(started.State, new Random(), false);
        var first = TestGame.Advance(request.State, new Choice("attack:target"), new Random(), false);
        Assert.Equal(1, first.State.ActionsUsedThisActivation); Assert.Equal(1, first.State.ActionsRemaining);
        Assert.Contains(first.NextInput!.Candidates, c => c.Key == "attack:target");
        var second = TestGame.Advance(first.State, new Choice("attack:target"), new Random(), false);
        Assert.Equal(2, second.State.ActionsUsedThisActivation); Assert.Equal(0, second.State.ActionsRemaining);
        Assert.DoesNotContain(second.NextInput!.Candidates, c => c.Key == "attack:target");
        var persisted = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(second.State))!;
        Assert.Equal(2, persisted.ActionsUsedThisActivation);
        var ended = TestGame.Advance(second.State, new Choice("end-turn"), new Random(), false);
        Assert.Equal(0, ended.State.ActionsUsedThisActivation);
    }

    [Fact]
    public void SurgeAllowsEachBonusTwiceButDoesNotBypassRemainingUses()
    {
        var started = Start(State(WorldEffect.Surge));
        var first = TestGame.Advance(started.State, new Choice("bonus-action:Rage"), new Random(), false);
        Assert.Equal(1, first.State.BonusActionUsesThisActivation("Rage"));
        var second = TestGame.Advance(first.State, new Choice("bonus-action:Rage"), new Random(), false);
        Assert.Equal(2, second.State.BonusActionUsesThisActivation("Rage"));
        Assert.Equal(0, second.State.Units[0].BonusActionUses["Rage"].RemainingUses);
        Assert.DoesNotContain(second.NextInput!.Candidates, c => c.BonusAction?.Name == "Rage");
        var persisted = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(second.State))!;
        Assert.Equal(2, persisted.BonusActionUsesThisActivation("Rage"));
        var low = State(WorldEffect.Surge);
        low.Units[0] = low.Units[0] with { BonusActionUses = low.Units[0].BonusActionUses.SetItem("Rage", new(2, 1)) };
        var lowFirst = TestGame.Advance(Start(low).State, new Choice("bonus-action:Rage"), new Random(), false);
        Assert.DoesNotContain(lowFirst.NextInput!.Candidates, c => c.BonusAction?.Name == "Rage");
    }

    [Theory]
    [InlineData(WorldEffect.Fatigue)] [InlineData(WorldEffect.Hesitation)]
    public void OpportunityReductionsClampAtZeroWithoutPreventingMovement(WorldEffect effect)
    {
        var state = State(effect, effect);
        state.WorldEffects = new(2, 2);
        var result = Start(state);
        if (effect == WorldEffect.Fatigue) Assert.Equal(0, result.State.EffectiveActionsPerActivation);
        else
        {
            Assert.Equal(0, result.State.EffectiveBonusActionUsesPerActivation);
            Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
        }
        Assert.Contains(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Move);
    }

    [Fact]
    public void BloodlustDoesNotGrantNormalAttackAndWeaknessCanPreventOne()
    {
        var state = State(WorldEffect.Bloodlust);
        state.Types[0] = state.Types[0] with { Actions = UnitAction.None, Atk = 0 };
        var result = Start(state);
        Assert.Equal(1, result.State.EffectiveAtkOf("hero"));
        result.State.MoveDone = true;
        Assert.DoesNotContain(GameEngine.RefreshChoices(result.State, new Random(), false).Events, e => e.Kind == "AttackStarted");
        var weak = State(WorldEffect.Weakness, WorldEffect.Weakness);
        weak.WorldEffects = new(2, 2);
        weak.Types[0] = weak.Types[0] with { Atk = 1 };
        weak.Physical.Figures[1] = new("grunt", new(1, 0));
        var weakened = Start(weak); weakened.State.MoveDone = true;
        Assert.Equal(0, weakened.State.EffectiveAtkOf("hero"));
        Assert.DoesNotContain(GameEngine.RefreshChoices(weakened.State, new Random(), false).NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
    }

    [Fact]
    public void InvalidShuffleFailsWithoutMutatingPreviousState()
    {
        var state = State(WorldEffect.Bloodlust, WorldEffect.Calm);
        var before = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => Start(state, new Random { InvalidShuffle = true }));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void MultiCellRecipientsUseAnchorOrderAndAreAffectedOnlyOnce()
    {
        var state = State(WorldEffect.Repulsion);
        state.Types.Add(UnitType.RedDragon());
        state.Physical.Figures[0] = new("hero", new(4, 0));
        state.Physical.Figures[1] = new("grunt", new(1, 2));
        state.PlaceUnit(UnitTypeIds.RedDragon, "dragon", "blue", new(2, 0));
        TestGame.Author(state);
        var result = Start(state);
        Assert.Equal(new[] { "dragon", "hero", "grunt" }, result.Events.Where(e => e.Kind == "WorldDamageResolved").Select(e => e.TargetId));
        Assert.Equal(7, result.State.Units.Single(u => u.Id == "dragon").CurrentHp);
    }

    [Fact]
    public void SurgeChangesTheLimitOfEveryUniqueUnlimitedBonusIndependently()
    {
        var state = State(WorldEffect.Surge);
        state.Types[0] = state.Types[0] with { BonusActions = [
            new("Stride", null, [new(Stat.Mov, 1)]), new("Strength", null, [new(Stat.Atk, 1)])] };
        state.Units[0] = state.Types[0].CreateUnit("hero", "red");
        var result = Start(state);
        foreach (var name in new[] { "Stride", "Strength", "Stride", "Strength" })
            result = TestGame.Advance(result.State, new Choice($"bonus-action:{name}"), new Random(), false);
        Assert.Equal(2, result.State.BonusActionUsesThisActivation("Stride"));
        Assert.Equal(2, result.State.BonusActionUsesThisActivation("Strength"));
        Assert.Empty(result.State.Units[0].BonusActionUses);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.BonusAction);
        var stayed = TestGame.Advance(result.State, new Choice("stay"), new Random(), false);
        Assert.Contains(stayed.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "hero");
        Assert.Empty(stayed.State.BonusActionUseCountsThisActivation);
        Assert.Empty(stayed.State.BonusActionsUsedThisActivation);
    }

    [Fact]
    public void FrenzyWaitsForCleaveFollowUpBeforeExposingTheNextAction()
    {
        var state = State(WorldEffect.Frenzy);
        state.Types[0] = state.Types[0] with { Atk = 2 };
        var target = UnitType.Cleric() with { Hp = 10 };
        state.Types.Add(target);
        state.Units.RemoveAt(1); state.Physical.Figures.RemoveAt(1);
        state.PlaceUnit(target.Id, "target", "blue", new(1, 0));
        TestGame.Author(state);
        var started = Start(state); started.State.MoveDone = true;
        var ready = GameEngine.RefreshChoices(started.State, new Random(), false);
        var first = TestGame.Advance(ready.State, new Choice("attack:target"), new Random(), false);
        Assert.Equal(1, first.State.ActionsRemaining);
        Assert.Equal(DecisionKind.Cleave, first.NextInput!.Kind);
        Assert.DoesNotContain(first.NextInput.Candidates, c => c.Action == UnitAction.NormalAttack);
        var declined = TestGame.Advance(first.State, new Choice(null), new Random(), false);
        Assert.Contains(declined.NextInput!.Candidates, c => c.Key == "attack:target");
        var second = TestGame.Advance(declined.State, new Choice("attack:target"), new Random(), false);
        Assert.Equal(DecisionKind.Cleave, second.NextInput!.Kind);
        Assert.Equal(0, second.State.ActionsRemaining);
    }

    [Fact]
    public void WorldStatsCombineWithOrdinaryAuraFuryAndThisTurnModifiersBeforeClamping()
    {
        var state = State(WorldEffect.Vulnerability, WorldEffect.Weakness);
        state.WorldEffects = new(2, 2);
        state.Types[0] = state.Types[0] with { Def = 0, Atk = 0, Fury = new() };
        state.Types.Add(UnitType.Cleric());
        state.PlaceUnit(UnitTypeIds.Cleric, "cleric", "red", new(0, 1));
        state.Physical.Figures[1] = new("grunt", new(1, 0));
        state.PlaceUnit(UnitTypeIds.Grunt, "grunt-2", "blue", new(1, 1));
        TestGame.Author(state);
        var result = Start(state);
        Assert.Equal(0, result.State.EffectiveDefOf("hero")); // DEF 0 -1 +1 Aura
        Assert.Equal(0, result.State.EffectiveAtkOf("hero")); // ATK 0 -1 +1 Fury
        var boosted = TestGame.Advance(result.State, new Choice("bonus-action:Rage"), new Random(), false);
        Assert.Equal(2, boosted.State.EffectiveAtkOf("hero"));
        Assert.Equal(0, result.State.EffectiveAtkOf("hero"));
    }

    [Fact]
    public void LaterRoundsContinueTheSamePhysicalDeckWithoutInitialReshuffling()
    {
        var state = State(WorldEffect.Earthquake, WorldEffect.Calm);
        state.WorldEffects = new(1, 1);
        var random = new Random();
        var first = Start(state, random);
        Assert.True(first.State.RoundComplete);
        var second = Start(first.State, random);
        Assert.Equal(WorldEffect.Calm, Assert.Single(second.State.WorldDeck!.ActiveContinuous).Effect);
        Assert.Equal(WorldEffect.Earthquake, Assert.Single(second.State.WorldDeck.DiscardPile).Effect);
        Assert.DoesNotContain(second.Events, e => e.Kind is "WorldDeckShuffled" or "WorldDeckReshuffled");
        Assert.Equal(new[] { 2 }, random.ShuffleBounds);
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("immediate-active")] [InlineData("overflow")]
    [InlineData("invalid-effect")] [InlineData("resolving")]
    public void InvalidCardLocationsFailBeforeAnyRoundMutation(string invalid)
    {
        var state = State(WorldEffect.Calm);
        var deck = state.WorldDeck!;
        switch (invalid)
        {
            case "duplicate": deck.DiscardPile.Add(deck.DrawPile[0]); break;
            case "immediate-active": deck.ActiveContinuous.Add(new("extra", WorldEffect.Miracle)); break;
            case "overflow": deck.ActiveContinuous.AddRange([new("one", WorldEffect.Calm), new("two", WorldEffect.Calm)]); break;
            case "invalid-effect": deck.DrawPile[0] = new("invalid", (WorldEffect)999); break;
            case "resolving": deck.ResolvingCard = new("held", WorldEffect.Calm); break;
        }
        Assert.Throws<ArgumentException>(() => Start(state));
        Assert.Equal(0, state.Round);
    }
}
