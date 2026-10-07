using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DisplacerDemonTests
{
    private sealed class Dice(bool hits = false) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => hits ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => throw new InvalidOperationException("Unexpected D6 roll.");
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static GameState World(Cell? enemy = null, UnitType? actor = null, UnitType? target = null)
    {
        actor ??= UnitRoster.DisplacerDemon();
        target ??= UnitRoster.Grunt();
        return new()
        {
            Physical = new(new Board(8, 6, []), [new("demon", new(2, 2)), new("enemy", enemy ?? new(3, 2))]),
            Types = [actor, target], Units = [actor.CreateUnit("demon", "red"), target.CreateUnit("enemy", "blue")]
        };
    }
    private static void Add(GameState state, string id, UnitType type, Cell cell, string side, Posture posture = Posture.Upright)
    {
        if (!state.Types.Any(t => t.Id == type.Id)) state.Types.Add(type);
        state.Units.Add(type.CreateUnit(id, side));
        state.Physical.Figures.Add(new(id, cell, posture));
    }
    private static EngineResult Start(GameState? state = null)
    {
        var result = TestGame.StartRound(state ?? World(), new Dice(), false);
        return result.NextInput?.Kind == DecisionKind.SelectUnit ? Pick(result, "demon") : result;
    }
    private static EngineResult Pick(EngineResult result, string key, Dice? dice = null) =>
        TestGame.Advance(result.State, new Choice(key), dice ?? new Dice(), false);
    private static string AutoKey(EngineResult result) =>
        new DefaultAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State))!;
    private static IEnumerable<Candidate> SwapChoices(EngineResult result) => result.NextInput!.Candidates.Where(c => c.BonusAction?.Swap is not null);
    private static IEnumerable<Candidate> DisplaceChoices(EngineResult result) => result.NextInput!.Candidates.Where(c => c.BonusAction?.Displace is not null);
    private static Cell Position(GameState state, string id) => state.Physical.Figures.Single(f => f.Id == id).Position;
    private static EngineResult DisplaceDecision(GameState world)
    {
        var result = Start(world);
        result.State.MoveDone = true;
        result.State.ActionDone = true;
        result.State.BonusActionsUsedThisActivation.Add("Swap");
        return GameEngine.RefreshChoices(result.State, new Dice(), false);
    }

    [Fact]
    public void CanonicalContentHasStatsReadableCardsAndNoUnlimitedCounters()
    {
        var type = CanonicalUnitTypes.Find(UnitTypeIds.DisplacerDemon)!;
        Assert.Equal("Displacer Demon", type.DisplayName);
        Assert.Equal((3, 1, 4, 4, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.False(type.Unique);
        Assert.Equal(Footprint.OneByOne, type.Footprint);
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Equal(UnitBehavior.SwapThenAttackThenDisplace, type.Behaviors);
        Assert.Equal(new CardEntryDescription("bonus:Swap", "Swap", "Bonus Action", "Swap places with an adjacent enemy Unit."),
            Assert.Single(type.CardEntries(), e => e.Id == "bonus:Swap"));
        Assert.Equal(new CardEntryDescription("bonus:Displace", "Displace", "Bonus Action", "Move an adjacent enemy Unit up to 1 Cell."),
            Assert.Single(type.CardEntries(), e => e.Id == "bonus:Displace"));
        Assert.Equal(1, type.AdjacentFriendlyUnitsDefenceBonus!.Amount);
        var start = Start();
        Assert.Empty(start.State.Units[0].BonusActionUses);
        Assert.All(type.BonusActions, a => Assert.Null(type.UsesFor(start.State.Units[0], ContentDescriptions.BonusEntryId(a.Name))));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    public void SwapIsSimultaneousPreservesPostureAndDoesNotCompleteMoveOrAction(int x, int y)
    {
        var world = World(new(x, y));
        world.Physical.Figures[1] = world.Physical.Figures[1] with { Posture = Posture.Lying };
        var start = Start(world);
        var swapped = Pick(start, Assert.Single(SwapChoices(start)).Key);
        Assert.Equal(new(x, y), Position(swapped.State, "demon"));
        Assert.Equal(new(2, 2), Position(swapped.State, "enemy"));
        Assert.Equal(Posture.Lying, swapped.State.Physical.Figures[1].Posture);
        Assert.False(swapped.State.MoveDone);
        Assert.False(swapped.State.ActionDone);
        Assert.Null(swapped.State.MoveAfterAttackAllowance);
        Assert.False(swapped.State.CleavePending);
        Assert.Equal(start.State.Units, swapped.State.Units);
        Assert.Equal(new(2, 2), Position(start.State, "demon"));
        Assert.Empty(SwapChoices(swapped));
        Assert.NotEmpty(DisplaceChoices(swapped));
        Assert.DoesNotContain(swapped.Events, e => e.Kind is "MovementCompleted" or "AttackStarted" or "DiceRolled");
        var used = Assert.Single(swapped.Events, e => e.Kind == "ActionUsed");
        Assert.Equal(("bonus:Swap", ActivationChoiceKind.BonusAction, "enemy"), (used.ActionId, used.Category, used.TargetId));
        var occurrence = Assert.Single(swapped.Events, e => e.Kind == "PlacesSwapped");
        var snapshot = swapped.ResolutionSteps.Single(s => swapped.Events[s.EventIndex] == occurrence).StateAfter;
        Assert.Equal(new(x, y), Position(snapshot, "demon"));
        Assert.Equal(new(2, 2), Position(snapshot, "enemy"));
        Assert.Throws<ArgumentException>(() => Pick(swapped, "bonus-action:Swap:enemy"));
    }

    [Theory]
    [InlineData("large")]
    [InlineData("friendly")]
    [InlineData("removed")]
    [InlineData("distant")]
    [InlineData("wall")]
    [InlineData("closed-door")]
    public void ExternalTargetsMustBePlacedSmallAdjacentHostilesAndStaleChoicesAreRejected(string condition)
    {
        var start = Start();
        switch (condition)
        {
            case "large": start.State.Types[1] = start.State.Types[1] with { Footprint = Footprint.TwoByTwo }; break;
            case "friendly": start.State.Units[1] = start.State.Units[1] with { SideId = "red" }; break;
            case "removed": start.State.Units[1] = start.State.Units[1] with { CurrentHp = 0 }; start.State.Physical.Figures.RemoveAt(1); break;
            case "distant": start.State.Physical.Figures[1] = start.State.Physical.Figures[1] with { Position = new(5, 2) }; break;
            case "wall": start.State.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.Wall)); break;
            case "closed-door": start.State.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.ClosedDoor)); break;
        }
        var refreshed = GameEngine.RefreshChoices(start.State, new Dice(), false);
        Assert.Empty(SwapChoices(refreshed));
        Assert.Empty(DisplaceChoices(refreshed));
        Assert.Throws<ArgumentException>(() => Pick(start, "bonus-action:Swap:enemy"));
        Assert.Throws<ArgumentException>(() => Pick(start, "bonus-action:Displace:enemy:3,2"));
    }

    [Fact]
    public void LargeEnemiesRemainAttackableAndTheirOwnMovesRemainLegal()
    {
        var start = Start(World(target: UnitRoster.RedDragon()));
        Assert.Empty(SwapChoices(start));
        Assert.Empty(DisplaceChoices(start));
        Assert.Contains(MovementRules.FindPaths(start.State, "enemy", new(3, 2), 1).Keys, c => c == new Cell(4, 2));
        var afterMove = Pick(start, "stay");
        Assert.Contains(afterMove.NextInput!.Candidates, c => c.Key == "attack:enemy");
        // Restriction is on another Unit's effect, not on the large Unit's own ability.
        Assert.True(ExternalMovementRules.CanAffect(start.State, "enemy", "enemy"));
        Assert.False(ExternalMovementRules.CanAffect(start.State, "demon", "enemy"));
    }

    [Fact]
    public void LargeSourceCanDisplaceSmallEnemyButCannotUseTheOneCellSwapRule()
    {
        var start = Start(World(enemy: new(4, 2), actor: UnitRoster.DisplacerDemon() with { Footprint = Footprint.TwoByTwo }));
        Assert.Empty(SwapChoices(start));
        Assert.NotEmpty(DisplaceChoices(start));
        var result = Pick(start, "bonus-action:Displace:enemy:5,2");
        Assert.Equal(new(5, 2), Position(result.State, "enemy"));
        Assert.Equal(new(2, 2), Position(result.State, "demon"));
    }

    [Theory]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.WallWithWindow, false)]
    public void DisplaceUsesOrdinaryEdgePassabilityEvenOnPhaseUnits(EdgeKind edge, bool legal)
    {
        var world = World(target: UnitRoster.Ghost());
        world.Physical.Board.Edges.Add(new(new(3, 2), new(4, 2), edge));
        var start = Start(world);
        Assert.Equal(legal, DisplaceChoices(start).Any(c => c.Destination == new Cell(4, 2)));
        Assert.Contains(DisplaceChoices(start), c => c.Destination == new Cell(3, 2));
    }

    [Theory]
    [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.Water)]
    [InlineData(TerrainKind.StoneFloorWithTable)]
    public void DisplaceNeverUsesTargetsPhaseToEnterImpassableTerrain(TerrainKind terrain)
    {
        var world = World(target: UnitRoster.Ghost());
        world.Physical.Board.Terrain.Add(new(new(4, 2), terrain));
        var start = Start(world);
        Assert.DoesNotContain(DisplaceChoices(start), c => c.Destination == new Cell(4, 2));
        Assert.Throws<ArgumentException>(() => Pick(start, "bonus-action:Displace:enemy:4,2"));
    }

    [Fact]
    public void DisplaceOffersStayAndOnlyOrthogonalEmptyOnBoardDestinations()
    {
        var world = World();
        Add(world, "large-friend", UnitRoster.RedDragon(), new(4, 1), "blue");
        var start = Start(world);
        Assert.Equal(new[] { new Cell(3, 1), new(3, 2), new(3, 3) },
            DisplaceChoices(start).Select(c => c.Destination!).OrderBy(c => c.Y));
        Assert.Throws<ArgumentException>(() => Pick(start, "bonus-action:Displace:enemy:4,3"));
        var corner = World(new(0, 0));
        corner.Physical.Figures[0] = corner.Physical.Figures[0] with { Position = new(1, 1) };
        Assert.Equal(new[] { new Cell(0, 0), new(1, 0), new(0, 1) }, DisplaceChoices(Start(corner)).Select(c => c.Destination!));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    public void DisplaceIgnoresMovAllowsLyingTargetsAndMarksOnlyItsOwnBonusUsed(int x, int y)
    {
        var world = World(target: UnitRoster.Ghost() with { Mov = 0 });
        world.Physical.Figures[1] = world.Physical.Figures[1] with { Posture = Posture.Lying };
        var start = Start(world);
        var result = Pick(start, $"bonus-action:Displace:enemy:{x},{y}");
        Assert.Equal(new(x, y), Position(result.State, "enemy"));
        Assert.Equal(Posture.Lying, result.State.Physical.Figures[1].Posture);
        Assert.False(result.State.MoveDone);
        Assert.False(result.State.ActionDone);
        Assert.Equal(new[] { "Displace" }, result.State.BonusActionsUsedThisActivation);
        Assert.Empty(DisplaceChoices(result));
        Assert.Equal(x == 3, SwapChoices(result).Any());
        Assert.DoesNotContain(result.Events, e => e.Kind is "MovementCompleted" or "AttackStarted" or "DiceRolled");
        Assert.Equal(start.State.Units, result.State.Units);
        var repositioned = Assert.Single(result.Events, e => e.Kind == "UnitRepositioned");
        Assert.Equal(("demon", "enemy", "bonus:Displace"), (repositioned.SourceUnitId, repositioned.UnitId, repositioned.ActionId));
        Assert.Equal(x == 3 ? 1 : 2, repositioned.Path!.Count);
    }

    [Fact]
    public void SwapCanCrossWindowByAdjacencyEvenThoughDisplaceCannotMoveThroughIt()
    {
        var world = World();
        world.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.WallWithWindow));
        var start = Start(world);
        Assert.Single(SwapChoices(start));
        Assert.Equal(new(3, 2), Position(Pick(start, "bonus-action:Swap:enemy").State, "demon"));
    }

    [Fact]
    public void BonusesCombineWithLimitedModifiersAndResetEachActivationWithoutCounters()
    {
        var demon = UnitRoster.DisplacerDemon();
        var rage = UnitRoster.Barbarian().BonusActions.Single();
        var start = Start(World(actor: demon with { BonusActions = demon.BonusActions.Add(rage) }));
        var result = Pick(start, "bonus-action:Rage");
        result = Pick(result, "bonus-action:Swap:enemy");
        result = Pick(result, "bonus-action:Displace:enemy:2,2");
        Assert.Equal(3, result.State.BonusActionsUsedThisActivation.Count);
        Assert.Equal(new AbilityUses(2, 1), result.State.Units[0].BonusActionUses["Rage"]);
        Assert.Single(result.State.Units[0].BonusActionUses);
        Assert.False(result.State.MoveDone);
        result = Pick(result, "stay");
        result = Pick(result, "end-turn");
        result = Pick(result, "stay");
        result = Pick(result, "end-turn");
        Assert.True(result.State.RoundComplete);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        var next = TestGame.StartRound(restored, new Dice(), false);
        Assert.Single(SwapChoices(next));
        Assert.NotEmpty(DisplaceChoices(next));
        Assert.Equal(new AbilityUses(2, 1), next.State.Units[0].BonusActionUses["Rage"]);
    }

    [Fact]
    public void AuraStacksWithClericCountsLargeAndLyingRecipientsOnceAndRecomputesAfterSwap()
    {
        var world = World(new(3, 3));
        Add(world, "large", UnitRoster.RedDragon(), new(0, 2), "red", Posture.Lying);
        Add(world, "cleric", UnitRoster.Cleric(), new(1, 1), "red");
        Assert.Equal(6, world.EffectiveDefOf("large"));
        Assert.Equal(5, world.EffectiveDefOf("demon")); // Other source only.
        Assert.Equal(4, world.EffectiveDefOf("cleric"));
        Assert.Equal(3, world.EffectiveDefOf("enemy"));
        var swapped = Pick(Start(world), "bonus-action:Swap:enemy");
        Assert.Equal(5, swapped.State.EffectiveDefOf("large")); // Demon no longer adjacent.
        Assert.Equal(3, swapped.State.EffectiveDefOf("cleric"));
        swapped.State.Physical.Figures[0] = swapped.State.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal(5, swapped.State.EffectiveDefOf("large"));
    }

    [Fact]
    public void LyingDemonProvidesNoAuraAndItsActivationOnlyStandsUp()
    {
        var world = World();
        Add(world, "friend", UnitRoster.Grunt(), new(2, 1), "red");
        world.Physical.Figures[0] = world.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.Equal(3, world.EffectiveDefOf("friend"));
        var result = Start(world);
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "demon");
        Assert.Equal(Posture.Upright, result.State.Physical.Figures[0].Posture);
        Assert.Equal(4, result.State.EffectiveDefOf("friend"));
        Assert.DoesNotContain(result.Events, e => e.Kind is "ActionUsed" or "MovementCompleted");
    }

    [Fact]
    public void BehaviorSwapsThenMovesThenAttacksThenDisplacesThenEnds()
    {
        var result = Start();
        Assert.Equal("bonus-action:Swap:enemy", AutoKey(result));
        result = Pick(result, AutoKey(result));
        Assert.Equal("stay", AutoKey(result));
        result = Pick(result, AutoKey(result));
        Assert.Equal("attack:enemy", AutoKey(result));
        result = Pick(result, AutoKey(result));
        Assert.True(result.State.ActionDone);
        Assert.Equal("bonus-action:Displace:enemy:2,1", AutoKey(result));
        result = Pick(result, AutoKey(result));
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "demon");
        Assert.DoesNotContain(result.Events, e => e.Kind == "MovementCompleted");
    }

    [Fact]
    public void BehaviorUsesOrdinaryMovementWhenSwapUnavailableAndSwapsWhenItBecomesLegal()
    {
        var result = Start(World(new(6, 2)));
        Assert.Empty(SwapChoices(result));
        var key = AutoKey(result);
        Assert.Equal("5,2", key);
        Assert.Equal(new ApproachMovementProvider(new Choice("stay")).Choose(result.NextInput!, new GameplayQueries(result.State)), key);
        result = Pick(result, key);
        Assert.Equal("bonus-action:Swap:enemy", AutoKey(result));
        result = Pick(result, AutoKey(result));
        Assert.Equal("attack:enemy", AutoKey(result));
    }

    [Fact]
    public void BehaviorRanksOnlySuppliedTargetsAndDestinationsDeterministically()
    {
        var world = World(new(1, 1));
        Add(world, "orthogonal", UnitRoster.Grunt(), new(3, 2), "blue");
        Add(world, "upper", UnitRoster.Grunt(), new(2, 1), "blue");
        var result = Start(world);
        Assert.Equal("bonus-action:Swap:upper", AutoKey(result));
        var request = result.NextInput! with { Candidates = SwapChoices(result).Where(c => c.TargetId == "enemy").ToList() };
        Assert.Equal("bonus-action:Swap:enemy", new DefaultAutomatedProvider().Choose(request, new GameplayQueries(result.State)));
        result = Pick(result, "stay");
        result = Pick(result, "attack:upper");
        var supplied = DisplaceChoices(result).Single(c => c.TargetId == "orthogonal" && c.Destination == new Cell(4, 2));
        request = result.NextInput! with { Candidates = [supplied, new("end-turn", Kind: ActivationChoiceKind.EndTurn)] };
        Assert.Equal(supplied.Key, new DefaultAutomatedProvider().Choose(request, new GameplayQueries(result.State)));
    }

    [Fact]
    public void BonusLegalityIsIndependentOfBehaviorAndSurvivesCompletionOfMoveAndAction()
    {
        var type = UnitRoster.DisplacerDemon() with { Behaviors = UnitBehavior.None };
        var result = Start(World(actor: type));
        Assert.Single(SwapChoices(result));
        Assert.NotEmpty(DisplaceChoices(result));
        result = Pick(result, "stay");
        result = Pick(result, "attack:enemy");
        Assert.True(result.State.MoveDone);
        Assert.True(result.State.ActionDone);
        result = Pick(result, "bonus-action:Swap:enemy");
        Assert.True(result.State.MoveDone);
        Assert.True(result.State.ActionDone);
        Assert.NotEmpty(DisplaceChoices(result));
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Action);
    }

    [Fact]
    public void ZeroCellDisplaceIsLegalEvenWhenAllStepsAreBlocked()
    {
        var world = World();
        Add(world, "top", UnitRoster.Grunt(), new(3, 1), "blue");
        Add(world, "bottom", UnitRoster.Grunt(), new(3, 3), "red");
        world.Physical.Board.Edges.Add(new(new(3, 2), new(4, 2), EdgeKind.WallWithWindow));
        var start = Start(world);
        var choice = Assert.Single(DisplaceChoices(start).Where(c => c.TargetId == "enemy"));
        Assert.Equal(new(3, 2), choice.Destination);
        var result = Pick(start, choice.Key);
        Assert.Contains("Displace", result.State.BonusActionsUsedThisActivation);
        Assert.Equal(new(3, 2), Position(result.State, "enemy"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagonalSwapUsesOrdinaryCornerLos(bool bothPassagesBlocked)
    {
        var world = World(new(3, 3));
        world.Physical.Board.Edges.Add(new(new(2, 2), new(3, 2), EdgeKind.Wall));
        if (bothPassagesBlocked)
            world.Physical.Board.Edges.Add(new(new(2, 2), new(2, 3), EdgeKind.Wall));
        Assert.Equal(!bothPassagesBlocked, SwapChoices(Start(world)).Any());
    }

    [Fact]
    public void DisplaceBreaksEnemyAuraAndNextAttackUsesTheNewDefence()
    {
        var world = World();
        Add(world, "enemy-cleric", UnitRoster.Cleric(), new(4, 3), "blue");
        var result = Start(world);
        Assert.Equal(4, result.State.EffectiveDefOf("enemy"));
        result = Pick(result, "bonus-action:Displace:enemy:3,1");
        Assert.Equal(3, result.State.EffectiveDefOf("enemy"));
        result = Pick(result, "stay");
        result = Pick(result, "attack:enemy");
        var attack = Assert.Single(result.Events, e => e.Kind == "AttackStarted");
        Assert.Equal(3, Assert.Single(attack.AttackContext!.Targets).DefenceDice);
    }

    [Fact]
    public void DemonAuraIsSnapshottedAcrossOneAttackAndRemovedForLaterAttacks()
    {
        var world = World();
        Add(world, "friend", UnitRoster.Grunt("durable-friend") with { Hp = 5, Unique = true }, new(2, 3), "red");
        // Start a Wizard's multi-target Attack with the Demon resolved before its friend.
        Add(world, "wizard", UnitRoster.Wizard(), new(1, 2), "blue");
        world.Types = [world.Types.Single(t => t.Id == UnitTypeIds.Wizard), .. world.Types.Where(t => t.Id != UnitTypeIds.Wizard)];
        var result = TestGame.StartRound(world, new Dice(), false);
        result = Pick(result, "stay");
        result = Pick(result, "fireball:2,2", new Dice(hits: true));
        var attack = Assert.Single(result.Events, e => e.Kind == "AttackStarted").AttackContext!;
        Assert.Equal(4, attack.Targets.Single(t => t.TargetId == "friend").DefenceDice);
        Assert.DoesNotContain(result.State.Physical.Figures, f => f.Id == "demon");
        Assert.Equal(2, result.State.Units.Single(u => u.Id == "friend").CurrentHp);
        Assert.Equal(3, result.State.EffectiveDefOf("friend"));
        Assert.Equal(4, Assert.Single(result.Events, e => e.Kind == "AttackResolved").Attack!
            .Targets.Single(t => t.TargetId == "friend").DefenceDice);
    }

    [Fact]
    public void DisplaceBehaviorPrefersDistanceIncreaseOverStayingOrEarlierBoardCoordinates()
    {
        var world = World(); // Demon (2,2), target (3,2).
        world.Physical.Board.Edges.Add(new(new(3, 2), new(3, 1), EdgeKind.Wall));
        var result = DisplaceDecision(world);
        Assert.Contains(DisplaceChoices(result), c => c.Destination == new Cell(3, 2));
        Assert.Equal("bonus-action:Displace:enemy:4,2", AutoKey(result));
        var displaced = Pick(result, AutoKey(result));
        Assert.Equal(new(4, 2), Position(displaced.State, "enemy"));
    }

    [Fact]
    public void DisplaceBehaviorRanksCompleteCombinationsBeforeChoosingANearestTarget()
    {
        var world = World(new(1, 2)); // Nearest/earliest target cannot be moved outward.
        foreach (var destination in new[] { new Cell(1, 1), new(0, 2), new(1, 3) })
            world.Physical.Board.Edges.Add(new(new(1, 2), destination, EdgeKind.Wall));
        Add(world, "diagonal", UnitRoster.Grunt(), new(3, 3), "blue");
        var result = DisplaceDecision(world);
        Assert.Single(DisplaceChoices(result).Where(c => c.TargetId == "enemy"));
        Assert.Equal("bonus-action:Displace:diagonal:4,3", AutoKey(result));
        Assert.Equal("bonus-action:Swap:enemy", AutoKey(Start(world))); // Swap retains nearest-target ranking.
    }

    [Fact]
    public void DisplaceBehaviorStaysWhenEveryActualStepWouldDecreaseDistance()
    {
        var world = World(new(3, 3));
        world.Physical.Board.Edges.Add(new(new(3, 3), new(4, 3), EdgeKind.Wall));
        world.Physical.Board.Edges.Add(new(new(3, 3), new(3, 4), EdgeKind.ClosedDoor));
        var result = DisplaceDecision(world);
        Assert.Equal(3, DisplaceChoices(result).Count()); // Stay plus two steps toward the Demon.
        Assert.Equal("bonus-action:Displace:enemy:3,3", AutoKey(result));
    }

    [Fact]
    public void DisplaceBehaviorUsesFewestCellsMovedBeforeCoordinatesWhenDistanceIncreaseTies()
    {
        var world = World(new(4, 3), actor: UnitRoster.DisplacerDemon() with { Footprint = Footprint.TwoByTwo });
        world.Physical.Board.Edges.Add(new(new(4, 3), new(5, 3), EdgeKind.Wall));
        world.Physical.Board.Edges.Add(new(new(4, 3), new(4, 4), EdgeKind.Wall));
        var result = DisplaceDecision(world);
        var queries = new GameplayQueries(result.State);
        Assert.Equal(1, queries.ManhattanDistanceBetweenUnits("demon", "enemy"));
        Assert.Equal(2, DisplaceChoices(result).Count()); // Stay (4,3), and (4,2) along the large base.
        Assert.Contains(DisplaceChoices(result), c => c.Destination == new Cell(4, 2));
        Assert.Equal("bonus-action:Displace:enemy:4,3", AutoKey(result)); // Both have increase 0; Stay moves fewer Cells.
    }

    [Fact]
    public void DisplaceBehaviorBreaksEqualIncreaseAndMoveLengthByCurrentTargetDistance()
    {
        var world = World();
        Add(world, "diagonal", UnitRoster.Grunt(), new(1, 1), "blue");
        var result = DisplaceDecision(world);
        Assert.Equal("bonus-action:Displace:enemy:3,1", AutoKey(result)); // Both can increase distance by 1; orthogonal target wins.
        result.NextInput!.Candidates.Reverse();
        Assert.Equal("bonus-action:Displace:enemy:3,1", AutoKey(result));
    }

    [Fact]
    public void DisplaceBehaviorRanksTargetCoordinatesBeforeDestinationCoordinatesOnRemainingTies()
    {
        var world = World(new(1, 2));
        world.Physical.Board.Edges.Add(new(new(1, 2), new(1, 1), EdgeKind.Wall));
        world.Physical.Board.Edges.Add(new(new(1, 2), new(0, 2), EdgeKind.Wall));
        Add(world, "right", UnitRoster.Grunt(), new(3, 2), "blue");
        var result = DisplaceDecision(world);
        Assert.Contains(DisplaceChoices(result), c => c.TargetId == "right" && c.Destination == new Cell(3, 1));
        Assert.Equal("bonus-action:Displace:enemy:1,3", AutoKey(result)); // Target X=1 precedes X=3, despite its later destination row.
        result.NextInput!.Candidates.Reverse();
        Assert.Equal("bonus-action:Displace:enemy:1,3", AutoKey(result));
    }

    [Fact]
    public void DisplaceBehaviorBreaksTargetRowTiesBeforeTargetColumnTies()
    {
        var world = World(new(1, 2));
        Add(world, "upper", UnitRoster.Grunt(), new(2, 1), "blue");
        var result = DisplaceDecision(world);
        Assert.Equal("bonus-action:Displace:upper:2,0", AutoKey(result));
    }

    [Fact]
    public void DisplaceBehaviorBreaksDestinationColumnTiesAfterDestinationRowTies()
    {
        var world = World(new(2, 1));
        world.Physical.Board.Edges.Add(new(new(2, 1), new(2, 0), EdgeKind.Wall));
        var result = DisplaceDecision(world);
        Assert.Equal("bonus-action:Displace:enemy:1,1", AutoKey(result)); // Equal increases to left/right: X=1 before X=3.
    }
}
