using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EquipmentStationTests
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
        definition = definition with { ItemTypes = [new("station-pass", "Station Pass") {
            LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate())] }] };
        definition.Units[0] = definition.Units[0] with { Inventory = [.. definition.Units[0].Inventory!,
            new("physical-pass") { ItemTypeId = "station-pass" }] };
        var opened = Open(Ready(definition)).State;
        Assert.Equal("physical-pass", opened.MachineInteraction!.ReservedConsumptionObjectId);
        Assert.DoesNotContain(opened.MachineInteraction.EquipmentStation.OriginalAllocation.Assignments, a => a.ObjectId == "physical-pass");
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
