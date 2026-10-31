using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AbilityAccountingTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static LocalAbilityDefinition Strength(string key) =>
        new(key, "Borrowed Name", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, 2), new(Stat.Rng, 1)])) { Usage = new(2) };
    private static ScenarioDefinition Example() => new()
    {
        Board = new() { Width = 6, Height = 4 },
        UnitTypeIds = ["bearers"],
        UnitTypes = [new("bearers", 2, 1, 1, 1, 1) { CanHaveEquipment = true, CanHaveItems = true,
            LocalAbilities = [Strength("first"), Strength("second")] }],
        Units = [new("bearers", "blue", new(1, 1), UnitId: "a"), new("bearers", "blue", new(1, 2), UnitId: "b"), new("bearers", "red", new(2, 1), UnitId: "enemy")],
        Agency = [new("bearers", "blue", ControllerKind.Human), new("bearers", "red", ControllerKind.Human)],
        ItemTypes = [new("tickets", "Small Tickets") { LocalAbilities = [new("redeem", "A Second Chance", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))] }],
        EquipmentTypes = [new("shade", "Shade Box") { LocalAbilities = [new("share", "Shared Shade", AbilityTiming.Passive, new AdjacentModifiersMechanic([new(Stat.Def, 2), new(Stat.Mov, 1)]))] }],
        Loot = LootSettings.Empty,
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
    };
    private static GameState Ready(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        state.Round = 1; state.ActiveToken = new("bearers", "blue"); state.CurrentUnitId = "a";
        state.Pending = new(DecisionKind.Activation, "bearers", "a", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);

    [Fact]
    public void SameDefinitionInTwoNativeGrantSlotsHasIndependentCountersAndBonusAllowances()
    {
        var state = Ready(Example());
        var result = Use(state, "ability:first");
        Assert.Equal(3, result.State.EffectiveAtkOf("a"));
        Assert.Equal(2, result.State.EffectiveRngOf("a"));
        Assert.Equal(1, result.State.EffectiveAtkOf("b"));
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Key == "ability:first");
        Assert.Contains(result.NextInput.Candidates, c => c.Key == "ability:second");
        var renamed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        renamed.Types[0] = renamed.Types[0] with { LocalAbilities = renamed.Types[0].LocalAbilities.SetItem(1, renamed.Types[0].LocalAbilities[1] with { Name = "Entirely Different Print" }) };
        var second = Use(renamed, "ability:second");
        Assert.Equal(5, second.State.EffectiveAtkOf("a"));
        Assert.Equal(1, second.State.Units.Single(u => u.Id == "a").AbilityUseCounts["unit:first"]);
        Assert.Equal(1, second.State.Units.Single(u => u.Id == "a").AbilityUseCounts["unit:second"]);
        Assert.Empty(second.State.Units.Single(u => u.Id == "b").AbilityUseCounts);
    }

    [Theory]
    [InlineData(WorldEffect.Calm, 1)]
    [InlineData(WorldEffect.Surge, 2)]
    [InlineData(WorldEffect.Hesitation, 0)]
    public void ItemCopiesShareBonusAllowanceWithWorldModifiers(WorldEffect effect, int allowed)
    {
        var scenario = Example();
        scenario.Units[0] = scenario.Units[0] with { Inventory = Enumerable.Range(1, 3).Select(i => new OwnedObject($"ticket-{i}") { ItemTypeId = "tickets" }).ToList() };
        var state = Ready(scenario);
        state.WorldDeck = new() { ActiveContinuous = [new("world", effect)] };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        for (var i = 0; i < allowed; i++)
        {
            var candidate = state.Pending!.Candidates.First(c => c.Ability?.SourceKind == AbilitySourceKind.Item);
            state = Use(state, candidate.Key).State;
        }
        Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.SourceKind == AbilitySourceKind.Item);
        Assert.Equal(3 - allowed, state.Units.Single(u => u.Id == "a").Inventory.Length);
        Assert.Equal(allowed, state.ExtraActionsThisActivation);
        Assert.Equal(allowed, state.LootDeck!.DiscardPile.Count);
    }

    [Fact]
    public void EquipmentAuraUsesSharedAdjacencyAndPostureWithoutAffectingItsBearerOrGroupPeersAtDistance()
    {
        var scenario = Example();
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("shade-1", "shade")] };
        var state = GameEngine.CreateGame(scenario);
        Assert.Equal(1, state.EffectiveDefOf("a"));
        Assert.Equal(3, state.EffectiveDefOf("b"));
        Assert.Equal(3, state.EffectiveMovOf("b"));
        Assert.Equal(1, state.EffectiveDefOf("enemy"));
        var copy = state.Copy();
        copy.Physical.Figures[0] = copy.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal(1, copy.EffectiveDefOf("b"));
        Assert.Equal(3, state.EffectiveDefOf("b"));
        copy.Physical.Figures[0] = copy.Physical.Figures[0] with { Posture = Posture.Upright, Position = new(5, 3) };
        Assert.Equal(1, copy.EffectiveDefOf("b"));
    }

    [Fact]
    public void RenewalReplenishesEachNativeGrantButDoesNotRecreateConsumedItems()
    {
        var state = GameEngine.CreateGame(Example());
        state.Units[0] = state.Units[0] with { AbilityUseCounts = state.Units[0].AbilityUseCounts.Add("unit:first", 2).Add("unit:second", 1) };
        state.LootDeck!.DiscardPile.Add(new("spent-ticket") { ItemTypeId = "tickets" });
        state.WorldEffects = new(1, 1);
        state.WorldDeck = new() { DrawPile = [new("renewal", WorldEffect.Renewal)] };
        var result = GameEngine.StartRound(state, new Dice(), false);
        var unit = result.State.Units.Single(u => u.Id == "a");
        Assert.Equal(1, unit.AbilityUseCounts["unit:first"]);
        Assert.Equal(0, unit.AbilityUseCounts["unit:second"]);
        Assert.Empty(unit.Inventory);
        Assert.Equal("spent-ticket", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
    }

}
