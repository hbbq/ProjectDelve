using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ControllerAndDiceTests
{
    private sealed class Dice : IRandomProvider
    {
        public int AttackCalls, DefenceCalls;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() { AttackCalls++; return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { DefenceCalls++; return DefenceFace.Block; }
        public int RollD6() => 6;
    }
    private static GameState Setup(ControllerKind attacker, ControllerKind defender)
    {
        // Ordinary same Type on two arbitrary Sides, with independent agency.
        var type = new UnitType("ordinary", 0, 1, 2, 1, 1);
        return new()
        {
            Physical = new(new(3, 1, []), [new("one", new(0, 0)), new("two", new(1, 0))]),
            Types = [type], Units = [type.CreateUnit("one", "amber"), type.CreateUnit("two", "violet")],
            Controllers = [new(new(type.Id, "amber"), attacker), new(new(type.Id, "violet"), defender)]
        };
    }

    [Fact]
    public void AutomatedAttackerStopsAtHumanDefenceAndReprojectionNeverRolls()
    {
        var dice = new Dice();
        var game = new PlaytestGame(dice, Setup(ControllerKind.Automated, ControllerKind.Human));
        var result = game.StartRound(0);
        Assert.Equal(2, dice.AttackCalls); Assert.Equal(0, dice.DefenceCalls);
        Assert.Equal(DecisionKind.RollDice, result.Result.NextInput!.Kind);
        Assert.Equal("two", result.Result.NextInput.UnitId);
        Assert.Equal(DiceFamily.Defence, result.Presentation.Decision!.Roll!.Family);
        Assert.Equal("Roll Dice", Assert.Single(result.Presentation.Decision.Candidates).Label);
        Assert.Equal(InteractionKind.Direct, result.Presentation.Decision.Candidates[0].Interaction.Kind);
        Assert.Null(result.Presentation.Decision.NoneChoice);
        Assert.Throws<PlaytestRequestException>(() => game.Decide(result.Revision, null));
        Assert.Throws<PlaytestRequestException>(() => game.Decide(result.Revision, "invalid"));
        var refreshed = game.SetRelevanceAutoChoice(result.Revision, false);
        Assert.Equal(0, dice.DefenceCalls);
        Assert.Equal("roll-dice", refreshed.Result.NextInput!.Candidates.Single().Key);
        Assert.Equal("roll-dice", game.Snapshot().Presentation.Decision!.Candidates.Single().Key);
        Assert.Equal(0, dice.DefenceCalls);
        var rolled = game.Decide(refreshed.Revision, "roll-dice");
        Assert.Equal(1, dice.DefenceCalls);
        var outcome = rolled.Result.Events.Single(e => e.Kind == "DiceRolled");
        Assert.Equal(new[] { "Block" }, outcome.Dice!.Faces);
        Assert.Equal("one", outcome.SourceUnitId); Assert.Equal("two", outcome.UnitId);
        Assert.Contains("Block", BrowserProjection.Outcome(outcome, rolled.Result.State).Text);
    }

    [Fact]
    public void HumanAttackWaitsForInputThenHostConsumesAutomatedDefenceNormally()
    {
        var dice = new Dice();
        var game = new PlaytestGame(dice, Setup(ControllerKind.Human, ControllerKind.Automated));
        var started = game.StartRound(0);
        var committed = game.Decide(started.Revision, "attack:two");
        Assert.Equal(0, dice.AttackCalls); Assert.Equal(0, dice.DefenceCalls);
        Assert.Equal(DecisionKind.RollDice, committed.Result.NextInput!.Kind);
        Assert.Equal("one", committed.Result.NextInput.UnitId);
        Assert.Contains("2 Attack dice", committed.Presentation.Decision!.Prompt);
        Assert.Equal("Roll Dice", committed.Presentation.Decision.Candidates.Single().Label);
        var rolled = game.Decide(committed.Revision, "roll-dice");
        // The host may subsequently commit the other group's Attack, but stops again at human DEF.
        var pools = rolled.Result.Events.Where(e => e.Kind == "DiceRolled").Select(e => e.Dice!.Pool).ToArray();
        Assert.Equal(DiceFamily.Attack, pools[0].Family); Assert.Equal("one", pools[0].OwnerUnitId);
        Assert.Equal(DiceFamily.Defence, pools[1].Family); Assert.Equal("two", pools[1].OwnerUnitId);
        Assert.Equal(1, dice.DefenceCalls);
        Assert.Equal(DecisionKind.RollDice, rolled.Result.NextInput!.Kind);
        Assert.Equal("one", rolled.Result.NextInput.UnitId);
        Assert.Equal(DiceFamily.Defence, rolled.Result.NextInput.Roll!.Family);
    }

    [Fact]
    public void CanonicalScenarioControllersAreAuthoredAndIndependentAcrossFreshCopies()
    {
        foreach (var scenario in PlaytestScenarios.Catalog)
        {
            var state = PlaytestScenarios.Create(scenario.Id);
            Assert.Equal(state.Units.Select(u => new ActivationToken(u.TypeId, u.SideId)).Distinct().OrderBy(t => t.TypeId),
                state.Controllers.Select(c => c.Token).OrderBy(t => t.TypeId));
            foreach (var id in new[] { UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Wizard, UnitTypeIds.Cleric })
                Assert.All(state.Controllers.Where(c => c.Token.TypeId == id), c => Assert.Equal(ControllerKind.Human, c.Controller));
            Assert.All(state.Controllers.Where(c => c.Token.SideId == "red"), c => Assert.Equal(ControllerKind.Automated, c.Controller));
            state.Controllers.Clear();
            Assert.NotEmpty(PlaytestScenarios.Create(scenario.Id).Controllers);
        }
    }

    [Fact]
    public void ProjectionPresentsAllNewOccurrencesThroughGenericMetadata()
    {
        var state = Setup(ControllerKind.Human, ControllerKind.Automated);
        var token = new ActivationToken("ordinary", "violet");
        var pool = new DicePool(DiceFamily.D6, 1, "two", "two", "arbitrary-check", "Check", SuccessCount: 3);
        var occurrences = new RulesEvent[]
        {
            new("RoundStarted") { Round = 9 },
            new("TokenDrawn", TypeId: token.TypeId) { Token = token, SideId = token.SideId },
            new("ActivationStarted", "two") { Token = token },
            new("ActionUsed", "two") { ActionId = "bonus:unfamiliar", Category = ActivationChoiceKind.BonusAction },
            new("AttackStarted", "two") { ActionId = "unfamiliar", AttackContext = new("two", "unfamiliar", null, null, [new("one", 1)], 2, Results: []) },
            new("DiceRolled", "two") { Dice = new(pool, ["2"], 1), ActionId = pool.SourceActionId },
            new("UnitCreated", "new", Posture: Posture.Lying) { SourceUnitId = "two", SideId = "violet", Cell = new(2, 0) },
            new("ActivationCompleted", "two") { Token = token },
            new("RoundCompleted")
        };
        var text = occurrences.Select(e => BrowserProjection.Outcome(e, state).Text).ToArray();
        Assert.Contains("9", text[0]); Assert.Contains("violet", text[1]);
        Assert.Contains("activation started", text[2]); Assert.Contains("bonus:unfamiliar", text[3]);
        Assert.Contains("one", text[4]); Assert.Contains("[2]", text[5]);
        Assert.Contains("Lying", text[6]); Assert.Contains("activation completed", text[7]);
        Assert.All(text, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        var copied = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.Controllers, copied.Controllers);
    }
}
