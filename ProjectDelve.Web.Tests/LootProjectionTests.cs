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
            Types = [type], Units = [type.CreateUnit("a", "blue") with { Inventory = [new("boots", LootCardKind.LuckyBoots), new("boots-2", LootCardKind.LuckyBoots)] },
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
        Assert.Equal(2, Assert.Single(response.Presentation.Cards["a"].Entries.Where(e => e.Source == "Equipment")).CopyCount);
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
        Assert.Equal(2, unit.Inventory!.Count);
        Assert.All(unit.Inventory, o => Assert.Equal(LootCardKind.LuckyBoots, o.Kind));
    }
}
