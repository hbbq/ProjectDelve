using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class LabyrinthShaperProjectionTests
{
    private sealed class ShaperFirstRandom : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) =>
            bag.FirstOrDefault(t => t.TypeId == UnitTypeIds.LabyrinthShaper) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    [Fact]
    public void ImportedBossHasUnlimitedUntargetedBonusActionWithCanonicalPresentation()
    {
        var definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(
            WebScenarioFixtures.Shaper()));
        var started = TestGame.StartRound(GameEngine.CreateGame(definition), new ShaperFirstRandom(), false);
        var presentation = BrowserProjection.Create(started);
        var card = presentation.Cards[started.State.CurrentUnitId!];
        Assert.Equal("Labyrinth Shaper", card.DisplayName);
        var entry = Assert.Single(card.Entries, e => e.Content.Id == "ability:reshape-room");
        Assert.Equal("Bonus Action", entry.Content.Category);
        Assert.Null(entry.Uses);
        Assert.Null(entry.Content.MaxUses);
        var choice = Assert.Single(presentation.Decision!.Candidates, c => c.EntryId == entry.Content.Id);
        Assert.Equal(InteractionKind.Direct, choice.Interaction.Kind);
        Assert.Empty(choice.AffectedUnitIds);
        Assert.Empty(choice.PlacementCells);
        var result = TestGame.Advance(started.State, new Choice(choice.Key!), new ShaperFirstRandom(), false);
        Assert.DoesNotContain(BrowserProjection.Create(result).Decision!.Candidates, c => c.EntryId == entry.Content.Id);
    }

    [Fact]
    public void AutomatedScenarioReshapesBeforeMovingAndSuppliesAuthoritativeBoardSnapshot()
    {
        var game = new PlaytestGame(new ShaperFirstRandom(), GameEngine.CreateGame(WebScenarioFixtures.Shaper()));
        var initial = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(WebScenarioFixtures.Shaper()));
        var boss = Assert.Single(initial.Result.State.Units, u => u.TypeId == UnitTypeIds.LabyrinthShaper);
        var round = game.StartRound(initial.Revision);
        var occurrence = Assert.Single(round.Result.Events, e => e.Kind == "ActionUsed" && e.UnitId == boss.Id &&
            e.Category == ActivationChoiceKind.BonusAction && e.ActionId == "ability:reshape-room");
        Assert.Equal("Reshape Room", occurrence.AbilityName);
        var reshapeIndex = round.Result.Events.IndexOf(occurrence);
        var moveIndex = round.Result.Events.FindIndex(e => e.Kind == "MovementCompleted" && e.UnitId == boss.Id);
        Assert.True(moveIndex > reshapeIndex);
        var reshaped = round.Result.ResolutionSteps.Single(s => s.EventIndex == reshapeIndex).StateAfter;
        var board = reshaped.Physical.Board;
        Assert.Equal(initial.Result.State.Physical.Figures, reshaped.Physical.Figures);
        Assert.Equal(initial.Result.State.Physical.Board.Terrain, board.Terrain);
        Assert.Equal(EdgeKind.None, board.EdgeBetween(new(4, 4), new(5, 4)));
        Assert.Equal(EdgeKind.None, board.EdgeBetween(new(3, 4), new(3, 5)));
        Assert.Equal(EdgeKind.None, board.EdgeBetween(new(4, 2), new(4, 3)));
        Assert.Equal(EdgeKind.Wall, board.EdgeBetween(new(2, 3), new(3, 3)));
        Assert.Equal(EdgeKind.None, board.EdgeBetween(new(5, 3), new(6, 3)));
        Assert.Equal(EdgeKind.WallWithWindow, board.EdgeBetween(new(7, 6), new(7, 7)));
        Assert.Contains("Reshape Room", round.Presentation.Events[reshapeIndex].Text);
        var restarted = game.Restart(round.Revision);
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(initial.Result.State, restarted.Result.State);
    }
}
