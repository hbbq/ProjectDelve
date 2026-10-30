using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class WorldProjectionTests
{
    [Fact]
    public void DrawAndAtomicReplacementProjectTheirOwnAuthoritativeRowsAndContent()
    {
        var state = GameEngine.CreateGame(Scenario.Define(Scenario.Map(4, 4),
            [Scenario.Group(UnitTypeIds.Barbarian, "red", ControllerKind.Human, Scenario.At(0, 0))],
            worldEffects: new(2, 1)));
        // Deterministic top cards; card identities and all remaining copies stay physical.
        var calm = state.WorldDeck!.DrawPile.Single(c => c.Id == "world-Calm-1");
        state.WorldDeck.DrawPile.Remove(calm); state.WorldDeck.DrawPile.Insert(1, calm);
        var result = GameEngine.StartRound(state, new Dice(), false);
        var projection = BrowserProjection.Create(result);
        var draws = result.Events.Select((e, i) => (e, i)).Where(x => x.e.Kind == "WorldCardDrawn").ToArray();
        var firstDraw = projection.ResolutionSteps.Single(s => s.EventIndex == draws[0].i).WorldEffects!;
        Assert.Empty(firstDraw.ActiveContinuous);
        Assert.Equal("Bloodlust", firstDraw.ResolvingCard!.Name);
        Assert.Equal("All Units have ATK +1.", firstDraw.ResolvingCard.Text);
        var secondDraw = projection.ResolutionSteps.Single(s => s.EventIndex == draws[1].i).WorldEffects!;
        Assert.Equal("Bloodlust", Assert.Single(secondDraw.ActiveContinuous).Name);
        Assert.Equal("Calm", secondDraw.ResolvingCard!.Name);
        Assert.Equal("Calm", Assert.Single(projection.WorldEffects!.ActiveContinuous).Name);
        Assert.Equal(38, projection.WorldEffects.DrawPileCount);
        Assert.Equal(1, projection.WorldEffects.DiscardPileCount);
        Assert.Contains(projection.Events, e => e.Text.Contains("simultaneously cycles out"));
        Assert.Contains(projection.Events, e => e.Text.Contains("Continuous") && e.Text.Contains("No Effect."));
        Assert.Equal(projection.WorldEffects, JsonSerializer.Deserialize<BrowserWorldEffects>(JsonSerializer.Serialize(projection.WorldEffects))!
            with { ActiveContinuous = projection.WorldEffects.ActiveContinuous });
    }

    [Theory]
    [InlineData("WorldDamageResolved", OutcomeRole.Damage)]
    [InlineData("HealResolved", OutcomeRole.Healing)]
    [InlineData("DoorOpened", OutcomeRole.DoorOpened)]
    [InlineData("DoorClosed", OutcomeRole.DoorClosed)]
    public void WorldOutcomesReuseOrdinaryDamageHealingAndDoorPresentation(string kind, OutcomeRole role)
    {
        var state = GameEngine.CreateGame(Scenario.Define(Scenario.Map(3, 3), []));
        var effect = kind switch
        {
            "WorldDamageResolved" => WorldEffect.Repulsion, "HealResolved" => WorldEffect.Miracle,
            "DoorOpened" => WorldEffect.OpenSesame, _ => WorldEffect.Lockdown
        };
        var outcome = BrowserProjection.Outcome(new RulesEvent(kind, TargetId: "recipient", Damage: 1, Healing: 2,
            Door: new(new(0, 0), new(1, 0), kind == "DoorClosed" ? EdgeKind.ClosedDoor : EdgeKind.OpenDoor))
            { WorldCard = new("physical-card", effect) }, state);
        Assert.Equal(role, outcome.Role);
        Assert.Contains(WorldCards.Content(effect).Name, outcome.Text);
        Assert.DoesNotContain("Dice", outcome.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowserTransportRetainsLegacyCanonicalActiveCardDefinitionAndEffectiveStats(bool fog)
    {
        var initial = GameEngine.CreateGame(Scenario.Define(Scenario.Map(4, 4),
            [Scenario.Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, Scenario.At(0, 0))],
            worldEffects: new(1, 1)));
        if (fog) initial.FogOfWarSides = ["blue"];
        FogOfWar.Refresh(initial);
        var result = GameEngine.StartRound(initial, new Dice(), false);
        var active = Assert.Single(result.State.WorldDeck!.ActiveContinuous);
        Assert.Null(active.TypeId); // Legacy physical instances retain their old wire identity.
        using var game = new PlaytestGame(new Dice(), result.State);
        var browser = game.BrowserResponse(new(0, result));
        Assert.Contains(browser.Result.State.WorldCardDefinitions, definition => definition.Id == active.ResolvedTypeId);
        var unitId = Assert.Single(result.State.Units).Id;
        Assert.Equal(result.State.EffectiveAtkOf(unitId), browser.Result.State.EffectiveAtkOf(unitId));
        Assert.Equal("Bloodlust", Assert.Single(browser.Presentation.WorldEffects!.ActiveContinuous).Name);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(browser));
        Assert.Contains("Bloodlust", serialized.RootElement.GetRawText());
    }

    private static GameState LocalState(int amount = 2, bool fog = false)
    {
        var type = new UnitType("weather-watcher", 3, 1, 1, 0, 6)
        { Unique = true, Actions = UnitAction.Heal, Heal = new(3) };
        var state = new GameState
        {
            Physical = new(new Board(20, 3, []), [new("watcher", new(0, 0)), new("unseen-unit", new(19, 0))]),
            Types = [type], Units = [type.CreateUnit("watcher", "blue") with { CurrentHp = 1 },
                type.CreateUnit("unseen-unit", "red") with { CurrentHp = 1 }],
            Controllers = [new(new(type.Id, "blue"), ControllerKind.Human), new(new(type.Id, "red"), ControllerKind.Human)],
            FogOfWarSides = fog ? ["blue"] : [], WorldEffects = new(2, 1),
            WorldDeck = new() { DrawPile = [new("wind-physical", "west-wind"), new("rain-physical", "silver-rain"),
                new("future-physical", "undrawn-omen")] }
        };
        state.WorldCardDefinitions.AddRange([
            new("west-wind", "An Unexpected Breeze", WorldCardLifecycle.Continuous)
            { Modifiers = [new(WorldModifierTarget.Mov, amount), new(WorldModifierTarget.Def, -1)] },
            new("silver-rain", "Silver Rainfall", WorldCardLifecycle.Immediate)
            { Immediate = new HealDamagedUnitsMechanic(amount) },
            new("undrawn-omen", "Secret Undrawn Omen", WorldCardLifecycle.Immediate)
            { Immediate = new ReplenishLimitedAbilitiesMechanic(3) }
        ]);
        FogOfWar.Refresh(state);
        return state;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void LocalParametersDriveActualHealingStatsDescriptionsAndEventPlayback(int amount)
    {
        var result = GameEngine.StartRound(LocalState(amount), new Dice(), false);
        var view = BrowserProjection.Create(result);
        var continuous = Assert.Single(view.WorldEffects!.ActiveContinuous);
        Assert.Equal("west-wind", continuous.TypeId);
        Assert.Equal("wind-physical", continuous.Id);
        Assert.Equal("An Unexpected Breeze", continuous.Name);
        Assert.Contains($"MOV +{amount}", continuous.Text);
        Assert.Equal(3 + amount, result.State.EffectiveMovOf("watcher"));
        Assert.Equal(1 + amount, result.State.Units.Single(u => u.Id == "watcher").CurrentHp);
        var rainDraw = result.Events.FindIndex(e => e.Kind == "WorldCardDrawn" && e.WorldCard!.TypeId == "silver-rain");
        var resolving = view.ResolutionSteps.Single(s => s.EventIndex == rainDraw).WorldEffects!.ResolvingCard!;
        Assert.Equal("Silver Rainfall", resolving.Name);
        Assert.Contains($"{amount} HP", resolving.Text);
        Assert.Contains(view.Events, e => e.Role == OutcomeRole.Healing && e.Text.Contains("Silver Rainfall"));
        Assert.Contains(view.Events, e => e.Text.Contains("Silver Rainfall (Immediate)") && e.Text.Contains($"{amount} HP"));
        Assert.Null(view.WorldEffects.ResolvingCard);
        Assert.Equal(1, view.WorldEffects.DiscardPileCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowserAndGambitKeepUndrawnDefinitionsPrivateAndFogHidesRecipients(bool fog)
    {
        var result = GameEngine.StartRound(LocalState(fog: fog), new Dice(), false);
        using var game = new PlaytestGame(new Dice(), result.State);
        var browser = game.BrowserResponse(new(0, result));
        var json = JsonSerializer.Serialize(browser);
        Assert.DoesNotContain("Secret Undrawn Omen", json);
        Assert.DoesNotContain("future-physical", json);
        Assert.Contains("An Unexpected Breeze", json);
        Assert.Contains("Silver Rainfall", json);
        Assert.Empty(browser.Result.State.WorldDeck!.DrawPile);
        Assert.Equal(1, browser.Presentation.WorldEffects!.DrawPileCount);
        var observation = GambitProjection.Observation(result.State, "blue", new Dictionary<ActivationToken, string>());
        Assert.Equal("An Unexpected Breeze", Assert.Single(observation.WorldEffects!.ActiveContinuous).Name);
        Assert.DoesNotContain("Secret Undrawn Omen", JsonSerializer.Serialize(observation));
        if (fog)
        {
            Assert.DoesNotContain("unseen-unit", json);
            Assert.DoesNotContain("unseen-unit", JsonSerializer.Serialize(observation));
            Assert.Single(browser.Presentation.Events.Where(e => e.Role == OutcomeRole.Healing));
            Assert.Equal(3, result.State.Units.Single(u => u.Id == "unseen-unit").CurrentHp);
        }
    }

    [Fact]
    public void LocalCardDefinitionsSurviveStateReloadAndDeterministicPlaybackSerialization()
    {
        var initial = LocalState();
        var reloaded = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(initial))!;
        var firstDice = new CountingDice();
        var secondDice = new CountingDice();
        var first = GameEngine.StartRound(initial, firstDice, false);
        var second = GameEngine.StartRound(reloaded, secondDice, false);
        Assert.Equal(firstDice.ShuffleSizes, secondDice.ShuffleSizes);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        var playback = JsonSerializer.Deserialize<EngineResult>(JsonSerializer.Serialize(first))!;
        Assert.Equal(JsonSerializer.Serialize(BrowserProjection.Create(first)), JsonSerializer.Serialize(BrowserProjection.Create(playback)));
        Assert.Equal("west-wind", Assert.Single(playback.State.WorldDeck!.ActiveContinuous).TypeId);
        Assert.Equal("silver-rain", Assert.Single(playback.State.WorldDeck.DiscardPile).TypeId);
        Assert.Equal(5, playback.State.EffectiveMovOf("watcher"));
    }

    [Fact]
    public void RenewalJournalAndGambitEventUseAuthoredAmount()
    {
        var state = LocalState();
        state.WorldEffects = new(1, 1);
        state.WorldDeck!.DrawPile = [new("renewal-physical", "undrawn-omen")];
        state.Units[0] = state.Units[0] with { HealUses = new(3, 0) };
        var result = GameEngine.StartRound(state, new Dice(), false);
        var occurrence = result.Events.Single(e => e.Kind == "AbilityUsesReplenished" && e.UnitId == "watcher");
        Assert.Equal(3, occurrence.ReplenishedUses);
        Assert.Equal(3, GambitProjection.Event(occurrence).ReplenishedUses);
        Assert.Equal(3, result.State.Units[0].HealUses!.RemainingUses);
        Assert.Contains("replenish 3 uses", BrowserProjection.Outcome(occurrence, result.State).Text);
    }

    [Fact]
    public void BrowserJournalReplayRetainsLocalDrawAndConsequencePresentation()
    {
        using var game = new PlaytestGame(new Dice(), LocalState(fog: true));
        using var live = game.Subscribe(null);
        Assert.True(live.Reader.TryRead(out var baseline));
        game.StartRound(0);
        var updates = new List<GameUpdate>();
        while (live.Reader.TryRead(out var update)) updates.Add(update);
        Assert.Contains(updates, update => update.Snapshot.Result.Events.Any(e => e.Kind == "WorldCardDrawn"));
        using var replay = game.Subscribe($"{baseline.SessionId}:{baseline.Sequence}");
        // Subscribe synchronously queues retained updates. Assert that contract
        // directly, without a deadline also spent on StartRound and serialization.
        foreach (var original in updates)
        {
            Assert.True(replay.Reader.TryRead(out var repeated));
            Assert.Equal(original.Sequence, repeated.Sequence);
            var json = JsonSerializer.Serialize(game.BrowserUpdate(repeated));
            Assert.Equal(JsonSerializer.Serialize(game.BrowserUpdate(original)), json);
            Assert.DoesNotContain("Secret Undrawn Omen", json);
            Assert.DoesNotContain("unseen-unit", json);
        }
        Assert.Contains(updates.SelectMany(update => game.BrowserUpdate(update).Snapshot.Presentation.Events),
            outcome => outcome.Text.Contains("Silver Rainfall"));
    }

    private sealed class CountingDice : IRandomProvider
    {
        public List<int> ShuffleSizes { get; } = [];
        public int WorldShuffleIndex(int exclusiveMax) { ShuffleSizes.Add(exclusiveMax); return exclusiveMax - 1; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Dice : IRandomProvider
    {
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
