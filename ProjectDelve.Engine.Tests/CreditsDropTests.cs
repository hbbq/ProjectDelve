using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CreditsDropTests
{
    private sealed class Dice(int face, params int[] followingFaces) : IRandomProvider
    {
        public int Rolls { get; private set; }
        public int Draws { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6()
        {
            Rolls++;
            return Rolls == 1 || followingFaces.Length == 0 ? face : followingFaces[Rolls - 2];
        }
        public int LootDrawIndex(int exclusiveMax) { Draws++; return 0; }
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    private static UnitType Victim(int loot = 2, int credits = 3, int amount = 7) =>
        new("victim", 2, 1, 1, 0, 1) { LootDropSuccesses = loot, CreditsDropSuccesses = credits, CreditsDropAmount = amount };
    private static ScenarioDefinition Definition(UnitType? victim = null) => new()
    {
        Board = new() { Width = 8, Height = 5 }, UnitTypeIds = ["actor", "victim"],
        UnitTypes = [new("actor", 3, 1, 2, 0, 1), victim ?? Victim()],
        Units = [new("actor", "blue", new(1, 1), UnitId: "a"), new("victim", "red", new(2, 1), UnitId: "b")],
        Agency = [new("actor", "blue", ControllerKind.Human), new("victim", "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = new([LootCardKind.LuckyBoots, LootCardKind.PocketBreakfast])
    };
    private static GameState Ready(ScenarioDefinition? definition = null)
    {
        var state = GameEngine.CreateGame(definition ?? Definition());
        state.Round = 1; state.CurrentUnitId = "a"; state.ActiveToken = state.Units[0].Token;
        state.ResolvingTokenUnitIds = ["a"]; state.MoveDone = true;
        return GameEngine.RefreshChoices(state, new Dice(1), false).State;
    }
    private static EngineResult Defeat(GameState state, Dice dice, string? targetId = null)
    {
        var ready = GameEngine.RefreshChoices(state, dice, false);
        var key = ready.NextInput!.Candidates.First(c => c.Action == UnitAction.NormalAttack && (targetId is null || c.TargetId == targetId)).Key;
        var result = GameEngine.Advance(ready.State, new Choice(key), dice, false);
        while (result.NextInput?.Kind == DecisionKind.RollDice)
            result = GameEngine.Advance(result.State, new Choice(result.NextInput.Candidates.Single().Key), dice, false);
        return result;
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, false, true)]
    [InlineData(5, false, true)]
    [InlineData(6, false, false)]
    public void OneDiePartitionsLootCreditsAndNothing(int face, bool loot, bool credits)
    {
        var dice = new Dice(face); var input = Ready(); var result = Defeat(input, dice);
        Assert.Equal(loot ? 1 : 0, result.State.LootMarkers.Count);
        Assert.Equal(credits ? 1 : 0, result.State.CreditsPiles.Count);
        Assert.Equal(1, dice.Rolls); Assert.Equal(0, dice.Draws);
        var e = Assert.Single(result.Events.Where(e => e.Kind == "DefeatDropResolved"));
        Assert.Equal(face, e.DieRoll); Assert.Equal(loot || credits, e.Succeeded);
        Assert.Equal(credits ? 7 : (int?)null, e.CreditsAmount);
        Assert.Equal(new Cell(2, 1), e.Cell);
        Assert.Equal(2, result.State.LootDeck!.DrawPile.Count);
        Assert.Empty(input.CreditsPiles); Assert.Empty(input.LootMarkers);
        Assert.DoesNotContain(result.Events, e => e.Kind is "CreditsCollected" or "LootCardDrawn");
        if (credits) Assert.Equal(7, Assert.Single(result.State.CreditsPiles).Amount);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public void ReservedLootFacesRemainNothingAndCreditsRemainAvailable(int face, bool credits)
    {
        var state = Ready(); state.LootMarkers = [new("one", new(7, 4)), new("two", new(7, 4))];
        var dice = new Dice(face); var result = Defeat(state, dice);
        Assert.Equal(2, result.State.LootMarkers.Count); Assert.Equal(credits ? 1 : 0, result.State.CreditsPiles.Count);
        Assert.Equal(1, dice.Rolls);
    }

    [Fact]
    public void GuaranteedCreditsNeedsNoDieMergesPilesAndDoesNotCollect()
    {
        var state = Ready(Definition(Victim(0, 6)) with { Loot = LootSettings.Empty, CreditsPiles = [new(new(2, 1), 4)] });
        var dice = new Dice(6); var result = Defeat(state, dice);
        Assert.Equal(0, dice.Rolls); Assert.Equal(11, Assert.Single(result.State.CreditsPiles).Amount);
        Assert.Null(Assert.Single(result.Events.Where(e => e.Kind == "DefeatDropResolved")).DieRoll);
        Assert.Empty(result.State.SideCredits);
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(6, false, 1)]
    [InlineData(6, true, 0)]
    public void LegacyLootRetainsExactRandomCalls(int loot, bool empty, int calls)
    {
        var definition = Definition(Victim(loot, 0, 0));
        if (empty) definition = definition with { Loot = LootSettings.Empty };
        var dice = new Dice(1); var result = Defeat(Ready(definition), dice);
        Assert.Equal(calls, dice.Rolls);
        Assert.Equal(calls, result.Events.Count(e => e.Kind == "LootDropRolled"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "DefeatDropResolved");
    }

    [Fact]
    public void LegacyDefeatChainKeepsItsRollOrderAndTheNextUnusedFace()
    {
        var definition = Definition(Victim(2, 0, 0) with
        {
            LocalAbilities = [new("explosion", "Explosion", AbilityTiming.WhenDefeated, new Explosion { Damage = 1 })]
        });
        definition.UnitTypes![0] = definition.UnitTypes[0] with { LootDropSuccesses = 2 };
        var dice = new Dice(2, 5, 4);
        var result = Defeat(Ready(definition), dice);

        var drops = result.Events.Where(e => e.Kind == "LootDropRolled").ToArray();
        Assert.Equal(["b", "a"], drops.Select(e => e.UnitId));
        Assert.Equal(new int?[] { 2, 5 }, drops.Select(e => e.DieRoll));
        Assert.Equal(new bool?[] { true, false }, drops.Select(e => e.Succeeded));
        Assert.Equal("drop:b", Assert.Single(result.State.LootMarkers).Id);
        Assert.Equal(0, dice.Draws);
        Assert.Equal(2, dice.Rolls);
        Assert.Equal(4, dice.RollD6());
    }

    [Fact]
    public void UndyingReplacesDefeatAndPreventsEveryDrop()
    {
        var victim = Victim() with { LocalAbilities = [new("undying", "Undying", AbilityTiming.InsteadOfDefeat, new Undying())] };
        var dice = new Dice(3); var result = Defeat(Ready(Definition(victim)), dice);
        Assert.Equal(0, dice.Rolls); Assert.Empty(result.State.CreditsPiles); Assert.Empty(result.State.LootMarkers);
        Assert.DoesNotContain(result.Events, e => e.Kind == "DefeatDropResolved");
    }

    [Fact]
    public void OwnedObjectsDropAlongsideCreditsWithTheirPhysicalIdentities()
    {
        var state = Ready(Definition(Victim(0, 6) with { CanHaveItems = true, CanHaveEquipment = true }));
        state.Units[1] = state.Units[1] with { Inventory = [new("key-instance", LootCardKind.OfficialKey), new("wind-instance", LootCardKind.SecondWind)] };
        var result = Defeat(state, new Dice(1));
        Assert.Equal(new[] { "key-instance", "wind-instance" }, result.State.GroundObjects.Select(g => g.Object.Id));
        Assert.All(result.State.GroundObjects, g => Assert.Equal(new Cell(2, 1), g.Position));
        Assert.Equal(7, Assert.Single(result.State.CreditsPiles).Amount); Assert.Empty(result.State.Units[1].Inventory);
    }

    [Fact]
    public void OverflowFailsWithoutMutatingTheInputOrInventingAReward()
    {
        var state = Ready(Definition(Victim(0, 6)) with { SideCredits = new() { ["blue"] = int.MaxValue } });
        var serialized = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => Defeat(state, new Dice(1)));
        Assert.Equal(serialized, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void ExplosionChainResolvesEachActualDefeatWithoutPickup()
    {
        var definition = Definition(Victim(2, 3) with
            { LocalAbilities = [new("explosion", "Explosion", AbilityTiming.WhenDefeated, new Explosion { Damage = 1 })] });
        definition.UnitTypes![0] = definition.UnitTypes[0] with { CreditsDropSuccesses = 2, CreditsDropAmount = 2 };
        var dice = new Dice(4, 2);
        var result = Defeat(Ready(definition), dice);
        Assert.Equal(new[] { new CreditsPile(new(1, 1), 2), new(new(2, 1), 7) }, result.State.CreditsPiles);
        var drops = result.Events.Where(e => e.Kind == "DefeatDropResolved").ToArray();
        Assert.Equal(["b", "a"], drops.Select(e => e.UnitId));
        Assert.Equal(new int?[] { 4, 2 }, drops.Select(e => e.DieRoll));
        Assert.Equal(2, dice.Rolls);
        Assert.DoesNotContain(result.Events, e => e.Kind == "CreditsCollected");
    }

    [Fact]
    public void SummonedUnitUsesTheSameTypeDropConfigurationAfterRestore()
    {
        var definition = Definition(Victim(0, 6));
        definition.UnitTypes![0] = definition.UnitTypes[0] with { LocalAbilities =
            [new("summon", "Summon", AbilityTiming.Action, new AdjacentUnitSummonMechanic("victim", Posture.Upright)),
             new("reserve", "Reserve", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))] };
        var state = Ready(definition); var dice = new Dice(1);
        var summon = state.Pending!.Candidates.First(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic && c.Destination == new Cell(2, 2));
        var summoned = GameEngine.Advance(state, new Choice(summon.Key), dice, false).State;
        var spawn = Assert.Single(summoned.Units.Where(u => u.SummonerUnitId == "a"));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(summoned))!;
        restored.CurrentUnitId = "b"; restored.ActiveToken = restored.Units.Single(u => u.Id == "b").Token;
        restored.ResolvingTokenUnitIds = ["b"]; restored.ActionsUsedThisActivation = 0; restored.BonusActionUseCountsThisActivation.Clear();
        restored.Pending = null;
        var result = Defeat(restored, dice, spawn.Id);
        Assert.Equal(7, Assert.Single(result.State.CreditsPiles).Amount);
        Assert.Equal(new Cell(2, 2), result.State.CreditsPiles[0].Position); Assert.Equal(0, dice.Rolls);
    }

    [Fact]
    public void DropEventAndPileUseAuthoritativeExplorationVisibility()
    {
        var state = Ready(Definition(Victim(0, 6)) with { FogOfWarSides = ["blue"] });
        var result = Defeat(state, new Dice(1));
        var e = Assert.Single(result.Events.Where(e => e.Kind == "DefeatDropResolved"));
        Assert.NotNull(FogOfWar.ObserveEvent(result.State, "blue", e));
        Assert.Single(FogOfWar.Observe(result.State, "blue").CreditsPiles);
        var hidden = e with { Cell = new(7, 4) };
        Assert.Null(FogOfWar.ObserveEvent(result.State, "blue", hidden));
    }

    [Fact]
    public void AdvanceOnKillCollectsNewCreditsOnlyAfterTheAdvanceArrives()
    {
        var definition = Definition(Victim(0, 6));
        definition.UnitTypes![0] = definition.UnitTypes[0] with
            { LocalAbilities = [new("advance", "Advance", AbilityTiming.AfterAttack, new AdvanceOnKill())] };
        var dice = new Dice(1); var defeated = Defeat(Ready(definition), dice);
        Assert.Single(defeated.State.CreditsPiles); Assert.Empty(defeated.State.SideCredits);
        Assert.DoesNotContain(defeated.Events, e => e.Kind == "CreditsCollected");
        var arrived = GameEngine.Advance(defeated.State, new Choice("2,1"), dice, false);
        Assert.Empty(arrived.State.CreditsPiles); Assert.Equal(7, arrived.State.SideCredits["blue"]);
        Assert.Equal(0, dice.Rolls);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(7, 0, 0)]
    [InlineData(4, 3, 1)]
    [InlineData(0, -1, 1)]
    [InlineData(0, 7, 1)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, -1)]
    public void InvalidDropDefinitionsFailTypedAndScenarioValidation(int loot, int credits, int amount)
    {
        var type = Victim(loot, credits, amount);
        Assert.NotEmpty(ContentDefinitions.Validate(new(type)));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Definition(type)));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Definition(Victim(0, 0, 0)) with
        { LootUnitOverrides = new() { ["victim"] = new(loot, CreditsDropSuccesses: credits, CreditsDropAmount: amount) } }));
    }

    [Fact]
    public void OptionalOverridesAndDefinitionsRoundTripThroughScenarioAndStateBoundaries()
    {
        var definition = Definition() with { LootUnitOverrides = new() { ["victim"] = new(CreditsDropAmount: 9) } };
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var state = GameEngine.CreateGame(decoded);
        var type = Assert.Single(state.Types.Where(t => t.Id == "victim"));
        Assert.Equal(2, type.LootDropSuccesses); Assert.Equal(3, type.CreditsDropSuccesses); Assert.Equal(9, type.CreditsDropAmount);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        var restoredType = restored.Types.Single(t => t.Id == "victim");
        Assert.Equal((2, 3, 9), (restoredType.LootDropSuccesses, restoredType.CreditsDropSuccesses, restoredType.CreditsDropAmount));
        var disabled = GameEngine.CreateGame(definition with { LootUnitOverrides = new() { ["victim"] = new(CreditsDropSuccesses: 0) } });
        Assert.Equal(0, disabled.Types.Single(t => t.Id == "victim").CreditsDropSuccesses);
        var legacyJson = ScenarioDefinitionJson.ToJson(Definition(Victim(0, 0, 0)));
        Assert.DoesNotContain("creditsDrop", legacyJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CSharpAuthoringAndSchemaExposeTheSameTypedValues()
    {
        var type = UnitType.Define("authored", "Authored", UnitAuthoring.Stats(1, 1, 1, 0, 1),
            UnitAuthoring.Loot(2, creditsDropSuccesses: 3, creditsDropAmount: 7));
        Assert.Equal((2, 3, 7), (type.LootDropSuccesses, type.CreditsDropSuccesses, type.CreditsDropAmount));
        Assert.Empty(ContentDefinitions.Validate(new(type)));
        Assert.Contains(UnitAuthoringSchema.Contract.Fields, f => f.Name == "creditsDropSuccesses" && f.Minimum == 0 && f.Maximum == 6);
        Assert.Contains(UnitAuthoringSchema.Contract.Fields, f => f.Name == "creditsDropAmount" && f.Minimum == 0);
        Assert.Throws<ArgumentException>(() => UnitType.Define("bad", "Bad", UnitAuthoring.Stats(1, 1, 1, 0, 1),
            UnitAuthoring.Loot(4, creditsDropSuccesses: 3, creditsDropAmount: 1)));
    }

    [Fact]
    public void LegacyLootOverrideShapeKeepsLootOnlyBehaviorAndSerialization()
    {
        var definition = Definition() with { LootUnitOverrides = new() { ["victim"] = new(6) } };
        var json = ScenarioDefinitionJson.ToJson(definition);
        var result = Defeat(Ready(ScenarioDefinitionJson.FromJson(json)), new Dice(1));
        Assert.Single(result.State.LootMarkers); Assert.Empty(result.State.CreditsPiles);
        Assert.Contains(result.Events, e => e.Kind == "LootDropRolled");
        Assert.DoesNotContain("CreditsDrop", JsonSerializer.Serialize(new LootUnitOverride(6)), StringComparison.Ordinal);
        var ownershipOnly = GameEngine.CreateGame(definition with { LootUnitOverrides = new() { ["victim"] = new(CanHaveItems: true) } });
        Assert.Equal(3, ownershipOnly.Types.Single(t => t.Id == "victim").CreditsDropSuccesses);
        var modern = definition with { LootUnitOverrides = new() { ["victim"] = new(6, CreditsDropAmount: 7) } };
        var error = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(modern));
        Assert.Equal("lootUnitOverrides[victim].creditsDropSuccesses", ScenarioValidationContext.PathOf(error));
    }

    [Fact]
    public void EditingOnlyLootFacesCanKeepBothCreditsFieldsInheritedAcrossTypeChanges()
    {
        var definition = Definition() with
        {
            LootUnitOverrides = new() { ["victim"] = new(DropSuccesses: 1, InheritCreditsDrop: true) }
        };
        var transported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(new LootUnitOverride(1, InheritCreditsDrop: true), transported.LootUnitOverrides!["victim"]);
        var original = GameEngine.CreateGame(transported).Types.Single(t => t.Id == "victim");
        var updated = transported with { UnitTypes = [transported.UnitTypes![0], Victim(2, 2, 11)] };
        var refreshed = GameEngine.CreateGame(updated).Types.Single(t => t.Id == "victim");
        Assert.Equal((1, 3, 7), (original.LootDropSuccesses, original.CreditsDropSuccesses, original.CreditsDropAmount));
        Assert.Equal((1, 2, 11), (refreshed.LootDropSuccesses, refreshed.CreditsDropSuccesses, refreshed.CreditsDropAmount));
    }
}
