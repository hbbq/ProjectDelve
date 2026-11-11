using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class BreakProjectionTests
{
    private static GameState Ready(int threshold = 6, AbilityTiming timing = AbilityTiming.Action)
    {
        var type = new UnitType("breaker", 1, 1, 1, 0, 2)
        { LocalAbilities = [new("break", "Demolish", timing, new BreakObject(threshold))] };
        var state = new GameState
        {
            Physical = new(new Board(5, 5, [new(new(1, 1), new(1, 2), EdgeKind.Wall)])
                { Terrain = [new(new(2, 1), TerrainKind.SolidRock)] }, [new("actor", new(1, 1))]),
            Types = [type], Units = [type.CreateUnit("actor", "blue")],
            Round = 1, ActiveToken = new(type.Id, "blue"), CurrentUnitId = "actor", MoveDone = true,
            Pending = new(DecisionKind.Activation, type.Id, "actor", [], false)
        };
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    [Theory]
    [InlineData(AbilityTiming.Action, "Action")]
    [InlineData(AbilityTiming.BonusAction, "Bonus Action")]
    public void MixedTargetsRemainTypedInBrowserAndGambit(AbilityTiming timing, string category)
    {
        var state = Ready(6, timing);
        var browser = BrowserProjection.Decision(state.Pending, state)!;
        var choices = browser.Candidates.Where(c => c.AccountingId == "unit:break").ToArray();
        Assert.Equal(2, choices.Length);
        Assert.Contains(choices, c => c.Interaction.Kind == InteractionKind.Position && c.Interaction.Position == new Cell(2, 1));
        Assert.Contains(choices, c => c.Interaction.Kind == InteractionKind.Edge && c.Interaction.Edge!.Kind == EdgeKind.Wall);
        Assert.All(choices, c => { Assert.Contains($"({category})", c.Label); Assert.Contains("no roll", c.Label); Assert.Empty(c.PlacementCells); });
        var gambit = GambitProjection.Decision(state.Pending!, state);
        var typed = gambit.Candidates.Where(c => c.BreakTarget is not null).ToArray();
        Assert.Equal(2, typed.Length);
        Assert.All(typed, c => Assert.Equal(6, c.SuccessThreshold));
        Assert.Contains(typed, c => c.BreakTarget!.Cell == new Cell(2, 1));
        Assert.Contains(typed, c => c.BreakTarget!.Edge?.Kind == EdgeKind.Wall);
    }

    [Fact]
    public void GuaranteedBreakPublishesGeometryAndOutcomeWithoutDice()
    {
        var state = Ready();
        var dice = new Dice();
        var result = GameEngine.Advance(state, new Selection(r => r.Candidates.Single(c => c.BreakTarget?.Cell is not null).Key), dice, false);
        Assert.Equal(0, dice.Rolls);
        Assert.DoesNotContain(result.Events, e => e.Kind == "DiceRolled");
        var raw = Assert.Single(result.Events, e => e.Kind == "ObjectBreakResolved");
        var browser = BrowserProjection.Create(result);
        var outcome = Assert.Single(browser.Events, e => e.BreakOutcomes.Count > 0);
        Assert.Contains("no roll", outcome.Text);
        Assert.Contains("Gravel", outcome.Text);
        var gambit = GambitProjection.Event(raw);
        Assert.Equal("gravel", Assert.Single(gambit.BreakOutcomes!).AfterTerrainTypeId);
        var frame = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == raw.Kind);
        Assert.Equal("gravel", frame.StateAfter.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Equal("solid-rock", state.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
    }

    [Fact]
    public void SelectedPendingRollRoundTripsTypedTargetAndFailureNarration()
    {
        var state = Ready(2, AbilityTiming.BonusAction);
        var pending = GameEngine.Advance(state, new Selection(r => r.Candidates.Single(c => c.BreakTarget?.Edge is not null).Key), new Dice(), false);
        Assert.Equal(DecisionKind.RollDice, pending.NextInput!.Kind);
        Assert.NotNull(GambitProjection.Decision(pending.NextInput, pending.State).Roll!.BreakTarget!.Edge);
        Assert.Contains("Edge", BrowserProjection.Decision(pending.NextInput, pending.State)!.Prompt);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(pending.State))!;
        var failed = GameEngine.Advance(restored, new Selection(_ => "roll-dice"), new Dice(), false);
        var outcome = Assert.Single(BrowserProjection.Create(failed).Events, e => e.BreakOutcomes.Count > 0);
        Assert.Contains("failed; unchanged", outcome.Text);
        Assert.Equal(EdgeKind.Wall, failed.State.Physical.Board.EdgeBetween(new(1, 1), new(1, 2)));
    }

    [Fact]
    public void EventTimeObservationDoesNotExposeHiddenBeforeStateOrLaterDiscovery()
    {
        var initial = Ready();
        initial.FogOfWarSides.Add("viewer");
        initial.ExploredCells["viewer"] = [];
        var later = initial.DetachedCopy();
        later.ExploredCells["viewer"] = [new(2, 1)];
        var occurrence = new RulesEvent("AdjacentObjectsBreakResolved", "actor")
        { SourceUnitId = "actor", ActionId = "secret", AbilityName = "Hidden demolition",
            BreakOutcomes = [new(new(Cell: new(2, 1)), 6, null, true, "secret-rock", "gravel")] };
        var result = new EngineResult(later, [occurrence], null) { ResolutionSteps = [new(0, initial)] };
        var observed = BrowserProjection.Observe(result, "viewer");
        Assert.Empty(observed.Events);
        Assert.Empty(observed.ResolutionSteps);
        var discovered = FogOfWar.ObserveEvent(later, "viewer", occurrence)!;
        Assert.NotNull(discovered);
        Assert.Null(discovered.UnitId);
        Assert.Null(Assert.Single(discovered.BreakOutcomes).BeforeTerrainTypeId);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(GambitProjection.Event(discovered)));
    }

    private sealed class Selection(Func<DecisionRequest, string> select) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => select(request); }
    private sealed class Dice : IRandomProvider
    {
        public int Rolls;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int RollD6() { Rolls++; return 6; }
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
}
