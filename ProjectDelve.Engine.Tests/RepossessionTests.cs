using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RepossessionTests
{
    private static GameState Ready(int successes = 2, string destination = StorageRules.RepossessedEquipmentId)
    {
        var actor = new UnitType("test-actor", 0, 1, 0, 0, 3, Actions: UnitAction.None)
        {
            Unique = true,
            LocalAbilities = [new("repossess", "Custom Collection", AbilityTiming.Action,
                new TryRepossessRandomEquipment(successes, destination))]
        };
        var target = new UnitType("test-target", 0, 1, 1, 0, 5) { Unique = true, CanHaveEquipment = true };
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 4, Height = 3 }, UnitTypeIds = [actor.Id, target.Id], UnitTypes = [actor, target],
            EquipmentTypes = [new("test-a", "First"), new("test-b", "Second")],
            Storages = [new("side-locker", "Side locker", "red", [])],
            Units = [new(actor.Id, "blue", new(1, 1), UnitId: "actor"),
                new(target.Id, "red", new(2, 1), UnitId: "target")
                { Inventory = [new("instance-b", "test-b"), new("instance-a", "test-a")] }],
            Agency = [new(actor.Id, "blue", ControllerKind.Human), new(target.Id, "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario)));
        state.Round = 1; state.ActiveToken = new(actor.Id, "blue"); state.CurrentUnitId = "actor";
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, actor.Id, "actor", [], false);
        return GameEngine.RefreshChoices(state, new Random(), false).State;
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    [Fact]
    public void AuthoredDestinationCanBelongToAnotherSide()
    {
        var state = Ready(6, "side-locker");
        var random = new Random();
        var candidate = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
        var attempt = GameEngine.Advance(state, new Choice(candidate.Key), random, false);
        var result = GameEngine.Advance(attempt.State, new Choice("roll-dice"), random, false);
        Assert.Empty(result.State.Storages.Single(s => s.StorageId == StorageRules.RepossessedEquipmentId).Contents);
        var locker = result.State.Storages.Single(s => s.StorageId == "side-locker");
        Assert.Equal("red", locker.SideId);
        Assert.Equal("instance-b", Assert.Single(locker.Contents).Id);
    }
    private sealed class Random(int face = 1) : IRandomProvider
    {
        public int Rolls, Selections;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new Exception("Not an Attack");
        public DefenceFace RollDefenceDie() => throw new Exception("Not an Attack");
        public int RollD6() { Rolls++; return face; }
        public int LootDrawIndex(int exclusiveMax) { Selections++; Assert.Equal(2, exclusiveMax); return 1; }
    }
    private sealed class PhysicalRandom(int selectionFace) : IRandomProvider
    {
        public int Rolls;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new Exception("Not an Attack");
        public DefenceFace RollDefenceDie() => throw new Exception("Not an Attack");
        public int RollD6() => ++Rolls == 1 ? 1 : selectionFace;
    }

    [Fact]
    public void PhysicalD6SelectionIsUniformAcrossSortedEquipmentInstances()
    {
        List<string> selected = [];
        for (var face = 1; face <= 6; face++)
        {
            var state = Ready(6);
            var random = new PhysicalRandom(face);
            var choice = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
            var started = GameEngine.Advance(state, new Choice(choice.Key), random, false);
            var outcome = GameEngine.Advance(started.State, new Choice("roll-dice"), random, false);
            selected.Add(outcome.Events.Single(e => e.Kind == "EquipmentRepossessionAttemptResolved").RepossessedEquipmentId!);
            Assert.Equal(2, random.Rolls);
        }
        Assert.Equal(new[] { "instance-a", "instance-b", "instance-a", "instance-b", "instance-a", "instance-b" }, selected);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, false)]
    public void AttemptSpendsActionAndResumesDiceBeforeSelectingExactPhysicalInstance(int roll, bool success)
    {
        var state = Ready();
        var original = state.Units.Single(u => u.Id == "target").Inventory;
        var random = new Random(roll);
        var candidate = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
        var start = GameEngine.Advance(state, new Choice(candidate.Key), random, false);
        Assert.Equal(DecisionKind.RollDice, start.State.Pending!.Kind);
        Assert.Equal(1, start.State.ActionsUsedThisActivation);
        Assert.Equal(0, random.Rolls); Assert.Equal(0, random.Selections);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(start.State))!;
        var result = GameEngine.Advance(restored, new Choice("roll-dice"), random, false);
        Assert.Equal(1, random.Rolls); Assert.Equal(success ? 1 : 0, random.Selections);
        Assert.Equal(success, result.Events.Single(e => e.Kind == "EquipmentRepossessionAttemptResolved").Succeeded);
        Assert.DoesNotContain(result.Events, e => e.Kind is "UnitDefeated" or "LootDropRolled" or "AttackStarted");
        Assert.DoesNotContain("actor", result.State.DefeatedUnitIds);
        Assert.Equal(success ? 1 : 2, result.State.Units.Single(u => u.Id == "target").Inventory.Length);
        var stored = result.State.Storages.Single(s => s.StorageId == StorageRules.RepossessedEquipmentId).Contents;
        if (success) Assert.Equal(original.Single(o => o.Id == "instance-b"), Assert.Single(stored));
        else Assert.Empty(stored);
        Assert.Equal(!success, result.State.Physical.Figures.Any(f => f.Id == "actor"));
        Assert.Equal(2, state.Units.Single(u => u.Id == "target").Inventory.Length);
    }

    [Fact]
    public void RangeOneIncludesDiagonalButExcludesTwoCellsAndBlockedSight()
    {
        var state = Ready();
        Assert.True(RepossessionRules.CanTarget(state, "actor", new(1, 1), "target"));
        Assert.True(RepossessionRules.CanTarget(state, "actor", new(1, 0), "target"));
        Assert.False(RepossessionRules.CanTarget(state, "actor", new(0, 1), "target"));
        state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall));
        Assert.False(RepossessionRules.CanTarget(state, "actor", new(1, 1), "target"));
    }

    [Fact]
    public void EquipmentExtendsRangeToTwoAndPayingPostureCostPreservesCommittedTarget()
    {
        var state = Ready(6);
        var actor = state.Types[0];
        state.Types[0] = actor with { CanHaveEquipment = true,
            LocalAbilities = [actor.LocalAbilities[0] with { Costs = [new LayDownSelf()] }] };
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(3, 1) };
        var random = new Random();
        var initial = GameEngine.RefreshChoices(state, random, false).State;
        Assert.DoesNotContain(initial.Pending?.Candidates ?? [], c => c.Ability?.Key == "repossess");

        state.EquipmentTypes.Add(new("scope", "Scope") { LocalAbilities = [
            new("extend", "Extended Range", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Rng, 1)]))] });
        state.Units[0] = state.Units[0] with { Inventory = [new("actor-scope", "scope")] };
        state = GameEngine.RefreshChoices(state, random, false).State;
        Assert.Equal(2, state.EffectiveRngOf("actor"));
        var choice = Assert.Single(state.Pending!.Candidates, c => c.Ability?.Key == "repossess");
        Assert.Equal("target", choice.TargetId);
        var started = GameEngine.Advance(state, new Choice(choice.Key), random, false).State;
        Assert.Equal(1, started.EffectiveRngOf("actor"));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(started))!;
        var result = GameEngine.Advance(restored, new Choice("roll-dice"), random, false);
        Assert.Equal("instance-b", Assert.Single(result.State.Storages.Single(s =>
            s.StorageId == StorageRules.RepossessedEquipmentId).Contents).Id);
    }

    [Fact]
    public void LevelTwoRangeUsesManhattanDistanceLineOfSightAndSideVisibility()
    {
        var state = Ready();
        var actor = state.Types[0];
        state.Types[0] = actor with { AdditionalLevels = [new(0, 2, 0, 0, 3)
            { LocalAbilities = actor.LocalAbilities, Actions = UnitAction.None, SightDistance = 0 }] };
        state.Units[0] = state.Units[0] with { Level = 2 };
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(3, 1) };
        bool Offered() => GameEngine.RefreshChoices(state, new Random(), false).State.Pending?.Candidates
            .Any(c => c.Ability?.Key == "repossess" && c.TargetId == "target") == true;

        Assert.True(Offered());
        Assert.False(RepossessionRules.CanTarget(state, "actor", new(1, 0), "target"));
        var wall = new Edge(new(2, 1), new(3, 1), EdgeKind.Wall);
        state.Physical.Board.Edges.Add(wall);
        Assert.False(Offered());
        state.Physical.Board.Edges.Remove(wall);
        state.FogOfWarSides = ["blue"];
        state.ExploredCells["blue"] = [new(1, 1), new(2, 1)];
        Assert.False(Offered());
        state.ExploredCells["blue"].Add(new(3, 1));
        Assert.True(Offered());
    }

    [Fact]
    public void SuccessfulDepartureBypassesUndyingExplosionAndDropsWhileEquipmentLossClampsHp()
    {
        var state = Ready(6);
        state.Types[0] = state.Types[0] with
        {
            LootDropSuccesses = 6,
            LocalAbilities = [.. state.Types[0].LocalAbilities,
                new("undying", "Stay", AbilityTiming.InsteadOfDefeat, new Undying()),
                new("explode", "Burst", AbilityTiming.WhenDefeated, new Explosion { Damage = 2 })]
        };
        var equipmentIndex = state.EquipmentTypes.FindIndex(t => t.Id == "test-b");
        state.EquipmentTypes[equipmentIndex] = state.EquipmentTypes[equipmentIndex] with { MaxHpModifier = 2 };
        state.Units[1] = state.Units[1] with { CurrentHp = 7 };
        var candidate = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
        var started = GameEngine.Advance(state, new Choice(candidate.Key), new Random(), false);
        var result = GameEngine.Advance(started.State, new Choice("roll-dice"), new Random(), false);
        Assert.Equal(5, result.State.Units.Single(u => u.Id == "target").CurrentHp);
        Assert.Empty(result.State.PendingDefeatEffects);
        Assert.DoesNotContain(result.Events, e => e.Kind is "UnitDefeated" or "PostureChanged" or "ExplosionDamageResolved" or "LootDropRolled");
    }

    [Theory]
    [InlineData(-1)] [InlineData(7)]
    public void AuthoringRejectsInvalidSuccessCounts(int count) =>
        Assert.Throws<ArgumentException>(() => AbilityContent.Validate(
            [new("test", "Test", AbilityTiming.Action, new TryRepossessRandomEquipment(count, "storage"))], AbilitySourceKind.Unit));

    [Theory]
    [InlineData(AbilitySourceKind.Equipment)] [InlineData(AbilitySourceKind.Item)]
    public void ReusableObjectSourcesPayCostsBeforeDiceAndDepartWithOrdinaryInventoryDrops(AbilitySourceKind source)
    {
        var state = Ready(6);
        var declaration = state.Types[0].LocalAbilities[0] with { Costs = [new LayDownSelf()] };
        state.Types[0] = state.Types[0] with { LocalAbilities = [], CanHaveItems = true, CanHaveEquipment = true };
        OwnedObject granting;
        if (source == AbilitySourceKind.Equipment)
        {
            state.EquipmentTypes.Add(new("source-gear", "Granting Gear") { LocalAbilities = [declaration] });
            granting = new("physical-grant", "source-gear");
        }
        else
        {
            state.ItemTypes.Add(new("source-item", "Granting Item") { LocalAbilities = [declaration] });
            granting = new("physical-grant") { ItemTypeId = "source-item" };
        }
        state.Units[0] = state.Units[0] with { Inventory = [granting, new("other-gear", "test-a")] };
        state = GameEngine.RefreshChoices(state, new Random(), false).State;
        var choice = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
        var started = GameEngine.Advance(state, new Choice(choice.Key), new Random(), false);
        Assert.False(started.State.IsUpright("actor"));
        Assert.Equal(1, started.State.ActionsUsedThisActivation);
        if (source == AbilitySourceKind.Item)
            Assert.Equal("physical-grant", Assert.Single(started.State.LootDeck!.DiscardPile).Id);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(started.State))!;
        var outcome = GameEngine.Advance(restored, new Choice("roll-dice"), new Random(), false);
        Assert.Empty(outcome.State.Units.Single(u => u.Id == "actor").Inventory);
        Assert.Contains(outcome.State.GroundObjects, ground => ground.Object.Id == "other-gear" && ground.Position == new Cell(1, 1));
        Assert.Equal(source == AbilitySourceKind.Equipment,
            outcome.State.GroundObjects.Any(ground => ground.Object.Id == "physical-grant"));
        var resolved = outcome.ResolutionSteps.Single(step => outcome.Events[step.EventIndex].Kind == "EquipmentRepossessionAttemptResolved");
        Assert.DoesNotContain(resolved.StateAfter.Physical.Figures, figure => figure.Id == "actor");
        Assert.Single(resolved.StateAfter.Storages.Single(s => s.StorageId == StorageRules.RepossessedEquipmentId).Contents);
    }

    [Fact]
    public void EquipmentEnablingCurrentOccupancyIsExcludedIndividually()
    {
        var state = Ready();
        var equipmentIndex = state.EquipmentTypes.FindIndex(t => t.Id == "test-a");
        state.EquipmentTypes[equipmentIndex] = state.EquipmentTypes[equipmentIndex] with
        { LocalAbilities = [new("swim", "Swim", AbilityTiming.Passive, new Amphibious())] };
        state.Physical.Board.Terrain.Add(new(new(2, 1), TerrainKind.Water));
        Assert.Equal("instance-b", Assert.Single(RepossessionRules.EligibleEquipment(state, "target")).Id);
        Assert.True(RepossessionRules.CanTarget(state, "actor", new(1, 1), "target"));
        state.Units[1] = state.Units[1] with { Inventory = [state.Units[1].Inventory.Single(o => o.Id == "instance-a")] };
        Assert.Empty(RepossessionRules.EligibleEquipment(state, "target"));
        Assert.False(RepossessionRules.CanTarget(state, "actor", new(1, 1), "target"));
    }

    [Fact]
    public void UnknownStorageAndForgedPendingRollAreRejectedBeforeRandomness()
    {
        var state = Ready();
        var ability = state.Types[0].LocalAbilities[0];
        state.Types[0] = state.Types[0] with { LocalAbilities = [ability with { Mechanic = new TryRepossessRandomEquipment(2, "missing") }] };
        Assert.Throws<ArgumentException>(() => AbilityContent.ValidateState(state));
        state = Ready();
        var choice = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
        state = GameEngine.Advance(state, new Choice(choice.Key), new Random(), false).State;
        state.RepossessionInProgress = state.RepossessionInProgress! with { SuccessCount = 6 };
        var random = new Random();
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(state, new Choice("roll-dice"), random, false));
        Assert.Equal(0, random.Rolls);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(6, true)]
    public void BoundarySuccessCountsStillRollExactlyOneAttemptDie(int successes, bool success)
    {
        var state = Ready(successes);
        var random = new Random(6);
        var choice = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
        var started = GameEngine.Advance(state, new Choice(choice.Key), random, false);
        var resolved = GameEngine.Advance(started.State, new Choice("roll-dice"), random, false);
        Assert.Equal(1, random.Rolls);
        Assert.Equal(success, resolved.Events.Single(e => e.Kind == "EquipmentRepossessionAttemptResolved").Succeeded);
    }

    [Fact]
    public void FogHidesTargetsAndContinuationAndRedactsStorageContentsFromOutcomes()
    {
        var state = Ready();
        state.FogOfWarSides = ["blue"];
        state.Types[0] = state.Types[0] with { SightDistance = 0 };
        state.ExploredCells["blue"] = [new(1, 1)];
        var refreshed = GameEngine.RefreshChoices(state, new Random(), false).State;
        Assert.DoesNotContain(refreshed.Pending?.Candidates ?? [], c => c.Ability?.Key == "repossess");
        state.ExploredCells["blue"].Add(new(2, 1));
        var choice = state.Pending!.Candidates.Single(c => c.Ability?.Key == "repossess");
        var started = GameEngine.Advance(state, new Choice(choice.Key), new Random(), false);
        Assert.NotNull(started.State.RepossessionInProgress);
        Assert.Null(FogOfWar.Observe(started.State, "blue").RepossessionInProgress);
        var outcome = GameEngine.Advance(started.State, new Choice("roll-dice"), new Random(), false);
        var attempt = outcome.Events.Single(e => e.Kind == "EquipmentRepossessionAttemptResolved");
        var observed = FogOfWar.ObserveEvent(outcome.State, "blue", attempt);
        Assert.NotNull(observed); Assert.Null(observed.ObjectCard); Assert.Null(observed.StorageId);
        Assert.Equal("instance-b", observed.RepossessedEquipmentId);
        Assert.Equal("Second", observed.RepossessedEquipmentName);
        var hidden = outcome.State.Copy(); hidden.ExploredCells["blue"] = [new(1, 1)];
        var hiddenOutcome = FogOfWar.ObserveEvent(hidden, "blue", attempt);
        Assert.NotNull(hiddenOutcome); Assert.Null(hiddenOutcome.RepossessedEquipmentId); Assert.Null(hiddenOutcome.RepossessedEquipmentName);
        hidden.ExploredCells["blue"] = [new(2, 1)];
        var targetOnly = FogOfWar.ObserveEvent(hidden, "blue", attempt);
        Assert.NotNull(targetOnly);
        Assert.Equal("instance-b", targetOnly.RepossessedEquipmentId);
        Assert.Null(targetOnly.UnitId); Assert.Null(targetOnly.ActionId); Assert.Null(targetOnly.AbilitySource);
        Assert.Null(targetOnly.SuccessCount); Assert.Null(targetOnly.DieRoll);
        Assert.NotNull(FogOfWar.ObserveEvent(outcome.State, "blue", outcome.Events.Single(e => e.Kind == "UnitRemoved")));
    }
}
