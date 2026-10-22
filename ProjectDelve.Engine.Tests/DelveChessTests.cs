using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DelveChessTests
{
    private sealed class Dice(bool hit = true) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => hit ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState State(UnitType? type = null, Cell? target = null)
    {
        type ??= UnitRoster.CastleGuard();
        var state = new GameState
        {
            Physical = new(new Board(7, 7, []), [new("m", new(3, 3)), new("t", target ?? new(4, 3)), new("reserve", new(6, 6))]),
            Types = [type, new("target", 1, 1, 1, 0, 1)],
            Units = [type.CreateUnit("m", "a"), new("t", "target", "b", 1), new("reserve", "target", "b", 1)],
            Objectives = [new("a", ScenarioObjective.NoObjective), new("b", ScenarioObjective.NoObjective)],
            Round = 1, CurrentUnitId = "m", ActiveToken = new(type.Id, "a")
        };
        TestGame.Author(state);
        return state;
    }
    private static IReadOnlyDictionary<Cell, IReadOnlyList<Cell>> Paths(GameState state, int? max = null) =>
        MovementRules.FindPaths(state, "m", new(3, 3), max ?? state.EffectiveMovOf("m"));
    private static EngineResult Choices(GameState state) => GameEngine.RefreshChoices(state, new Dice(), false);
    private static EngineResult Attack(GameState state, bool hit = true, UnitAction action = UnitAction.NormalAttack)
    {
        state.MoveDone = true;
        var pending = Choices(state);
        var candidate = pending.NextInput!.Candidates.First(c => c.Action == action &&
            (c.TargetId == "t" || action == UnitAction.Fireball && c.TargetIds.Contains("t") && !c.TargetIds.Contains("m")));
        return TestGame.Advance(pending.State, new Choice(candidate.Key), new Dice(hit), false);
    }

    [Theory]
    [InlineData(3, 0)] [InlineData(0, 3)] [InlineData(3, 6)] [InlineData(6, 3)]
    public void StraightCardinalPathsAndStay(int x, int y)
    {
        var state = State(target: new(5, 5));
        var paths = Paths(state);
        Assert.Equal(new Cell(3, 3), Assert.Single(paths[new(3, 3)]));
        var path = paths[new(x, y)];
        Assert.Equal(4, path.Count);
        Assert.All(path, c => Assert.True(c.X == 3 || c.Y == 3));
        Assert.DoesNotContain(new Cell(4, 4), paths.Keys);
    }

    [Fact]
    public void EffectiveMovAndNoTurningOrDetouring()
    {
        var state = State(target: new(5, 5));
        state.Types[0] = state.Types[0] with { Mov = 1 };
        Assert.DoesNotContain(new Cell(3, 1), Paths(state).Keys);
        state.ModifiersThisTurn.Add(new(Stat.Mov, 1));
        Assert.Contains(new Cell(3, 1), Paths(state).Keys);
        state.Physical.Board.Edges.Add(new(new(3, 3), new(3, 2), EdgeKind.Wall));
        Assert.DoesNotContain(new Cell(3, 1), Paths(state).Keys);
        Assert.Contains(new Cell(3, 1), MovementRules.FindPaths(state, "m", new(3, 3), 4, ignoreMovementPattern: true).Keys);
    }

    [Fact]
    public void ExecutionRejectsOrdinaryTurningDestination()
    {
        var pending = Choices(State(target: new(5, 5)));
        Assert.Throws<ArgumentException>(() => TestGame.Advance(pending.State, new Choice("4,4"), new Dice(), false));
        Assert.Equal(new Cell(3, 3), pending.State.Physical.Figures.Single(f => f.Id == "m").Position);
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)] [InlineData(EdgeKind.WallWithWindow, false)]
    [InlineData(EdgeKind.ClosedDoor, false)] [InlineData(EdgeKind.OpenDoor, true)]
    public void StraightMovementRespectsEdges(EdgeKind edge, bool passable)
    {
        var state = State(target: new(5, 5));
        state.Physical.Board.Edges.Add(new(new(3, 3), new(3, 2), edge));
        Assert.Equal(passable, Paths(state).ContainsKey(new(3, 1)));
    }

    [Theory]
    [InlineData("a", true)] [InlineData("b", false)]
    public void StraightFriendlyPassThroughAndHostileBlocking(string side, bool passable)
    {
        var state = State(target: new(5, 5));
        state.Units.Add(new("blocker", "target", side, 1));
        state.Physical.Figures.Add(new("blocker", new(3, 2)));
        Assert.DoesNotContain(new Cell(3, 2), Paths(state).Keys);
        Assert.Equal(passable, Paths(state).ContainsKey(new(3, 1)));
    }

    [Fact]
    public void StraightTerrainResourcesAndPeel()
    {
        var state = State(target: new(5, 5));
        state.PlaceResource(ResourceTypes.Coal, new(3, 2));
        Assert.Contains(new Cell(3, 1), Paths(state).Keys);
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(3, 2));
        Assert.Contains(new Cell(3, 2), Paths(state).Keys);
        Assert.DoesNotContain(new Cell(3, 1), Paths(state).Keys);
        state.Physical.Board.Terrain.Add(new(new(2, 3), TerrainKind.Water));
        Assert.DoesNotContain(new Cell(1, 3), Paths(state).Keys);
    }

    [Fact]
    public void AdditionalOrdinaryMoveMayChooseNewDirectionAndBehaviorUsesLegalCandidates()
    {
        var state = State(target: new(5, 5));
        state.MoveOpportunityModifierThisActivation = 1;
        var first = Choices(state);
        var second = TestGame.Advance(first.State, new Choice("3,2"), new Dice(), false);
        Assert.False(second.State.MoveDone);
        Assert.Contains(second.NextInput!.Candidates, c => c.Destination == new Cell(5, 2));
        Assert.DoesNotContain(second.NextInput.Candidates, c => c.Destination == new Cell(5, 3));
        var chosen = new DefaultAutomatedProvider().Choose(second.NextInput, new GameplayQueries(second.State));
        Assert.Contains(second.NextInput.Candidates, c => c.Key == chosen);
    }

    [Fact]
    public void MushroomBoostGrantsAnotherRestrictedMove()
    {
        var type = UnitType.Define("boost-rook", "Boost Rook", UnitAuthoring.Stats(2, 1, 3, 4, 1),
            UnitAuthoring.StraightOrthogonal(), UnitAuthoring.ResourceStorage(ResourceTypes.Mushroom, 1), UnitAuthoring.Boost(1));
        var state = State(type, new(5, 5));
        state.StoredResources.Add(new("a", type.Id, ResourceTypes.Mushroom, 1));
        var moved = TestGame.Advance(Choices(state).State, new Choice("3,2"), new Dice(), false);
        var boost = moved.NextInput!.Candidates.Single(c => c.BonusAction?.Boost is not null);
        var boosted = TestGame.Advance(moved.State, new Choice(boost.Key), new Dice(), false);
        Assert.False(boosted.State.MoveDone);
        Assert.Equal(0, boosted.State.StoredQuantity("a", type.Id, ResourceTypes.Mushroom.Id));
        Assert.Contains(boosted.NextInput!.Candidates, c => c.Destination == new Cell(5, 2));
        Assert.DoesNotContain(boosted.NextInput.Candidates, c => c.Destination == new Cell(4, 3));
    }

    [Fact]
    public void CustomMovementPatternSurvivesScenarioSerialization()
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 7 }, UnitTypeIds = ["custom"],
            UnitTypes = [UnitRoster.CastleGuard("custom")],
            Units = [new("custom", "a", new(3, 3))],
            Agency = [new("custom", "a", ControllerKind.Human)],
            Objectives = [new("a", ScenarioObjective.NoObjective)]
        };
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
        Assert.Equal(MovementPattern.StraightOrthogonal, state.Types.Single().MovementPattern);
        Assert.NotNull(state.Types.Single().AdvanceOnKill);
        var paths = MovementRules.FindPaths(state, state.Units.Single().Id, new(3, 3), 16);
        Assert.Contains(new Cell(3, 0), paths.Keys);
        Assert.DoesNotContain(new Cell(4, 4), paths.Keys);
    }

    [Fact]
    public void KillOffersOnlyFormerCellAndStayThenClosesOrdinaryOpportunities()
    {
        var result = Attack(State());
        Assert.Equal(DecisionKind.Move, result.NextInput!.Kind);
        Assert.True(result.NextInput.IsMoveAfterAttack);
        Assert.True(result.NextInput.AllowsNone);
        Assert.Equal(new Cell(4, 3), Assert.Single(result.NextInput.Candidates).Destination);
        Assert.Contains(result.Events, e => e.Kind == "UnitDefeated" && e.UnitId == "t");
        var before = JsonSerializer.Serialize(result.State);
        var advanced = TestGame.Advance(result.State, new Choice("4,3"), new Dice(), false);
        var movement = Assert.Single(advanced.Events, e => e.Kind == "MovementCompleted");
        Assert.True(movement.IsMoveAfterAttack);
        Assert.Equal(new[] { new Cell(3, 3), new Cell(4, 3) }, movement.Path);
        Assert.Equal(1, advanced.ResolutionSteps[advanced.Events.IndexOf(movement)].StateAfter.MovesCompletedThisActivation);
        Assert.Equal(new Cell(4, 3), advanced.State.Physical.Figures.Single(f => f.Id == "m").Position);
        Assert.Contains(advanced.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "m");
        Assert.Equal(before, JsonSerializer.Serialize(result.State));
        var replay = TestGame.Advance(JsonSerializer.Deserialize<GameState>(before)!, new Choice("4,3"), new Dice(), false);
        Assert.Equal(JsonSerializer.Serialize(advanced.Events), JsonSerializer.Serialize(replay.Events));
        Assert.Equal(JsonSerializer.Serialize(advanced.State), JsonSerializer.Serialize(replay.State));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, true)]
    public void MissOrPreventedDefeatDoesNotAdvance(bool hit, bool undying)
    {
        var state = State();
        if (undying) state.Types[1] = state.Types[1] with { Undying = new() };
        var result = Attack(state, hit);
        Assert.NotEqual(DecisionKind.Move, result.NextInput?.Kind);
        Assert.Null(result.State.AdvanceOnKillCells);
        Assert.DoesNotContain(result.Events, e => e.Kind == "UnitDefeated");
    }

    [Fact]
    public void NonlethalAttackDoesNotAdvance()
    {
        var state = State();
        state.Types[1] = state.Types[1] with { Hp = 10 };
        state.Units[1] = state.Units[1] with { CurrentHp = 10 };
        Assert.NotEqual(DecisionKind.Move, Attack(state).NextInput?.Kind);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DiagonalAdvanceUsesEitherLegalIntermediateAndIgnoresMovAndPattern(bool blockNorth)
    {
        var state = State(UnitRoster.CastleGuard() with { Mov = 0 }, new(4, 2));
        var blocked = blockNorth ? new Cell(3, 2) : new Cell(4, 3);
        state.Physical.Board.Edges.Add(new(new(3, 3), blocked, EdgeKind.WallWithWindow));
        var result = Attack(state);
        var move = Assert.Single(result.NextInput!.Candidates);
        Assert.Equal(new Cell(4, 2), move.Destination);
        Assert.Equal(3, move.Path!.Count);
        Assert.DoesNotContain(blocked, move.Path);
    }

    [Fact]
    public void BothDiagonalPathsBlockedStillOffersStayAndNeverDetours()
    {
        var state = State(target: new(4, 2));
        state.Physical.Board.Edges.AddRange([
            new(new(3, 3), new(3, 2), EdgeKind.WallWithWindow),
            new(new(3, 3), new(4, 3), EdgeKind.WallWithWindow)]);
        var result = Attack(state);
        // With Stay the only legal choice the existing forced-choice convention resolves it.
        Assert.Equal(new Cell(3, 3), Assert.Single(result.Events, e => e.Kind == "MovementCompleted").Path!.Single());
        Assert.Null(result.State.AdvanceOnKillCells);
    }

    [Fact]
    public void WindowAllowsAttackButBlocksOrthogonalAdvance()
    {
        var state = State();
        state.Physical.Board.Edges.Add(new(new(3, 3), new(4, 3), EdgeKind.WallWithWindow));
        Assert.Equal(new Cell(3, 3), Assert.Single(Attack(state).Events, e => e.Kind == "MovementCompleted").Path!.Single());
    }

    [Fact]
    public void DecliningReachableAdvanceStaysAndClearsContinuation()
    {
        var result = Attack(State());
        var stayed = TestGame.Advance(result.State, new Choice(null), new Dice(), false);
        Assert.Equal(new Cell(3, 3), Assert.Single(stayed.Events, e => e.Kind == "MovementCompleted").Path!.Single());
        Assert.Null(stayed.State.AdvanceOnKillCells);
        Assert.Null(stayed.State.MoveAfterAttackAllowance);
        Assert.Contains(stayed.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "m");
    }

    [Fact]
    public void AdvanceResolvesBeforeNextGroupUnitAndDoesNotReopenNormalActions()
    {
        var state = State();
        state.Units.Add(state.Types[0].CreateUnit("friend", "a"));
        state.Physical.Figures.Add(new("friend", new(0, 0)));
        state.WorldEffects = new(1, 1);
        state.WorldDeck = new() { ActiveContinuous = [new("frenzy", WorldEffect.Frenzy)] };
        var result = Attack(state);
        Assert.Equal("m", result.NextInput!.UnitId);
        Assert.Equal(DecisionKind.Move, result.NextInput.Kind);
        Assert.Equal(1, result.State.ActionsRemaining);
        var moved = TestGame.Advance(result.State, new Choice("4,3"), new Dice(), false);
        var movementIndex = moved.Events.FindIndex(e => e.Kind == "MovementCompleted");
        Assert.Equal(0, moved.ResolutionSteps[movementIndex].StateAfter.ActionsRemaining);
        var completedIndex = moved.Events.FindIndex(e => e.Kind == "ActivationCompleted" && e.UnitId == "m");
        Assert.True(movementIndex < completedIndex);
        Assert.Equal("friend", moved.NextInput!.UnitId);
        Assert.Equal(ActivationChoiceKind.Move, moved.NextInput.Candidates.First(c => c.Kind == ActivationChoiceKind.Move).Kind);
    }

    [Fact]
    public void PeelInIntermediateCellBlocksDiagonalPassage()
    {
        var state = State(target: new(4, 2));
        state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(3, 2));
        state.Physical.Board.Edges.Add(new(new(3, 3), new(4, 3), EdgeKind.WallWithWindow));
        var result = Attack(state);
        Assert.Single(result.Events, e => e.Kind == "MovementCompleted" && e.Path!.Count == 1);
        Assert.DoesNotContain(result.Events, e => e.Kind == "PostureChanged");
    }

    [Fact]
    public void DefeatingLyingUndyingTargetQualifiesAndExplosionsResolveBeforeAdvance()
    {
        var state = State();
        state.Types[1] = state.Types[1] with { Undying = new(), Explosion = new() { Damage = 0 } };
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Posture = Posture.Lying };
        var result = Attack(state);
        Assert.Equal(DecisionKind.Move, result.NextInput!.Kind);
        Assert.Contains(result.Events, e => e.Kind == "UnitDefeated" && e.UnitId == "t");
        Assert.Empty(result.State.PendingExplosions);
    }

    [Fact]
    public void AttackerDefeatedByExplosionDoesNotReceiveAdvance()
    {
        var state = State();
        state.Types[1] = state.Types[1] with { Explosion = new() { Damage = 1 } };
        var result = Attack(state);
        Assert.Contains(result.Events, e => e.Kind == "UnitDefeated" && e.UnitId == "m");
        Assert.Null(result.State.AdvanceOnKillCells);
        Assert.DoesNotContain(result.Events, e => e.Kind == "MovementCompleted");
    }

    [Theory]
    [InlineData("a", true)] [InlineData("b", false)]
    public void DiagonalIntermediateOccupancy(string side, bool passable)
    {
        var state = State(target: new(4, 2));
        state.Physical.Board.Edges.Add(new(new(3, 3), new(4, 3), EdgeKind.WallWithWindow));
        state.Units.Add(new("blocker", "target", side, 1));
        state.Physical.Figures.Add(new("blocker", new(3, 2)));
        var result = Attack(state);
        Assert.Equal(passable, result.NextInput?.Kind == DecisionKind.Move);
        if (!passable) Assert.Single(result.Events, e => e.Kind == "MovementCompleted");
    }

    [Fact]
    public void ArrivalOnPeelLaysDownAndCompletesActivationAfterMovement()
    {
        var result = Attack(State());
        // Inject a legal arrival hazard at the continuation boundary to exercise
        // the common arrival resolver independently of how the marker got there.
        result.State.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(4, 3));
        var moved = TestGame.Advance(result.State, new Choice("4,3"), new Dice(), false);
        Assert.Equal(Posture.Lying, moved.State.Physical.Figures.Single(f => f.Id == "m").Posture);
        var kinds = moved.Events.Select(e => e.Kind).ToList();
        Assert.True(kinds.IndexOf("MovementCompleted") < kinds.IndexOf("PostureChanged"));
        Assert.True(kinds.IndexOf("PostureChanged") < kinds.IndexOf("ActivationCompleted"));
    }

    [Theory]
    [InlineData(UnitAction.ClawAttack)] [InlineData(UnitAction.Fireball)]
    public void SpecialAttackKillsDoNotTriggerAdvance(UnitAction action)
    {
        var type = UnitRoster.CastleGuard() with { Rng = 3, Actions = UnitAction.NormalAttack | UnitAction.ClawAttack | UnitAction.Fireball,
            ClawAttack = new(), Fireball = new() };
        var result = Attack(State(type, action == UnitAction.Fireball ? new(5, 3) : new(4, 3)), action: action);
        Assert.Contains(result.Events, e => e.Kind == "UnitDefeated" && e.UnitId == "t");
        Assert.Null(result.State.AdvanceOnKillCells);
        Assert.NotEqual(DecisionKind.Move, result.NextInput?.Kind);
    }

    [Fact]
    public void RangedNormalAttackDoesNotTriggerAdvance()
    {
        var result = Attack(State(UnitRoster.CastleGuard() with { Rng = 2 }));
        Assert.Null(result.State.AdvanceOnKillCells);
    }

    [Fact]
    public void ShootShellKillDoesNotTriggerAdvance()
    {
        var type = UnitType.Define("shell-rook", "Shell Rook", UnitAuthoring.Stats(16, 1, 3, 4, 1),
            UnitAuthoring.StraightOrthogonal(), UnitAuthoring.AdvanceOnKill(),
            UnitAuthoring.ResourceStorage(ResourceTypes.Shell, 1), UnitAuthoring.ShootShell(1, 4, 3));
        var state = State(type);
        state.StoredResources.Add(new("a", type.Id, ResourceTypes.Shell, 1));
        var result = Attack(state, action: UnitAction.ShootShell);
        Assert.Contains(result.Events, e => e.Kind == "UnitDefeated" && e.UnitId == "t");
        Assert.Null(result.State.AdvanceOnKillCells);
        Assert.DoesNotContain(result.Events, e => e.Kind == "MovementCompleted");
    }

    [Fact]
    public void GoblinMoveAfterAttackStillExistsWithoutDefeatAndAllowsOrdinaryTurns()
    {
        var state = State(UnitRoster.Goblin() with { MoveAfterAttack = new(2) });
        var result = Attack(state, hit: false);
        Assert.Equal(DecisionKind.Move, result.NextInput!.Kind);
        Assert.Null(result.State.AdvanceOnKillCells);
        Assert.Contains(result.NextInput.Candidates, c => c.Destination == new Cell(2, 2));
    }

    [Fact]
    public void PeasantChoosesLowestEffectiveDefenseAndHumanKeepsEveryTarget()
    {
        var state = State(UnitRoster.RoyalPeasant());
        state.Types.Add(new("weak", 1, 1, 1, 1, 1));
        state.Types[1] = state.Types[1] with { Def = 3 };
        state.Units.Add(new("weak", "weak", "b", 1));
        state.Physical.Figures.Add(new("weak", new(2, 3)));
        state.MoveDone = true;
        var result = Choices(state);
        Assert.Equal(2, result.NextInput!.Candidates.Count(c => c.Action == UnitAction.NormalAttack));
        var provider = new DefaultAutomatedProvider();
        var selected = provider.Choose(result.NextInput, new GameplayQueries(result.State));
        Assert.Equal("weak", result.NextInput.Candidates.Single(c => c.Key == selected).TargetId);
        // A continuous DEF bonus changes the preferred target immediately.
        state.Types.Add(new("aura", 0, 0, 0, 0, 1) { AdjacentFriendlyUnitsDefenceBonus = new(4) });
        state.Units.Add(new("aura", "aura", "b", 1));
        state.Physical.Figures.Add(new("aura", new(1, 3)));
        result = Choices(state);
        selected = provider.Choose(result.NextInput!, new GameplayQueries(result.State));
        Assert.Equal("t", result.NextInput!.Candidates.Single(c => c.Key == selected).TargetId);
    }

    [Fact]
    public void PeasantEqualDefUsesExistingTieBreakersAndDoesNotChangeMovement()
    {
        var state = State(UnitRoster.RoyalPeasant());
        state.Units.Add(new("z", "target", "b", 1));
        state.Physical.Figures.Add(new("z", new(2, 3)));
        var provider = new DefaultAutomatedProvider();
        var ordinary = state.Copy();
        ordinary.Types[0] = ordinary.Types[0] with { Behaviors = UnitBehavior.None };
        Assert.Equal(provider.Choose(Choices(ordinary).NextInput!, new GameplayQueries(ordinary)),
            provider.Choose(Choices(state).NextInput!, new GameplayQueries(state)));
        state.MoveDone = ordinary.MoveDone = true;
        Assert.Equal(provider.Choose(Choices(ordinary).NextInput!, new GameplayQueries(ordinary)),
            provider.Choose(Choices(state).NextInput!, new GameplayQueries(state)));
        state.CurrentUnitId = "t";
        state.ModifiersThisTurn.Add(new(Stat.Def, 5));
        Assert.Equal(5, new GameplayQueries(state).EffectiveDefOf("t"));
        var request = new DecisionRequest(DecisionKind.Act, state.Types[0].Id, "m",
            [new("attack:t", Action: UnitAction.NormalAttack, TargetId: "t"),
             new("attack:z", Action: UnitAction.NormalAttack, TargetId: "z")], false);
        Assert.Equal("attack:z", provider.Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void DefensePreferencePreservesOtherAttackAbilityPriorities()
    {
        var state = State(UnitRoster.RoyalPeasant() with
            { Actions = UnitAction.NormalAttack | UnitAction.ClawAttack, ClawAttack = new(2) });
        state.MoveDone = true;
        var result = Choices(state);
        var chosen = new DefaultAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));
        Assert.Equal(UnitAction.ClawAttack, result.NextInput!.Candidates.Single(c => c.Key == chosen).Action);
    }

    [Theory]
    [InlineData(UnitTypeIds.RoyalPeasant, UnitTypeIds.GoblinPeasant)]
    [InlineData(UnitTypeIds.CastleGuard, UnitTypeIds.TowerTroll)]
    public void CounterpartsMatchAndWorkOnArbitrarySidesAndRoundTrip(string a, string b)
    {
        var first = CanonicalUnitTypes.Find(a)!;
        var second = CanonicalUnitTypes.Find(b)!;
        // Compare authored mechanics without the distinct presentation identities.
        Assert.Equal(JsonSerializer.Serialize(first with { Id = "same", DisplayName = "same" }),
            JsonSerializer.Serialize(second with { Id = "same", DisplayName = "same" }));
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 4, Height = 4 }, UnitTypeIds = [a, b],
            Units = [new(a, "arbitrary", new(0, 0)), new(b, "another", new(3, 3))],
            Agency = [new(a, "arbitrary", ControllerKind.Human), new(b, "another", ControllerKind.Automated)]
        };
        var loaded = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scenario)));
        Assert.Equal(2, loaded.Units.Count);
        Assert.Equal(first.MovementPattern, loaded.Types.Single(t => t.Id == a).MovementPattern);
    }
}
