using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EquipmentMechanicsTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int LootDrawIndex(int exclusiveMax) => 0;
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static ScenarioDefinition Scenario(params EquipmentDefinition[] equipment) => new()
    {
        Board = new() { Width = 20, Height = 5 },
        UnitTypeIds = ["test-bearer"],
        UnitTypes = [new("test-bearer", 3, 1, 2, 1, 1) { CanHaveEquipment = true, CanHaveItems = true }],
        EquipmentTypes = [.. equipment],
        Units = [new("test-bearer", "blue", new(1, 1), UnitId: "a"),
            new("test-bearer", "blue", new(2, 1), Posture.Lying, UnitId: "friend"),
            new("test-bearer", "red", new(18, 1), UnitId: "enemy")],
        Agency = [new("test-bearer", "blue", ControllerKind.Human), new("test-bearer", "red", ControllerKind.Automated)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };

    private static GameState Ready(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        // Keep an ordinary Attack available so exhausted optional choices do not
        // automatically finish the activation before its accounting is inspected.
        var enemyFigure = state.Physical.Figures.FindIndex(f => f.Id == "enemy");
        state.Physical.Figures[enemyFigure] = new("enemy", new(1, 0));
        state.Round = 1;
        state.CurrentUnitId = "a";
        state.ActiveToken = new("test-bearer", "blue");
        state.Pending = new(DecisionKind.Activation, "test-bearer", "a", [], false);
        return state;
    }

    private static EngineResult Do(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);

    private static ScenarioDefinition Own(ScenarioDefinition scenario, params OwnedObject[] objects)
    {
        scenario.Units[0] = scenario.Units[0] with { Inventory = [.. objects] };
        return scenario;
    }

    [Fact]
    public void UnfamiliarEquipmentCombinesAllModifiersAndIdenticalEffectsStackAcrossTypes()
    {
        var multistat = new EquipmentDefinition("moth-ledger", "A Most Unexpected Ledger")
        { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2), new(Stat.Atk, 3), new(Stat.Def, 2), new(Stat.Rng, 1)]))] };
        var identical = new EquipmentDefinition("spare-sock", "A Sock With Ambitions")
        { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2)]))] };
        var state = Ready(Own(Scenario(multistat, identical), new("ledger-1", multistat.Id), new("sock-1", identical.Id)));

        Assert.Equal(7, state.EffectiveMovOf("a"));
        Assert.Equal(5, state.EffectiveAtkOf("a"));
        Assert.Equal(3, state.EffectiveDefOf("a"));
        Assert.Equal(2, state.EffectiveRngOf("a"));
        Assert.Equal(3, state.EffectiveMovOf("friend"));
        Assert.Equal(2, state.EffectiveAtkOf("friend"));
        Assert.Equal(1, state.EffectiveDefOf("friend"));
        Assert.Equal(1, state.EffectiveRngOf("friend"));
        Assert.Equal(3, state.Types.Single().Mov);
        Assert.Equal(multistat.Name, state.ObjectContent(state.Units[0].Inventory[0]).Name);
        Assert.Contains("MOV +2", state.ObjectContent(state.Units[0].Inventory[0]).Text);
    }

    [Fact]
    public void EquipmentEffectsFollowIndividualPostureWithoutChangingOwnership()
    {
        var definition = new EquipmentDefinition("quiet-sail", "Quiet Sail")
        { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2), new(Stat.Def, 3)])), new("phase", "Phase", AbilityTiming.Passive, new Phase()), new("open-door", "Open Door", AbilityTiming.FreeAction, new OpenDoor())] };
        var state = Ready(Own(Scenario(definition), new OwnedObject("sail", definition.Id)));
        Assert.True(state.HasPhase("a")); Assert.True(state.HasOpenDoor("a"));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal(3, state.EffectiveMovOf("a")); Assert.Equal(1, state.EffectiveDefOf("a"));
        Assert.False(state.HasPhase("a")); Assert.False(state.HasOpenDoor("a"));
        Assert.Single(state.Units[0].Inventory);
    }

    [Fact]
    public void PhaseAndOpenDoorUseSharedLegalityForAnUnfamiliarType()
    {
        var definition = new EquipmentDefinition("mist-pass", "Committee Permission Slip")
        { LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase()), new("open-door", "Open Door", AbilityTiming.FreeAction, new OpenDoor())] };
        var scenario = Own(Scenario(definition), new OwnedObject("permit", definition.Id));
        scenario.Board.Edges.Add(new(new(1, 1), EdgeDirection.Down, EdgeKind.ClosedDoor));
        var result = GameEngine.RefreshChoices(Ready(scenario), new Dice(), false);
        Assert.Contains(result.NextInput!.Candidates, c => c.Destination == new Cell(1, 2));
        var door = Assert.Single(result.NextInput.Candidates.Where(c => c.FreeAction == UnitFreeAction.OpenDoor));
        var opened = Do(result.State, door.Key);
        Assert.Equal(EdgeKind.OpenDoor, Assert.Single(opened.State.Physical.Board.Edges).Kind);
        Assert.Equal(0, opened.State.ActionsUsedThisActivation);
        Assert.False(opened.State.HasPhase("friend"));
        Assert.False(opened.State.HasOpenDoor("friend"));
    }

    [Fact]
    public void OpenDoorChoicesBindEveryPhysicalSourceAndPreserveFreeActionAccountingAcrossResume()
    {
        var first = new EquipmentDefinition("brass-key", "Brass Key")
        { LocalAbilities = [new("open", "First Door Call", AbilityTiming.FreeAction, new OpenDoor())] };
        var second = new EquipmentDefinition("silver-key", "Silver Key")
        { LocalAbilities = [new("open", "Second Door Call", AbilityTiming.FreeAction, new OpenDoor())] };
        var scenario = Own(Scenario(first, second), new("brass-copy", first.Id), new("silver-copy", second.Id));
        scenario.UnitTypes![0] = scenario.UnitTypes[0] with
        { LocalAbilities = [new("open", "Native Door Call", AbilityTiming.FreeAction, new OpenDoor())] };
        scenario.Board.Edges.Add(new(new(1, 1), EdgeDirection.Down, EdgeKind.ClosedDoor));
        var ready = GameEngine.RefreshChoices(Ready(scenario), new Dice(), false);
        var choices = ready.NextInput!.Candidates.Where(c => c.FreeAction == UnitFreeAction.OpenDoor).ToArray();
        Assert.Equal(3, choices.Length);
        Assert.Equal(3, choices.Select(c => c.Key).Distinct().Count());
        Assert.Equal(new[] { "a", "brass-copy", "silver-copy" }, choices.Select(c => c.Ability!.SourceInstanceId));
        Assert.Equal(new[] { "Native Door Call", "First Door Call", "Second Door Call" }, choices.Select(c => c.AbilityName));
        foreach (var choice in choices)
        {
            var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(ready.State))!;
            var result = Do(restored, choice.Key);
            Assert.Equal(EdgeKind.OpenDoor, Assert.Single(result.State.Physical.Board.Edges).Kind);
            var used = Assert.Single(result.Events.Where(e => e.Kind == "ActionUsed"));
            Assert.Equal(choice.Ability!.EntryId, used.ActionId);
            Assert.Equal(choice.Ability.Definition.Name, used.AbilityName);
            Assert.Equal(ActivationChoiceKind.FreeAction, used.Category);
            Assert.Equal(choice.Ability.EntryId, Assert.Single(result.Events.Where(e => e.Kind == "DoorOpened")).ActionId);
            Assert.Equal(0, result.State.ActionsUsedThisActivation);
            Assert.Empty(result.State.Units[0].AbilityUseCounts);
            Assert.Empty(result.State.BonusActionUseCountsThisActivation);
            Assert.Equal(new[] { "brass-copy", "silver-copy" }, result.State.Units[0].Inventory.Select(o => o.Id));
            Assert.DoesNotContain(result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        }
    }

    [Fact]
    public void SetupRejectsTwoPhysicalInstancesOfOneTypeButAllowsDifferentOwners()
    {
        var equipment = new EquipmentDefinition("silver-string", "Silver String") { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 1)]))] };
        var scenario = Own(Scenario(equipment), new("string-1", equipment.Id), new("string-2", equipment.Id));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario));
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("string-1", equipment.Id)] };
        scenario.Units[1] = scenario.Units[1] with { Inventory = [new("string-2", equipment.Id)] };
        var state = GameEngine.CreateGame(scenario);
        Assert.Equal("string-1", Assert.Single(state.Units[0].Inventory).Id);
        Assert.Equal("string-2", Assert.Single(state.Units[1].Inventory).Id);
    }

    [Fact]
    public void AutomaticDuplicateBecomesKnownGroundObjectAndCannotBePickedUpByBearer()
    {
        var equipment = new EquipmentDefinition("ruby-tag", "Unfamiliar Ruby Tag") { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 1)]))] };
        var state = Ready(Own(Scenario(equipment), new OwnedObject("old-tag", equipment.Id)));
        state.LootDeck = new() { DrawPile = [new("new-tag", equipment.Id)] };
        state.LootMarkers = [new("reward", new(1, 1))];
        var result = Do(state, "stay");
        Assert.Equal("old-tag", Assert.Single(result.State.Units[0].Inventory).Id);
        var dropped = Assert.Single(result.State.GroundObjects);
        Assert.Equal("new-tag", dropped.Object.Id); Assert.Equal(new Cell(1, 1), dropped.Position);
        Assert.Empty(result.State.LootDeck!.DrawPile);
        Assert.Contains(result.Events, e => e.Kind == "LootCardDrawn" && e.ObjectCard!.Id == "new-tag");
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.ObjectId == "new-tag");
        Assert.Equal(4, result.State.EffectiveMovOf("a"));
    }

    [Fact]
    public void DifferentTypeWithIdenticalModifierCanBePickedUpAndRetainsInstanceIdentity()
    {
        var first = new EquipmentDefinition("red-thread", "Thread") { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 1)]))] };
        var second = new EquipmentDefinition("blue-thread", "Thread") { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 1)]))] };
        var scenario = Own(Scenario(first, second), new OwnedObject("red-1", first.Id));
        scenario = scenario with { GroundObjects = [new(new("blue-1", second.Id), new(1, 1))] };
        var state = Ready(scenario);
        var result = Do(state, "pickup-object:blue-1");
        Assert.Empty(result.State.GroundObjects);
        Assert.Equal(new[] { "red-1", "blue-1" }, result.State.Units[0].Inventory.Select(o => o.Id));
        Assert.Equal(5, result.State.EffectiveMovOf("a"));
        Assert.Equal(0, result.State.ActionsUsedThisActivation);
    }

    [Fact]
    public void MultipleSourcesOfSameActionHaveStableDistinctChoicesAndOrdinaryActionAccounting()
    {
        var first = new EquipmentDefinition("brass-whistle", "Brass Whistle")
        { LocalAbilities = [new("rally", "Wake Up", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())] };
        var second = new EquipmentDefinition("velvet-drum", "Velvet Drum")
        { LocalAbilities = [new("rally", "Wake Up", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())] };
        var state = Ready(Own(Scenario(first, second), new("whistle-1", first.Id), new("drum-1", second.Id), new("wind", LootCardKind.SecondWind)));
        state.MoveDone = true;
        var result = GameEngine.RefreshChoices(state, new Dice(), false);
        var candidates = result.NextInput!.Candidates.Where(c => c.Ability is { SourceKind: AbilitySourceKind.Equipment, Key: "rally" }).ToArray();
        Assert.Equal(2, candidates.Length);
        Assert.Equal(2, candidates.Select(c => c.Key).Distinct().Count());
        Assert.All(candidates, c => Assert.Equal("friend", c.TargetId));
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        var refreshed = GameEngine.RefreshChoices(resumed, new Dice(), false);
        Assert.Equal(candidates.Select(c => c.Key), refreshed.NextInput!.Candidates.Where(c => c.Ability is { SourceKind: AbilitySourceKind.Equipment, Key: "rally" }).Select(c => c.Key));
        var used = Do(resumed, candidates.Single(c => c.EquipmentTypeId == second.Id).Key);
        Assert.True(used.State.IsUpright("friend"));
        Assert.Equal(1, used.State.ActionsUsedThisActivation);
        Assert.Equal(3, used.State.Units[0].Inventory.Length);
        Assert.DoesNotContain(used.NextInput!.Candidates, c => c.Ability?.SourceKind == AbilitySourceKind.Equipment);
    }

    [Fact]
    public void EquipmentActionRequiresMovementOpportunityAndOnlyOffersAdjacentFriendlyLyingTargets()
    {
        var equipment = new EquipmentDefinition("knitted-horn", "Knitted Horn")
        { LocalAbilities = [new("wake", "Morning Call", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())] };
        var scenario = Own(Scenario(equipment), new OwnedObject("horn-1", equipment.Id));
        scenario.Units.Add(new("test-bearer", "blue", new(1, 2), UnitId: "upright-friend"));
        scenario.Units.Add(new("test-bearer", "blue", new(5, 1), Posture.Lying, UnitId: "distant-friend"));
        scenario.Units.Add(new("test-bearer", "red", new(0, 1), Posture.Lying, UnitId: "lying-enemy"));
        var state = Ready(scenario);
        Assert.DoesNotContain(GameEngine.RefreshChoices(state, new Dice(), false).NextInput!.Candidates, c => c.Ability?.SourceKind == AbilitySourceKind.Equipment);
        state.MoveDone = true;
        var choice = Assert.Single(GameEngine.RefreshChoices(state, new Dice(), false).NextInput!.Candidates.Where(c => c.Ability is { SourceKind: AbilitySourceKind.Equipment, Key: "wake" }));
        Assert.Equal("friend", choice.TargetId);
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.DoesNotContain(GameEngine.RefreshChoices(state, new Dice(), false).NextInput!.Candidates, c => c.Ability?.SourceKind == AbilitySourceKind.Equipment);
    }

    [Fact]
    public void MultipleSingleUseItemsConsumeOneInstanceWithoutMultiplyingOrBypassingBonusRestriction()
    {
        var state = Ready(Own(Scenario(), new("wind-1", LootCardKind.SecondWind), new("wind-2", LootCardKind.SecondWind), new("wind-3", LootCardKind.SecondWind)));
        state.MoveDone = true; state.ActionsUsedThisActivation = 1;
        var result = Do(state, "item:wind-2:use");
        Assert.Equal(1, result.State.ActionsRemaining);
        Assert.Equal(new[] { "wind-1", "wind-3" }, result.State.Units[0].Inventory.Select(o => o.Id));
        Assert.Equal("wind-2", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.ItemTypeId == "second-wind");
    }

    [Fact]
    public void SaveResumeAndReplayedChoicesPreserveEquipmentEffectsEventsAndSnapshots()
    {
        var equipment = new EquipmentDefinition("opal-pencil", "Opal Pencil")
        { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 1)])), new("raise", "Please Rise", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())] };
        var state = Ready(Own(Scenario(equipment), new OwnedObject("pencil-1", equipment.Id)));
        state.LootDeck = new() { DrawPile = [new("pencil-2", equipment.Id), new("wind", LootCardKind.SecondWind)] };
        state.LootMarkers = [new("loot-1", new(1, 1)), new("loot-2", new(1, 1))];
        var before = JsonSerializer.Serialize(state);
        var first = Do(state, "stay");
        var replay = Do(JsonSerializer.Deserialize<GameState>(before)!, "stay");
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        var action = first.NextInput!.Candidates.Single(c => c.Ability is { SourceKind: AbilitySourceKind.Equipment, Key: "raise" }).Key;
        var continued = Do(first.State, action);
        var resumed = Do(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(first.State))!, action);
        Assert.Equal(JsonSerializer.Serialize(continued), JsonSerializer.Serialize(resumed));
        Assert.True(continued.State.IsUpright("friend"));
        Assert.Equal("pencil-2", Assert.Single(continued.State.GroundObjects).Object.Id);
    }

    [Fact]
    public void FogObservationHidesUnknownEquipmentInstancesAndPreservesVisibleEffects()
    {
        var visible = new EquipmentDefinition("sun-thread", "Sun Thread") { LocalAbilities = [new("stats", "Adjustments", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2)]))] };
        var hidden = new EquipmentDefinition("secret-moon", "Hidden Moon Token") { LocalAbilities = [new("phase", "Phase", AbilityTiming.Passive, new Phase())] };
        var scenario = Own(Scenario(visible, hidden), new OwnedObject("visible-thread", visible.Id)) with
        { FogOfWarSides = ["blue"], GroundObjects = [new(new("hidden-ground", hidden.Id), new(19, 1))] };
        scenario.Units[2] = scenario.Units[2] with { Inventory = [new("hidden-inventory", hidden.Id)] };
        var state = GameEngine.CreateGame(scenario);
        var observed = FogOfWar.Observe(state, "blue");
        Assert.DoesNotContain(observed.Units, u => u.Id == "enemy");
        Assert.Empty(observed.GroundObjects);
        Assert.Null(observed.LootDeck);
        Assert.Equal(5, observed.EffectiveMovOf("a"));
        Assert.Equal("Sun Thread", observed.ObjectContent(observed.Units.Single(u => u.Id == "a").Inventory.Single()).Name);
        Assert.DoesNotContain("hidden-inventory", JsonSerializer.Serialize(observed));
        Assert.DoesNotContain("hidden-ground", JsonSerializer.Serialize(observed));
        Assert.DoesNotContain("Hidden Moon Token", JsonSerializer.Serialize(observed));
    }

    [Fact]
    public void LegacyScenarioDuplicateEquipmentMigratesExcessCopiesToOwnersCell()
    {
        var scenario = Own(Scenario(), new("old-boots-1", LootCardKind.LuckyBoots),
            new("old-boots-2", LootCardKind.LuckyBoots), new("wind-1", LootCardKind.SecondWind), new("wind-2", LootCardKind.SecondWind));
        var state = GameEngine.CreateGame(scenario);
        Assert.Equal(new[] { "old-boots-1", "wind-1", "wind-2" }, state.Units[0].Inventory.Select(o => o.Id));
        var ground = Assert.Single(state.GroundObjects);
        Assert.Equal("old-boots-2", ground.Object.Id);
        Assert.Equal(new Cell(1, 1), ground.Position);
        Assert.Equal(4, state.EffectiveMovOf("a"));
        Assert.Equal(4, scenario.Units[0].Inventory!.Count); // Migration never mutates authored input.
    }

    [Fact]
    public void OmittedEquipmentCatalogMigratesDuplicateObjectsAndResumesOwnedChoices()
    {
        var state = Ready(Scenario());
        state.MoveDone = true;
        state.Units[0] = state.Units[0] with { Inventory = [new("bell-1", LootCardKind.GoatBell),
            new("bell-2", LootCardKind.GoatBell), new("wind", LootCardKind.SecondWind)] };
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(state))!.AsObject();
        json.Remove("EquipmentTypes");
        var restored = JsonSerializer.Deserialize<GameState>(json.ToJsonString())!;
        Assert.Equal(new[] { "bell-1", "wind" }, restored.Units[0].Inventory.Select(o => o.Id));
        Assert.Equal("bell-2", Assert.Single(restored.GroundObjects).Object.Id);
        var first = Do(restored, "equipment:bell-1:stand-up:friend");
        var second = Do(JsonSerializer.Deserialize<GameState>(json.ToJsonString())!, "equipment:bell-1:stand-up:friend");
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.True(first.State.IsUpright("friend"));
        Assert.Equal(1, first.State.ActionsUsedThisActivation);
        var savedAgain = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(restored))!;
        Assert.Single(savedAgain.GroundObjects); // Migration is idempotent.
    }
}


