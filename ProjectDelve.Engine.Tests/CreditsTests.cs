using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CreditsTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
        public int LootDrawIndex(int exclusiveMax) => 0;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static ScenarioDefinition Definition(UnitType? actor = null) => new()
    {
        Board = new() { Width = 9, Height = 6 },
        UnitTypeIds = ["actor", "other"],
        UnitTypes = [actor ?? new("actor", 4, 1, 2, 0, 1), new("other", 2, 1, 1, 0, 1)],
        Units = [new("actor", "blue", new(1, 1), UnitId: "a"), new("other", "red", new(7, 4), UnitId: "b")],
        Agency = [new("actor", "blue", ControllerKind.Human), new("other", "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition? definition = null)
    {
        var state = GameEngine.CreateGame(definition ?? Definition());
        state.Round = 1; state.ActiveToken = state.Units[0].Token; state.CurrentUnitId = "a";
        state.ResolvingTokenUnitIds = ["a"];
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static EngineResult Do(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);
    private static EngineResult Use(GameState state, Func<Candidate, bool> match)
    {
        var ready = GameEngine.RefreshChoices(state, new Dice(), false);
        return Do(ready.State, ready.NextInput!.Candidates.First(match).Key);
    }

    [Fact]
    public void SetupMergesPilesDefaultsBalancesAndRoundTripsThroughAllBoundaries()
    {
        var definition = Definition() with
        {
            SideCredits = new() { ["blue"] = 9 },
            CreditsPiles = [new(new(2, 1), 3), new(new(2, 1), 4), new(new(3, 1), 2)]
        };
        var transported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var state = GameEngine.CreateGame(transported);
        Assert.Equal(9, state.SideCredits["blue"]); Assert.Equal(0, state.SideCredits.GetValueOrDefault("red"));
        Assert.Equal(new[] { new CreditsPile(new(2, 1), 7), new(new(3, 1), 2) }, state.CreditsPiles);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.CreditsPiles, restored.CreditsPiles);
        Assert.Equal(state.SideCredits, restored.SideCredits);
        var copy = state.DetachedCopy(); copy.SideCredits["blue"] = 0; copy.CreditsPiles.Clear();
        Assert.Equal(9, state.SideCredits["blue"]); Assert.Equal(2, state.CreditsPiles.Count);
        Assert.DoesNotContain("additionalSideIds", ScenarioDefinitionJson.ToJson(Definition()));
    }

    [Fact]
    public void AdditionalSidesNeedNoUnitsOrControllersAndRemainValidAfterRestore()
    {
        var definition = Definition() with { AdditionalSideIds = ["away"], SideCredits = new() { ["away"] = 12 } };
        var state = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
        Assert.Equal(12, state.SideCredits["away"]);
        Assert.Contains(state.Objectives, o => o.SideId == "away" && o.Objective == ScenarioObjective.EliminateEnemies);
        Assert.DoesNotContain(state.Controllers, c => c.Token.SideId == "away");
        GameEngine.ValidateScenario(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { AdditionalSideIds = ["away", "away"] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { AdditionalSideIds = [" "] }));
    }

    [Fact]
    public void InvalidBalancesPilesAndPotentialCollectionOverflowFailAtTheSharedBoundary()
    {
        var definition = Definition();
        foreach (var balances in new[] { new Dictionary<string, int> { ["blue"] = -1 }, new() { ["unknown"] = 1 }, new() { [" "] = 0 } })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { SideCredits = balances }));
        foreach (var piles in new List<CreditsPile>[] { [new(new(2, 1), 0)], [new(new(2, 1), -1)], [new(new(9, 0), 1)],
                     [new(new(2, 1), int.MaxValue), new(new(2, 1), 1)], [null!] })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { CreditsPiles = piles }));
        var overflow = definition with { SideCredits = new() { ["blue"] = int.MaxValue }, CreditsPiles = [new(new(2, 1), 1)] };
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(overflow)));
        var state = Ready(); state.SideCredits["blue"] = -1;
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(state));
    }

    [Theory]
    [InlineData("stay", 1)]
    [InlineData("2,1", 2)]
    public void MoveAndStayCollectWithoutOwnershipPermissionAndSnapshotsAreDetached(string key, int x)
    {
        // Keep an adjacent Attack available after either destination so both
        // runs pause in the same activation with observable Action accounting.
        var definition = Definition();
        var state = Ready(definition with
        { Units = [definition.Units[0], definition.Units[1] with { Anchor = new(2, 2) }] });
        state.SideCredits["blue"] = 5;
        state.CreditsPiles = [new(new(x, 1), 4), new(new(3, 1), 7)];
        var result = Do(state, key);
        var withoutCredits = state.DetachedCopy();
        withoutCredits.CreditsPiles.Clear();
        var ordinary = Do(withoutCredits, key);
        Assert.Equal(ordinary.State.ActionsUsedThisActivation, result.State.ActionsUsedThisActivation);
        Assert.Equal(ordinary.State.BonusActionUseCountsThisActivation, result.State.BonusActionUseCountsThisActivation);
        Assert.Equal(ordinary.NextInput!.Candidates.Select(c => c.Key), result.NextInput!.Candidates.Select(c => c.Key));
        var collected = Assert.Single(result.Events, e => e.Kind == "CreditsCollected");
        Assert.Equal("a", collected.UnitId); Assert.Equal("blue", collected.SideId);
        Assert.Equal(new Cell(x, 1), collected.Cell); Assert.Equal(4, collected.CreditsAmount); Assert.Equal(9, collected.CreditsBalance);
        Assert.Equal(9, result.State.SideCredits["blue"]); Assert.Equal(5, state.SideCredits["blue"]);
        Assert.Single(result.State.CreditsPiles);
        var step = result.ResolutionSteps.Single(s => s.EventIndex == result.Events.IndexOf(collected));
        Assert.Equal(9, step.StateAfter.SideCredits["blue"]);
        var replay = Do(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!, key);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
    }

    [Fact]
    public void OrdinaryMoveDoesNotCollectTransitPilesAndActivationStartDoesNotCollect()
    {
        var state = Ready();
        state.PlaceCredits(2, new(1, 1));
        state.PlaceCredits(3, new(2, 1));
        state.PlaceCredits(2, new(4, 1));
        state.PlaceCredits(3, new(4, 1));
        Assert.Equal(3, state.CreditsPiles.Count);
        Assert.Throws<ArgumentException>(() => state.PlaceCredits(int.MaxValue, new(4, 1)));
        Assert.Equal(5, state.CreditsPiles.Single(p => p.Position == new Cell(4, 1)).Amount);
        Assert.Equal(0, state.SideCredits.GetValueOrDefault("blue"));
        var refreshed = GameEngine.RefreshChoices(state, new Dice(), false);
        Assert.DoesNotContain(refreshed.Events, e => e.Kind == "CreditsCollected");
        var result = Do(state, "4,1");
        Assert.Equal(5, result.State.SideCredits["blue"]); Assert.Equal(2, result.State.CreditsPiles.Count);
    }

    [Fact]
    public void InitialPlacementCollectsOnceInBoardOrderIncludingLyingUnits()
    {
        var definition = Definition() with { Units = [new("actor", "blue", new(1, 1), Posture.Lying, UnitId: "a"),
            new("other", "red", new(7, 4), UnitId: "b")], CreditsPiles = [new(new(7, 4), 7), new(new(1, 1), 2)] };
        var initial = GameEngine.CreateGame(definition);
        Assert.Equal(0, initial.SideCredits.GetValueOrDefault("blue"));
        var started = GameEngine.StartRound(initial, new Dice(), false);
        Assert.Equal(new[] { "a", "b" }, started.Events.Where(e => e.Kind == "CreditsCollected").Select(e => e.UnitId));
        Assert.Equal(2, started.State.SideCredits["blue"]); Assert.Equal(7, started.State.SideCredits["red"]);
    }

    [Fact]
    public void NeutralUnitCollectsCreditsFromACellSharedWithCoalAndGroundEquipment()
    {
        var cell = new Cell(1, 1);
        var definition = Definition() with
        {
            NeutralSides = ["blue"],
            CreditsPiles = [new(cell, 6)],
            Resources = [new(ResourceTypes.Coal, cell)],
            GroundObjects = [new(new("ground-key", LootCardKind.OfficialKey), cell)]
        };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
        Assert.Equal(6, result.State.SideCredits["blue"]);
        Assert.Equal(cell, Assert.Single(result.State.Resources).Position);
        Assert.Equal("ground-key", Assert.Single(result.State.GroundObjects).Object.Id);
        Assert.Empty(result.State.CreditsPiles);
        Assert.Empty(result.State.Units.Single(u => u.Id == "a").Inventory);
    }

    [Fact]
    public void MultiCellPickupUsesCellOrderAndCreditsBeforeSameCellLootAfterHazards()
    {
        var actor = new UnitType("actor", 4, 1, 2, 0, 1) { Footprint = Footprint.TwoByTwo };
        var definition = Definition(actor) with { Loot = new([LootCardKind.PocketBreakfast, LootCardKind.PocketBreakfast]) };
        var state = Ready(definition);
        state.CreditsPiles = [new(new(3, 2), 7), new(new(3, 1), 3)];
        state.LootMarkers = [new("first", new(2, 1)), new("second", new(3, 1))];
        state.CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 1))];
        var result = Do(state, "2,1");
        Assert.Equal(new[] { "PostureChanged", "LootCardDrawn", "CreditsCollected", "LootCardDrawn", "CreditsCollected" },
            result.Events.Where(e => e.Kind is "PostureChanged" or "LootCardDrawn" or "CreditsCollected").Select(e => e.Kind));
        Assert.Equal(10, result.State.SideCredits["blue"]); Assert.Empty(result.State.CreditsPiles);
    }

    [Fact]
    public void SwapCollectsForBothSidesAfterBothHazardsAndActorFirst()
    {
        var actor = new UnitType("actor", 4, 1, 2, 0, 1) { LocalAbilities = [new("swap", "Swap", AbilityTiming.BonusAction, new Swap())] };
        var definition = Definition(actor);
        definition = definition with { Units = [definition.Units[0], definition.Units[1] with { Anchor = new(2, 1) }] };
        var state = Ready(definition);
        state.CreditsPiles = [new(new(1, 1), 8), new(new(2, 1), 3)];
        state.CellMarkers = [new(CellMarkerTypes.BananaPeel, new(1, 1)), new(CellMarkerTypes.BananaPeel, new(2, 1))];
        var result = Use(state, c => c.Ability?.Definition.Mechanic is Swap);
        Assert.Equal(new[] { "a", "b" }, result.Events.Where(e => e.Kind == "CreditsCollected").Select(e => e.UnitId));
        Assert.True(result.Events.FindLastIndex(e => e.Kind == "PostureChanged") < result.Events.FindIndex(e => e.Kind == "CreditsCollected"));
        Assert.Equal(3, result.State.SideCredits["blue"]); Assert.Equal(8, result.State.SideCredits["red"]);
    }

    [Fact]
    public void FriendlySwapAccumulatesIntoOneSharedSideBalance()
    {
        var actor = new UnitType("actor", 4, 1, 2, 0, 1) { LocalAbilities = [new("swap", "Swap", AbilityTiming.BonusAction,
            new Swap { Selector = RelationshipSelector.Friendlies })] };
        var definition = Definition(actor);
        definition = definition with
        {
            Units = [definition.Units[0], new("other", "blue", new(2, 1), UnitId: "b")],
            Agency = [definition.Agency[0], new("other", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)]
        };
        var state = Ready(definition); state.SideCredits["blue"] = 5;
        state.CreditsPiles = [new(new(1, 1), 8), new(new(2, 1), 3)];
        var result = Use(state, c => c.Ability?.Definition.Mechanic is Swap);
        Assert.Equal(new int?[] { 8, 16 }, result.Events.Where(e => e.Kind == "CreditsCollected").Select(e => e.CreditsBalance));
        Assert.Equal(16, result.State.SideCredits["blue"]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void DisplacementIncludingStayCreditsTheAffectedSide(int x)
    {
        var actor = new UnitType("actor", 4, 1, 2, 0, 1) { LocalAbilities = [new("displace", "Displace", AbilityTiming.BonusAction, new Displace(1))] };
        var definition = Definition(actor);
        definition = definition with { Units = [definition.Units[0], definition.Units[1] with { Anchor = new(2, 1) }] };
        var state = Ready(definition); state.CreditsPiles = [new(new(x, 1), 6)];
        var result = Use(state, c => c.Ability?.Definition.Mechanic is Displace && c.Destination == new Cell(x, 1));
        Assert.Equal(6, result.State.SideCredits["red"]); Assert.Equal(0, result.State.SideCredits.GetValueOrDefault("blue"));
        Assert.Equal("b", Assert.Single(result.Events, e => e.Kind == "CreditsCollected").UnitId);
    }

    [Fact]
    public void SummonCollectsImmediatelyOnEveryOccupiedCell()
    {
        var actor = new UnitType("actor", 4, 1, 2, 0, 1) { LocalAbilities = [new("summon", "Summon", AbilityTiming.Action,
            new AdjacentUnitSummonMechanic("other", Posture.Lying))] };
        var state = Ready(Definition(actor));
        state.MoveDone = true;
        var ready = GameEngine.RefreshChoices(state, new Dice(), false);
        var candidate = ready.NextInput!.Candidates.First(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic);
        ready.State.CreditsPiles = [new(candidate.Destination!, 11)];
        var result = Do(ready.State, candidate.Key);
        var created = Assert.Single(result.Events, e => e.Kind == "UnitCreated");
        Assert.Equal(created.UnitId, Assert.Single(result.Events, e => e.Kind == "CreditsCollected").UnitId);
        Assert.Equal(11, result.State.SideCredits["blue"]);
    }

    [Fact]
    public void RunRailsCollectsEachStepBeforeContinuingAndStopsAfterBananaPeel()
    {
        var actor = new UnitType("actor", 0, 0, 0, 0, 1, Actions: UnitAction.None)
            { LocalAbilities = [new("rails", "Run Rails", AbilityTiming.InsteadOfActivation, new RunRails(4))] };
        var definition = Definition(actor) with
        {
            Board = new() { Width = 9, Height = 6, Cells = Enumerable.Range(1, 5)
                .Select(x => new CellDefinition(new(x, 1), TerrainKind.Rail, RailDirection.East)).ToList() },
            CreditsPiles = [new(new(2, 1), 2), new(new(3, 1), 3), new(new(4, 1), 4)],
            CellMarkers = [new(CellMarkerTypes.BananaPeel, new(3, 1))]
        };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
        Assert.Equal(new int?[] { 2, 3 }, result.Events.Where(e => e.Kind == "CreditsCollected").Select(e => e.CreditsAmount));
        Assert.Equal(5, result.State.SideCredits["blue"]); Assert.Equal(4, Assert.Single(result.State.CreditsPiles).Amount);
    }

    [Fact]
    public void AdvanceOnKillCollectsAfterTheAttackAndDefeatRetainsSharedBalance()
    {
        var actor = new UnitType("actor", 4, 1, 2, 0, 1) { LocalAbilities = [new("advance", "Advance", AbilityTiming.AfterAttack, new AdvanceOnKill())] };
        var definition = Definition(actor);
        definition = definition with { Units = [definition.Units[0], definition.Units[1] with { Anchor = new(2, 1) }] };
        var state = Ready(definition); state.MoveDone = true; state.CreditsPiles = [new(new(2, 1), 4)];
        var result = Use(state, c => c.Action == UnitAction.NormalAttack);
        while (result.NextInput?.Kind == DecisionKind.RollDice) result = Do(result.State, result.NextInput.Candidates.Single().Key);
        Assert.DoesNotContain(result.Events, e => e.Kind == "CreditsCollected");
        result = Do(result.State, "2,1");
        Assert.Equal(4, result.State.SideCredits["blue"]);
        GameEngine.ResolveDefeat(result.State, "a");
        Assert.Equal(4, result.State.SideCredits["blue"]); Assert.Empty(result.State.CreditsPiles);
    }

    [Fact]
    public void DefeatDuringArrivalConsequencesPreventsCollectionAndPreservesPile()
    {
        var train = new UnitType("actor", 0, 0, 0, 0, 1, Actions: UnitAction.None)
            { LocalAbilities = [new("rails", "Run Rails", AbilityTiming.InsteadOfActivation, new RunRails(4))] };
        var bomb = new UnitType("other", 0, 0, 0, 0, 1)
            { LocalAbilities = [new("blast", "Explosion", AbilityTiming.WhenDefeated, new Explosion { Damage = 1 })] };
        var definition = Definition(train) with
        {
            UnitTypes = [train, bomb],
            Units = [new("actor", "blue", new(1, 1), UnitId: "a"), new("other", "red", new(2, 1), Posture.Lying, UnitId: "b"),
                new("other", "red", new(3, 1), UnitId: "secondary")],
            Board = new() { Width = 9, Height = 6, Cells = Enumerable.Range(1, 5)
                .Select(x => new CellDefinition(new(x, 1), TerrainKind.Rail, RailDirection.East)).ToList() },
            SideCredits = new() { ["blue"] = 9 }, CreditsPiles = [new(new(2, 1), 7)]
        };
        var state = GameEngine.CreateGame(definition);
        // Isolate a later-round arrival from the initial-placement pickup boundary.
        state.Round = 1; state.RoundComplete = true;
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(0, result.State.Units.Single(u => u.Id == "a").CurrentHp);
        Assert.DoesNotContain(result.Events, e => e.Kind == "CreditsCollected");
        Assert.Equal(9, result.State.SideCredits["blue"]);
        Assert.Equal(new CreditsPile(new(2, 1), 7), Assert.Single(result.State.CreditsPiles));
    }
}
