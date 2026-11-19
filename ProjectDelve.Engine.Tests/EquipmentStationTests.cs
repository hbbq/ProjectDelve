using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed partial class EquipmentStationTests
{
    private sealed class Dice : IRandomProvider
    {
        public int Calls { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) { Calls++; return bag[0]; }
        public AttackFace RollAttackDie() { Calls++; return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { Calls++; return DefenceFace.Miss; }
        public int RollD6() { Calls++; return 1; }
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    private static LocalAbilityDefinition Passive(string key, AbilityMechanic mechanic) =>
        new(key, "Same display name", AbilityTiming.Passive, mechanic);
    private static UnitType Carrier(string id) => new(id, 3, 2, 2, 1, 6) {
        Unique = true, CanHaveEquipment = true, CanHaveItems = true,
        LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate()),
            Passive("self", new SelfModifiersMechanic([new(Stat.Atk, 2)]))] };
    private static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 12, Height = 8 },
        UnitTypeIds = ["carrier", "carrier-b", "carrier-c", "incapable"],
        UnitTypes = [Carrier("carrier"), Carrier("carrier-b"), Carrier("carrier-c"),
            new("incapable", 1, 1, 1, 1, 1)],
        EquipmentTypes = [new("boots", "Shared name") {
            LocalAbilities = [Passive("move", new SelfModifiersMechanic([new(Stat.Mov, 2)]))] },
            new("bell", "Shared name") { LocalAbilities = [new("ring", "Same display name", AbilityTiming.BonusAction,
                new SelfModifiersMechanic([new(Stat.Atk, 1)])) { Usage = new(3) }] },
            new("tube", "Tube") { LocalAbilities = [Passive("swim", new Amphibious())] }],
        Units = [new("carrier", "blue", new(1, 1), UnitId: "a") {
                Inventory = [new("boots-a", "boots"), new("item-one", LootCardKind.SecondWind), new("item-two", LootCardKind.SecondWind)] },
            new("carrier-b", "blue", new(8, 1), Posture: Posture.Lying, InitialHp: 2, UnitId: "b") {
                Inventory = [new("bell-b", "bell") { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("ring", 2) }] },
            new("carrier-c", "blue", new(8, 5), UnitId: "c", Dormant: true, WakeDistance: 0) { Inventory = [new("boots-c", "boots")] },
            new("incapable", "blue", new(5, 6), UnitId: "incapable"),
            new("carrier", "red", new(11, 7), UnitId: "enemy") { Inventory = [new("foreign", "bell")] }],
        Agency = [new("carrier", "blue", ControllerKind.Human), new("carrier-b", "blue", ControllerKind.Human),
            new("carrier-c", "blue", ControllerKind.Human), new("carrier", "red", ControllerKind.Human),
            new("incapable", "blue", ControllerKind.Human)],
        Machines = [new("station", new(2, 1), MachineTypes.EquipmentStation)],
        GroundObjects = [new(new("ground", "tube"), new(3, 4))],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };

    private static GameState Ready(ScenarioDefinition? definition = null)
    {
        var state = GameEngine.CreateGame(definition ?? Fixture());
        state.Round = 1; state.RoundComplete = false; state.ActiveToken = new("carrier", "blue");
        state.CurrentUnitId = "a"; state.MoveDone = true;
        state.ResolvingTokenUnitIds = ["a"];
        state.Pending = new(DecisionKind.Activation, "carrier", "a", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static EngineResult Open(GameState? state = null)
    {
        state ??= Ready();
        var candidate = state.Pending!.Candidates.First(c => c.Action == UnitAction.Operate && c.MachineId == "station");
        return GameEngine.Advance(state, new Choice(candidate.Key), new Dice(), false);
    }
    private static EquipmentAllocation Allocation(GameState state, params (string Object, string Owner)[] changes) =>
        new([.. state.Units.Where(u => u.SideId == "blue" && u.CurrentHp > 0)
            .SelectMany(u => u.Inventory.Where(o => o.Id != state.MachineInteraction?.ReservedConsumptionObjectId).Select(o => new EquipmentAssignment(o.Id,
                changes.FirstOrDefault(c => c.Object == o.Id) is var change && change.Owner is not null ? change.Owner : u.Id)))]);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static OwnedObject Object(GameState state, string unitId, string objectId) =>
        state.Units.Single(u => u.Id == unitId).Inventory.Single(o => o.Id == objectId);

    private static ScenarioDefinition SalesFixture()
    {
        var definition = Fixture();
        return definition with { EquipmentTypes = [.. definition.EquipmentTypes!.Select(e => e with {
            SellValue = e.Id == "boots" ? 4 : e.Id == "bell" ? 0 : null })],
            ItemTypes = [new("sale-item", "Sale item") { SellValue = 3,
                LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate())] }] };
    }

    [Fact]
    public void LevelledEquipmentCanOperateTransferAndSellWithLiveHpAndUses()
    {
        var scene = SalesFixture();
        var opener = new LocalAbilityDefinition("service", "Service", AbilityTiming.Action, new Operate()) { Usage = new(2) };
        var boots = scene.EquipmentTypes![0];
        scene.EquipmentTypes[0] = boots with { AdditionalLevels = [new() { MaxHpModifier = 2, LocalAbilities = [opener] }] };
        scene.UnitTypes![0] = scene.UnitTypes[0] with { LocalAbilities = [] };
        scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!.Select(o => o.Id == "boots-a" ? o with { Level = 2 } : o)] };
        var state = Open(Ready(scene)).State;
        var draft = Allocation(state, ("boots-a", "b"));
        var preview = GameEngine.PreviewEquipmentStation(state, draft);
        Assert.True(preview.CanConfirm);
        Assert.Equal(6, preview.Units.Single(p => p.UnitId == "a").Hp);
        Assert.Equal(8, preview.Units.Single(p => p.UnitId == "b").Hp);
        Assert.Equal(8, state.Units.Single(u => u.Id == "a").CurrentHp);
        var random = new Dice();
        var result = GameEngine.ConfirmEquipmentStation(state, draft, random, false);
        Assert.Equal(0, random.Calls);
        Assert.Equal(6, result.State.Units.Single(u => u.Id == "a").CurrentHp);
        Assert.Equal(2, result.State.Units.Single(u => u.Id == "b").CurrentHp);
        Assert.Equal(1, Object(result.State, "b", "boots-a").AbilityUseCounts["service"]);
        Assert.Equal(2, Object(result.State, "b", "boots-a").Level);
        Assert.DoesNotContain(result.Events, e => e.Kind.Contains("Damage") || e.Kind.Contains("Defeat"));
        var sale = Sell(state, ["boots-a"]);
        var sold = GameEngine.ConfirmEquipmentStation(state, sale, random, false);
        Assert.Equal(4, sold.State.SideCredits["blue"]);
        Assert.Equal(6, sold.State.Units.Single(u => u.Id == "a").CurrentHp);
        Assert.DoesNotContain(sold.State.Units.SelectMany(u => u.Inventory), o => o.Id == "boots-a");
        Assert.Equal(Json(sold), Json(GameEngine.ConfirmEquipmentStation(JsonSerializer.Deserialize<GameState>(Json(state))!, sale, new Dice(), false)));
        Assert.Equal(0, random.Calls);
    }

    private static EquipmentAllocation Sell(GameState state, string[] sold, params (string Object, string Owner)[] changes)
    {
        var draft = Allocation(state, changes);
        return draft with { Assignments = [.. draft.Assignments.Where(a => !sold.Contains(a.ObjectId))], SoldObjectIds = [.. sold] };
    }

    [Fact]
    public void SellingOneOfTwoChargedCopiesUsesItsLiveTypeValueAndTheLiveSideBalance()
    {
        var scene = SalesFixture();
        scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!, new("fresh-bell", "bell")] };
        var state = Open(Ready(scene)).State;
        var draft = Sell(state, ["bell-b"]);
        var first = GameEngine.PreviewEquipmentStation(state, draft);
        Assert.True(first.CanConfirm);
        Assert.Equal(0L, first.ResultingCredits);
        var index = state.EquipmentTypes.FindIndex(type => type.Id == "bell");
        state.EquipmentTypes[index] = state.EquipmentTypes[index] with { SellValue = 6 };
        state.SideCredits["blue"] = 10;
        Assert.Equal(2, Object(state, "b", "bell-b").AbilityUseCounts["ring"]);
        Assert.Equal(6, state.SellValueOf(Object(state, "a", "fresh-bell")));
        Assert.Equal(6, state.SellValueOf(Object(state, "b", "bell-b")));
        var committed = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false);
        Assert.Equal(16, committed.State.SideCredits["blue"]);
        Assert.DoesNotContain(committed.State.Units.SelectMany(u => u.Inventory), obj => obj.Id == "bell-b");
        Assert.Empty(Object(committed.State, "a", "fresh-bell").AbilityUseCounts);
    }

    [Fact]
    public void SalesAndTransfersSettleTogetherWithPhysicalIdentityAndPrivateDeterministicProceeds()
    {
        var definition = SalesFixture();
        definition.Units[0] = definition.Units[0] with { Inventory = [.. definition.Units[0].Inventory!,
            new("sale-copy-one") { ItemTypeId = "sale-item" }, new("sale-copy-two") { ItemTypeId = "sale-item" }] };
        var state = Open(Ready(definition)).State;
        state.SideCredits["blue"] = 7;
        var before = Json(state);
        var allocation = Sell(state, ["sale-copy-one", "boots-c"], ("boots-a", "c"), ("bell-b", "a"));
        var preview = GameEngine.PreviewEquipmentStation(state, allocation);
        Assert.True(preview.CanConfirm);
        Assert.Equal(7L, preview.TotalProceeds);
        Assert.Equal(7, preview.CurrentCredits);
        Assert.Equal(14L, preview.ResultingCredits);
        Assert.Equal(4, state.SellValueOf(Object(state, "c", "boots-c")));
        Assert.DoesNotContain(preview.Units.SelectMany(u => u.Allocation), o => o.Id == "boots-c" || o.Id == "sale-copy-one");
        var random = new Dice();
        var result = GameEngine.ConfirmEquipmentStation(state, allocation, random, false);
        var replay = GameEngine.ConfirmEquipmentStation(JsonSerializer.Deserialize<GameState>(before)!,
            allocation with { Assignments = [.. allocation.Assignments.Reverse()], SoldObjectIds = [.. allocation.SoldObjectIds.Reverse()] }, new Dice(), false);
        Assert.Equal(Json(result), Json(replay));
        Assert.Equal(before, Json(state));
        Assert.Equal(0, random.Calls);
        Assert.Equal(14, result.State.SideCredits["blue"]);
        Assert.Equal("sale-copy-two", Object(result.State, "a", "sale-copy-two").Id);
        Assert.Equal(2, Object(result.State, "a", "bell-b").AbilityUseCounts["ring"]);
        Assert.Equal("boots-a", Object(result.State, "c", "boots-a").Id);
        Assert.DoesNotContain(result.State.LootDeck!.DiscardPile, o => o.Id == "boots-c" || o.Id == "sale-copy-one");
        var occurrence = Assert.Single(result.Events, e => e.Kind == "MachineOperated");
        Assert.Equal(new[] { new EquipmentSale("boots-c", "c", 4), new EquipmentSale("sale-copy-one", "a", 3) }, occurrence.EquipmentSales);
        var opposing = FogOfWar.ObserveEvent(result.State, "red", occurrence)!;
        Assert.Empty(opposing.EquipmentSales);
        Assert.Null(opposing.CreditsAmount);
        Assert.Null(opposing.CreditsBalance);
        foreach (var step in result.ResolutionSteps)
        {
            Assert.Equal(14, step.StateAfter.SideCredits["blue"]);
            Assert.DoesNotContain(step.StateAfter.Units.SelectMany(u => u.Inventory), o => o.Id == "boots-c" || o.Id == "sale-copy-one");
            Assert.Equal(1L, step.StateAfter.Physical.Board.Machines[0].TotalUses);
        }
        GameEngine.ValidateScenario(result.State);
    }

    [Fact]
    public void ZeroValueSaleAloneIsMeaningfulAndCancelPaysNothing()
    {
        var state = Open(Ready(SalesFixture())).State;
        var allocation = Sell(state, ["bell-b"]);
        Assert.True(GameEngine.PreviewEquipmentStation(state, allocation).CanConfirm);
        var canceled = GameEngine.CancelMachineInteraction(state, new Dice(), false).State;
        Assert.Equal("bell-b", Object(canceled, "b", "bell-b").Id);
        Assert.Equal(0, canceled.SideCredits.GetValueOrDefault("blue"));
        var result = GameEngine.ConfirmEquipmentStation(state, allocation, new Dice(), false);
        Assert.Equal(0, result.State.SideCredits["blue"]);
        Assert.DoesNotContain(result.State.Units.SelectMany(u => u.Inventory), o => o.Id == "bell-b");
        Assert.Equal(1L, result.State.Physical.Board.Machines[0].TotalUses);
    }

    [Fact]
    public void SingleInventoryBearerCanSellWithExactOutstandingPileHeadroomAndCannotConfirmTwice()
    {
        var definition = SalesFixture();
        definition = definition with { Units = [definition.Units[0], definition.Units[4]] };
        var state = Open(Ready(definition)).State;
        Assert.Single(state.MachineInteraction!.EquipmentStation!.EligibleUnitIds);
        state.SideCredits["blue"] = int.MaxValue - 5;
        state.PlaceCredits(1, new(4, 4));
        var allocation = Sell(state, ["boots-a"]);
        Assert.True(GameEngine.PreviewEquipmentStation(state, allocation).CanConfirm);
        var result = GameEngine.ConfirmEquipmentStation(state, allocation, new Dice(), false);
        Assert.Equal(int.MaxValue - 1, result.State.SideCredits["blue"]);
        GameEngine.ValidateScenario(result.State);
        Assert.Throws<InvalidOperationException>(() => GameEngine.ConfirmEquipmentStation(result.State, allocation, new Dice(), false));
    }

    [Theory]
    [InlineData("duplicate-sale")]
    [InlineData("assigned-and-sold")]
    [InlineData("foreign")]
    [InlineData("ground")]
    [InlineData("unknown")]
    [InlineData("unsellable")]
    [InlineData("missing")]
    [InlineData("overflow")]
    public void InvalidSalesKeepPendingStateAndRandomUnchanged(string defect)
    {
        var definition = SalesFixture();
        if (defect == "unsellable") definition.Units[0] = definition.Units[0] with {
            Inventory = [.. definition.Units[0].Inventory!, new("unpriced-tube", "tube")] };
        var state = Open(Ready(definition)).State;
        var allocation = Sell(state, ["boots-c"]);
        allocation = defect switch {
            "duplicate-sale" => allocation with { SoldObjectIds = ["boots-c", "boots-c"] },
            "assigned-and-sold" => Allocation(state) with { SoldObjectIds = ["boots-c"] },
            "foreign" => Allocation(state) with { SoldObjectIds = ["foreign"] },
            "ground" => Allocation(state) with { SoldObjectIds = ["ground"] },
            "unknown" => Allocation(state) with { SoldObjectIds = ["absent"] },
            "unsellable" => Sell(state, ["unpriced-tube"]),
            "missing" => allocation with { SoldObjectIds = [] },
            _ => allocation };
        if (defect == "overflow")
        {
            state.SideCredits["blue"] = int.MaxValue - 4;
            state.PlaceCredits(1, new(4, 4));
        }
        var before = Json(state); var random = new Dice();
        Assert.False(GameEngine.PreviewEquipmentStation(state, allocation).CanConfirm);
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmEquipmentStation(state, allocation, random, false));
        Assert.Equal(before, Json(state));
        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void SellingWaterCapabilityChecksFinalPlacementAndReplacementMakesSaleLegal()
    {
        var definition = SalesFixture();
        definition = definition with { EquipmentTypes = [.. definition.EquipmentTypes!.Select(e => e with { SellValue = 2 })],
            Board = definition.Board with { Cells = [new(new(8, 1), TerrainKind.Water)] } };
        definition.Units[1] = definition.Units[1] with { Inventory = [.. definition.Units[1].Inventory!, new("tube-b", "tube")] };
        definition.Units[0] = definition.Units[0] with { Inventory = [.. definition.Units[0].Inventory!, new("tube-a", "tube")] };
        var state = Open(Ready(definition)).State;
        var invalid = Sell(state, ["tube-b"]);
        Assert.Contains(GameEngine.PreviewEquipmentStation(state, invalid).Diagnostics, d => d.Code == "illegal-occupancy" && d.UnitId == "b");
        var valid = Sell(state, ["tube-b"], ("tube-a", "b"));
        Assert.True(GameEngine.PreviewEquipmentStation(state, valid).CanConfirm);
        var result = GameEngine.ConfirmEquipmentStation(state, valid, new Dice(), false);
        Assert.Contains("water", result.State.EffectiveMovementCapabilitiesOf("b"));
        Assert.False(result.State.IsUpright("b"));
        GameEngine.ValidateScenario(result.State);
    }

    [Fact]
    public void OpeningAndCancelingSpendNoActionMachineUseOrObjectAndRetainOrdinaryChoices()
    {
        var before = Ready(); var serialized = Json(before);
        var opened = Open(before);
        Assert.Equal(DecisionKind.MachineInteraction, opened.NextInput!.Kind);
        Assert.Equal("station", opened.State.MachineInteraction!.MachineId);
        Assert.Equal("a", opened.State.MachineInteraction.OperatorId);
        Assert.Equal("blue", opened.State.MachineInteraction.SideId);
        Assert.Equal(0, opened.State.ActionsUsedThisActivation);
        Assert.Equal(0L, opened.State.Physical.Board.Machines[0].TotalUses);
        Assert.Equal(serialized, Json(before));
        var canceled = GameEngine.CancelMachineInteraction(opened.State, new Dice(), false);
        Assert.Null(canceled.State.MachineInteraction);
        Assert.Equal(serialized, Json(canceled.State));
        Assert.Contains(canceled.NextInput!.Candidates, c => c.Action == UnitAction.Operate);
    }

    [Fact]
    public void ConfirmMovesPhysicalItemsAndChargedEquipmentIncludingLyingDormantAndSummonedRecipients()
    {
        var initial = Ready();
        initial.Units[2] = initial.Units[2] with { SummonerUnitId = "a" };
        var state = Open(initial).State; var before = Json(state);
        var allocation = Allocation(state, ("boots-a", "b"), ("bell-b", "c"), ("item-one", "c"), ("item-two", "c"));
        var confirmed = GameEngine.ConfirmEquipmentStation(state, allocation, new Dice(), false);
        Assert.Equal(before, Json(state));
        Assert.Null(confirmed.State.MachineInteraction);
        Assert.Equal("boots", Object(confirmed.State, "b", "boots-a").EquipmentTypeId);
        Assert.Equal(2, Object(confirmed.State, "c", "bell-b").AbilityUseCounts["ring"]);
        Assert.Equal(2, confirmed.State.Units.Single(u => u.Id == "c").Inventory.Count(o => o.Kind == LootCardKind.SecondWind));
        Assert.False(confirmed.State.IsUpright("b"));
        Assert.Equal(2, confirmed.State.Units.Single(u => u.Id == "b").CurrentHp);
        Assert.True(confirmed.State.Units.Single(u => u.Id == "c").Dormant);
        Assert.Equal("a", confirmed.State.Units.Single(u => u.Id == "c").SummonerUnitId);
        Assert.Equal("foreign", Assert.Single(confirmed.State.Units.Single(u => u.Id == "enemy").Inventory).Id);
        Assert.Equal("ground", Assert.Single(confirmed.State.GroundObjects).Object.Id);
        Assert.Equal(1L, confirmed.State.Physical.Board.Machines[0].TotalUses);
        GameEngine.ValidateScenario(confirmed.State);
    }

    [Fact]
    public void SimultaneousCycleCanExchangeSameEquipmentTypesWithoutTransientDuplicates()
    {
        var state = Open().State;
        var allocation = Allocation(state, ("boots-a", "c"), ("boots-c", "b"), ("bell-b", "a"));
        Assert.True(GameEngine.PreviewEquipmentStation(state, allocation).CanConfirm);
        var confirmed = GameEngine.ConfirmEquipmentStation(state, allocation, new Dice(), false);
        Assert.Equal("boots-c", Assert.Single(confirmed.State.Units.Single(u => u.Id == "b").Inventory).Id);
        Assert.Equal("boots-a", Assert.Single(confirmed.State.Units.Single(u => u.Id == "c").Inventory).Id);
        Assert.Equal(2, Object(confirmed.State, "a", "bell-b").AbilityUseCounts["ring"]);
        GameEngine.ValidateScenario(confirmed.State);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("ground")]
    [InlineData("consumed")]
    [InlineData("incapable")]
    [InlineData("enemy")]
    [InlineData("same-type")]
    [InlineData("unknown-unit")]
    [InlineData("no-change")]
    public void InvalidAllocationsHaveDiagnosticsAndNeverMutateAuthoritativeState(string defect)
    {
        var state = Open().State;
        var assignments = Allocation(state, ("bell-b", "a")).Assignments.ToList();
        switch (defect)
        {
            case "missing": assignments.RemoveAt(0); break;
            case "duplicate": assignments.Add(assignments[0]); break;
            case "foreign": assignments.Add(new("foreign", "a")); break;
            case "ground": assignments.Add(new("ground", "a")); break;
            case "consumed": assignments.Add(new("already-consumed-item", "a")); break;
            case "incapable": assignments[0] = assignments[0] with { UnitId = "incapable" }; break;
            case "enemy": assignments[0] = assignments[0] with { UnitId = "enemy" }; break;
            case "same-type": assignments[0] = assignments[0] with { UnitId = "c" }; break;
            case "unknown-unit": assignments[0] = assignments[0] with { UnitId = "missing-unit" }; break;
            case "no-change": assignments = Allocation(state).Assignments.ToList(); break;
        }
        var allocation = new EquipmentAllocation([.. assignments]); var before = Json(state); var random = new Dice();
        var preview = GameEngine.PreviewEquipmentStation(state, allocation);
        Assert.False(preview.CanConfirm);
        Assert.NotEmpty(preview.Diagnostics);
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmEquipmentStation(state, allocation, random, false));
        Assert.Equal(before, Json(state));
        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void ConfirmationIsDeterministicAcrossSerializationAndEveryTransactionSnapshotHasCompleteAllocation()
    {
        var state = Open().State;
        var restored = JsonSerializer.Deserialize<GameState>(Json(state))!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(DecisionKind.MachineInteraction, restored.Pending!.Kind);
        var allocation = Allocation(state, ("bell-b", "a"), ("boots-a", "b"));
        var random = new Dice();
        var result = GameEngine.ConfirmEquipmentStation(state, allocation, random, false);
        var reordered = new EquipmentAllocation([.. allocation.Assignments.Reverse()]);
        var replay = GameEngine.ConfirmEquipmentStation(restored, reordered, new Dice(), false);
        Assert.Equal(Json(GameEngine.PreviewEquipmentStation(state, allocation)),
            Json(GameEngine.PreviewEquipmentStation(restored, reordered)));
        Assert.Equal(Json(result), Json(replay));
        Assert.Equal(0, random.Calls);
        var operated = Assert.Single(result.Events, occurrence => occurrence.Kind == "MachineOperated");
        Assert.Equal(new[] { new EquipmentTransfer("bell-b", "b", "a"), new EquipmentTransfer("boots-a", "a", "b") },
            operated.EquipmentTransfers.ToArray());
        Assert.Empty(FogOfWar.ObserveEvent(result.State, "red", operated)!.EquipmentTransfers);
        foreach (var step in result.ResolutionSteps)
        {
            Assert.Equal("boots-a", Object(step.StateAfter, "b", "boots-a").Id);
            Assert.Equal("bell-b", Object(step.StateAfter, "a", "bell-b").Id);
            Assert.Equal(1L, step.StateAfter.Physical.Board.Machines[0].TotalUses);
            Assert.Null(step.StateAfter.MachineInteraction);
        }
    }

    [Fact]
    public void NormalizedRowsRestoreUprightFullHpAndIncludeSelfPassivesWithoutAuraWorldOrActivationModifiers()
    {
        var initial = Ready();
        for (var i = 0; i < 3; i++)
            initial.Types[i] = initial.Types[i] with { LocalAbilities = [.. initial.Types[i].LocalAbilities,
                Passive("aura", new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies,
                    [new(Stat.Def, 7), new(Stat.Atk, 11)]))] };
        initial.Physical.Figures[1] = initial.Physical.Figures[1] with { Position = new(1, 2) };
        initial.WorldCardDefinitions.Add(new("test-world", "Test world", WorldCardLifecycle.Continuous)
            { Modifiers = [new(WorldModifierTarget.Mov, 20), new(WorldModifierTarget.Atk, 30)] });
        initial.WorldDeck = new() { ActiveContinuous = [new("physical-world", "test-world")] };
        initial.ModifiersThisTurn = [new(Stat.Mov, 9), new(Stat.Atk, 13)];
        var state = Open(initial).State; var before = Json(state);
        var current = GameEngine.PreviewEquipmentStation(state, null);
        var proposed = GameEngine.PreviewEquipmentStation(state, Allocation(state, ("boots-a", "b")));
        Assert.Equal(before, Json(state));
        Assert.False(current.CanConfirm);
        Assert.True(proposed.CanConfirm);
        var a = current.Units.Single(u => u.UnitId == "a");
        var b = current.Units.Single(u => u.UnitId == "b");
        Assert.Equal((5, 2, 4, 1, 6), (a.Mov, a.Rng, a.Atk, a.Def, a.Hp));
        Assert.Equal((3, 2, 4, 1, 6), (b.Mov, b.Rng, b.Atk, b.Def, b.Hp));
        Assert.Equal(3, proposed.Units.Single(u => u.UnitId == "a").Mov);
        Assert.Equal(5, proposed.Units.Single(u => u.UnitId == "b").Mov);
        Assert.Equal(-2, proposed.Units.Single(u => u.UnitId == "a").Difference.Mov);
        Assert.Equal(2, proposed.Units.Single(u => u.UnitId == "b").Difference.Mov);
        Assert.Equal(3, proposed.Units.Single(u => u.UnitId == "b").Baseline.Mov);
        Assert.Contains(b.Abilities, ability => ability.Key == "self");
        Assert.Contains(b.Abilities, ability => ability.Key == "aura");
        Assert.False(b.AbilityPreviews.Single(p => p.Ability.Key == "aura").Applicable);
        Assert.True(b.AbilityPreviews.Single(p => p.Ability.Key == "self").Applicable);
        var charged = b.AbilityPreviews.Single(p => p.Ability.SourceInstanceId == "bell-b" && p.Ability.Key == "ring");
        Assert.Equal(1, charged.RemainingUses);
        Assert.Equal(AbilitySourceKind.Equipment, charged.Ability.SourceKind);
        Assert.Equal("bell", charged.Ability.SourceTypeId);
        Assert.Equal(Json(proposed), Json(GameEngine.PreviewEquipmentStation(state, Allocation(state, ("boots-a", "b")))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingWaterCapabilityRejectsCompleteLiveFootprintEvenWhileLying(bool large)
    {
        var definition = Fixture();
        if (large)
            for (var i = 0; i < 3; i++) definition.UnitTypes![i] = definition.UnitTypes[i] with { Footprint = Footprint.TwoByTwo };
        // Keep the operator clear of the Station when its complete footprint is enlarged.
        definition = definition with { Machines = [new("station", new(0, 1), MachineTypes.EquipmentStation)],
            Board = definition.Board with { Cells = [new(new(8 + (large ? 1 : 0), 1 + (large ? 1 : 0)), TerrainKind.Water)] } };
        definition.Units[1] = definition.Units[1] with { Inventory = [.. definition.Units[1].Inventory!, new("tube-b", "tube")] };
        if (large) definition.Units[4] = definition.Units[4] with { Anchor = new(10, 6) };
        var state = Open(Ready(definition)).State; var before = Json(state);
        var allocation = Allocation(state, ("tube-b", "a"));
        var preview = GameEngine.PreviewEquipmentStation(state, allocation);
        Assert.False(preview.CanConfirm);
        Assert.Contains(preview.Diagnostics, d => d.UnitId == "b");
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmEquipmentStation(state, allocation, new Dice(), false));
        Assert.Equal(before, Json(state));
        Assert.Contains("water", state.EffectiveMovementCapabilitiesOf("b"));
    }

    [Fact]
    public void LossesDescribeTheOpeningPhysicalGrantIncludingOneOfTwoItemsAndEquipmentOnALyingBearer()
    {
        var state = Open().State;
        var preview = GameEngine.PreviewEquipmentStation(state, Allocation(state, ("item-two", "c"), ("bell-b", "a")));
        Assert.True(preview.CanConfirm);
        var item = Assert.Single(preview.Units.Single(u => u.UnitId == "a").LostAbilityPreviews);
        Assert.Equal("item-two", item.Ability.SourceInstanceId);
        Assert.Equal("Second Wind", item.Content.Name);
        Assert.Equal(1, item.RemainingUses);
        var bell = Assert.Single(preview.Units.Single(u => u.UnitId == "b").LostAbilityPreviews);
        Assert.Equal("bell-b", bell.Ability.SourceInstanceId);
        Assert.Equal(bell.Ability.CardEntry(), bell.Content);
        Assert.Equal(1, bell.RemainingUses);
    }

    [Fact]
    public void StationActionGrantedByEquipmentSpendsItsPhysicalChargeBeforeMovingThatSource()
    {
        var definition = Fixture();
        definition.UnitTypes![0] = definition.UnitTypes[0] with {
            LocalAbilities = [.. definition.UnitTypes[0].LocalAbilities.Where(a => a.Mechanic is not Operate)] };
        definition = definition with { EquipmentTypes = [.. definition.EquipmentTypes!,
            new("remote", "Remote") { LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate()) { Usage = new(2) }] }] };
        definition.Units[0] = definition.Units[0] with { Inventory = [.. definition.Units[0].Inventory!,
            new("physical-remote", "remote")] };
        var state = Open(Ready(definition)).State;
        var allocation = Allocation(state, ("physical-remote", "b"));
        var result = GameEngine.ConfirmEquipmentStation(state, allocation, new Dice(), false);
        Assert.Equal(1, Object(result.State, "b", "physical-remote").AbilityUseCounts["operate"]);
        Assert.Empty(result.State.Units.Single(u => u.Id == "a").AbilityUseCounts);
        Assert.Empty(Object(state, "a", "physical-remote").AbilityUseCounts);
        Assert.Single(result.Events, e => e.Kind == "ActionUsed");
        Assert.Single(result.Events, e => e.Kind == "MachineOperated");
        foreach (var step in result.ResolutionSteps)
            Assert.Equal(1, Object(step.StateAfter, "b", "physical-remote").AbilityUseCounts["operate"]);
    }

    [Fact]
    public void InvokingChargedEquipmentCanBeSoldAfterPayingItsUse()
    {
        var definition = SalesFixture();
        definition.UnitTypes![0] = definition.UnitTypes[0] with {
            LocalAbilities = [.. definition.UnitTypes[0].LocalAbilities.Where(a => a.Mechanic is not Operate)] };
        definition = definition with { EquipmentTypes = [.. definition.EquipmentTypes!,
            new("remote", "Remote") { SellValue = 5,
                LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate()) { Usage = new(2) }] }] };
        definition.Units[0] = definition.Units[0] with { Inventory = [.. definition.Units[0].Inventory!, new("physical-remote", "remote")] };
        var state = Open(Ready(definition)).State;
        var allocation = Sell(state, ["physical-remote"]);
        Assert.True(GameEngine.PreviewEquipmentStation(state, allocation).CanConfirm);
        var result = GameEngine.ConfirmEquipmentStation(state, allocation, new Dice(), false);
        Assert.Equal(5, result.State.SideCredits["blue"]);
        Assert.Equal(1, result.State.ActionsUsedThisActivation);
        Assert.DoesNotContain(result.State.Units.SelectMany(u => u.Inventory), o => o.Id == "physical-remote");
        Assert.Empty(result.State.LootDeck!.DiscardPile);
        Assert.Single(result.Events, e => e.Kind == "ActionUsed");
        Assert.Single(result.Events, e => e.Kind == "MachineOperated");
        Assert.Empty(Object(state, "a", "physical-remote").AbilityUseCounts);
        GameEngine.ValidateScenario(result.State);
    }

    [Fact]
    public void ItemAndEquipmentOwnershipCapabilitiesAreIndependentAndDeadOrUnplacedUnitsCannotReceive()
    {
        var state = Ready();
        state.Types.Add(new("item-only", 1, 1, 1, 1, 1) { CanHaveItems = true });
        state.Types.Add(new("gear-only", 1, 1, 1, 1, 1) { CanHaveEquipment = true });
        state.Units.AddRange([new("items", "item-only", "blue", 1), new("gear", "gear-only", "blue", 1),
            new("dead", "carrier", "blue", 0), new("unplaced", "carrier", "blue", 1)]);
        state.Physical.Figures.AddRange([new("items", new(4, 4)), new("gear", new(5, 4))]);
        state = Open(state).State;
        var valid = Allocation(state, ("item-one", "items"), ("boots-a", "gear"));
        Assert.True(GameEngine.PreviewEquipmentStation(state, valid).CanConfirm);
        foreach (var invalid in new[] { Allocation(state, ("boots-a", "items")), Allocation(state, ("item-one", "gear")),
            Allocation(state, ("item-one", "dead")), Allocation(state, ("boots-a", "unplaced")) })
            Assert.False(GameEngine.PreviewEquipmentStation(state, invalid).CanConfirm);
    }

    [Fact]
    public void AuthoredStationRoundTripsThroughJsonAndDelveTransport()
    {
        var definition = Fixture();
        var json = ScenarioDefinitionJson.ToJson(definition);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(ScenarioDefinitionJson.FromJson(json)));
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(restored));
        var opened = Open(Ready(restored));
        Assert.Equal(DecisionKind.MachineInteraction, opened.NextInput!.Kind);
        Assert.Equal("equipment-station", opened.State.Physical.Board.Machines[0].Type.Id);
    }

    [Fact]
    public void NormalWorkerAutomationEndsItsTurnInsteadOfReopeningRedistribution()
    {
        var definition = Fixture();
        definition.UnitTypes![0] = definition.UnitTypes[0] with { Behaviors = UnitBehavior.Worker };
        definition.Units[0] = definition.Units[0] with { Inventory = [new("boots-a", "boots")] };
        var state = Ready(definition);
        Assert.Contains(state.Pending!.Candidates, c => c.RequiresMachineInteraction);
        var provider = new DefaultAutomatedProvider();
        var key = provider.Choose(state.Pending, new GameplayQueries(state));
        Assert.Equal(ActivationChoiceKind.EndTurn, state.Pending.Candidates.Single(c => c.Key == key).Kind);
        var result = GameEngine.Advance(state, provider, new Dice(), false);
        Assert.Null(result.State.MachineInteraction);
        Assert.DoesNotContain(result.Events, e => e.Kind == "MachineOperated");
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "a");
    }

    [Fact]
    public void UnitActuallyCreatedBySummonMayReceiveEquipmentBeforeItsFirstActivation()
    {
        var definition = Fixture();
        definition.UnitTypes![0] = definition.UnitTypes[0] with { LocalAbilities = [.. definition.UnitTypes[0].LocalAbilities,
            new("summon", "Summon", AbilityTiming.Action, new AdjacentUnitSummonMechanic("summoned", Posture.Lying))] };
        definition = definition with { UnitTypeIds = [.. definition.UnitTypeIds!, "summoned"],
            UnitTypes = [.. definition.UnitTypes!, Carrier("summoned") with { DefaultController = ControllerKind.Human }] };
        var state = Ready(definition);
        state.ExtraActionsThisActivation = 1;
        state = GameEngine.RefreshChoices(state, new Dice(), false).State;
        var choice = state.Pending!.Candidates.First(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic);
        var summoned = GameEngine.Advance(state, new Choice(choice.Key), new Dice(), false);
        var created = summoned.State.Units.Single(u => u.SummonerUnitId == "a");
        Assert.False(summoned.State.IsUpright(created.Id));
        var opened = Open(summoned.State).State;
        var result = GameEngine.ConfirmEquipmentStation(opened, Allocation(opened, ("boots-a", created.Id)), new Dice(), false);
        Assert.Equal("boots", Object(result.State, created.Id, "boots-a").EquipmentTypeId);
        Assert.False(result.State.IsUpright(created.Id));
        Assert.Equal("a", result.State.Units.Single(u => u.Id == created.Id).SummonerUnitId);
        Assert.DoesNotContain(created.Id, result.State.ResolvingTokenUnitIds);
    }

    [Fact]
    public void SingleUseOperateItemIsReservedOutsidePoolAndConsumedOnlyWithActualConfirmedTransfer()
    {
        var definition = Fixture();
        definition.UnitTypes![0] = definition.UnitTypes[0] with {
            LocalAbilities = [.. definition.UnitTypes[0].LocalAbilities.Where(a => a.Mechanic is not Operate)] };
        definition = definition with { ItemTypes = [new("station-pass", "Station Pass") { SellValue = 20,
            LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate())] }] };
        definition.Units[0] = definition.Units[0] with { Inventory = [.. definition.Units[0].Inventory!,
            new("physical-pass") { ItemTypeId = "station-pass" }] };
        var opened = Open(Ready(definition)).State;
        Assert.Equal("physical-pass", opened.MachineInteraction!.ReservedConsumptionObjectId);
        Assert.DoesNotContain(opened.MachineInteraction.EquipmentStation!.OriginalAllocation.Assignments, a => a.ObjectId == "physical-pass");
        Assert.Equal("station-pass", Object(opened, "a", "physical-pass").ItemTypeId);
        var preview = GameEngine.PreviewEquipmentStation(opened, null);
        Assert.False(preview.CanConfirm);
        Assert.Contains(preview.Diagnostics, d => d.Code == "no-transfer");
        var row = preview.Units.Single(u => u.UnitId == "a");
        Assert.DoesNotContain(row.Allocation, o => o.Id == "physical-pass");
        Assert.DoesNotContain(row.Abilities, a => a.SourceInstanceId == "physical-pass");
        var valid = Allocation(opened, ("boots-a", "b"));
        var illegallyAssigned = new EquipmentAllocation([.. valid.Assignments, new("physical-pass", "b")]);
        Assert.False(GameEngine.PreviewEquipmentStation(opened, illegallyAssigned).CanConfirm);
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmEquipmentStation(opened, illegallyAssigned, new Dice(), false));
        var illegallySold = valid with { SoldObjectIds = ["physical-pass"] };
        Assert.False(GameEngine.PreviewEquipmentStation(opened, illegallySold).CanConfirm);
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmEquipmentStation(opened, illegallySold, new Dice(), false));
        var canceled = GameEngine.CancelMachineInteraction(opened, new Dice(), false);
        Assert.Equal("station-pass", Object(canceled.State, "a", "physical-pass").ItemTypeId);
        Assert.Empty(canceled.State.LootDeck!.DiscardPile);
        var saved = JsonSerializer.Deserialize<GameState>(Json(opened))!;
        GameEngine.ValidateScenario(saved);
        var result = GameEngine.ConfirmEquipmentStation(opened, valid, new Dice(), false);
        var replay = GameEngine.ConfirmEquipmentStation(saved, valid, new Dice(), false);
        Assert.Equal(Json(result), Json(replay));
        Assert.DoesNotContain(result.State.Units.SelectMany(u => u.Inventory), o => o.Id == "physical-pass");
        Assert.Equal("physical-pass", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        Assert.Equal("boots-a", Object(result.State, "b", "boots-a").Id);
        foreach (var step in result.ResolutionSteps)
        {
            Assert.DoesNotContain(step.StateAfter.Units.SelectMany(u => u.Inventory), o => o.Id == "physical-pass");
            Assert.Equal("physical-pass", Assert.Single(step.StateAfter.LootDeck!.DiscardPile).Id);
            Assert.Equal("boots-a", Object(step.StateAfter, "b", "boots-a").Id);
        }
    }
}
