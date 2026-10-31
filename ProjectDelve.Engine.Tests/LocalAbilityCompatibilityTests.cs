using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LocalAbilityCompatibilityTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState Ready(UnitType actor, List<AbilityDefinition>? definitions = null, bool refresh = true)
    {
        var state = GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 3, Height = 2 }, UnitTypeIds = [actor.Id, "friend"],
            UnitTypes = [actor, new("friend", 0, 0, 0, 0, 5) { Unique = true }], AbilityDefinitions = definitions,
            Units = [new(actor.Id, "blue", new(0, 0), UnitId: "actor"),
                new("friend", "blue", new(1, 0), InitialHp: 1, UnitId: "friend")],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new("friend", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        });
        state.Round = 1; state.ActiveToken = new(actor.Id, "blue"); state.CurrentUnitId = "actor"; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, actor.Id, "actor", [], false);
        return refresh ? GameEngine.RefreshChoices(state, new Dice(), false).State : state;
    }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string choice) => GameEngine.Advance(state, new Choice(choice), new Dice(), false);

    [Fact]
    public void DetachedUnitConstructionKeepsUnboundScenarioLocalReferencesLazy()
    {
        var type = new UnitType("detached-source", 1, 1, 0, 0, 1, Actions: UnitAction.None)
        { AbilityGrants = [new("care", "scenario-remedy")] };
        var unit = type.CreateUnit("actor", "blue");
        Assert.Empty(unit.AbilityUseCounts);
        Assert.Null(unit.HealUses);
        var state = Ready(type, [new("scenario-remedy", "Scenario Remedy", AbilityTiming.Action, new HealMechanic(2)) { Usage = new(3) }]);
        var ability = Assert.Single(state.AbilitiesOf("actor"));
        Assert.Equal("scenario-remedy", ability.Definition.Id);
        Assert.Equal(new AbilityUses(3, 3), state.AbilityUsesOf("actor", ability));
    }

    [Fact]
    public void OwnedClericAbilitiesKeepHistoricalCountersAndPendingChoicesWithoutGlobalDefinitions()
    {
        var state = Ready(UnitRoster.Cleric("owned-cleric"));
        state.AbilityDefinitions.Clear();
        var typeIndex = state.Types.FindIndex(type => type.Id == "owned-cleric");
        state.Types[typeIndex] = state.Types[typeIndex] with
        {
            DisplayName = "Field Medic",
            LocalAbilities = [.. state.Types[typeIndex].LocalAbilities.Select(ability => ability.Key == "heal"
                ? ability with { Name = "Field Dressing" } : ability)]
        };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var restored = Restore(state);
        GameEngine.ValidateScenario(restored);
        Assert.Empty(restored.AbilityDefinitions);
        Assert.Equal(state.Pending!.Candidates.Select(candidate => candidate.Key), restored.Pending!.Candidates.Select(candidate => candidate.Key));
        var heal = restored.AbilitiesOf("actor").Single(ability => ability.GrantId == "heal");
        Assert.Equal("Field Dressing", heal.Definition.Name);
        Assert.Equal("Field Medic", heal.SourceName);
        Assert.Equal(("heal", "heal", "unit:heal"), (heal.EntryId, heal.CandidatePrefix, heal.AccountingId));
        Assert.Equal(new AbilityUses(2, 2), restored.Units[0].HealUses);
        Assert.Equal(1, restored.EffectiveDefOf("friend"));

        var result = Use(restored, "heal:friend");
        Assert.Equal(3, result.State.Units.Single(unit => unit.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].HealUses);
        Assert.Empty(result.State.Units[0].AbilityUseCounts);
        var after = Restore(result.State);
        GameEngine.ValidateScenario(after);
        Assert.Equal(new AbilityUses(2, 1), after.Units[0].HealUses);
        Assert.Equal("Field Dressing", after.Types[typeIndex].CardEntries(abilityDefinitions: after.AbilityDefinitions)
            .Single(entry => entry.Id == "heal").Name);
    }

    [Fact]
    public void HistoricalGlobalReferencesStayLiveAndRetainDefinitionIdentityAcrossPendingSave()
    {
        var original = new AbilityDefinition("saved-remedy", "Original Remedy", AbilityTiming.Action, new HealMechanic(1))
        { Usage = new(3) };
        var type = new UnitType("legacy-source", 1, 1, 0, 0, 4, Actions: UnitAction.None)
        { Unique = true, AbilityGrants = [new("local-care", original.Id) { Compatibility = new(LegacyAbilityKind.Heal) }] };
        var state = Restore(Ready(type, [original]));
        Assert.Empty(state.Types[0].LocalAbilities);
        var definitionIndex = state.AbilityDefinitions.FindIndex(definition => definition.Id == original.Id);
        state.AbilityDefinitions[definitionIndex] = original with { Name = "Saved Remedy", Mechanic = new HealMechanic(3) };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var candidate = state.Pending!.Candidates.Single(choice => choice.Key == "heal:friend");
        Assert.Equal("saved-remedy", candidate.GrantedAbility!.Definition.Id);
        Assert.Equal("local-care", candidate.GrantedAbility.GrantId);
        Assert.Equal("Saved Remedy", candidate.GrantedAbility.Definition.Name);
        Assert.Equal(3, Assert.IsType<HealMechanic>(candidate.GrantedAbility.Definition.Mechanic).Amount);
        Assert.Equal("Original Remedy", original.Name);
        Assert.Equal(1, Assert.IsType<HealMechanic>(original.Mechanic).Amount);

        var resumed = Restore(state);
        Assert.Equal(JsonSerializer.Serialize(candidate.GrantedAbility),
            JsonSerializer.Serialize(resumed.Pending!.Candidates.Single(choice => choice.Key == candidate.Key).GrantedAbility));
        var result = Use(resumed, candidate.Key);
        Assert.Equal(4, result.State.Units.Single(unit => unit.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(3, 2), result.State.Units[0].HealUses);
        Assert.Equal("saved-remedy", Assert.Single(result.State.Types[0].AbilityGrants).AbilityId);
        Assert.Empty(result.State.Types[0].LocalAbilities);
    }

    [Fact]
    public void OwnedSecondWindKeepsPhysicalConsumptionAllowanceAndProjectionAcrossResume()
    {
        var state = Ready(new UnitType("bearer", 1, 1, 0, 0, 4, Actions: UnitAction.None) { Unique = true, CanHaveItems = true }, refresh: false);
        state.AbilityDefinitions.Clear();
        state.Units[0] = state.Units[0] with
        { Inventory = [new("wind-one", LootCardKind.SecondWind), new("wind-two", LootCardKind.SecondWind)] };
        var itemIndex = state.ItemTypes.FindIndex(item => item.Id == "second-wind");
        state.ItemTypes[itemIndex] = state.ItemTypes[itemIndex] with
        {
            Name = "Fresh Supplies",
            LocalAbilities = [Assert.Single(state.ItemTypes[itemIndex].LocalAbilities) with { Name = "Second Breath" }]
        };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        Assert.Contains("Gain +1 Action", state.ObjectContent(state.Units[0].Inventory[0]).Text);
        var grants = state.AbilitiesOf("actor").Where(ability => ability.SourceKind == AbilitySourceKind.Item).ToArray();
        Assert.Equal(2, grants.Length);
        Assert.All(grants, grant =>
        {
            Assert.Equal("Second Wind", grant.AccountingId);
            Assert.Equal("object:SecondWind", grant.EntryId);
            Assert.Equal("Second Breath", grant.Definition.Name);
            Assert.Equal("Fresh Supplies", grant.SourceName);
        });
        Assert.Equal(new[] { "wind-one", "wind-two" }, grants.Select(grant => grant.SourceInstanceId));

        var resumed = Restore(state);
        GameEngine.ValidateScenario(resumed);
        var expected = Use(state, "use-object:wind-one");
        var actual = Use(resumed, "use-object:wind-one");
        Assert.Equal(expected.Events, actual.Events);
        Assert.Equal("wind-two", Assert.Single(actual.State.Units[0].Inventory).Id);
        Assert.Equal("wind-one", Assert.Single(actual.State.LootDeck!.DiscardPile).Id);
        var used = Assert.Single(actual.Events.Where(gameEvent => gameEvent.Kind == "ItemUsed"));
        Assert.Equal("second-wind", used.ActionId);
        Assert.Equal("Second Breath", used.AbilityName);
        var useIndex = actual.Events.IndexOf(used);
        var committed = Assert.Single(actual.ResolutionSteps.Where(step => step.EventIndex == useIndex)).StateAfter;
        Assert.Equal(1, committed.BonusActionUseCountsThisActivation["Second Wind"]);
        Assert.False(committed.CanUseAbility("actor", Assert.Single(committed.AbilitiesOf("actor"))));
    }

    [Fact]
    public void OmittedHistoricalItemCatalogRetainsTheActualSavedGlobalPayload()
    {
        var state = Ready(new UnitType("old-bearer", 1, 1, 0, 0, 1, Actions: UnitAction.None) { CanHaveItems = true }, refresh: false);
        state.Units[0] = state.Units[0] with { Inventory = [new("saved-wind", LootCardKind.SecondWind)] };
        var definitionIndex = state.AbilityDefinitions.FindIndex(definition => definition.Id == "second-wind");
        state.AbilityDefinitions[definitionIndex] = state.AbilityDefinitions[definitionIndex] with
        { Name = "Saved Supply", Mechanic = new ExtraActionsMechanic(2) };

        // Explicit new Item content retains its own payload even when an unrelated
        // old global definition is present in the same saved catalog.
        var explicitCatalog = Restore(state);
        Assert.Equal(1, Assert.IsType<ExtraActionsMechanic>(Assert.Single(explicitCatalog.AbilitiesOf("actor")).Definition.Mechanic).Amount);

        var document = JsonNode.Parse(JsonSerializer.Serialize(state))!.AsObject();
        document.Remove("ItemTypes");
        var restored = JsonSerializer.Deserialize<GameState>(document.ToJsonString())!;
        GameEngine.ValidateScenario(restored);
        Assert.Empty(restored.Item("second-wind").LocalAbilities);
        Assert.Equal("second-wind", Assert.Single(restored.Item("second-wind").Grants).AbilityId);
        var ability = Assert.Single(restored.AbilitiesOf("actor"));
        Assert.Equal("second-wind", ability.Definition.Id);
        Assert.Equal("Saved Supply", ability.Definition.Name);
        Assert.Equal(2, Assert.IsType<ExtraActionsMechanic>(ability.Definition.Mechanic).Amount);
        Assert.Contains("+2 Action", restored.ObjectContent(restored.Units[0].Inventory[0]).Text);

        restored = GameEngine.RefreshChoices(Restore(restored), new Dice(), false).State;
        var result = Use(restored, "use-object:saved-wind");
        var usedIndex = result.Events.FindIndex(gameEvent => gameEvent.Kind == "ItemUsed");
        var committed = Assert.Single(result.ResolutionSteps.Where(step => step.EventIndex == usedIndex)).StateAfter;
        Assert.Equal(2, committed.ExtraActionsThisActivation);
        Assert.Equal(1, committed.BonusActionUseCountsThisActivation["Second Wind"]);
        Assert.Empty(committed.Units[0].Inventory);
        Assert.Equal("saved-wind", Assert.Single(committed.LootDeck!.DiscardPile).Id);
    }
}
