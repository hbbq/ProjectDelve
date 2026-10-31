using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class LootProjectionTests
{
    private static GameState State(bool fog = false)
    {
        var type = new UnitType("collector", 2, 1, 1, 0, 1) { CanHaveItems = true, CanHaveEquipment = true };
        var state = new GameState
        {
            Physical = new(new Board(20, 3, []), [new("a", new(0, 0)), new("b", new(19, 0))]),
            Types = [type], Units = [type.CreateUnit("a", "blue") with { Inventory = [new("boots", LootCardKind.LuckyBoots), new("wind-1", LootCardKind.SecondWind), new("wind-2", LootCardKind.SecondWind)] },
                type.CreateUnit("b", "red") with { Inventory = [new("sheet", LootCardKind.Bedsheet)] }],
            Controllers = [new(new(type.Id, "blue"), ControllerKind.Human), new(new(type.Id, "red"), ControllerKind.Automated)],
            FogOfWarSides = fog ? ["blue"] : [],
            LootDeck = new() { DrawPile = [new("secret", LootCardKind.SecondWind)] },
            LootMarkers = [new("hidden", new(18, 0))],
            GroundObjects = [new(new("hidden-key", LootCardKind.OfficialKey), new(19, 1))]
        };
        FogOfWar.Refresh(state);
        return state;
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowserTransportNeverContainsDrawPileAndFiltersHiddenGroundObjects(bool fog)
    {
        using var game = new PlaytestGame(new Dice(), State(fog));
        var response = game.BrowserResponse(game.Snapshot());
        Assert.Null(response.Result.State.LootDeck);
        Assert.Equal(fog ? 0 : 1, response.Result.State.LootMarkers.Count);
        Assert.Equal(fog ? 0 : 1, response.Result.State.GroundObjects.Count);
        Assert.Equal(fog ? 1 : 2, response.Presentation.Cards.Count);
        Assert.Equal(1, Assert.Single(response.Presentation.Cards["a"].Entries.Where(e => e.Source == "Equipment")).CopyCount);
        Assert.Equal(2, Assert.Single(response.Presentation.Cards["a"].Entries.Where(e => e.Source == "Item")).CopyCount);
        Assert.Contains(response.Presentation.Cards["a"].Entries, e => e.Source == "Equipment" && e.Content.Description == "MOV +1.");
    }

    [Fact]
    public void DrawPresentationUsesEventTimeVisibilityAndCollectorController()
    {
        var state = State(true);
        var hidden = new RulesEvent("LootCardDrawn", "b") { Cell = new(19, 0), ObjectCard = new("draw", LootCardKind.PocketBreakfast) };
        var visible = new RulesEvent("LootCardDrawn", "a") { Cell = new(0, 0), ObjectCard = new("draw-2", LootCardKind.LuckyBoots) };
        var result = new EngineResult(state, [hidden, visible], null)
            { ResolutionSteps = [new(0, state), new(1, state)] };
        var presentation = BrowserProjection.Create(result);
        var draw = Assert.Single(presentation.Events);
        Assert.Equal("Lucky Boots", draw.LootCard!.Name);
        Assert.True(draw.HumanCollector);
        Assert.Equal(0, Assert.Single(presentation.ResolutionSteps).EventIndex);
        Assert.False(BrowserProjection.Outcome(hidden, State()).HumanCollector);
    }

    [Fact]
    public void TypedImmediateLootUsesDeclaredNameAndAmountAndPreservesEventVisibility()
    {
        var state = State(true);
        var card = new LootCardDefinition("restorative-letter", "A Restorative Letter")
        { Immediate = new HealCollectorMechanic(3) };
        var secret = card with { Id = "secret-letter", Name = "Unseen Remedy" };
        state.LootCardDefinitions.AddRange([card, secret]);
        var hidden = new RulesEvent("LootCardDrawn", "b")
        { Cell = new(19, 0), ObjectCard = new("hidden-card") { LootCardId = secret.Id } };
        var visible = new RulesEvent("LootCardDrawn", "a")
        { Cell = new(0, 0), ObjectCard = new("revealed-card") { LootCardId = card.Id } };
        var result = new EngineResult(state, [hidden, visible], null)
        { ResolutionSteps = [new(0, state), new(1, state)] };

        var presentation = BrowserProjection.Create(result);
        var draw = Assert.Single(presentation.Events);
        Assert.Equal(card.Id, draw.LootCard!.LootCardId);
        Assert.Equal(card.Name, draw.LootCard.Name);
        Assert.Equal(LootCategory.Immediate, draw.LootCard.Category);
        Assert.Equal("Restore 3 HP, without exceeding maximum HP.", draw.LootCard.Text);
        Assert.True(draw.HumanCollector);
        Assert.DoesNotContain(secret.Name, System.Text.Json.JsonSerializer.Serialize(presentation));
    }

    [Fact]
    public void GroundObjectChoicesRemainExplicitAndUseSuppliedObjectIdentity()
    {
        var state = State();
        var choice = new Candidate("pickup-object:opaque-id", new(0, 0), Kind: ActivationChoiceKind.FreeAction)
            { ObjectId = "opaque-id", ObjectKind = LootCardKind.GoatBell };
        var request = new DecisionRequest(DecisionKind.Activation, "collector", "a", [choice], false);
        var projected = Assert.Single(BrowserProjection.Decision(request, state)!.Candidates);
        Assert.Equal(choice.Key, projected.Key);
        Assert.Equal(InteractionKind.Position, projected.Interaction.Kind);
        Assert.Contains("Emergency Goat Bell", projected.Label);
    }

    [Fact]
    public void ExternalControllerObservationIncludesVisibleIndividualInventoryOnly()
    {
        var state = State(true);
        var observation = GambitProjection.Observation(state, "blue", state.Controllers.ToDictionary(c => c.Token, _ => "Gambit"));
        Assert.Null(observation.LootMarkers); Assert.Null(observation.GroundObjects);
        var unit = Assert.Single(observation.Units);
        Assert.Equal(3, unit.Inventory!.Count);
        Assert.Equal(2, unit.Inventory.Count(o => o.Kind == LootCardKind.SecondWind));
    }

    [Fact]
    public void UnfamiliarEquipmentProjectsEveryGrantAndSourceWithoutContentSpecificWebCode()
    {
        var state = State();
        var equipment = new EquipmentDefinition("untold-moon", "Velvet Astrolabe")
        {
            LocalAbilities = [
                new("wind", "Tailwind", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2), new(Stat.Def, 3)])),
                new("phase", "Phase", AbilityTiming.Passive, new Phase()),
                new("door", "Moonlit Entrance", AbilityTiming.FreeAction, new OpenDoor()),
                new("wake", "Morning Chorus", AbilityTiming.Action, new StandAdjacentFriendlyMechanic())]
        };
        state.EquipmentTypes.Add(equipment);
        state.Units[0] = state.Units[0] with { Inventory = [new("unique-physical-object", equipment.Id)] };
        state.GroundObjects.Add(new(new("other-physical-object", equipment.Id), new(0, 0)));
        var card = BrowserProjection.Cards(state)["a"];
        Assert.Equal(equipment.Name, Assert.Single(card.Inventory).Name);
        var acquired = card.Entries.Where(e => e.Source == "Equipment").ToArray();
        Assert.Equal(equipment.CardEntries(), acquired.Select(e => e.Content));
        Assert.All(acquired, e => { Assert.Equal(equipment.Id, e.SourceTypeId); Assert.Equal(equipment.Name, e.SourceName); });
        Assert.Contains(acquired, e => e.Content.Description == "MOV +2; DEF +3.");
        var ability = state.AbilitiesOf("a").Single(a => a.SourceTypeId == equipment.Id && a.Key == "wake");
        var choice = new Candidate(ability.CandidatePrefix + ":b", TargetId: "b")
            { EquipmentTypeId = equipment.Id, ObjectId = ability.SourceInstanceId, Ability = ability, AbilityName = ability.Definition.Name };
        var request = new DecisionRequest(DecisionKind.Activation, "collector", "a", [choice], false);
        var projected = Assert.Single(BrowserProjection.Decision(request, state)!.Candidates);
        Assert.Equal(equipment.EntryId("wake"), projected.EntryId);
        Assert.Contains("Morning Chorus", projected.Label);
        Assert.Equal(equipment.Name, Assert.Single(GambitProjection.Decision(request, state).Candidates).ObjectContent!.Name);
        Assert.Equal(equipment.Name, BrowserProjection.GroundObjects(state).Single(g => g.Object.Id == "other-physical-object").Object.Content.Name);
        var doorAbility = state.AbilitiesOf("a").Single(a => a.SourceTypeId == equipment.Id && a.Key == "door");
        var door = new Candidate(doorAbility.CandidatePrefix + ":0,0:0,1", Door: new(new(0, 0), new(0, 1), EdgeKind.ClosedDoor),
            Kind: ActivationChoiceKind.FreeAction, FreeAction: UnitFreeAction.OpenDoor)
            { Ability = doorAbility, EquipmentTypeId = equipment.Id, ObjectId = doorAbility.SourceInstanceId };
        var doorRequest = new DecisionRequest(DecisionKind.Activation, "collector", "a", [door], false);
        var doorChoice = Assert.Single(BrowserProjection.Decision(doorRequest, state)!.Candidates);
        Assert.Contains("Moonlit Entrance", doorChoice.Label);
        Assert.Equal(equipment.EntryId("door"), doorChoice.EntryId);
        Assert.Equal(doorAbility.AccountingId, doorChoice.AccountingId);
        Assert.DoesNotContain("Official-Looking Key", doorChoice.Label);
    }

    [Fact]
    public void HiddenScenarioEquipmentDoesNotLeakThroughObservationOrPresentation()
    {
        var state = State(true);
        var secret = new EquipmentDefinition("hidden-type", "Unseen Prism") { LocalAbilities = [
            new("strength", "Unseen Strength", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Atk, 2)]))] };
        state.EquipmentTypes.Add(secret);
        state.Units[1] = state.Units[1] with { Inventory = [new("hidden-instance", secret.Id)] };
        state.GroundObjects = [new(new("hidden-ground-instance", secret.Id), new(19, 1))];
        using var game = new PlaytestGame(new Dice(), state);
        var response = game.BrowserResponse(game.Snapshot());
        var json = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.DoesNotContain(secret.Name, json);
        Assert.DoesNotContain("hidden-instance", json);
        Assert.Empty(response.Presentation.GroundObjects);
        var observation = GambitProjection.Observation(state, "blue", state.Controllers.ToDictionary(c => c.Token, _ => "Gambit"));
        Assert.DoesNotContain(secret.Name, System.Text.Json.JsonSerializer.Serialize(observation));
    }

}
