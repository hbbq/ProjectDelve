using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CanonicalEquipmentGrantTests
{
    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 3, Height = 2 }, UnitTypeIds = ["bearer"],
        UnitTypes = [new("bearer", 3, 1, 1, 1, 1) { CanHaveItems = true, CanHaveEquipment = true }],
        Units = [new("bearer", "blue", new(0, 0), UnitId: "actor") { Inventory = [new("bell-physical", "goat-bell")] },
            new("bearer", "blue", new(1, 0), Posture.Lying, UnitId: "friend")],
        Agency = [new("bearer", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition? scenario = null)
    {
        var state = GameEngine.CreateGame(scenario ?? Scenario());
        state.Round = 1; state.ActiveToken = new("bearer", "blue"); state.CurrentUnitId = "actor"; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "bearer", "actor", [], false);
        return state;
    }
    private static EngineResult Ring(GameState state) => GameEngine.Advance(state, new Choice("goat-bell:friend"), new Random(), false);
    private static EquipmentDefinition LegacyBell() => new("goat-bell", "Emergency Goat Bell")
    {
        Abilities = [new("stand-up", "Emergency Goat Bell", EquipmentActionMechanic.StandAdjacentFriendlyLyingUnit)
            { CandidatePrefix = "goat-bell" }]
    };
    private static EquipmentDefinition ReferencedBell() => new("goat-bell", "Emergency Goat Bell")
    {
        Grants = [new("stand-up", "goat-bell-stand-up")
            { Compatibility = new(LegacyAbilityKind.EquipmentAction, CandidatePrefix: "goat-bell") }]
    };

    [Fact]
    public void CanonicalBellOwnsItsAbilityAndKeepsHistoricalSourceIdentities()
    {
        var state = Ready();
        var equipment = state.Equipment("goat-bell");
        Assert.Empty(equipment.Abilities);
        Assert.Empty(equipment.Grants);
        var ability = Assert.Single(equipment.LocalAbilities);
        Assert.Equal("stand-up", ability.Key);
        Assert.Equal(AbilityTiming.Action, ability.Timing); Assert.Null(ability.Usage.MaxUses);
        Assert.IsType<StandAdjacentFriendlyMechanic>(ability.Mechanic);
        var grant = Assert.Single(state.AbilitiesOf("actor"));
        Assert.Equal("goat-bell", grant.CandidatePrefix); Assert.Equal("goat-bell", grant.ActionId);
        Assert.Equal("equipment:goat-bell:stand-up", grant.EntryId);
        Assert.Equal("equipment:bell-physical:stand-up", grant.AccountingId);
        Assert.Equal("bell-physical", grant.SourceInstanceId);
        Assert.Equal("Action: Stand up one adjacent friendly Lying Unit.", state.ObjectContent(state.Units[0].Inventory[0]).Text);
    }

    [Fact]
    public void SharedAndLegacyInlineBellResolveIdenticallyAndResumeTheSameChoice()
    {
        var shared = Ready();
        var legacy = Ready();
        legacy.EquipmentTypes[legacy.EquipmentTypes.FindIndex(d => d.Id == "goat-bell")] = LegacyBell();
        var referenced = Ready();
        referenced.EquipmentTypes[referenced.EquipmentTypes.FindIndex(d => d.Id == "goat-bell")] = ReferencedBell();
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(shared))!;
        var first = Ring(shared); var old = Ring(legacy); var saved = Ring(resumed); var global = Ring(referenced);
        Assert.Equal(first.Events, old.Events);
        Assert.Equal(first.Events, saved.Events);
        Assert.Equal(first.Events, global.Events);
        Assert.True(first.State.IsUpright("friend"));
        foreach (var result in new[] { first, old, saved, global })
        {
            var actionIndex = result.Events.FindIndex(e => e.Kind == "ActionUsed");
            var actionState = Assert.Single(result.ResolutionSteps.Where(s => s.EventIndex == actionIndex)).StateAfter;
            Assert.Equal("actor", actionState.CurrentUnitId);
            Assert.Equal(1, actionState.ActionsUsedThisActivation);
            Assert.Equal(0, actionState.ActionsRemaining);
        }
        Assert.Equal("bell-physical", Assert.Single(first.State.Units[0].Inventory).Id);
        Assert.Equal("goat-bell", Assert.Single(first.Events.Where(e => e.Kind == "ActionUsed")).ActionId);
    }

    [Fact]
    public void RenamingCanonicalBellAndAbilityChangesLabelsWithoutChangingItsMechanicOrCompatibilityKeys()
    {
        var state = Ready();
        var equipment = state.EquipmentTypes.FindIndex(d => d.Id == "goat-bell");
        state.EquipmentTypes[equipment] = state.EquipmentTypes[equipment] with
        {
            Name = "Silver Chime",
            LocalAbilities = [Assert.Single(state.EquipmentTypes[equipment].LocalAbilities) with { Name = "Rise Again" }]
        };
        state.AbilityDefinitions.Reverse();
        var grant = Assert.Single(state.AbilitiesOf("actor"));
        Assert.Equal("Rise Again", grant.Definition.Name); Assert.Equal("Silver Chime", grant.SourceName);
        Assert.Equal("goat-bell", grant.CandidatePrefix);
        var result = Ring(state);
        Assert.True(result.State.IsUpright("friend"));
        Assert.Equal("Rise Again", Assert.Single(result.Events.Where(e => e.Kind == "ActionUsed")).AbilityName);
    }

    [Fact]
    public void HistoricalReferencedBellReadsItsSavedCatalogAfterAnAbilityRename()
    {
        var state = Ready();
        var equipmentIndex = state.EquipmentTypes.FindIndex(equipment => equipment.Id == "goat-bell");
        state.EquipmentTypes[equipmentIndex] = ReferencedBell();
        state = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        var definitionIndex = state.AbilityDefinitions.FindIndex(ability => ability.Id == "goat-bell-stand-up");
        state.AbilityDefinitions[definitionIndex] = state.AbilityDefinitions[definitionIndex] with { Name = "Saved Chime" };
        state = GameEngine.RefreshChoices(state, new Random(), false).State;
        var candidate = state.Pending!.Candidates.Single(choice => choice.Key == "goat-bell:friend");
        Assert.Equal("goat-bell-stand-up", candidate.GrantedAbility!.Definition.Id);
        Assert.Equal("Saved Chime", candidate.GrantedAbility.Definition.Name);
        Assert.Empty(state.Equipment("goat-bell").LocalAbilities);
        var result = Ring(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!);
        Assert.True(result.State.IsUpright("friend"));
        Assert.Equal("Saved Chime", Assert.Single(result.Events.Where(gameEvent => gameEvent.Kind == "ActionUsed")).AbilityName);
    }

    [Fact]
    public void OmittedOldEquipmentCatalogReceivesCompleteCanonicalLocalAbilities()
    {
        var state = Ready();
        state.AbilityDefinitions = [AbilityCatalog.All.Single(a => a.Id == "second-wind")];
        var json = JsonNode.Parse(JsonSerializer.Serialize(state))!.AsObject();
        json.Remove("EquipmentTypes");
        var restored = JsonSerializer.Deserialize<GameState>(json.ToJsonString())!;
        GameEngine.ValidateScenario(restored);
        Assert.DoesNotContain(restored.AbilityDefinitions, ability => ability.Id == "goat-bell-stand-up");
        Assert.IsType<StandAdjacentFriendlyMechanic>(Assert.Single(restored.Equipment("goat-bell").LocalAbilities).Mechanic);
        Assert.True(Ring(restored).State.IsUpright("friend"));
        var again = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(restored))!;
        Assert.Equal(JsonSerializer.Serialize(restored), JsonSerializer.Serialize(again));
    }

    [Fact]
    public void OmittedEquipmentCatalogDoesNotOverwriteAnOldLocalAbilityWithTheNewCanonicalId()
    {
        var state = Ready();
        state.Types[0] = state.Types[0] with { AbilityGrants = [new("old-local", "goat-bell-stand-up")] };
        state.AbilityDefinitions = [AbilityCatalog.All.Single(a => a.Id == "second-wind"),
            new("goat-bell-stand-up", "Old Local", AbilityTiming.BonusAction, new ExtraActionsMechanic(3)),
            new("canonical-equipment:goat-bell-stand-up", "Occupied Alias", AbilityTiming.BonusAction, new ExtraActionsMechanic(2))];
        var json = JsonNode.Parse(JsonSerializer.Serialize(state))!.AsObject();
        json.Remove("EquipmentTypes");
        var restored = JsonSerializer.Deserialize<GameState>(json.ToJsonString())!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(3, Assert.IsType<ExtraActionsMechanic>(restored.Ability("goat-bell-stand-up").Mechanic).Amount);
        Assert.Equal("goat-bell-stand-up", Assert.Single(restored.Types[0].AbilityGrants).AbilityId);
        Assert.Empty(restored.Equipment("goat-bell").Grants);
        Assert.IsType<StandAdjacentFriendlyMechanic>(Assert.Single(restored.Equipment("goat-bell").LocalAbilities).Mechanic);
        Assert.Equal(3, restored.AbilityDefinitions.Count);
        Assert.Equal("bell-physical", Assert.Single(restored.Units[0].Inventory).Id);
        Assert.True(Ring(restored).State.IsUpright("friend"));
        var again = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(restored))!;
        Assert.Equal(JsonSerializer.Serialize(restored), JsonSerializer.Serialize(again));
    }

    [Fact]
    public void ExplicitLegacyEquipmentCatalogAndFilteredObservationsStayUnchanged()
    {
        var state = Ready();
        state.EquipmentTypes = [LegacyBell()];
        state.LootCardDefinitions = [];
        state.AbilityDefinitions = [AbilityCatalog.All.Single(a => a.Id == "second-wind")];
        var json = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<GameState>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Single(restored.Equipment("goat-bell").Abilities);
        Assert.DoesNotContain(restored.AbilityDefinitions, a => a.Id == "goat-bell-stand-up");
        Assert.True(Ring(restored).State.IsUpright("friend"));
        state.Units[0] = state.Units[0] with { Inventory = [] };
        var observed = FogOfWar.Observe(state, null);
        Assert.Empty(observed.EquipmentTypes); Assert.Empty(observed.AbilityDefinitions);
        var observedAgain = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(observed))!;
        Assert.Empty(observedAgain.EquipmentTypes); Assert.Empty(observedAgain.AbilityDefinitions);
    }

    [Fact]
    public void NewlyCanonicalAbilityCollisionsRewriteOnlyLocalGrantsAndLeaveAuthoringUntouched()
    {
        var scenario = Scenario();
        scenario = scenario with
        {
            AbilityDefinitions = [new("goat-bell-stand-up", "Local Power", AbilityTiming.BonusAction, new ExtraActionsMechanic(3)),
                new("local-ability:goat-bell-stand-up", "Occupied Alias", AbilityTiming.BonusAction, new ExtraActionsMechanic(2))],
            UnitTypes = [scenario.UnitTypes![0] with { AbilityGrants = [new("local-unit", "goat-bell-stand-up")] }],
            EquipmentTypes = [new("local-equipment", "Local Equipment") { Grants = [new("local-equip", "goat-bell-stand-up")] }],
            ItemTypes = [new("local-item", "Local Item") { Grants = [new("local-item", "goat-bell-stand-up")] }]
        };
        var before = ScenarioDefinitionJson.ToJson(scenario);
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scenario));
        var state = Ready(loaded);
        const string alias = "local-ability:local-ability:goat-bell-stand-up";
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(scenario));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(loaded));
        Assert.Equal(alias, Assert.Single(state.Types[0].AbilityGrants).AbilityId);
        Assert.Equal(alias, Assert.Single(state.Equipment("local-equipment").Grants).AbilityId);
        Assert.Equal(alias, Assert.Single(state.Item("local-item").Grants).AbilityId);
        Assert.Empty(state.Equipment("goat-bell").Grants);
        Assert.Equal("stand-up", Assert.Single(state.Equipment("goat-bell").LocalAbilities).Key);
        Assert.Equal(3, Assert.IsType<ExtraActionsMechanic>(state.Ability(alias).Mechanic).Amount);
        Assert.True(Ring(state).State.IsUpright("friend"));
    }

    [Fact]
    public void AbilityAliasesPreserveMissingReferenceAndDuplicateDefinitionRejections()
    {
        var scenario = Scenario();
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario with
        {
            AbilityDefinitions = [new("heal", "Local", AbilityTiming.Action, new HealMechanic(3))],
            UnitTypes = [scenario.UnitTypes![0] with { AbilityGrants = [new("bad", "local-ability:heal")] }]
        }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario with
        {
            AbilityDefinitions = [new("heal", "First", AbilityTiming.Action, new HealMechanic(3)),
                new("heal", "Second", AbilityTiming.Action, new HealMechanic(2))]
        }));
        // This ID was canonical before the extraction and was never legal local content.
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario with
        {
            AbilityDefinitions = [new("second-wind", "Invalid Replacement", AbilityTiming.BonusAction, new ExtraActionsMechanic(3))]
        }));
    }
}
