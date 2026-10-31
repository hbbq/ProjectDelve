using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class GrantedActionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static LocalAbilityDefinition Wave(string id, string name, int? uses = null) =>
        new(id, name, AbilityTiming.Action, new HolyWaveMechanic()) { Usage = new(uses) };

    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 8, Height = 5 },
        UnitTypeIds = [UnitTypeIds.Cleric, UnitTypeIds.Barbarian, "test-enemy"],
        UnitTypes = [new("test-enemy", 0, 1, 1, 0, 5) { Unique = true }],
        Units = [new(UnitTypeIds.Cleric, "blue", new(2, 2), UnitId: "actor"),
            new(UnitTypeIds.Barbarian, "blue", new(1, 2), InitialHp: 1, UnitId: "friend"),
            new("test-enemy", "red", new(3, 2), UnitId: "enemy")],
        Agency = [new(UnitTypeIds.Cleric, "blue", ControllerKind.Human),
            new(UnitTypeIds.Barbarian, "blue", ControllerKind.Human), new("test-enemy", "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };

    private static GameState Ready(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        state.Round = 1;
        state.ActiveToken = new(UnitTypeIds.Cleric, "blue");
        state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, UnitTypeIds.Cleric, "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static EngineResult Use(GameState state, Candidate choice) => GameEngine.Advance(state, new Choice(choice.Key), new Dice(), false);
    private static Candidate Grant(GameState state, AbilitySourceKind source, string grantId) =>
        state.Pending!.Candidates.Single(c => c.Ability is { } grant && grant.SourceKind == source && grant.Key == grantId);
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void RenamedEquipmentHolyWaveUsesTheSameMechanicWithoutSpendingNativeUses()
    {
        var scenario = Scenario() with
        { EquipmentTypes = [new("unfamiliar-reliquary", "Soft Lantern") { LocalAbilities = [Wave("sing", "A Very Quiet Tide")] }] };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("lantern-instance", "unfamiliar-reliquary")] };
        var state = Ready(scenario);
        Assert.Contains(state.Pending!.Candidates, c => c.Key == "ability:holy-wave");
        var result = Use(state, Grant(state, AbilitySourceKind.Equipment, "sing"));
        Assert.False(result.State.IsUpright("actor"));
        Assert.False(result.State.IsUpright("enemy"));
        Assert.True(result.State.IsUpright("friend"));
        Assert.Equal(new AbilityUses(2, 2), result.State.AbilityUsesOf("actor", result.State.AbilitiesOf("actor").Single(a => a.Key == "holy-wave")));
        Assert.Equal("A Very Quiet Tide", result.Events.Single(e => e.Kind == "ActionUsed").AbilityName);
        Assert.Single(result.State.Units.Single(u => u.Id == "actor").Inventory);
    }

    [Fact]
    public void ItemHolyWaveConsumesExactlyOneCopyAndLyingPreventsUsingTheOther()
    {
        var scenario = Scenario() with
        { ItemTypes = [new("paper-tide", "Paper Tide") { LocalAbilities = [Wave("unfold", "Folded Tide")] }] };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("paper-one") { ItemTypeId = "paper-tide" }, new("paper-two") { ItemTypeId = "paper-tide" }] };
        var state = Ready(scenario);
        var choices = state.Pending!.Candidates.Where(c => c.Ability?.SourceKind == AbilitySourceKind.Item).ToArray();
        Assert.Equal(2, choices.Length);
        Assert.Equal(choices[0].Ability!.AccountingId, choices[1].Ability!.AccountingId);
        var result = Use(state, choices[0]);
        var bearer = result.State.Units.Single(u => u.Id == "actor");
        Assert.Equal("paper-two", Assert.Single(bearer.Inventory).Id);
        Assert.Equal("paper-one", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        Assert.False(result.State.IsUpright("actor"));
        Assert.False(result.State.CanUseAbility("actor", result.State.AbilitiesOf("actor").Single(g => g.SourceKind == AbilitySourceKind.Item)));
        Assert.Equal(new AbilityUses(2, 2), result.State.AbilityUsesOf("actor", result.State.AbilitiesOf("actor").Single(a => a.Key == "holy-wave")));
        Assert.Equal(new[] { "ItemUsed", "ActionUsed", "PostureChanged", "PostureChanged" }, result.Events.Take(4).Select(e => e.Kind));
        Assert.Equal(1, result.ResolutionSteps[0].StateAfter.ActionsUsedThisActivation);
    }

    [Fact]
    public void ChangedItemActionReportsConsumptionBeforeResolution()
    {
        var scenario = Scenario();
        scenario.Units[0] = scenario.Units[0] with
        { Inventory = [new("wind-one", LootCardKind.SecondWind), new("wind-two", LootCardKind.SecondWind)] };
        var state = Ready(scenario);
        var itemIndex = state.ItemTypes.FindIndex(item => item.Id == "second-wind");
        state.ItemTypes[itemIndex] = state.ItemTypes[itemIndex] with { LocalAbilities = [Wave("use", "Folded Wind")] };
        state = GameEngine.RefreshChoices(Restore(state), new Dice(), false).State;
        var choice = state.Pending!.Candidates.Single(c => c.Key == "item:wind-one:use");
        var result = Use(state, choice);

        var consumed = Assert.Single(result.Events.Where(e => e.Kind == "ItemUsed"));
        Assert.Equal("wind-one", consumed.ObjectCard!.Id);
        Assert.Equal("Folded Wind", consumed.AbilityName);
        Assert.Equal("item:second-wind:use", consumed.ActionId);
        Assert.Equal(ActivationChoiceKind.Action, consumed.Category);
        Assert.Equal(new[] { "ItemUsed", "ActionUsed", "PostureChanged", "PostureChanged" },
            result.Events.Take(4).Select(e => e.Kind));
        var committed = result.ResolutionSteps[0].StateAfter;
        Assert.Equal(1, committed.ActionsUsedThisActivation);
        Assert.Empty(committed.BonusActionUseCountsThisActivation);
        Assert.True(committed.IsUpright("actor"));
        Assert.Equal("wind-two", Assert.Single(committed.Units.Single(u => u.Id == "actor").Inventory).Id);
        Assert.Equal("wind-one", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        Assert.False(result.State.IsUpright("actor"));
        Assert.False(result.State.IsUpright("enemy"));
    }

    [Fact]
    public void CanonicalSecondWindConsumesPhysicalItemAndAppliesItsDeclaredExtraAction()
    {
        var scenario = Scenario();
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("wind-one", LootCardKind.SecondWind)] };
        var state = Ready(scenario);
        var result = Use(state, state.Pending!.Candidates.Single(c => c.Key == "item:wind-one:use"));

        var consumed = Assert.Single(result.Events.Where(e => e.Kind == "ItemUsed"));
        Assert.Equal("item:second-wind:use", consumed.ActionId);
        Assert.Equal(ActivationChoiceKind.BonusAction, consumed.Category);
        Assert.Single(result.Events.Where(e => e.Kind == "ActionUsed"));
        var checkpoint = result.ResolutionSteps.Single(step => result.Events[step.EventIndex].Kind == "ActionUsed").StateAfter;
        Assert.Equal(1, checkpoint.ExtraActionsThisActivation);
        Assert.Equal(1, checkpoint.BonusActionUseCountsThisActivation["item:second-wind:use"]);
        Assert.Equal(0, checkpoint.ActionsUsedThisActivation);
        Assert.Empty(checkpoint.Units.Single(u => u.Id == "actor").Inventory);
        Assert.Equal("wind-one", Assert.Single(checkpoint.LootDeck!.DiscardPile).Id);
    }

    [Fact]
    public void DistinctUnitGrantsOfOneMechanicHaveIndependentLimits()
    {
        var scenario = Scenario();
        scenario = scenario with { UnitTypes = [.. scenario.UnitTypes!, UnitRoster.Cleric() with
            { LocalAbilities = [.. UnitRoster.Cleric().LocalAbilities, Wave("first", "One Song", 1), Wave("second", "Two Songs", 2)] }] };
        var state = Ready(scenario);
        var firstGrant = Grant(state, AbilitySourceKind.Unit, "first").Ability!;
        var secondGrant = Grant(state, AbilitySourceKind.Unit, "second").Ability!;
        var result = Use(state, Grant(state, AbilitySourceKind.Unit, "first"));
        Assert.Equal(new AbilityUses(1, 0), result.State.AbilityUsesOf("actor", firstGrant));
        Assert.Equal(new AbilityUses(2, 2), result.State.AbilityUsesOf("actor", secondGrant));
        Assert.Equal(new AbilityUses(2, 2), result.State.AbilityUsesOf("actor", result.State.AbilitiesOf("actor").Single(a => a.Key == "holy-wave")));
        Assert.Equal(JsonSerializer.Serialize(result.State), JsonSerializer.Serialize(Restore(result.State)));
    }

    [Fact]
    public void HealUsesTheSelectedPayloadInsteadOfTheNativeUnitFieldAndSpendsOneAction()
    {
        var scenario = Scenario() with
        { EquipmentTypes = [new("blue-thread", "Blue Thread") { LocalAbilities = [new("mend", "Small Mend", AbilityTiming.Action, new HealMechanic(1))] }] };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("thread", "blue-thread")] };
        var state = Ready(scenario);
        var result = Use(state, Grant(state, AbilitySourceKind.Equipment, "mend"));
        Assert.Equal(2, result.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Equal(new AbilityUses(2, 2), result.State.AbilityUsesOf("actor", result.State.AbilitiesOf("actor").Single(a => a.Key == "heal")));
        var actionState = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ActionUsed").StateAfter;
        Assert.Equal(1, actionState.ActionsUsedThisActivation);
        Assert.Equal(0, actionState.ActionsRemaining);
        Assert.Equal("Small Mend", result.Events.Single(e => e.Kind == "HealResolved").AbilityName);
    }

    [Fact]
    public void ConsumedFireballItemKeepsTypedAttackContextAcrossSaveResume()
    {
        var scenario = Scenario() with
        { ItemTypes = [new("folded-flame", "Folded Flame") { LocalAbilities = [new("burn", "Red Origami", AbilityTiming.Action, new FireballMechanic())] }] };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("flame-copy") { ItemTypeId = "folded-flame" }] };
        var state = Ready(scenario);
        var choice = state.Pending!.Candidates.Single(c => c.Ability?.Key == "burn" && c.Destination == new Cell(3, 2));
        var begun = Use(state, choice);
        var replayedStart = Use(Restore(state), choice);
        Assert.Equal(JsonSerializer.Serialize(begun), JsonSerializer.Serialize(replayedStart));
        Assert.Empty(begun.State.Units.Single(u => u.Id == "actor").Inventory);
        Assert.Equal(UnitAction.Fireball, begun.State.AttackInProgress!.SemanticAction);
        Assert.Equal("Red Origami", begun.State.AttackInProgress.AbilityName);
        Assert.Equal(DecisionKind.RollDice, begun.NextInput!.Kind);
        var continued = GameEngine.Advance(begun.State, new Choice("roll-dice"), new Dice(), false);
        var replayed = GameEngine.Advance(Restore(begun.State), new Choice("roll-dice"), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(continued), JsonSerializer.Serialize(replayed));
        Assert.Equal("flame-copy", Assert.Single(replayed.State.LootDeck!.DiscardPile).Id);
    }

    [Fact]
    public void EmptyHolyWaveRemainsLegalAndLaysDownTheBearer()
    {
        var scenario = Scenario() with
        { EquipmentTypes = [new("empty-vessel", "Empty Vessel") { LocalAbilities = [Wave("ripple", "Silent Ripple")] }] };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("vessel", "empty-vessel")] };
        scenario.Units[2] = scenario.Units[2] with { Anchor = new(7, 4) };
        var state = Ready(scenario);
        var choice = Grant(state, AbilitySourceKind.Equipment, "ripple");
        Assert.False(choice.Relevant);
        Assert.Empty(choice.TargetIds);
        Assert.False(Use(state, choice).State.IsUpright("actor"));
    }

    [Fact]
    public void EquipmentActionBelongsToItsOwnerWithinOneActivationGroup()
    {
        var scenario = Scenario() with
        {
            UnitTypes = [new("test-enemy", 0, 1, 1, 0, 5) { Unique = true }, UnitRoster.Cleric() with { Unique = false, Hp = 1 }],
            EquipmentTypes = [new("group-lantern", "Group Lantern") { LocalAbilities = [Wave("sing", "Shared Song")] }]
        };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("group-lantern-copy", "group-lantern")] };
        scenario.Units[1] = scenario.Units[1] with { UnitTypeId = UnitTypeIds.Cleric };
        var state = Ready(scenario);
        Assert.Equal("group-lantern-copy", Grant(state, AbilitySourceKind.Equipment, "sing").Ability!.SourceInstanceId);
        Assert.DoesNotContain(state.AbilitiesOf("friend"), g => g.SourceKind == AbilitySourceKind.Equipment);
        state.CurrentUnitId = "friend";
        state.Pending = state.Pending! with { UnitId = "friend" };
        var friendChoices = GameEngine.RefreshChoices(state, new Dice(), false);
        Assert.DoesNotContain(friendChoices.NextInput!.Candidates, c => c.Ability?.SourceKind == AbilitySourceKind.Equipment);
        Assert.Contains(friendChoices.NextInput.Candidates, c => c.Key == "ability:holy-wave");
    }

    [Fact]
    public void RenamedItemFireballFiltersHiddenRecipientsAndResumesAuthoritativeTargets()
    {
        var scenario = ProjectDelve.Engine.Scenario.Define(
            ProjectDelve.Engine.Scenario.Map(10, 8, cells: [ProjectDelve.Engine.Scenario.Tile(4, 4, TerrainKind.Tree)]),
            [ProjectDelve.Engine.Scenario.Group(UnitTypeIds.Wizard, "explorers", ControllerKind.Human,
                ProjectDelve.Engine.Scenario.At(3, 4, unitId: "wizard")),
             ProjectDelve.Engine.Scenario.Group(UnitTypeIds.Grunt, "dungeon", ControllerKind.Automated,
                ProjectDelve.Engine.Scenario.At(5, 4, unitId: "hidden"))]) with
        {
            FogOfWarSides = ["explorers"], Loot = LootSettings.Empty,
            ItemTypes = [new("folded-sun", "Folded Sun") { LocalAbilities = [new("open", "Bright Fold", AbilityTiming.Action, new FireballMechanic())] }]
        };
        scenario.Units[0] = scenario.Units[0] with { Inventory = [new("sun-copy") { ItemTypeId = "folded-sun" }] };
        var state = GameEngine.CreateGame(scenario);
        state.Round = 1; state.ActiveToken = new(UnitTypeIds.Wizard, "explorers");
        state.CurrentUnitId = "wizard"; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, UnitTypeIds.Wizard, "wizard", [], false);
        var ready = GameEngine.RefreshChoices(state, new Dice(), false);
        var choice = ready.NextInput!.Candidates.Single(c => c.Ability?.Key == "open" && c.Destination == new Cell(4, 3));
        Assert.DoesNotContain("hidden", choice.TargetIds);
        Assert.False(choice.Relevant);
        var attack = Use(Restore(ready.State), choice);
        Assert.Equal(UnitAction.Fireball, attack.State.AttackInProgress!.SemanticAction);
        Assert.Contains(attack.State.AttackInProgress.Targets, t => t.TargetId == "hidden");
        Assert.DoesNotContain("hidden", attack.NextInput!.Roll!.TargetIds);
        Assert.False(FogOfWar.IsUnitObservable(attack.State, "explorers", "hidden"));
        Assert.DoesNotContain("hidden", JsonSerializer.Serialize(FogOfWar.Observe(attack.State, "explorers")));
        while (attack.NextInput?.Kind == DecisionKind.RollDice)
            attack = GameEngine.Advance(Restore(attack.State), new Choice("roll-dice"), new Dice(), false);
        Assert.DoesNotContain(attack.State.Physical.Figures, f => f.Id == "hidden");
        Assert.Equal("sun-copy", Assert.Single(attack.State.LootDeck!.DiscardPile).Id);
    }
}
