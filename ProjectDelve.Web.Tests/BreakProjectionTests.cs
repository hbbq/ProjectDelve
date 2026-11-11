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

    [Fact]
    public void AreaEdgeBatchFiltersHiddenTargetsAndSourceWithoutBackfillingLaterDiscovery()
    {
        var visibleEdge = new Edge(new(1, 1), new(2, 1), EdgeKind.Wall);
        var hiddenEdge = new Edge(new(3, 2), new(3, 3), EdgeKind.ClosedDoor);
        var type = new UnitType("area-breaker", 0, 0, 0, 0, 1, Actions: UnitAction.None)
        {
            LocalAbilities = [new("self-defeat", "Self defeat", AbilityTiming.InsteadOfActivation, new DefeatSelf()),
                new("secret-break", "Hidden demolition", AbilityTiming.WhenDefeated, new BreakAdjacentObjects(6))]
        };
        var state = GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 5, Height = 5,
                Edges = [new(visibleEdge.A, EdgeDirection.Right, visibleEdge.Kind),
                    new(hiddenEdge.A, EdgeDirection.Down, hiddenEdge.Kind)] },
            UnitTypeIds = [type.Id], UnitTypes = [type],
            Units = [new(type.Id, "blue", new(2, 2), UnitId: "actor")],
            Agency = [new(type.Id, "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        });
        state.FogOfWarSides.Add("viewer");
        state.ExploredCells["viewer"] = [visibleEdge.A];

        var result = GameEngine.StartRound(state, new Dice(), false);
        var raw = Assert.Single(result.Events, e => e.Kind == "AdjacentObjectsBreakResolved");
        Assert.Equal(2, raw.BreakOutcomes.Length);
        Assert.Equal(EdgeKind.None, result.State.Physical.Board.EdgeBetween(visibleEdge.A, visibleEdge.B));
        Assert.Equal(EdgeKind.None, result.State.Physical.Board.EdgeBetween(hiddenEdge.A, hiddenEdge.B));
        var visible = Assert.Single(raw.BreakOutcomes, o => o.Target.Edge == visibleEdge);
        Assert.Contains("viewer", visible.BeforeVisibleSideIds);
        Assert.Empty(Assert.Single(raw.BreakOutcomes, o => o.Target.Edge == hiddenEdge).BeforeVisibleSideIds);

        var later = result.State.DetachedCopy();
        later.ExploredCells["viewer"].UnionWith([new(2, 2), hiddenEdge.A, hiddenEdge.B]);
        var observed = BrowserProjection.Observe(result with { State = later }, "viewer");
        var occurrence = Assert.Single(observed.Events, e => e.Kind == raw.Kind);
        var outcome = Assert.Single(occurrence.BreakOutcomes);
        Assert.Equal(visibleEdge, outcome.Target.Edge);
        Assert.Equal(EdgeKind.Wall, outcome.BeforeEdgeKind);
        Assert.Equal(EdgeKind.None, outcome.AfterEdgeKind);
        Assert.Empty(outcome.BeforeVisibleSideIds);
        Assert.Null(occurrence.UnitId);
        Assert.Null(occurrence.SourceUnitId);
        Assert.Null(occurrence.ActionId);
        Assert.Null(occurrence.AbilityName);
        Assert.Null(occurrence.AbilitySource);
        var frame = observed.ResolutionSteps.Single(s => observed.Events[s.EventIndex].Kind == raw.Kind);
        Assert.False(FogOfWar.IsExplored(frame.StateAfter, "viewer", hiddenEdge.A));
        Assert.DoesNotContain("secret-break", JsonSerializer.Serialize(GambitProjection.Event(occurrence)));

        // A fresh observation may show the current opening, but cannot reveal its hidden prior structure.
        var discovered = FogOfWar.ObserveEvent(later, "viewer", raw)!;
        var discoveredEdge = Assert.Single(discovered.BreakOutcomes, o => o.Target.Edge?.A == hiddenEdge.A);
        Assert.Null(discoveredEdge.BeforeEdgeKind);
        Assert.Equal(EdgeKind.None, discoveredEdge.Target.Edge!.Kind);
        Assert.Null(discovered.SourceUnitId);
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
