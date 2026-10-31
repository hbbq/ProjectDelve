using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class GrantedFireballPlanningTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(string source)
    {
        var bearer = new UnitType("bearer", 1, 2, 1, 0, 1, Actions: UnitAction.None)
        {
            CanHaveEquipment = true,
            CanHaveItems = true,
            LocalAbilities = source is "Limited" or "Unlimited" or "Renamed" ? [
                new(source == "Renamed" ? "fireball" : "cast", "Unexpected Light", AbilityTiming.Action, new FireballMechanic())
                { Usage = new(source == "Unlimited" ? null : 1) }] : []
        };
        return GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 9, Height = 1 },
            UnitTypeIds = ["bearer", "target"],
            UnitTypes = [bearer, new("target", 0, 0, 0, 0, 1, Actions: UnitAction.None)],
            EquipmentTypes = [new("lantern", "Lantern") { LocalAbilities = [
                new("shine", "Portable Light", AbilityTiming.Action, new FireballMechanic())] }],
            ItemTypes = [new("spark", "Spark") { LocalAbilities = [
                new("ignite", "Portable Light", AbilityTiming.Action, new FireballMechanic())] }],
            Units = [new("bearer", "blue", new(0, 0), UnitId: "actor")
            {
                Inventory = source switch
                {
                    "Equipment" => [new("lantern-copy", "lantern")],
                    "Item" => [new("spark-one") { ItemTypeId = "spark" }, new("spark-two") { ItemTypeId = "spark" }],
                    _ => []
                }
            }, new("target", "red", new(7, 0), UnitId: "enemy")],
            Agency = [new("bearer", "blue", ControllerKind.Human), new("target", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        });
    }

    private static GameState Ready(GameState state)
    {
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(4, 0) };
        state.Round = 1;
        state.CurrentUnitId = "actor";
        state.ActiveToken = new("bearer", "blue");
        state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "bearer", "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }

    private static Candidate Attack(GameState state, string? key = null, string? instanceId = null) =>
        state.Pending!.Candidates.Single(c => c.Action == UnitAction.Fireball && c.Destination == new Cell(6, 0) &&
            (key is null || c.Ability?.Key == key) &&
            (instanceId is null || c.Ability?.SourceInstanceId == instanceId));

    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;

    [Theory]
    [InlineData("Renamed")]
    [InlineData("Limited")]
    [InlineData("Unlimited")]
    [InlineData("Equipment")]
    [InlineData("Item")]
    public void EverySourceProvidesFireballApproachWithoutNativeAttackFields(string source)
    {
        var state = State(source);
        var original = JsonSerializer.Serialize(state);
        var queries = new GameplayQueries(state);
        Assert.False(queries.CanAttackAnyFrom("actor", new(3, 0), ["enemy"]));
        Assert.True(queries.CanAttackAnyFrom("actor", new(4, 0), ["enemy"]));
        Assert.Equal(4, queries.DistanceToAttackPositionFrom("actor", new(0, 0), ["enemy"]));
        Assert.Equal(4, queries.DistanceToAttackPositionFrom("actor", new(0, 0)));
        Assert.Equal(original, JsonSerializer.Serialize(state));

        var ready = Ready(Restore(state));
        Assert.Contains(ready.Pending!.Candidates, c => c.Action == UnitAction.Fireball && c.TargetIds.Contains("enemy"));
        ready.ActionDone = true;
        // Approach is a future-position query, not an extra current Action opportunity.
        Assert.True(new GameplayQueries(ready).CanAttackAnyFrom("actor", new(4, 0), ["enemy"]));
        Assert.DoesNotContain(GameEngine.RefreshChoices(ready, new Dice(), false).NextInput?.Candidates ?? [],
            c => c.Action == UnitAction.Fireball);
    }

    [Theory]
    [InlineData("Renamed")]
    [InlineData("Limited")]
    public void SpentNativeUsesRemoveFireballFromPlanningAfterSave(string source)
    {
        var state = Ready(State(source));
        var result = TestGame.Advance(state, new Choice(Attack(state).Key), new Dice(), false);
        var restored = Restore(result.State);
        Assert.False(new GameplayQueries(restored).CanAttackHostileFrom("actor", new(4, 0)));
        Assert.Null(new GameplayQueries(restored).DistanceToAttackPositionFrom("actor", new(4, 0)));
        var committed = Assert.Single(result.ResolutionSteps.Where(step => result.Events[step.EventIndex].Kind == "ActionUsed"));
        Assert.Equal(1, committed.StateAfter.ActionsUsedThisActivation);
        if (source == "Renamed") Assert.Equal(0, restored.Units[0].Uses("fireball", 1).RemainingUses);
        else Assert.Equal(1, restored.Units[0].AbilityUseCounts["unit:cast"]);
    }

    [Fact]
    public void NativeAbilityCountersRemainIndependentWhilePlanningFindsAnyAvailableSource()
    {
        var state = State("Limited");
        var fireball = Assert.Single(state.Types[0].LocalAbilities);
        state.Types[0] = state.Types[0] with { LocalAbilities = [fireball with { Key = "first" }, fireball with { Key = "second" }] };
        state.ExtraActionsThisActivation = 1;
        state = Ready(state);
        state = Restore(TestGame.Advance(state, new Choice(Attack(state, "first").Key), new Dice(), false).State);
        Assert.Equal(1, state.Units[0].AbilityUseCounts["unit:first"]);
        Assert.False(state.Units[0].AbilityUseCounts.ContainsKey("unit:second"));
        Assert.True(new GameplayQueries(state).CanAttackHostileFrom("actor", new(4, 0)));

        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        Assert.DoesNotContain(state.Pending!.Candidates, c => c.Ability?.Key == "first");
        state = TestGame.Advance(state, new Choice(Attack(state, "second").Key), new Dice(), false).State;
        Assert.Equal(1, state.Units[0].AbilityUseCounts["unit:second"]);
        Assert.False(new GameplayQueries(state).CanAttackHostileFrom("actor", new(4, 0)));
    }

    [Fact]
    public void ItemPlanningTracksPhysicalCopiesAndConsumptionWithoutInventingCharges()
    {
        var state = State("Item");
        state.ExtraActionsThisActivation = 1;
        state = Ready(state);
        var attack = Attack(state, instanceId: "spark-one");
        Assert.True(new GameplayQueries(state).IsUnlimitedAttack("actor", attack));
        state = Restore(TestGame.Advance(state, new Choice(attack.Key), new Dice(), false).State);
        Assert.Equal("spark-two", Assert.Single(state.Units[0].Inventory).Id);
        Assert.Equal("spark-one", Assert.Single(state.LootDeck!.DiscardPile).Id);
        Assert.Empty(state.Units[0].AbilityUseCounts);
        Assert.True(new GameplayQueries(state).CanAttackHostileFrom("actor", new(4, 0)));
        Assert.Equal(1, state.ActionsUsedThisActivation);

        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        state = TestGame.Advance(state, new Choice(Attack(state, instanceId: "spark-two").Key), new Dice(), false).State;
        Assert.Empty(state.Units[0].Inventory);
        Assert.Equal(2, state.LootDeck!.DiscardPile.Count);
        Assert.False(new GameplayQueries(state).CanAttackHostileFrom("actor", new(4, 0)));
    }

    [Theory]
    [InlineData("Renamed", false)]
    [InlineData("Limited", false)]
    [InlineData("Unlimited", true)]
    [InlineData("Equipment", true)]
    public void AttackUsageClassificationComesFromTheSelectedDefinition(string source, bool unlimited)
    {
        var state = Ready(State(source));
        Assert.Equal(unlimited, new GameplayQueries(state).IsUnlimitedAttack("actor", Attack(state)));
    }

    [Fact]
    public void OtherwiseTiedAttacksPreferUnlimitedEquipmentOverLimitedNativeFireball()
    {
        var state = State("Limited");
        state.EquipmentTypes[0] = state.EquipmentTypes[0] with { LocalAbilities = [
            new("shine", "Endless Light", AbilityTiming.Action, new FireballMechanic())] };
        state.Units[0] = state.Units[0] with { Inventory = [new("lantern-copy", "lantern")] };
        state = Ready(state);
        var native = Attack(state, "cast");
        var equipment = Attack(state, "shine");
        var request = state.Pending! with { Kind = DecisionKind.Act, Candidates = [native, equipment], AllowsNone = true };
        Assert.Equal(equipment.Key, new DefaultAutomatedProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void AutomatedMovementApproachesAndThenStaysForEquipmentOnlyFireball()
    {
        var state = Ready(State("Equipment"));
        Assert.Equal(UnitAction.None, state.Types[0].Actions);
        state.MoveDone = false;
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(0, 0) };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var moves = state.Pending! with { Kind = DecisionKind.Move, AllowsNone = true,
            Candidates = state.Pending.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToList() };
        var next = moves.Candidates.Single(c => c.Destination == new Cell(1, 0));
        Assert.Equal(next.Key, new DefaultAutomatedProvider().Choose(moves, new GameplayQueries(state)));

        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new(4, 0) };
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        moves = state.Pending! with { Kind = DecisionKind.Move, AllowsNone = true,
            Candidates = state.Pending.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToList() };
        Assert.Null(new DefaultAutomatedProvider().Choose(moves, new GameplayQueries(state)));
    }

    [Theory]
    [InlineData("Equipment")]
    [InlineData("Item")]
    public void SavedCommittedAttackReplaysIdenticalEventsAndResolutionSnapshots(string source)
    {
        var state = Ready(State(source));
        var candidate = Attack(state, instanceId: source == "Item" ? "spark-one" : "lantern-copy");
        var original = GameEngine.Advance(state, new Choice(candidate.Key), new Dice(), false);
        Assert.Equal(DecisionKind.RollDice, original.NextInput!.Kind);
        Assert.Equal(1, original.State.ActionsUsedThisActivation);
        var replay = GameEngine.RefreshChoices(Restore(original.State), new Dice(), false);
        Assert.Equal(original.NextInput.Candidates.Select(c => c.Key), replay.NextInput!.Candidates.Select(c => c.Key));
        while (original.NextInput?.Kind == DecisionKind.RollDice)
        {
            original = GameEngine.Advance(original.State, new Choice("roll-dice"), new Dice(), false);
            replay = GameEngine.Advance(replay.State, new Choice("roll-dice"), new Dice(), false);
            Assert.Equal(JsonSerializer.Serialize(original.Events), JsonSerializer.Serialize(replay.Events));
            Assert.Equal(JsonSerializer.Serialize(original.ResolutionSteps), JsonSerializer.Serialize(replay.ResolutionSteps));
            Assert.Equal(JsonSerializer.Serialize(original.State), JsonSerializer.Serialize(replay.State));
        }
        Assert.Null(replay.State.CurrentUnitId);
        Assert.Equal(0, replay.State.ActionsUsedThisActivation); // Completed activations reset their budget.
        Assert.True(new GameplayQueries(replay.State).CanAttackHostileFrom("actor", new(4, 0)));
    }

    [Theory]
    [InlineData("Equipment")]
    [InlineData("Item")]
    public void AcquiredFireballPlanningPreservesPostureLosAndFogOfWar(string source)
    {
        var state = State(source);
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 0), ["enemy"]));
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Upright };
        state.Physical.Board.Edges.Add(new(new(4, 0), new(5, 0), EdgeKind.Wall));
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 0), ["enemy"]));
        state.Physical.Board.Edges.Clear();
        state.FogOfWarSides.Add("blue");
        state.ExploredCells["blue"] = [new(0, 0), new(4, 0), new(6, 0)];
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 0), ["enemy"]));
        state.ExploredCells["blue"].Add(new(7, 0));
        state.ExploredCells["blue"].Remove(new(6, 0));
        Assert.False(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 0), ["enemy"]));
        state.ExploredCells["blue"].Add(new(6, 0));
        var before = JsonSerializer.Serialize(state);
        Assert.True(new GameplayQueries(state).CanAttackAnyFrom("actor", new(4, 0), ["enemy"]));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }
}
