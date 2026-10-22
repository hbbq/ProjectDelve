using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class DelveChessPresentationTests
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

    [Fact]
    public void FollowUpProjectsAuthoritativeDestinationsStayAndCapabilityName()
    {
        var definition = Scenario.Define(Scenario.Map(5, 3),
            [Scenario.Group(UnitTypeIds.CastleGuard, "custom-side", ControllerKind.Human, Scenario.At(1, 1, unitId: "actor")),
             Scenario.Group(UnitTypeIds.GoblinPeasant, "opponent", ControllerKind.Human,
                 Scenario.At(2, 1, unitId: "victim"), Scenario.At(4, 2, unitId: "reserve"))]) with
        {
            Objectives = [new("custom-side", ScenarioObjective.NoObjective), new("opponent", ScenarioObjective.NoObjective)]
        };
        var random = new Dice();
        var result = TestGame.StartRound(GameEngine.CreateGame(definition), random, false);
        result = TestGame.Advance(result.State, new Choice("stay"), random, false);
        result = TestGame.Advance(result.State, new Choice("attack:victim"), random, false);
        Assert.Equal(DecisionKind.Move, result.NextInput!.Kind);
        var presentation = BrowserProjection.Create(result);
        Assert.Equal("Advance On Kill", presentation.Decision!.Prompt);
        Assert.Equal("Stay here", presentation.Decision.NoneChoice!.Label);
        Assert.Equal(result.NextInput.Candidates.Select(c => c.Key), presentation.Decision.Candidates.Select(c => c.Key));
        Assert.Contains(presentation.Cards["actor"].Entries, e => e.Content.Id == "straight-orthogonal");
        Assert.Contains(presentation.Cards["actor"].Entries, e => e.Content.Id == "advance-on-kill");
        var moved = TestGame.Advance(result.State, new Choice("2,1"), random, false);
        var projectedMove = Assert.Single(BrowserProjection.Create(moved).Events, e => e.Role == OutcomeRole.Movement);
        Assert.Equal(new Cell[] { new(1, 1), new(2, 1) }, projectedMove.Path);
    }
}
