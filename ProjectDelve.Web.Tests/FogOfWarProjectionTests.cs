using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using ProjectDelve.Testing;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class FogOfWarProjectionTests
{
    private static GameState Dungeon() => GameEngine.CreateGame(FogOfWarFixture.SleepingDungeon());
    private static GameState Copy(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static void Move(GameState state, string id, Cell cell)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = cell };
    }
    private sealed class Random(bool dungeonFirst = false) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.OrderBy(t =>
            t.SideId == (dungeonFirst ? "dungeon" : "explorers") ? 0 : 1).First();
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }

    [Fact]
    public void ObservationHidesUnknownContentsAndInteriorEdgesAndKeepsDimensions()
    {
        var state = Dungeon();
        state.Resources.Add(new(ResourceTypes.Diamond, new(12, 4)));
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(13, 4)));
        state.Physical.Board.Machines.Add(new("hidden-machine", new(14, 4), MachineTypes.DiamondPress));
        state.Physical.Board.Terrain.Add(new(new(12, 5), TerrainKind.Rail, RailDirection.South));
        state.Physical.Board.Edges.Add(new(new(12, 5), new(13, 5), EdgeKind.ClosedDoor));
        var before = JsonSerializer.Serialize(state);
        var observed = FogOfWar.Observe(state, "explorers");
        Assert.Equal((20, 9), (observed.Physical.Board.Width, observed.Physical.Board.Height));
        Assert.Equal("hero", Assert.Single(observed.Units).Id);
        Assert.Empty(observed.Resources); Assert.Empty(observed.CellMarkers); Assert.Empty(observed.Physical.Board.Machines);
        Assert.Empty(observed.Physical.Board.Terrain);
        Assert.DoesNotContain(observed.Physical.Board.Edges, e => e.A.X == 12);
        Assert.Contains(observed.Physical.Board.Edges, e => e.A == new Cell(5, 4));
        var view = BrowserProjection.Create(new(state, [], null));
        Assert.Equal("explorers", view.ViewingSideId); Assert.Contains(new Cell(12, 4), view.UnknownCells);
        Assert.DoesNotContain("guard", view.Cards.Keys);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void UnitsAppearAndDisappearAtFogBoundaryAndWholeLargeFigureIsObserved()
    {
        var state = Dungeon(); Move(state, "guard", new(5, 4));
        Assert.Contains(FogOfWar.Observe(state, "explorers").Units, u => u.Id == "guard");
        Move(state, "guard", new(6, 4));
        Assert.DoesNotContain(FogOfWar.Observe(state, "explorers").Units, u => u.Id == "guard");
        state.Types[1] = state.Types[1] with { Footprint = Footprint.TwoByTwo };
        state.ExploredCells["explorers"].Add(new(9, 5)); Move(state, "guard", new(8, 4));
        var view = BrowserProjection.Create(new(state, [], null));
        Assert.Equal(4, view.Figures["guard"].OccupiedCells.Count);
    }

    [Fact]
    public void HistoryUsesEventTimeExplorationAndReindexesSnapshotsAfterOmissions()
    {
        var before = Dungeon(); var after = Copy(before);
        after.ExploredCells["explorers"].UnionWith([new(8, 4), new(9, 4)]);
        var hidden = new RulesEvent("MovementCompleted", "guard", Path: [new(9, 4), new(8, 4)]);
        var opened = new RulesEvent("DoorOpened", "hero", Door: new(new(5, 4), new(6, 4), EdgeKind.OpenDoor));
        var result = new EngineResult(after, [hidden, opened, hidden], null)
        { ResolutionSteps = [new(0, before), new(1, after), new(2, after)] };
        var observed = BrowserProjection.Observe(result, "explorers");
        Assert.Equal(["DoorOpened", "MovementCompleted"], observed.Events.Select(e => e.Kind));
        Assert.Equal([0, 1], observed.ResolutionSteps.Select(s => s.EventIndex));
        var presentation = BrowserProjection.Create(result);
        Assert.Equal(2, presentation.Events.Count); Assert.Equal(2, presentation.ResolutionSteps.Count);
        Assert.Equal(OutcomeRole.DoorOpened, presentation.Events[0].Role);
    }

    [Fact]
    public void HiddenActivationCombatAndDiceAreOmittedWithoutPlaceholders()
    {
        var state = Dungeon();
        var events = new List<RulesEvent> {
            new("TokenDrawn", TypeId: UnitTypeIds.Grunt) { Token = new(UnitTypeIds.Grunt, "dungeon") },
            new("ActivationStarted", "guard"),
            new("ActionUsed", "guard", "sleeper"),
            new("AttackStarted", "guard") { AttackContext = new("guard", "attack", null, null, [new("sleeper", 1)], 1) },
            new("AttackTargetResolved", "guard", "sleeper", Damage: 1),
            new("ActivationCompleted", "guard") };
        var result = new EngineResult(state, events, null)
        { ResolutionSteps = events.Select((_, i) => new ResolutionStep(i, Copy(state))).ToList() };
        var view = BrowserProjection.Create(result);
        Assert.Empty(view.Events); Assert.Empty(view.ResolutionSteps);
        Assert.Equal(6, result.Events.Count);
    }

    [Fact]
    public void VisibleConsequencesHideSourceAndFilterIndividualAttackTargets()
    {
        var state = Dungeon();
        var damage = new RulesEvent("ExplosionDamageResolved", "guard", "hero", Damage: 1, AbilityName: "Secret bomb")
        { SourceUnitId = "guard", Cell = new(8, 4) };
        var visible = FogOfWar.ObserveEvent(state, "explorers", damage)!;
        Assert.Null(visible.UnitId); Assert.Null(visible.SourceUnitId); Assert.Null(visible.Cell); Assert.Null(visible.AbilityName);
        Assert.Equal("hero", visible.TargetId);
        var outcome = BrowserProjection.Outcome(visible, state);
        Assert.Contains("Unseen source", outcome.DisplayText); Assert.DoesNotContain("guard", outcome.DisplayText);
        var attack = new RulesEvent("AttackResolved", "hero", Attack: new(2, 1, [new("guard", 1, 0, 1), new("hero", 1, 0, 1)]));
        var projected = FogOfWar.ObserveEvent(state, "explorers", attack)!;
        Assert.Equal("hero", Assert.Single(projected.Attack!.Targets).TargetId);
    }

    [Fact]
    public void PreviewHidesAutomaticRecipientsWhileAreaResolutionRetainsThem()
    {
        var state = Dungeon();
        var choice = new Candidate("area", Action: UnitAction.Fireball, Destination: new(5, 4)) { TargetIds = ["hero", "guard"] };
        var request = new DecisionRequest(DecisionKind.Activation, UnitTypeIds.Barbarian, "hero", [choice], false)
        { Token = new(UnitTypeIds.Barbarian, "explorers") };
        var observed = FogOfWar.ObserveDecision(state, "explorers", request)!;
        Assert.Equal("hero", Assert.Single(observed.Candidates[0].TargetIds));
        Assert.Equal(2, choice.TargetIds.Length);
        Assert.Equal(["hero"], BrowserProjection.Decision(observed, state)!.Candidates[0].AffectedUnitIds);
    }

    [Fact]
    public void GambitGetsKnownWorldAndUnknownCellsWithoutPrivilegedTargetsOrBag()
    {
        var state = Dungeon();
        var observed = GambitProjection.Observation(state, "explorers", new Dictionary<ActivationToken, string>());
        Assert.Equal("hero", Assert.Single(observed.Units).Id); Assert.Empty(observed.Bag);
        Assert.NotEmpty(observed.UnknownCells); Assert.Contains("?", observed.AsciiBoard);
        Assert.DoesNotContain("guard", observed.AsciiBoard);
    }

    [Fact]
    public void BrowserUsesStableViewingSideAndGenericBagWithoutChangingAuthoritativeTokens()
    {
        var state = Dungeon(); state.Bag = [new(UnitTypeIds.Grunt, "dungeon")];
        state.ActiveToken = state.Bag[0]; state.CurrentUnitId = "guard";
        using var game = new PlaytestGame(new Random(), state);
        var response = game.BrowserResponse(new(0, new(state, [], null)));
        Assert.Empty(response.Result.State.Bag); Assert.Null(response.Result.State.ActiveToken); Assert.Null(response.Result.State.CurrentUnitId);
        Assert.Equal("explorers", response.Presentation.ViewingSideId);
        Assert.Single(state.Bag); Assert.Equal("guard", state.CurrentUnitId);
        Assert.Single(response.Presentation.Cards);
    }

    [Fact]
    public void BrowserImportAndRestartPreserveSettingAndResetRuntimeExploration()
    {
        using var game = new PlaytestGame(new Random());
        var loaded = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(FogOfWarFixture.SleepingDungeon()));
        Assert.Equal(["explorers"], loaded.Result.State.FogOfWarSides);
        var initial = loaded.Result.State.ExploredCells["explorers"];
        loaded.Result.State.ExploredCells["explorers"].Add(new(19, 8));
        var restarted = game.Restart(loaded.Revision);
        Assert.DoesNotContain(new Cell(19, 8), restarted.Result.State.ExploredCells["explorers"]);
        Assert.Equal("explorers", game.BrowserResponse(restarted).Presentation.ViewingSideId);
        var browser = game.BrowserResponse(restarted);
        var restartedFromBrowser = game.SetupRestart(restarted.Revision, null, browser.ControllerAssignments.ToList());
        Assert.Contains(restartedFromBrowser.ControllerAssignments, a => a.SideId == "dungeon" && a.Controller == "Default Behavior");
        Assert.DoesNotContain(new Cell(19, 8), restartedFromBrowser.Result.State.ExploredCells["explorers"]);
    }

    [Fact]
    public void DisabledObservationPreservesExistingContentsEventsAndPresentation()
    {
        var state = GameEngine.CreateGame(FogOfWarFixture.SleepingDungeon(false));
        var e = new RulesEvent("ActivationStarted", "guard");
        Assert.Same(e, FogOfWar.ObserveEvent(state, "explorers", e));
        var view = BrowserProjection.Create(new(state, [e], null));
        Assert.Null(view.ViewingSideId); Assert.Empty(view.UnknownCells);
        Assert.Equal(state.Units.Count, view.Cards.Count); Assert.Single(view.Events);
    }

    [Fact]
    public void DisconnectedVisibleMovementSegmentsNeverAnimateAnInventedHopAcrossUnknownCells()
    {
        var state = Dungeon();
        state.ExploredCells["explorers"].Add(new(8, 4));
        var e = new RulesEvent("MovementCompleted", "guard", Path: [new(5, 4), new(6, 4), new(7, 4), new(8, 4)]);
        var projected = FogOfWar.ObserveEvent(state, "explorers", e)!;
        Assert.Equal("MovementObserved", projected.Kind); Assert.Null(projected.Path);
        var outcome = BrowserProjection.Outcome(projected, state);
        Assert.Equal(OutcomeRole.Movement, outcome.Role);
        Assert.Equal(2, outcome.MovementSegments!.Count);
        Assert.Equal([new Cell(5, 4), new(6, 4)], outcome.MovementSegments[0]);
        Assert.Equal([new Cell(7, 4), new(8, 4)], outcome.MovementSegments[1]);
        Assert.NotNull(outcome.MovingFigure);
    }

    [Theory]
    [InlineData(4, 5, 1)] // known -> known
    [InlineData(7, 5, 1)] // unknown -> known
    [InlineData(5, 7, 1)] // known -> unknown
    [InlineData(8, 7, 0)] // unknown -> unknown
    public void MovementProjectionIncludesOnlyObservedRunsAndOneBoundaryStep(int from, int to, int expected)
    {
        var state = Dungeon(); Move(state, "guard", new(to, 4));
        var direction = Math.Sign(to - from);
        var path = Enumerable.Range(0, Math.Abs(to - from) + 1).Select(i => new Cell(from + i * direction, 4)).ToList();
        var occurrence = new RulesEvent("MovementCompleted", "guard", Path: path);
        var observed = FogOfWar.ObserveEvent(state, "explorers", occurrence);
        if (expected == 0) { Assert.Null(observed); return; }
        Assert.Single(observed!.ObservedMovement!);
        Assert.DoesNotContain(observed.ObservedMovement!.SelectMany(s => s), c => c.X > 6);
        var outcome = BrowserProjection.Create(new(state, [occurrence], null)).Events.Single();
        Assert.NotNull(outcome.MovingFigure);
        Assert.Equal(UnitTypeIds.Grunt, outcome.MovingFigure!.TypeId);
        Assert.DoesNotContain("7,4", outcome.Text);
        Assert.DoesNotContain("8,4", outcome.Text);
        // Observation remains idempotent without restoring hidden world state.
        var twice = FogOfWar.ObserveEvent(FogOfWar.Observe(state, "explorers"), "explorers", observed);
        Assert.NotNull(twice);
        Assert.Equal(observed.Kind, twice.Kind);
    }

    private sealed class PatrolRandom : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.OrderBy(t => t.SideId == "red" ? 0 : 1).First();
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }

    [Fact]
    public void TestOwnedPatrolUsesActualCanonicalMovementToEnterAndLeaveFog()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectDelve.sln"))) directory = directory.Parent;
        var definition = ScenarioDefinitionJson.FromJson(File.ReadAllText(Path.Combine(directory!.FullName, "ProjectDelve.Engine.Tests", "Fixtures", "fog-patrol.json")));
        using var game = new PlaytestGame(new PatrolRandom(), GameEngine.CreateGame(definition));
        Assert.DoesNotContain("patrol", game.BrowserResponse(game.Snapshot()).Presentation.Cards.Keys);
        var entered = game.StartRound(0);
        var entering = game.BrowserResponse(entered).Presentation.Events.Single(e => e.Role == OutcomeRole.Movement);
        Assert.Equal([new Cell(6, 2), new(5, 2)], entering.Path);
        Assert.Equal([new Cell(7, 2), new(6, 2), new(5, 2)], Assert.Single(entering.MovementSegments!));
        Assert.Equal("Grunt", entering.MovingFigure!.Name);
        Assert.Contains("patrol", game.BrowserResponse(entered).Presentation.Cards.Keys);
        for (var i = 0; !entered.Result.State.RoundComplete && i < 4; i++)
        {
            var candidates = entered.Result.NextInput!.Candidates;
            var key = candidates.Any(c => c.Key == "stay") ? "stay" : "end-turn";
            entered = game.Decide(entered.Revision, key);
        }
        Assert.True(entered.Result.State.RoundComplete);
        var left = game.StartRound(entered.Revision);
        var leaving = game.BrowserResponse(left).Presentation.Events.Single(e => e.Role == OutcomeRole.Movement);
        Assert.Equal([new Cell(5, 2), new(6, 2)], leaving.Path);
        Assert.Equal([new Cell(5, 2), new(6, 2), new(7, 2)], Assert.Single(leaving.MovementSegments!));
        Assert.Equal("Grunt", leaving.MovingFigure!.Name);
        Assert.DoesNotContain("patrol", game.BrowserResponse(left).Presentation.Cards.Keys);
    }

    [Fact]
    public async Task StreamAndReplayOmitHiddenActivityWithoutPlaceholderUpdatesOrSequenceGaps()
    {
        using var game = new PlaytestGame(new Random(dungeonFirst: true), Dungeon());
        using var live = game.Subscribe(null);
        var baseline = await live.Reader.ReadAsync();
        var round = game.StartRound(0);
        Assert.Contains(round.Result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "sleeper");
        var updates = new List<GameUpdate>();
        while (live.Reader.TryRead(out var update)) updates.Add(game.BrowserUpdate(update));
        Assert.NotEmpty(updates);
        Assert.Equal(Enumerable.Range(1, updates.Count).Select(i => (long)i), updates.Select(u => u.Sequence));
        Assert.All(updates.Where(u => u.Kind == "event"), u => Assert.NotEmpty(u.Snapshot.Result.Events));
        Assert.DoesNotContain(updates.SelectMany(u => u.Snapshot.Result.Events), e =>
            e.UnitId is "sleeper" or "guard" or "archer" || e.Token?.SideId == "dungeon");
        using var replay = game.Subscribe($"{baseline.SessionId}:0");
        var replayed = new List<GameUpdate>();
        while (replay.Reader.TryRead(out var update)) replayed.Add(game.BrowserUpdate(update));
        Assert.Equal(updates.Select(u => u.Sequence), replayed.Select(u => u.Sequence));
        Assert.Equal(updates.SelectMany(u => u.Snapshot.Result.Events).Select(e => e.Kind),
            replayed.SelectMany(u => u.Snapshot.Result.Events).Select(e => e.Kind));
    }

    [Fact]
    public void VisibleStatsRetainAuthoritativeBonusWithoutShowingItsHiddenProvider()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        Move(state, "guard", new(4, 3));
        state.Physical.Board.Terrain.Add(new(new(4, 4), TerrainKind.Tree));
        var aura = new UnitType("hidden-aura", 1, 0, 0, 0, 1) { LocalAbilities = [
            new("defence", "Hidden Defence", AbilityTiming.Passive, new AdjacentModifiersMechanic([new(Stat.Def, 2)]))] };
        state.Types.Add(aura); state.Units.Add(aura.CreateUnit("aura", "dungeon"));
        state.Physical.Figures.Add(new("aura", new(5, 4)));
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.True(FogOfWar.IsUnitObservable(state, "explorers", "guard"));
        Assert.False(FogOfWar.IsUnitObservable(state, "explorers", "aura"));
        using var game = new PlaytestGame(new Random(), state);
        var response = game.BrowserResponse(new(0, new(state, [], null)));
        Assert.DoesNotContain(response.Result.State.Units, u => u.Id == "aura");
        Assert.Equal(5, state.EffectiveDefOf("guard"));
        Assert.Equal(5, response.Result.State.EffectiveDef["guard"]);
        var json = JsonSerializer.SerializeToElement(response.Result.State);
        Assert.Equal(5, json.GetProperty("EffectiveDef").GetProperty("guard").GetInt32());
    }

    [Theory]
    [InlineData(5, 8, true)] [InlineData(8, 5, false)]
    public void DecisionCommentaryUsesVisibilityBeforeMovementRatherThanItsDestination(int from, int to, bool observed)
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        Move(state, "hero", new(0, 4)); Move(state, "guard", new(from, 4));
        state.Controllers = state.Controllers.Select(c => c with { Controller = ControllerKind.Human }).ToList();
        state.Round = 1; state.CurrentUnitId = "guard"; state.ActiveToken = new(UnitTypeIds.Grunt, "dungeon");
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        using var game = new PlaytestGame(new Random(), ready.State);
        using var subscription = game.Subscribe(null);
        var result = game.Decide(0, $"{to},4", "A comment at the decision boundary");
        var response = game.BrowserResponse(result);
        Assert.Equal(observed, response.Commentaries.Any());
        var streamed = new List<GameUpdate>();
        while (subscription.Reader.TryRead(out var update)) streamed.Add(game.BrowserUpdate(update));
        Assert.Equal(observed, streamed.Any(u => u.Commentary is not null));
        Assert.Equal(!observed, FogOfWar.IsUnitObservable(result.Result.State, "explorers", "guard"));
    }
}
