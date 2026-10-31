using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EquipmentAbilityAccountingTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    private static LocalAbilityDefinition Strength(string key, int? uses = 2) =>
        new(key, "Shared Name", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Atk, 1)])) { Usage = new(uses) };
    private static EquipmentDefinition Equipment(string id) => new(id, "Shared Equipment Name")
    { LocalAbilities = [Strength("first"), Strength("second"), Strength("unlimited", null)] };
    private static ScenarioDefinition Scenario(params EquipmentDefinition[] equipment) => new()
    {
        Board = new() { Width = 6, Height = 4 }, UnitTypeIds = ["carrier"],
        UnitTypes = [new("carrier", 2, 3, 1, 0, 1)
        { CanHaveEquipment = true, CanHaveItems = true, LocalAbilities = [Strength("first")] }],
        EquipmentTypes = [.. equipment],
        Units = [new("carrier", "blue", new(1, 1), UnitId: "a"),
            new("carrier", "blue", new(1, 2), UnitId: "b"), new("carrier", "red", new(2, 1), UnitId: "enemy")],
        Agency = [new("carrier", "blue", ControllerKind.Human), new("carrier", "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };
    private static GameState Ready(GameState state, string unitId = "a")
    {
        state.Round = 1; state.RoundComplete = false; state.ActiveToken = new("carrier", "blue"); state.CurrentUnitId = unitId;
        state.CompletedUnitIds.Clear(); state.BonusActionUseCountsThisActivation.Clear(); state.ModifiersThisTurn.Clear();
        state.ActionsUsedThisActivation = 0; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "carrier", unitId, [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static OwnedObject Object(GameState state, string unitId, string objectId) =>
        state.Units.Single(unit => unit.Id == unitId).Inventory.Single(obj => obj.Id == objectId);
    private static AbilityUses? Uses(GameState state, string unitId, string objectId, string key) =>
        state.AbilityUsesOf(unitId, state.AbilitiesOf(unitId).Single(ability => ability.SourceInstanceId == objectId && ability.Key == key));

    [Fact]
    public void PhysicalSourcesAndLocalKeysHaveIndependentChargesAndBonusAllowancesAcrossResume()
    {
        var scenario = Scenario(Equipment("one"), Equipment("two"));
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("one-a", "one"), new("two-a", "two")] };
        scenario.Units[1] = scenario.Units[1] with { Inventory = [new("one-b", "one")] };
        var initial = Ready(GameEngine.CreateGame(scenario));
        var state = Use(initial, "equipment:one-a:first").State;
        state = Use(state, "equipment:one-a:second").State;
        state = Use(state, "equipment:two-a:first").State;
        state = Use(state, "ability:first").State;
        Assert.Empty(Object(initial, "a", "one-a").AbilityUseCounts);
        Assert.Equal(1, Object(state, "a", "one-a").AbilityUseCounts["first"]);
        Assert.Equal(1, Object(state, "a", "one-a").AbilityUseCounts["second"]);
        Assert.Equal(1, Object(state, "a", "two-a").AbilityUseCounts["first"]);
        Assert.Empty(Object(state, "b", "one-b").AbilityUseCounts);
        Assert.Equal(new KeyValuePair<string, int>("unit:first", 1), Assert.Single(state.Units[0].AbilityUseCounts));
        Assert.Equal(4, state.BonusActionUseCountsThisActivation.Count);

        state = Restore(state);
        var index = state.EquipmentTypes.FindIndex(type => type.Id == "one");
        state.EquipmentTypes[index] = state.EquipmentTypes[index] with
        { Name = "Renamed Object", LocalAbilities = [.. state.EquipmentTypes[index].LocalAbilities.Reverse().Select(ability => ability with { Name = "Renamed Ability" })] };
        GameEngine.ValidateScenario(state);
        Assert.Equal(new AbilityUses(2, 1), Uses(state, "a", "one-a", "first"));
        Assert.Equal(new AbilityUses(2, 2), Uses(state, "b", "one-b", "first"));
        Assert.Null(Uses(state, "a", "one-a", "unlimited"));
        Assert.False(state.CanUseAbility("a", state.AbilitiesOf("a").Single(ability => ability.SourceInstanceId == "one-a" && ability.Key == "first")));
    }

    [Fact]
    public void ChargedHolyWavePreservesUsesThroughDefeatGroundSaveAndAnotherOwnerPickup()
    {
        var equipment = new EquipmentDefinition("wave", "Bell")
        { LocalAbilities = [new("ring", "Wave", AbilityTiming.Action, new HolyWaveMechanic()) { Usage = new(2) }] };
        var scenario = Scenario(equipment);
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("physical-bell", "wave")] };
        var state = Ready(GameEngine.CreateGame(scenario));
        state = Use(state, "equipment:physical-bell:ring").State;
        Assert.Equal(new AbilityUses(2, 1), Uses(state, "a", "physical-bell", "ring"));
        Assert.False(state.IsUpright("a"));
        GameEngine.ResolveDefeat(state, "a");
        state = Restore(state);
        GameEngine.ValidateScenario(state);
        var ground = Assert.Single(state.GroundObjects);
        Assert.Equal("physical-bell", ground.Object.Id);
        Assert.Equal(1, ground.Object.AbilityUseCounts["ring"]);
        Assert.Equal(new AbilityUses(2, 1), state.EquipmentAbilityUsesOf(ground.Object, equipment.LocalAbilities[0]));

        var figureIndex = state.Physical.Figures.FindIndex(figure => figure.Id == "b");
        state.Physical.Figures[figureIndex] = state.Physical.Figures[figureIndex] with { Position = ground.Position };
        state = Ready(state, "b");
        state = Use(state, "pickup-object:physical-bell").State;
        Assert.Empty(state.GroundObjects);
        Assert.Equal(new AbilityUses(2, 1), Uses(state, "b", "physical-bell", "ring"));
        state = Use(state, "equipment:physical-bell:ring").State;
        Assert.Equal(new AbilityUses(2, 0), Uses(state, "b", "physical-bell", "ring"));
        Assert.Empty(state.Units.Single(unit => unit.Id == "b").AbilityUseCounts);
        state = Restore(state);
        GameEngine.ValidateScenario(state);
        Assert.Equal(2, Object(state, "b", "physical-bell").AbilityUseCounts["ring"]);
    }

    [Fact]
    public void FireballSpendsOnePhysicalChargeBeforeDiceAndReplaysWithoutSpendingAgain()
    {
        var equipment = new EquipmentDefinition("flame", "Flame")
        { LocalAbilities = [new("cast", "Cast", AbilityTiming.Action, new FireballMechanic()) { Usage = new(1) }] };
        var scenario = Scenario(equipment);
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("flame-one", "flame")] };
        var initial = Ready(GameEngine.CreateGame(scenario));
        var key = initial.Pending!.Candidates.First(candidate => candidate.Ability?.SourceInstanceId == "flame-one").Key;
        var pending = Use(initial, key);
        Assert.Equal(DecisionKind.RollDice, pending.NextInput!.Kind);
        Assert.Equal(new AbilityUses(1, 0), Uses(pending.State, "a", "flame-one", "cast"));
        Assert.Empty(Object(initial, "a", "flame-one").AbilityUseCounts);
        var replay = Use(Restore(pending.State), "roll-dice");
        var continued = Use(pending.State, "roll-dice");
        Assert.Equal(JsonSerializer.Serialize(continued), JsonSerializer.Serialize(replay));
        Assert.Equal(1, Object(replay.State, "a", "flame-one").AbilityUseCounts["cast"]);
        GameEngine.ValidateScenario(replay.State);
    }

    [Fact]
    public void RenewalRestoresNativeAndCarriedEquipmentButNotGroundObjectsOrConsumedItems()
    {
        var scenario = Scenario(Equipment("charged"));
        var spent = ImmutableDictionary<string, int>.Empty.Add("first", 2).Add("second", 1);
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("carried", "charged") { AbilityUseCounts = spent }] };
        var initial = GameEngine.CreateGame(scenario);
        initial.Units[0] = initial.Units[0] with { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("unit:first", 2) };
        initial.GroundObjects.Add(new(new("ground", "charged") { AbilityUseCounts = spent }, new(5, 3)));
        initial.LootDeck!.DiscardPile.Add(new("consumed", LootCardKind.SecondWind));
        initial.WorldEffects = new(1, 1);
        initial.WorldDeck = new() { DrawPile = [new("renewal", WorldEffect.Renewal)] };
        var state = GameEngine.StartRound(initial, new Dice(), false).State;
        Assert.Equal(1, state.Units[0].AbilityUseCounts["unit:first"]);
        Assert.Equal(1, Object(state, "a", "carried").AbilityUseCounts["first"]);
        Assert.Equal(0, Object(state, "a", "carried").AbilityUseCounts["second"]);
        Assert.Equal(2, Assert.Single(state.GroundObjects).Object.AbilityUseCounts["first"]);
        Assert.Equal("consumed", Assert.Single(state.LootDeck!.DiscardPile).Id);
        Assert.Equal(2, Object(initial, "a", "carried").AbilityUseCounts["first"]);
        Assert.Null(Uses(state, "a", "carried", "unlimited"));
        GameEngine.ValidateScenario(Restore(state));
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("ground")]
    [InlineData("draw")]
    [InlineData("discard")]
    public void EquipmentCountersAreValidatedInEveryPhysicalLocation(string location)
    {
        foreach (var invalid in new[] { new KeyValuePair<string, int>("first", -1), new("first", 3), new("missing", 1), new("unlimited", 1) })
        {
            var state = GameEngine.CreateGame(Scenario(Equipment("charged")));
            var obj = new OwnedObject("invalid", "charged") { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add(invalid.Key, invalid.Value) };
            switch (location)
            {
                case "inventory": state.Units[0] = state.Units[0] with { Inventory = [obj] }; break;
                case "ground": state.GroundObjects.Add(new(obj, new(5, 3))); break;
                case "draw": state.LootDeck!.DrawPile.Add(obj); break;
                case "discard": state.LootDeck!.DiscardPile.Add(obj); break;
            }
            Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(Restore(state)));
        }
    }

    [Fact]
    public void SingleUseItemsCannotAcquireEquipmentChargeState()
    {
        var state = GameEngine.CreateGame(Scenario());
        state.Units[0] = state.Units[0] with { Inventory = [new("item", LootCardKind.SecondWind)
        { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("use", 1) }] };
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(state));
    }

    [Fact]
    public void ExhaustedEquipmentChoiceFromStalePendingDataCannotSpendAnotherCharge()
    {
        var equipment = new EquipmentDefinition("charged", "Charged") { LocalAbilities = [Strength("first", 1)] };
        var scenario = Scenario(equipment);
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("physical", "charged")] };
        var initial = Ready(GameEngine.CreateGame(scenario));
        var spent = Use(initial, "equipment:physical:first").State;
        var state = Ready(Restore(spent)); // A fresh activation removes the Bonus allowance as a competing restriction.
        Assert.DoesNotContain(state.Pending!.Candidates, candidate => candidate.Key == "equipment:physical:first");
        state.Pending = initial.Pending;
        var before = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => Use(state, "equipment:physical:first"));
        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.Equal(new AbilityUses(1, 0), Uses(state, "a", "physical", "first"));
        Assert.Single(state.Units[0].Inventory);
    }

    [Theory]
    [InlineData("first-copy")]
    [InlineData("second-copy")]
    public void StaleItemChoiceCannotConsumeAMissingCopyOrBypassSharedBonusAllowance(string selected)
    {
        var scenario = Scenario();
        scenario.Units[0] = scenario.Units[0] with
        { Inventory = [new("first-copy", LootCardKind.SecondWind), new("second-copy", LootCardKind.SecondWind)] };
        var initial = Ready(GameEngine.CreateGame(scenario));
        var state = Use(initial, "item:first-copy:use").State;
        state.Pending = initial.Pending;
        var before = JsonSerializer.Serialize(state);
        Assert.Throws<ArgumentException>(() => Use(state, $"item:{selected}:use"));
        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.Equal("second-copy", Assert.Single(state.Units[0].Inventory).Id);
        Assert.Equal("first-copy", Assert.Single(state.LootDeck!.DiscardPile).Id);
        Assert.Equal(1, state.BonusActionUseCountsThisActivation["item:second-wind:use"]);
    }
}
