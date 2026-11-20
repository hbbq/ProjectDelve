using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RepossessionPursuitTests
{
    private sealed class FailureDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.SideId == "red") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }

    private sealed class ChooseKey(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    [Fact]
    public void PursuitApproachesOpensDoorAndAttemptsAgainstEquippedEnemyInSingleCellAlcove()
    {
        var state = World(Bearer("target", 5, 3));
        state.Physical.Board.Edges.AddRange([
            new(new(4, 3), new(5, 3), EdgeKind.ClosedDoor),
            new(new(5, 3), new(6, 3), EdgeKind.Wall),
            new(new(5, 2), new(5, 3), EdgeKind.Wall),
            new(new(5, 3), new(5, 4), EdgeKind.Wall)]);
        var queries = new GameplayQueries(state);
        Assert.Null(queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "target"));
        Assert.Equal(3, queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "target", true));
        Assert.False(RepossessionRules.CanTarget(state, "actor", new(4, 3), "target"));
        var dice = new FailureDice(); var provider = new OrderAwareAutomatedProvider();
        state.Round = 1; state.CurrentUnitId = "actor"; state.ActiveToken = new(UnitTypeIds.Paul, "red");
        state = GameEngine.RefreshChoices(state, dice, false).State;
        var move = provider.Choose(state.Pending!, new GameplayQueries(state));
        Assert.Equal(new Cell(4, 3), state.Pending!.Candidates.Single(c => c.Key == move).Destination);
        state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.False(RepossessionRules.CanTarget(state, "actor", new(4, 3), "target"));
        Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is TryRepossessRandomEquipment);
        var open = provider.Choose(state.Pending!, new GameplayQueries(state));
        Assert.IsType<OpenDoor>(state.Pending!.Candidates.Single(c => c.Key == open).Ability!.Definition.Mechanic);
        state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.Equal(EdgeKind.OpenDoor, state.Physical.Board.EdgeBetween(new(4, 3), new(5, 3)));
        Assert.True(RepossessionRules.CanTarget(state, "actor", new(4, 3), "target"));
        var attempt = provider.Choose(state.Pending!, new GameplayQueries(state));
        Assert.IsType<TryRepossessRandomEquipment>(state.Pending!.Candidates.Single(c => c.Key == attempt).Ability!.Definition.Mechanic);
        state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.Equal(DecisionKind.RollDice, state.Pending!.Kind);
        Assert.Equal(1, state.ActionsUsedThisActivation);
    }

    [Fact]
    public void DefaultPursuitWaitsUntilLaterActivationAfterFailureDespiteExtraAction()
    {
        var state = World(Bearer("target", 2, 3));
        var dice = new FailureDice();
        state.Round = 1; state.CurrentUnitId = "actor"; state.ActiveToken = new(UnitTypeIds.Paul, "red");
        state.MoveDone = true; state.ExtraActionsThisActivation = 1;
        state = GameEngine.RefreshChoices(state, dice, false).State;
        var provider = new OrderAwareAutomatedProvider();
        var attempt = provider.Choose(state.Pending!, new GameplayQueries(state))!;
        Assert.IsType<TryRepossessRandomEquipment>(state.Pending!.Candidates.Single(c => c.Key == attempt).Ability!.Definition.Mechanic);
        state = GameEngine.Advance(state, new ChooseKey(attempt), dice, false).State;
        state = GameEngine.Advance(state, new ChooseKey("roll-dice"), dice, false).State;
        Assert.Equal(1, state.ActionsRemaining);
        Assert.Contains(state.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is TryRepossessRandomEquipment);
        state = System.Text.Json.JsonSerializer.Deserialize<GameState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        Assert.True(state.RepossessionFailedThisActivation);
        Assert.Equal("end-turn", provider.Choose(state.Pending!, new GameplayQueries(state)));
        state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.False(state.RepossessionFailedThisActivation);
        // Start the next ordinary activation through the Engine's round/selection boundaries.
        state = GameEngine.StartRound(state, dice, false).State;
        for (var step = 0; step < 5 && state.Pending?.Kind != DecisionKind.Activation; step++)
            state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.Equal("actor", state.CurrentUnitId);
        Assert.False(new GameplayQueries(state).HasFailedRepossessionThisActivation("actor"));
        state.MoveDone = true;
        state = GameEngine.RefreshChoices(state, dice, false).State;
        var next = provider.Choose(state.Pending!, new GameplayQueries(state));
        Assert.IsType<TryRepossessRandomEquipment>(state.Pending!.Candidates.Single(c => c.Key == next).Ability!.Definition.Mechanic);
    }
    [Fact]
    public void PaulIsDiscoverableDeclarativeNonAttacker()
    {
        var paul = Assert.IsType<UnitType>(CanonicalUnitTypes.Find(UnitTypeIds.Paul));
        Assert.Equal("Paul, Enforcement Officer", paul.DisplayName);
        Assert.Equal((3, 1, 0, 1, 1), (paul.Mov, paul.Rng, paul.Atk, paul.Def, paul.Hp));
        Assert.False(paul.HasAction(UnitAction.NormalAttack));
        Assert.Equal(ControllerKind.Automated, paul.DefaultController);
        var ability = Assert.Single(paul.LocalAbilities, a => a.Mechanic is TryRepossessRandomEquipment);
        Assert.Equal(AbilityTiming.Action, ability.Timing);
        Assert.Equal(new TryRepossessRandomEquipment(2, StorageRules.RepossessedEquipmentId), ability.Mechanic);
        Assert.Contains(UnitAuthoringSchema.Contract.Behaviors, b => b.Value == nameof(UnitBehavior.PreferEquippedEnemy));
    }

    private static GameState World(params UnitPlacement[] targets) => GameEngine.CreateGame(new ScenarioDefinition
    {
        Board = new() { Width = 10, Height = 7 },
        UnitTypeIds = [UnitTypeIds.Paul, "fixture-bearer"],
        UnitTypes = [new("fixture-bearer", 3, 1, 1, 1, 1) { CanHaveEquipment = true }],
        Units = [new(UnitTypeIds.Paul, "red", new(1, 3), UnitId: "actor"), .. targets],
        Agency = [new("fixture-bearer", "blue", ControllerKind.Human)],
        EquipmentTypes = [new("fixture-equipment", "Fixture Equipment")], Loot = LootSettings.Empty,
        Objectives = [new("red", ScenarioObjective.NoObjective), new("blue", ScenarioObjective.NoObjective)]
    });

    private static UnitPlacement Bearer(string id, int x, int y, bool equipped = true) =>
        new("fixture-bearer", "blue", new(x, y), UnitId: id)
        { Inventory = equipped ? [new(id + "-equipment", "fixture-equipment")] : [] };

    private static Candidate? Move(GameState world)
    {
        var paths = MovementRules.FindPaths(world, "actor", world.Physical.Figures.Single(f => f.Id == "actor").Position, 3);
        var request = new DecisionRequest(DecisionKind.Move, UnitTypeIds.Paul, "actor",
            paths.Where(p => p.Value.Count > 1).Select(p => new Candidate($"{p.Key.X},{p.Key.Y}", p.Key, p.Value.ToList())).ToList(), true);
        var choice = new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(world));
        return request.Candidates.SingleOrDefault(c => c.Key == choice);
    }

    [Fact]
    public void RangedPursuitApproachesClosedDoorBeforeAttemptingThroughIt()
    {
        var state = World(Bearer("target", 3, 3));
        state.Physical.Board.Edges.AddRange(Enumerable.Range(0, 7).Select(y =>
            new Edge(new(2, y), new(3, y), y == 3 ? EdgeKind.ClosedDoor : EdgeKind.Wall)));
        state.Round = 1; state.CurrentUnitId = "actor"; state.ActiveToken = new(UnitTypeIds.Paul, "red");
        state.ModifiersThisTurn.Add(new(Stat.Rng, 1));
        var queries = new GameplayQueries(state);
        Assert.Equal(1, queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "target", true));
        var dice = new FailureDice();
        var provider = new OrderAwareAutomatedProvider();
        state = GameEngine.RefreshChoices(state, dice, false).State;
        state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.Equal(new Cell(2, 3), state.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Definition.Mechanic is TryRepossessRandomEquipment);
        state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.Equal(EdgeKind.OpenDoor, state.Physical.Board.EdgeBetween(new(2, 3), new(3, 3)));
        state = GameEngine.Advance(state, provider, dice, false).State;
        Assert.Equal("target", state.RepossessionInProgress?.TargetId);
    }

    [Fact]
    public void InterveningHostileFigureBlocksRangedRepossessionLineOfSight()
    {
        var state = World(Bearer("blocker", 2, 3, false), Bearer("target", 3, 3));
        state.CurrentUnitId = "actor";
        state.ModifiersThisTurn.Add(new(Stat.Rng, 1));
        Assert.Equal(2, state.EffectiveRngOf("actor"));
        Assert.False(RepossessionRules.CanTarget(state, "actor", new(1, 3), "target"));
        var blocker = state.Units.FindIndex(u => u.Id == "blocker");
        state.Units[blocker] = state.Units[blocker] with { SideId = "red" };
        Assert.True(RepossessionRules.CanTarget(state, "actor", new(1, 3), "target"));
    }

    [Fact]
    public void PursuitStopsTwoCellsAwayWhenAnAdjacentAllyGrantsRangeAtTheDestination()
    {
        var state = World(Bearer("target", 6, 3), Bearer("ally", 4, 4, false));
        var ally = state.Units.FindIndex(u => u.Id == "ally");
        state.Units[ally] = state.Units[ally] with { SideId = "red", Inventory = [new("ally-aura", "range-aura")] };
        state.EquipmentTypes.Add(new("range-aura", "Range Aura") { LocalAbilities = [
            new("extend", "Extend Range", AbilityTiming.Passive,
                new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.Rng, 1)]))] });
        Assert.Equal(1, state.EffectiveRngOf("actor"));
        var queries = new GameplayQueries(state);
        Assert.Equal(3, queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "target"));
        Assert.Equal(0, queries.DistanceToRepossessionPositionFrom("actor", new(4, 3), "target"));
        Assert.Equal(new Cell(4, 3), Move(state)?.Destination);
        Assert.Equal(1, state.EffectiveRngOf("actor"));
    }

    [Fact]
    public void PursuitIgnoresNearUnequippedEnemyAndReevaluatesAfterEquipmentChanges()
    {
        var world = World(Bearer("near", 2, 3, false), Bearer("far", 7, 3));
        var move = Assert.IsType<Candidate>(Move(world));
        Assert.True(move.Destination!.X >= 3);
        var index = world.Units.FindIndex(u => u.Id == "far");
        world.Units[index] = world.Units[index] with { Inventory = [] };
        Assert.Null(Move(world));
        index = world.Units.FindIndex(u => u.Id == "near");
        world.Units[index] = world.Units[index] with { Inventory = [new("new-equipment", "fixture-equipment")] };
        Assert.Null(Move(world)); // Already in a legal melee position.
    }

    [Fact]
    public void SealedCloserEnemyIsIgnoredForReachableEquippedEnemy()
    {
        var world = World(Bearer("sealed", 3, 3), Bearer("reachable", 1, 6));
        // Enclose the closer enemy with ordinary Walls, including diagonal LOS.
        world.Physical.Board.Edges.AddRange([
            new(new(3, 3), new(2, 3), EdgeKind.Wall), new(new(3, 3), new(4, 3), EdgeKind.Wall),
            new(new(3, 3), new(3, 2), EdgeKind.Wall), new(new(3, 3), new(3, 4), EdgeKind.Wall)]);
        var queries = new GameplayQueries(world);
        Assert.Null(queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "sealed", true));
        var move = Assert.IsType<Candidate>(Move(world));
        Assert.True(move.Destination!.Y >= 5);
    }

    [Fact]
    public void PursuitTiesAreDeterministicAndHiddenEquipmentDoesNotAttractActor()
    {
        var world = World(Bearer("top", 6, 1), Bearer("bottom", 6, 5));
        var first = Assert.IsType<Candidate>(Move(world));
        Assert.True(first.Destination!.Y < 3);
        Assert.Equal(first.Key, Move(world)?.Key);
        world.FogOfWarSides = ["red"];
        world.ExploredCells["red"] = [new(1, 3)];
        Assert.Null(Move(world));
    }

    [Fact]
    public void HostileFigureBlocksRouteButFriendlyFigurePermitsTransit()
    {
        var world = World(Bearer("blocker", 2, 3, false), Bearer("behind", 4, 3), Bearer("reachable", 1, 6));
        world.Physical.Board.Edges.AddRange(Enumerable.Range(0, 7).Where(y => y != 3)
            .Select(y => new Edge(new(1, y), new(2, y), EdgeKind.Wall)));
        var queries = new GameplayQueries(world);
        Assert.Null(queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "behind", true));
        Assert.True(Assert.IsType<Candidate>(Move(world)).Destination!.Y >= 5);
        var index = world.Units.FindIndex(u => u.Id == "blocker");
        world.Units[index] = world.Units[index] with { SideId = "red" };
        Assert.NotNull(new GameplayQueries(world).DistanceToRepossessionPositionFrom("actor", new(1, 3), "behind", true));
    }

    [Fact]
    public void ClosedDoorUsesPlanningOnlyUntilAuthoritativeOpenDoorChoice()
    {
        var world = World(Bearer("behind", 4, 3));
        world.Physical.Board.Edges.AddRange(Enumerable.Range(0, 7)
            .Select(y => new Edge(new(1, y), new(2, y), y == 3 ? EdgeKind.ClosedDoor : EdgeKind.Wall)));
        var queries = new GameplayQueries(world);
        Assert.Null(queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "behind"));
        Assert.NotNull(queries.DistanceToRepossessionPositionFrom("actor", new(1, 3), "behind", true));
        Assert.Null(Move(world));
        var ability = world.AbilitiesOf("actor").Single(a => a.Definition.Mechanic is OpenDoor);
        var request = new DecisionRequest(DecisionKind.Activation, UnitTypeIds.Paul, "actor",
            [new("open", Door: new(new(1, 3), new(2, 3), EdgeKind.ClosedDoor), Kind: ActivationChoiceKind.FreeAction) { Ability = ability },
                new("stay", Kind: ActivationChoiceKind.Stay)], false);
        Assert.Equal("open", new OrderAwareAutomatedProvider().Choose(request, queries));
        Assert.Equal(EdgeKind.ClosedDoor, world.Physical.Board.EdgeBetween(new(1, 3), new(2, 3)));
    }

    [Fact]
    public void PopulationUsesExistingRareHighThreatWeightAndCanonicalDefinition()
    {
        var pool = PopulationComposition.Pool(ContentBundle.Canonical().Units.Select(t => t.Id));
        var paul = Assert.Single(pool, p => p.UnitTypeId == UnitTypeIds.Paul);
        Assert.Equal(5, paul.Threat);
        Assert.Equal(1, paul.RelativeWeight);
        Assert.True(paul.Weight(1) < paul.Weight(5));
        Assert.DoesNotContain(HeroesVsMonsters.Pool, p => p.UnitTypeId == UnitTypeIds.Paul);
        var random = Pcg32.Stream(73, "paul-population-test");
        Assert.Contains(UnitTypeIds.Paul, Enumerable.Range(0, 1024).Select(_ => PopulationComposition.Select(random, 5, pool)));
        var encounter = PopulationComposition.FromRoles("fixture", "LoneMonster", "fixture", 10,
            [(UnitTypeIds.Paul, 33, "lone", "fixture-cluster", null)], 1, random);
        var placement = Assert.Single(encounter.Units);
        Assert.Equal(UnitTypeIds.Paul, placement.UnitTypeId);
        Assert.Equal(ControllerKind.Automated, Assert.Single(encounter.Agency).Controller);
        Assert.Equal(OrderKind.Normal, Assert.Single(encounter.Orders).Kind);
    }
}
