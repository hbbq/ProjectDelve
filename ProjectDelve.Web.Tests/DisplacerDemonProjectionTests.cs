using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class DisplacerDemonProjectionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static EngineResult Start()
    {
        var definition = Scenario.Define(Scenario.Map(6, 6), [
            Scenario.Group(UnitTypeIds.DisplacerDemon, "red", ControllerKind.Human, Scenario.At(2, 2)),
            Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(3, 2))
        ]);
        // Exercise the same portable scenario import used by the browser designer.
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        return GameEngine.StartRound(GameEngine.CreateGame(decoded), new Dice(), false);
    }

    [Fact]
    public void CanonicalDemonCanBeImportedAndInspectedWithUnlimitedBonusCards()
    {
        var result = Start();
        var actor = result.State.CurrentUnitId!;
        var presentation = BrowserProjection.Create(result);
        var card = presentation.Cards[actor];
        Assert.Equal("Displacer Demon", card.DisplayName);
        foreach (var entryId in new[] { "bonus:Swap", "bonus:Displace" })
        {
            var entry = Assert.Single(card.Entries, e => e.Content.Id == entryId);
            Assert.Null(entry.Uses);
            Assert.Null(entry.Content.MaxUses);
            Assert.Equal("Bonus Action", entry.Content.Category);
        }
        var swap = Assert.Single(presentation.Decision!.Candidates, c => c.EntryId == "bonus:Swap");
        Assert.Equal(InteractionKind.Unit, swap.Interaction.Kind);
        Assert.Single(swap.AffectedUnitIds);
        var displace = Assert.Single(presentation.Decision.Candidates, c => c.EntryId == "bonus:Displace" && c.Interaction.Position == new Cell(4, 2));
        Assert.Equal(InteractionKind.Position, displace.Interaction.Kind);
        Assert.Contains("to (4,2)", displace.Label);
        Assert.Equal(new[] { new Cell(4, 2) }, displace.PlacementCells);
        Assert.Single(displace.AffectedUnitIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepositioningPlaybackUsesAuthoritativeSnapshotsAndDistinctEvents(bool swap)
    {
        var start = Start();
        var choice = start.NextInput!.Candidates.Single(c => swap
            ? c.BonusAction?.Swap is not null
            : c.BonusAction?.Displace is not null && c.Destination == new Cell(4, 2));
        var result = GameEngine.Advance(start.State, new Choice(choice.Key), new Dice(), false);
        var presentation = BrowserProjection.Create(result);
        var kind = swap ? "PlacesSwapped" : "UnitRepositioned";
        var occurrence = Assert.Single(result.Events, e => e.Kind == kind);
        var index = result.Events.IndexOf(occurrence);
        var step = presentation.ResolutionSteps.Single(s => s.EventIndex == index);
        Assert.Equal(swap ? new(2, 2) : new Cell(4, 2), step.Figures[choice.TargetId!].Anchor);
        Assert.Equal(swap ? new(3, 2) : new Cell(2, 2), step.Figures[start.State.CurrentUnitId!].Anchor);
        Assert.Equal(swap ? OutcomeRole.Notice : OutcomeRole.Movement, presentation.Events[index].Role);
        Assert.DoesNotContain(result.Events, e => e.Kind == "MovementCompleted");
        Assert.Contains(swap ? "swapped places" : "displaced", presentation.Events[index].Text);
    }
}
