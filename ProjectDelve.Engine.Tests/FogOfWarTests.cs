using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Testing;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class FogOfWarTests
{
    private static GameState Dungeon() => GameEngine.CreateGame(FogOfWarFixture.SleepingDungeon());
    private static bool Known(GameState state, int x, int y) => FogOfWar.IsExplored(state, "explorers", new(x, y));
    private static void Relocate(GameState state, string id, Cell cell)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == id);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = cell };
    }
    private static void Ready(GameState state, string id, bool moveDone = false)
    {
        state.Round = 1;
        var unit = state.Units.Single(u => u.Id == id);
        state.ActiveToken = new(unit.TypeId, unit.SideId); state.CurrentUnitId = id; state.MoveDone = moveDone;
        state.Pending = new(DecisionKind.Activation, unit.TypeId, id, [], false) { Token = state.ActiveToken };
    }
    private sealed class Pick(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Random(bool hits = false) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => hits ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }

    [Fact]
    public void InitialExplorationUsesOwnUnitsAndDoorConcealsOtherRoom()
    {
        var state = Dungeon();
        Assert.True(Known(state, 3, 4)); Assert.True(Known(state, 5, 4));
        Assert.False(Known(state, 6, 4)); Assert.False(FogOfWar.IsUnitObservable(state, "explorers", "guard"));
        Assert.True(FogOfWar.IsUnitObservable(state, "dungeon", "hero"));
    }

    [Fact]
    public void OpeningDoorExploresAndClosingItDoesNotForget()
    {
        var state = Dungeon(); Relocate(state, "hero", new(5, 4)); Ready(state, "hero");
        var result = GameEngine.Advance(state, new Pick("open-door:5,4:6,4"), new Random(), false);
        Assert.True(Known(result.State, 8, 4));
        Assert.True(FogOfWar.IsUnitObservable(result.State, "explorers", "guard"));
        var door = result.State.Physical.Board.Edges.FindIndex(e => e.Kind == EdgeKind.OpenDoor);
        result.State.Physical.Board.Edges[door] = result.State.Physical.Board.Edges[door] with { Kind = EdgeKind.ClosedDoor };
        Relocate(result.State, "hero", new(0, 0)); FogOfWar.Refresh(result.State);
        Assert.True(Known(result.State, 8, 4));
        Assert.False(Known(state, 8, 4));
    }

    [Fact]
    public void LyingUnitsSeeAndFiguresDoNotBlockVision()
    {
        var state = Dungeon();
        state.Physical.Board.Edges.Clear();
        Relocate(state, "guard", new(4, 4));
        var index = state.Physical.Figures.FindIndex(f => f.Id == "hero");
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Posture = Posture.Lying };
        FogOfWar.Refresh(state);
        Assert.True(Known(state, 8, 4)); Assert.False(Known(state, 9, 4));
    }

    [Theory]
    [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.SolidRock)]
    public void BlockingDestinationDiscoveredButTerrainBehindItIsHidden(TerrainKind terrain)
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Physical.Board.Terrain.Add(new(new(4, 4), terrain));
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.True(Known(state, 4, 4)); Assert.False(Known(state, 5, 4));
        state.Physical.Board.Terrain.Clear();
        state.Physical.Board.Machines.Add(new("press", new(4, 4), MachineTypes.DiamondPress));
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.True(Known(state, 4, 4)); Assert.False(Known(state, 5, 4));
    }

    [Theory]
    [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.SolidRock)]
    public void PhaseVisionIgnoresWallsAndDoorsButNotTreesOrAttackLos(TerrainKind terrain)
    {
        var state = Dungeon();
        state.Types[0] = state.Types[0] with { Phase = new() };
        FogOfWar.Refresh(state); Assert.True(Known(state, 8, 4));
        Assert.False(AttackRules.HasUnitLineOfSight(state, "hero", new(3, 4), new(8, 4), "guard"));
        state.Physical.Board.Terrain.Add(new(new(4, 4), terrain));
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.True(Known(state, 4, 4)); Assert.False(Known(state, 8, 4));
    }

    [Fact]
    public void SightUsesEffectiveRangeAndEveryFootprintCell()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Types[0] = state.Types[0] with { Footprint = Footprint.TwoByTwo, Rng = 4 };
        state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.True(Known(state, 11, 5)); Assert.False(Known(state, 12, 5));
        Ready(state, "hero"); state.ModifiersThisTurn.Add(new(Stat.Rng, 2)); FogOfWar.Refresh(state);
        Assert.True(Known(state, 13, 5));
        state.ModifiersThisTurn.Clear(); FogOfWar.Refresh(state); Assert.True(Known(state, 13, 5));
    }

    [Fact]
    public void CreationRefreshesVisionAndRestartResetsIt()
    {
        var state = Dungeon();
        state.PlaceUnit(UnitTypeIds.Grunt, "scout", "explorers", new(12, 0));
        Assert.True(Known(state, 12, 0)); Assert.False(Known(Dungeon(), 12, 0));
    }

    [Fact]
    public void CopySerializationAndScenarioTransportKeepIndependentExploration()
    {
        var state = Dungeon(); var copy = state.Copy(); copy.ExploredCells["explorers"].Add(new(19, 8));
        Assert.False(Known(state, 19, 8));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(copy))!;
        Assert.True(Known(restored, 19, 8));
        var definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(FogOfWarFixture.SleepingDungeon()));
        Assert.Equal(["explorers"], definition.FogOfWarSides);
        Assert.DoesNotContain("exploredCells", ScenarioDefinitionJson.ToJson(definition));
        Assert.False(Known(GameEngine.CreateGame(definition), 19, 8));
    }

    [Fact]
    public void PathsRejectUnknownTransitEvenWhenDestinationKnownAndPhaseEnabled()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Types[0] = state.Types[0] with { Phase = new() };
        state.ExploredCells["explorers"] = [new(3, 4), new(5, 4)];
        Assert.DoesNotContain(new Cell(5, 4), MovementRules.FindPaths(state, "hero", new(3, 4)).Keys);
        state.ExploredCells["explorers"].UnionWith([new(3, 3), new(4, 3), new(5, 3)]);
        var path = MovementRules.FindPaths(state, "hero", new(3, 4))[new(5, 4)];
        Assert.Equal(5, path.Count); Assert.DoesNotContain(new Cell(4, 4), path);
    }

    [Fact]
    public void EntireFootprintMustBeKnownAndLeapHasNoIntermediateTransit()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Types[0] = state.Types[0] with { Footprint = Footprint.TwoByTwo };
        state.ExploredCells["explorers"] = [new(3, 4), new(4, 4), new(3, 5), new(4, 5), new(5, 4)];
        Assert.DoesNotContain(new Cell(4, 4), MovementRules.FindPaths(state, "hero", new(3, 4)).Keys);
        state.Types[0] = state.Types[0] with { Footprint = Footprint.OneByOne, MovementPattern = MovementPattern.LShape };
        state.ExploredCells["explorers"] = [new(3, 4), new(5, 5)];
        Assert.Equal([new Cell(3, 4), new(5, 5)], MovementRules.FindPaths(state, "hero", new(3, 4))[new(5, 5)]);
    }

    [Fact]
    public void SweptSightIncludesCanonicalTransitAndDoesNotMutateHypotheticalQueries()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        var before = new HashSet<Cell>(state.ExploredCells["explorers"]);
        _ = new GameplayQueries(state).MovementPathsFrom("hero", new(4, 4));
        Assert.True(before.SetEquals(state.ExploredCells["explorers"]));
        FogOfWar.Sweep(state, "hero", [new(3, 4), new(3, 3), new(4, 3), new(4, 4)]);
        Assert.True(Known(state, 4, 0));
        Assert.Equal(new(3, 4), state.Physical.Figures.Single(f => f.Id == "hero").Position);
    }

    [Theory]
    [InlineData(ControllerKind.Human)] [InlineData(ControllerKind.Automated)]
    public void LegalDecisionsAndBehaviorRespectSameKnownSpace(ControllerKind controller)
    {
        var state = GameEngine.CreateGame(FogOfWarFixture.SleepingDungeon(controller: controller));
        Ready(state, "hero");
        var result = GameEngine.RefreshChoices(state, new Random(), false);
        Assert.All(result.NextInput!.Candidates.Where(c => c.Path is not null), c =>
            Assert.All(c.Path!, cell => Assert.True(FogOfWar.IsExplored(state, "explorers", cell))));
        Assert.Empty(new GameplayQueries(result.State).EligibleAttackOrderTargets("hero", OrderTarget.ForSide("dungeon")));
        var choice = new OrderAwareAutomatedProvider().Choose(result.NextInput, new GameplayQueries(result.State));
        Assert.Contains(result.NextInput.Candidates, c => c.Key == choice);
        state.AssignOrder(state.ActiveToken!, OrderKind.MoveTo, new(19, 8));
        Assert.Null(new GameplayQueries(state).RouteDistanceTo("hero", new(3, 4), new(19, 8)));
    }

    [Fact]
    public void DistantAttackDiscoversTrajectoryEvenOnMissButHiddenCombatDoesNot()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Types.Add(new("longbow", 1, 20, 1, 0, 1));
        state.Units.Add(state.Types[^1].CreateUnit("sniper", "dungeon"));
        state.Physical.Figures.Add(new("sniper", new(18, 4)));
        state.Controllers.Add(new(new("longbow", "dungeon"), ControllerKind.Automated));
        Ready(state, "sniper", true);
        var result = GameEngine.Advance(state, new Pick("attack:hero"), new Random(), false);
        Assert.True(Known(result.State, 18, 4)); Assert.True(Known(result.State, 12, 4));
        Assert.False(Known(state, 12, 4));
        var finished = result;
        while (finished.NextInput?.Kind == DecisionKind.RollDice)
            finished = GameEngine.Advance(finished.State, new Pick("roll-dice"), new Random(), false);
        Assert.Equal(5, finished.State.Units.Single(u => u.Id == "hero").CurrentHp);
        Assert.True(Known(finished.State, 18, 4));
        var hidden = Dungeon(); hidden.Physical.Board.Edges.Clear();
        FogOfWar.DiscoverAttack(hidden, new("archer", "attack", null, null, [new("sleeper", 1)], 1));
        Assert.False(Known(hidden, 15, 6));
    }

    [Fact]
    public void ConfigurationValidatesSidesAndDisabledScenarioHasNoExploration()
    {
        var definition = FogOfWarFixture.SleepingDungeon();
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { FogOfWarSides = ["missing"] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { FogOfWarSides = ["explorers", "explorers"] }));
        Assert.Empty(GameEngine.CreateGame(FogOfWarFixture.SleepingDungeon(false)).ExploredCells);
    }

    [Fact]
    public void CanonicalMoveSweepsSideAlcoveWithoutExtraDecisionsOrIntermediateMovementEvents()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        // A Cell offset from the midpoint is outside both endpoints' sight diamonds.
        state.Types[0] = state.Types[0] with { Mov = 8 };
        state.ExploredCells["explorers"] = Enumerable.Range(0, 8).Select(x => new Cell(x, 4)).ToHashSet();
        Ready(state, "hero");
        var result = GameEngine.Advance(state, new Pick("7,4"), new Random(), false);
        var movement = Assert.Single(result.Events.Where(e => e.Kind == "MovementCompleted"));
        Assert.Equal([new Cell(3, 4), new(4, 4), new(5, 4), new(6, 4), new(7, 4)], movement.Path);
        Assert.False(Known(state, 5, 0));
        Assert.True(Known(result.State, 5, 0));
        Assert.Equal(new(7, 4), result.State.Physical.Figures.Single(f => f.Id == "hero").Position);
        Assert.Equal(1, result.State.MovesCompletedThisActivation);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
    }

    [Fact]
    public void HiddenMonstersMoveNormallyAndBecomeObservableWhenEnteringExploredCells()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        Ready(state, "guard");
        var choices = GameEngine.RefreshChoices(state, new Random(), false);
        Assert.Contains(choices.NextInput!.Candidates, c => c.Destination == new Cell(5, 4));
        var moved = GameEngine.Advance(choices.State, new Pick("5,4"), new Random(), false);
        Assert.True(FogOfWar.IsUnitObservable(moved.State, "explorers", "guard"));
        Assert.Contains(moved.Events, e => e.Kind == "MovementCompleted" && e.UnitId == "guard");
    }

    [Fact]
    public void FireballPreviewOmitsHiddenRecipientButResolvesFullWorldAfterSerializedContinuation()
    {
        var definition = Scenario.Define(Scenario.Map(10, 8, cells: [Scenario.Tile(4, 4, TerrainKind.Tree)]),
            [Scenario.Group(UnitTypeIds.Wizard, "explorers", ControllerKind.Human, Scenario.At(3, 4, unitId: "wizard")),
             Scenario.Group(UnitTypeIds.Grunt, "dungeon", ControllerKind.Automated, Scenario.At(5, 4, unitId: "hidden"))]) with
            { FogOfWarSides = ["explorers"] };
        var state = GameEngine.CreateGame(definition); Ready(state, "wizard", true);
        Assert.False(FogOfWar.IsUnitObservable(state, "explorers", "hidden"));
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        var fireball = ready.NextInput!.Candidates.Single(c => c.Key == "fireball:4,3");
        Assert.DoesNotContain("hidden", fireball.TargetIds);
        Assert.Equal(new[] { "wizard" }, fireball.TargetIds);
        Assert.False(fireball.Relevant); // A hidden enemy must not make a self-only preview relevant.
        var revealed = ready.State.Copy();
        revealed.ExploredCells["explorers"].Add(new(5, 4));
        Assert.True(GameEngine.RefreshChoices(revealed, new Random(), false).NextInput!.Candidates
            .Single(c => c.Key == fireball.Key).Relevant);
        var continued = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(ready.State))!;
        var attack = GameEngine.Advance(continued, new Pick(fireball.Key), new Random(), false);
        Assert.Contains(attack.State.AttackInProgress!.Targets, t => t.TargetId == "hidden");
        Assert.DoesNotContain("hidden", attack.NextInput!.Roll!.TargetIds);
        Assert.False(FogOfWar.IsUnitObservable(attack.State, "explorers", "hidden"));
        Assert.False(FogOfWar.IsUnitObservable(ready.State, "explorers", "hidden"));
        var events = new List<RulesEvent>();
        while (attack.NextInput?.Kind == DecisionKind.RollDice)
        {
            attack = GameEngine.Advance(attack.State, new Pick("roll-dice"), new Random(true), false);
            events.AddRange(attack.Events);
        }
        Assert.Contains(events, e => e.Kind == "UnitDefeated" && e.UnitId == "hidden");
        Assert.DoesNotContain(attack.State.Physical.Figures, f => f.Id == "hidden");
    }

    [Fact]
    public void UnknownExplicitTargetsAreRejectedEvenWithLongerAbilityRange()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Types[0] = state.Types[0] with { Rng = 0, ShootShell = new(1, 20, 1), Actions = UnitAction.ShootShell,
            ResourceStorage = new([new(ResourceTypes.Shell, 1)]) };
        state.StoredResources.Add(new(UnitTypeIds.Barbarian, "explorers", ResourceTypes.Shell, 1));
        Ready(state, "hero", true);
        var result = GameEngine.RefreshChoices(state, new Random(), false);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.TargetId == "sleeper");
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(result.State, new Pick("shoot-shell:sleeper"), new Random(), false));
    }

    [Fact]
    public void HiddenTokenStillDrawsAndLyingUnitStandsWithoutExplorersSeeingItsActivation()
    {
        var state = Dungeon();
        state.Types = [.. state.Types.OrderBy(t => t.Id == UnitTypeIds.Grunt ? 0 : 1)];
        var result = GameEngine.StartRound(state, new Random(), false);
        Assert.Equal(new(UnitTypeIds.Grunt, "dungeon"), result.State.ActiveToken);
        var standing = GameEngine.Advance(result.State, new Pick("sleeper"), new Random(), false);
        Assert.Equal(Posture.Upright, standing.State.Physical.Figures.Single(f => f.Id == "sleeper").Posture);
        Assert.Contains(standing.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "sleeper");
        Assert.All(standing.Events.Where(e => e.UnitId == "sleeper"), e =>
            Assert.Null(FogOfWar.ObserveEvent(standing.ResolutionSteps[standing.Events.IndexOf(e)].StateAfter, "explorers", e)));
    }

    [Fact]
    public void RangeBonusIsRelevantForExplorationAndItsSightSurvivesTheModifierEnding()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Types[0] = state.Types[0] with { Rng = 4,
            BonusActions = [new("Scout", null, [new(Stat.Rng, 2)])] };
        state.ExploredCells.Clear(); FogOfWar.Refresh(state); Ready(state, "hero");
        Assert.False(Known(state, 12, 4));
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        Assert.True(ready.NextInput!.Candidates.Single(c => c.Key == "bonus-action:Scout").Relevant);
        Assert.False(Known(state, 12, 4));
        var scouted = GameEngine.Advance(ready.State, new Pick("bonus-action:Scout"), new Random(), false);
        Assert.True(Known(scouted.State, 12, 4));
        scouted.State.ModifiersThisTurn.Clear(); FogOfWar.Refresh(scouted.State);
        Assert.True(Known(scouted.State, 12, 4));
    }

    [Fact]
    public void WorkerHasNoUnknownResourceOrNavigationPrivilege()
    {
        var definition = Scenario.Define(Scenario.Map(20, 3),
            [Scenario.Group(UnitTypeIds.Worker, "explorers", ControllerKind.Automated, Scenario.At(1, 1, unitId: "worker")),
             Scenario.Group(UnitTypeIds.Grunt, "dungeon", ControllerKind.Automated, Scenario.At(18, 1))]) with
        { FogOfWarSides = ["explorers"], Resources = [new(ResourceTypes.Diamond, new(17, 1))] };
        var state = GameEngine.CreateGame(definition); Ready(state, "worker");
        var result = GameEngine.RefreshChoices(state, new Random(), false);
        var queries = new GameplayQueries(result.State);
        Assert.Empty(queries.Resources); Assert.Null(queries.RouteDistanceTo("worker", new(1, 1), new(17, 1)));
        Assert.Equal("stay", new OrderAwareAutomatedProvider().Choose(result.NextInput!, queries));
        Assert.All(result.NextInput!.Candidates.Where(c => c.Path is not null), c => Assert.All(c.Path!, p =>
            Assert.True(FogOfWar.IsExplored(state, "explorers", p))));
    }

    [Fact]
    public void TwoEnabledSidesMaintainSeparateExploration()
    {
        var definition = FogOfWarFixture.SleepingDungeon() with { FogOfWarSides = ["explorers", "dungeon"] };
        var state = GameEngine.CreateGame(definition);
        Assert.False(FogOfWar.IsUnitObservable(state, "explorers", "guard"));
        Assert.False(FogOfWar.IsUnitObservable(state, "dungeon", "hero"));
        Assert.True(FogOfWar.IsUnitObservable(state, "dungeon", "guard"));
        state.ExploredCells["explorers"].Add(new(19, 8));
        Assert.DoesNotContain(new Cell(0, 0), state.ExploredCells["dungeon"]);
        Assert.Equal("explorers", FogOfWar.ViewingSide(state));
    }

    [Fact]
    public void WorldOpeningAndClosingDoorRefreshesAtEachEventAndKeepsExploration()
    {
        var state = Dungeon(); Relocate(state, "hero", new(5, 4));
        state.WorldEffects = new(2, 1);
        state.WorldDeck = new() { DrawPile = [new("open", WorldEffect.OpenSesame), new("close", WorldEffect.Lockdown)] };
        // Skip the first-round shuffle so this test owns the concrete draw order.
        state.Round = 1; state.RoundComplete = true;
        var result = GameEngine.StartRound(state, new Random(), false);
        var opened = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "DoorOpened");
        var closed = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "DoorClosed");
        Assert.True(Known(opened.StateAfter, 8, 4)); Assert.True(Known(closed.StateAfter, 8, 4));
        Assert.Equal(EdgeKind.ClosedDoor, result.State.Physical.Board.EdgeBetween(new(5, 4), new(6, 4)));
        Assert.True(Known(result.State, 8, 4)); Assert.False(Known(state, 8, 4));
    }

    [Fact]
    public void SwapRefreshesVisionAtItsAtomicResult()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        Relocate(state, "guard", new(4, 4));
        state.Types[0] = state.Types[0] with { BonusActions = [new("Swap", null, []) { Swap = new() }] };
        Ready(state, "hero", true); FogOfWar.Refresh(state);
        Assert.False(Known(state, 9, 4));
        var result = GameEngine.Advance(state, new Pick("bonus-action:Swap:guard"), new Random(), false);
        var step = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "PlacesSwapped");
        Assert.Equal(new(4, 4), step.StateAfter.Physical.Figures.Single(f => f.Id == "hero").Position);
        Assert.Equal(new(3, 4), step.StateAfter.Physical.Figures.Single(f => f.Id == "guard").Position);
        Assert.True(Known(step.StateAfter, 9, 4));
        Assert.DoesNotContain(result.Events, e => e.Kind == "MovementCompleted");
    }

    [Fact]
    public void AdvisoryBehaviorPreviewStopsBeforeHypotheticalDiscoveryCanRevealItsNextAttackTarget()
    {
        var state = Dungeon(); state.Physical.Board.Edges.Clear();
        state.Physical.Board.Terrain.Add(new(new(4, 4), TerrainKind.Tree));
        Relocate(state, "guard", new(5, 4)); state.ExploredCells.Clear(); FogOfWar.Refresh(state);
        Assert.False(FogOfWar.IsUnitObservable(state, "explorers", "guard"));
        Ready(state, "hero"); state.AssignOrder(state.ActiveToken!, OrderKind.MoveTo, new(4, 3));
        var ready = GameEngine.RefreshChoices(state, new Random(), false);
        var known = new HashSet<Cell>(ready.State.ExploredCells["explorers"]);
        var preview = GameEngine.PreviewDefaultBehaviorActivation(ready.State, "hero");
        Assert.Equal(DefaultBehaviorPreviewStopReason.ExplorationChanged, preview.StopReason);
        Assert.Single(preview.Steps);
        Assert.DoesNotContain(preview.Steps, s => s.IntendedAction.Contains("guard", StringComparison.Ordinal));
        Assert.True(known.SetEquals(ready.State.ExploredCells["explorers"]));
    }
}
