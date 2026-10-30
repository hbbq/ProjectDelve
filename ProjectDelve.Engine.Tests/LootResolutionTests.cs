using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LootResolutionTests
{
    private sealed class Random : IRandomProvider
    {
        public int Rolls { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int LootDrawIndex(int exclusiveMax) => 0;
        public int RollD6() { Rolls++; return 1; }
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState State(UnitType? actor = null, UnitType? other = null)
    {
        actor ??= new UnitType("actor", 3, 1, 2, 0, 5) { Unique = true, CanHaveItems = true, CanHaveEquipment = true };
        other ??= new UnitType("other", 2, 1, 1, 0, 1) { LootDropSuccesses = 6 };
        var state = new GameState
        {
            Physical = new(new Board(8, 6, []), [new("a", new(1, 1)), new("b", new(5, 1))]),
            Types = [actor, other], Units = [actor.CreateUnit("a", "blue"), other.CreateUnit("b", "red")],
            Controllers = [new(new(actor.Id, "blue"), ControllerKind.Human), new(new(other.Id, "red"), ControllerKind.Automated)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Round = 1, CurrentUnitId = "a", ActiveToken = new(actor.Id, "blue"),
            Pending = new(DecisionKind.Activation, actor.Id, "a", [], false)
        };
        return state;
    }
    private static void Deck(GameState state, params LootCardKind[] kinds) => state.LootDeck = new()
        { DrawPile = kinds.Select((k, i) => new OwnedObject($"card-{i}", k)).ToList() };
    private static EngineResult Do(GameState state, string key, Random? random = null) =>
        GameEngine.Advance(state, new Choice(key), random ?? new(), false);

    [Fact]
    public void MoveResolvesEachCardAndSnapshotBeforeNextDraw()
    {
        var state = State(); state.Units[0] = state.Units[0] with { CurrentHp = 2 };
        Deck(state, LootCardKind.PocketBreakfast, LootCardKind.LuckyBoots);
        state.LootMarkers = [new("one", new(2, 1)), new("two", new(2, 1))];
        var result = Do(state, "2,1");
        Assert.Equal(new[] { "MovementCompleted", "LootCardDrawn", "HealResolved", "LootCardDrawn" }, result.Events.Take(4).Select(e => e.Kind));
        Assert.Equal(4, result.State.Units[0].CurrentHp);
        Assert.Equal(4, result.State.EffectiveMovOf("a"));
        Assert.Empty(result.State.LootMarkers); Assert.Empty(result.State.LootDeck!.DrawPile);
        Assert.Single(result.State.LootDeck.DiscardPile);
        Assert.Equal(2, result.ResolutionSteps[1].StateAfter.Units[0].CurrentHp);
        Assert.Equal(4, result.ResolutionSteps[2].StateAfter.Units[0].CurrentHp);
        Assert.Empty(result.ResolutionSteps[2].StateAfter.Units[0].Inventory);
        Assert.Equal(2, state.LootMarkers.Count); // Input and snapshots are detached.
    }

    [Fact]
    public void StayCollectsAndIncapableCollectorLeavesKnownObject()
    {
        var state = State(new("actor", 2, 1, 1, 0, 1));
        Deck(state, LootCardKind.OfficialKey); state.LootMarkers = [new("one", new(1, 1))];
        var result = Do(state, "stay");
        Assert.Empty(result.State.Units[0].Inventory);
        Assert.Equal(new Cell(1, 1), Assert.Single(result.State.GroundObjects).Position);
        Assert.DoesNotContain(result.NextInput?.Candidates ?? [], c => c.ObjectId is not null);
    }

    [Fact]
    public void ArrivalHazardPrecedesPickupAndEquipmentIsInactiveWhileLying()
    {
        var state = State(); Deck(state, LootCardKind.LuckyBoots);
        state.CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 1))];
        state.LootMarkers = [new("one", new(2, 1))];
        var result = Do(state, "2,1");
        Assert.True(result.Events.FindIndex(e => e.Kind == "PostureChanged") < result.Events.FindIndex(e => e.Kind == "LootCardDrawn"));
        Assert.Single(result.State.Units[0].Inventory);
        Assert.Equal(3, result.State.EffectiveMovOf("a"));
    }

    [Fact]
    public void LargeFootprintCollectsInBoardOrderAndCanChooseGroundObjects()
    {
        var state = State(new UnitType("actor", 3, 1, 1, 0, 1) { Footprint = Footprint.TwoByTwo, CanHaveEquipment = true });
        Deck(state, LootCardKind.Bedsheet, LootCardKind.LuckyBoots);
        state.LootMarkers = [new("bottom", new(1, 2)), new("top", new(2, 1))];
        state.GroundObjects = [new(new("key", LootCardKind.OfficialKey), new(2, 2))];
        var result = Do(state, "stay");
        Assert.Equal(new[] { "top", "bottom" }, result.Events.Where(e => e.Kind == "LootCardDrawn").Select(e => e.LootMarker!.Id));
        var collected = Do(result.State, "pickup-object:key");
        Assert.Equal(3, collected.State.Units[0].Inventory.Length);
        Assert.Equal(0, collected.State.ActionsUsedThisActivation);
    }

    [Fact]
    public void SimultaneousSwapCollectsActorFirstAfterBothArrivalHazards()
    {
        var actor = new UnitType("actor", 2, 1, 1, 0, 1) { CanHaveEquipment = true,
            BonusActions = [new("Swap", null, []) { Swap = new() }] };
        var state = State(actor, new UnitType("other", 2, 1, 1, 0, 1) { CanHaveEquipment = true });
        state.Physical.Figures[1] = new("b", new(2, 1));
        Deck(state, LootCardKind.Bedsheet, LootCardKind.LuckyBoots);
        state.LootMarkers = [new("left", new(1, 1)), new("right", new(2, 1))];
        state.CellMarkers = [new(CellMarkerTypes.BananaPeel, new(1, 1))];
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        var result = Do(ready.State, ready.NextInput!.Candidates.Single(c => c.BonusAction?.Swap is not null).Key);
        Assert.Equal(new[] { "a", "b" }, result.Events.Where(e => e.Kind == "LootCardDrawn").Select(e => e.UnitId));
        Assert.True(result.Events.FindIndex(e => e.Kind == "PostureChanged") < result.Events.FindIndex(e => e.Kind == "LootCardDrawn"));
        Assert.Equal(LootCardKind.Bedsheet, Assert.Single(result.State.Units[0].Inventory).Kind);
    }

    [Fact]
    public void EquipmentEffectsAreIndividualWithinAUnitGroup()
    {
        var type = new UnitType("shared", 2, 1, 1, 0, 1) { CanHaveEquipment = true };
        var state = State(type); state.Units.Add(type.CreateUnit("c", "blue")); state.Physical.Figures.Add(new("c", new(1, 3)));
        state.Units[0] = state.Units[0] with { Inventory = [new("boots-1", LootCardKind.LuckyBoots),
            new("sheet", LootCardKind.Bedsheet), new("key", LootCardKind.OfficialKey)] };
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.ClosedDoor));
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        Assert.Equal(3, ready.State.EffectiveMovOf("a")); Assert.Equal(2, ready.State.EffectiveMovOf("c"));
        Assert.True(ready.State.HasPhase("a")); Assert.False(ready.State.HasPhase("c"));
        Assert.Contains(ready.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        Assert.Contains(ready.NextInput.Candidates, c => c.Destination == new Cell(2, 1));
    }

    [Fact]
    public void SecondWindConsumesOneObjectAddsActionAndSurvivesResume()
    {
        var state = State(); state.MoveDone = true; state.ActionsUsedThisActivation = 1;
        state.Physical.Figures[1] = new("b", new(2, 1));
        state.Units[0] = state.Units[0] with { Inventory = [new("wind", LootCardKind.SecondWind), new("other-wind", LootCardKind.SecondWind)] };
        var used = Do(state, "use-object:wind");
        Assert.Equal(1, used.State.ActionsRemaining); Assert.Single(used.State.Units[0].Inventory);
        Assert.DoesNotContain(used.NextInput!.Candidates, c => c.ObjectKind == LootCardKind.SecondWind);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(used.State))!;
        Assert.Equal(1, restored.ActionsRemaining);
        var ended = Do(restored, "end-turn");
        Assert.Equal(0, ended.State.ExtraActionsThisActivation);
    }

    [Fact]
    public void GoatBellUsesOrdinaryActionAndOnlyStandsFriendlyAdjacentUnit()
    {
        var state = State(); state.MoveDone = true;
        state.Units.Add(state.Types[1].CreateUnit("friend", "blue"));
        state.Physical.Figures.Add(new("friend", new(2, 1), Posture.Lying));
        state.Controllers.Add(new(new(state.Types[1].Id, "blue"), ControllerKind.Human));
        state.Units[0] = state.Units[0] with { Inventory = [new("bell", LootCardKind.GoatBell), new("wind", LootCardKind.SecondWind)] };
        var result = Do(state, "goat-bell:friend");
        Assert.True(result.State.IsUpright("friend")); Assert.Equal(1, result.State.ActionsUsedThisActivation);
        Assert.Equal(2, result.State.Units[0].Inventory.Length);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.ObjectKind == LootCardKind.GoatBell);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void ActualDefeatDropsOnlyWithUnreservedSupply(bool undying, bool reserved, bool drops)
    {
        var victim = new UnitType("other", 2, 1, 1, 0, 1) { Undying = undying ? new() : null, LootDropSuccesses = 6, CanHaveEquipment = true };
        var state = State(other: victim); state.MoveDone = true;
        state.Physical.Figures[1] = new("b", new(2, 1));
        state.Units[1] = state.Units[1] with { Inventory = [new("victim-key", LootCardKind.OfficialKey)] };
        Deck(state, LootCardKind.LuckyBoots);
        if (reserved) state.LootMarkers = [new("reserved", new(7, 5))];
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        var result = Do(ready.State, ready.NextInput!.Candidates.First(c => c.Action == UnitAction.NormalAttack).Key);
        var random = new Random();
        while (result.NextInput?.Kind == DecisionKind.RollDice) result = Do(result.State, result.NextInput.Candidates.Single().Key, random);
        Assert.Equal(drops ? 1 : 0, result.State.LootMarkers.Count(m => m.Id == "drop:b"));
        Assert.Equal(undying ? 0 : 1, result.State.GroundObjects.Count);
        Assert.Equal(undying ? 1 : 0, result.State.Units[1].Inventory.Length);
        Assert.Equal(drops ? 1 : 0, random.Rolls);
    }

    [Fact]
    public void InvalidEmptyDrawPileFailsInsteadOfDeletingMarker()
    {
        var state = State(); state.LootMarkers = [new("bad", new(1, 1))];
        Assert.Throws<InvalidOperationException>(() => Do(state, "stay"));
        Assert.Single(state.LootMarkers);
    }

    [Fact]
    public void ExplosionDefeatChainReservesFiniteSupplyAndDoesNotCollectDrops()
    {
        var actor = new UnitType("actor", 2, 1, 2, 0, 1) { LootDropSuccesses = 6, CanHaveItems = true };
        var bomb = new UnitType("other", 2, 1, 1, 0, 1) { Explosion = new() { Damage = 1 }, LootDropSuccesses = 6 };
        var state = State(actor, bomb); state.MoveDone = true;
        state.Physical.Figures[1] = new("b", new(2, 1));
        state.Types.Add(new("third", 2, 1, 1, 0, 1) { LootDropSuccesses = 6 });
        state.Units.Add(state.Types[2].CreateUnit("c", "red")); state.Physical.Figures.Add(new("c", new(3, 1)));
        state.Controllers.Add(new(new("third", "red"), ControllerKind.Automated));
        state.Units[0] = state.Units[0] with { Inventory = [new("wind", LootCardKind.SecondWind)] };
        Deck(state, LootCardKind.LuckyBoots, LootCardKind.PocketBreakfast);
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        var result = Do(ready.State, ready.NextInput!.Candidates.First(c => c.Action == UnitAction.NormalAttack).Key);
        while (result.NextInput?.Kind == DecisionKind.RollDice) result = Do(result.State, result.NextInput.Candidates.Single().Key);
        Assert.All(result.State.Units, u => Assert.Equal(0, u.CurrentHp));
        Assert.Equal(new[] { "drop:b", "drop:a" }, result.State.LootMarkers.Select(m => m.Id));
        Assert.Equal(2, result.State.LootDeck!.DrawPile.Count);
        Assert.Equal(new Cell(1, 1), Assert.Single(result.State.GroundObjects).Position);
        Assert.DoesNotContain(result.Events, e => e.Kind == "LootCardDrawn");
    }

    [Fact]
    public void AutomatedCollectorCanUseAcquiredItemAndEquipmentWithoutNewDecisionKinds()
    {
        var state = State(); state.MoveDone = true; state.ActionsUsedThisActivation = 1;
        state.Physical.Figures[1] = new("b", new(2, 1));
        state.Units[0] = state.Units[0] with { Inventory = [new("wind", LootCardKind.SecondWind)] };
        var result = GameEngine.Advance(state, new DefaultAutomatedProvider(), new Random(), false);
        Assert.Contains(result.Events, e => e.Kind == "ItemUsed");
        Assert.Contains(result.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        Assert.Empty(result.State.Units[0].Inventory);
    }
}
