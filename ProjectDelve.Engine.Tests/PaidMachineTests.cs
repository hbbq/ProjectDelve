using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PaidMachineTests
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
    private static LocalAbilityDefinition Operate => new("operate", "Operate", AbilityTiming.Action, new Operate()) { Usage = new(3) };
    private static ScenarioDefinition Fixture(MachineType machine) => new()
    {
        Board = new() { Width = 6, Height = 4 }, UnitTypeIds = ["patient"],
        UnitTypes = [new("patient", 3, 1, 2, 1, 6) { Unique = true, CanHaveItems = true, CanHaveEquipment = true,
            LocalAbilities = [Operate] }],
        EquipmentTypes = [new("capacity", "Capacity") { MaxHpModifier = 2 }],
        Units = [new("patient", "blue", new(1, 1), UnitId: "operator", InitialHp: 2)
            { Inventory = [new("capacity-1", "capacity")] },
            new("patient", "red", new(5, 3), UnitId: "other", InitialHp: 3)],
        Agency = [new("patient", "blue", ControllerKind.Human), new("patient", "red", ControllerKind.Human)],
        Machines = [new("service", new(2, 1), machine)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        ItemTypes = [new("last-resort-fireball", "Test Fireball") { LocalAbilities = [new("cast", "Fireball", AbilityTiming.Action, new FireballMechanic())] }],
        Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition scene, int credits = 100)
    {
        var state = GameEngine.CreateGame(scene);
        state.SideCredits["blue"] = credits;
        state.Round = 1; state.RoundComplete = false; state.ActiveToken = new("patient", "blue");
        state.CurrentUnitId = "operator"; state.MoveDone = true; state.ResolvingTokenUnitIds = ["operator"];
        state.Pending = new(DecisionKind.Activation, "patient", "operator", [], false);
        return GameEngine.RefreshChoices(state, new Dice(), false).State;
    }
    private static GameState Open(ScenarioDefinition scene, int credits = 100)
    {
        var state = Ready(scene, credits);
        var candidate = state.Pending!.Candidates.Single(c => c.Action == UnitAction.Operate);
        var dice = new Dice();
        var result = GameEngine.Advance(state, new Choice(candidate.Key), dice, false);
        Assert.Equal(0, dice.Calls);
        Assert.Equal(DecisionKind.MachineInteraction, result.State.Pending!.Kind);
        Assert.Equal(0, result.State.ActionsUsedThisActivation);
        return result.State;
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    [Theory]
    [InlineData("minor-treatment", 4, 90)]
    [InlineData("full-treatment", 8, 50)]
    public void TreatmentIsAtomicOperatorOnlyAndUsesEffectiveMaximum(string service, int hp, int credits)
    {
        var state = Open(Fixture(MachineTypes.DelveCare)); var before = Json(state);
        var preview = GameEngine.PreviewPaidMachine(state, service);
        Assert.True(preview.CanConfirm); Assert.Equal(hp, preview.Offers.Single(o => o.ServiceId == service).ResultingHp);
        Assert.Equal(before, Json(state));
        var dice = new Dice(); var result = GameEngine.ConfirmPaidMachine(state, service, dice, false);
        Assert.Equal(0, dice.Calls); Assert.Equal(before, Json(state));
        Assert.Equal(hp, result.State.Units.Single(u => u.Id == "operator").CurrentHp);
        Assert.Equal(3, result.State.Units.Single(u => u.Id == "other").CurrentHp);
        Assert.Equal(credits, result.State.SideCredits["blue"]);
        Assert.Single(result.Events, e => e.Kind == "ActionUsed");
        Assert.Equal(1, result.State.Units[0].AbilityUseCounts[state.MachineInteraction!.Operation.Ability!.AccountingId]);
        Assert.Equal(1, result.State.Physical.Board.Machines[0].TotalUses);
        foreach (var step in result.ResolutionSteps)
        {
            Assert.Null(step.StateAfter.MachineInteraction);
            Assert.Equal(credits, step.StateAfter.SideCredits["blue"]);
            Assert.Equal(hp, step.StateAfter.Units[0].CurrentHp);
        }
    }

    [Fact]
    public void FullHealthAndUnaffordableOffersRemainInspectableAndCancelIsFree()
    {
        var scene = Fixture(MachineTypes.DelveCare);
        scene.Units[0] = scene.Units[0] with { InitialHp = 8 };
        var state = Open(scene, 0); var before = Json(state); var dice = new Dice();
        Assert.All(GameEngine.PreviewPaidMachine(state).Offers, offer =>
        { Assert.False(offer.CanConfirm); Assert.Contains(offer.Diagnostics, d => d.Code == "no-healing"); });
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmPaidMachine(state, "minor-treatment", dice, false));
        Assert.Equal(before, Json(state));
        var canceled = GameEngine.CancelMachineInteraction(state, dice, false).State;
        Assert.Null(canceled.MachineInteraction); Assert.Equal(0, canceled.ActionsUsedThisActivation);
        Assert.Empty(canceled.Units[0].AbilityUseCounts); Assert.Equal(0, canceled.SideCredits["blue"]);
        Assert.Equal(0, dice.Calls);
    }

    [Fact]
    public void MinorTreatmentCapsAtMaximumAndConfirmRechecksLiveCredits()
    {
        var scene = Fixture(MachineTypes.DelveCare); scene.Units[0] = scene.Units[0] with { InitialHp = 7 };
        var state = Open(scene); Assert.Equal(8, GameEngine.PreviewPaidMachine(state, "minor-treatment").Offers[0].ResultingHp);
        state.SideCredits["blue"] = 9; var before = Json(state);
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmPaidMachine(state, "minor-treatment", new Dice(), false));
        Assert.Equal(before, Json(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquipmentAndItemOperatePayOriginalSourceOnce(bool itemSource)
    {
        var scene = Fixture(MachineTypes.DelveCare);
        scene.UnitTypes![0] = scene.UnitTypes[0] with { LocalAbilities = [] };
        if (itemSource) scene = scene with { ItemTypes = [new("service-item", "Service") { LocalAbilities = [Operate with { Usage = new() }] }] };
        else scene.EquipmentTypes![0] = scene.EquipmentTypes[0] with { LocalAbilities = [Operate] };
        if (itemSource) scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!, new("service-item-1") { ItemTypeId = "service-item" }] };
        var state = Open(scene);
        var result = GameEngine.ConfirmPaidMachine(state, "minor-treatment", new Dice(), false);
        Assert.Empty(result.State.Units[0].AbilityUseCounts);
        if (itemSource)
        { Assert.DoesNotContain(result.State.Units[0].Inventory, o => o.Id == "service-item-1"); Assert.Single(result.State.LootDeck!.DiscardPile); }
        else Assert.Equal(1, result.State.Units[0].Inventory.Single(o => o.Id == "capacity-1").AbilityUseCounts["operate"]);
    }

    [Fact]
    public void VendingAddsOrdinaryIndependentItemAndRetainsExistingCopiesWithoutInventoryLimit()
    {
        var scene = Fixture(MachineTypes.MeteorOMat);
        scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!, new("old-fireball") { ItemTypeId = "last-resort-fireball" }] };
        var state = Open(scene); var dice = new Dice();
        Assert.Equal("Test Fireball", GameEngine.PreviewPaidMachine(state, "fireball").Offers.Single().ItemName);
        var result = GameEngine.ConfirmPaidMachine(state, "fireball", dice, false);
        var items = result.State.Units[0].Inventory.Where(o => o.ItemTypeId == "last-resort-fireball").ToArray();
        Assert.Equal(2, items.Length); Assert.Equal(2, items.Select(o => o.Id).Distinct().Count());
        Assert.Equal(85, result.State.SideCredits["blue"]); Assert.Equal(0, dice.Calls);
        Assert.Equal(2, result.State.Units[0].CurrentHp);
        Assert.Contains(result.State.AbilitiesOf("operator"), a => a.SourceInstanceId == items.Single(o => o.Id != "old-fireball").Id && a.Definition.Mechanic is FireballMechanic);
        GameEngine.ValidateScenario(result.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VendingMissingTypeOrIncapableOperatorCannotConfirm(bool missing)
    {
        var scene = Fixture(MachineTypes.MeteorOMat);
        if (!missing) scene.UnitTypes![0] = scene.UnitTypes[0] with { CanHaveItems = false };
        var state = Open(scene); if (missing) state.ItemTypes.RemoveAll(t => t.Id == "last-resort-fireball");
        var before = Json(state); Assert.False(GameEngine.PreviewPaidMachine(state, "fireball").CanConfirm);
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmPaidMachine(state, "fireball", new Dice(), false));
        Assert.Equal(before, Json(state));
    }

    [Fact]
    public void PendingRoundTripsDeterministicallyAndAccountingIsPrivate()
    {
        var state = Open(Fixture(MachineTypes.MeteorOMat));
        var restored = JsonSerializer.Deserialize<GameState>(Json(state))!;
        GameEngine.ValidateRestoredState(restored);
        var result = GameEngine.ConfirmPaidMachine(state, "fireball", new Dice(), false);
        Assert.Equal(Json(result), Json(GameEngine.ConfirmPaidMachine(restored, "fireball", new Dice(), false)));
        Assert.Null(FogOfWar.Observe(state, "red").MachineInteraction);
        var e = Assert.Single(result.Events, e => e.Kind == "MachineOperated");
        var hidden = FogOfWar.ObserveEvent(result.State, "red", e)!;
        Assert.Null(hidden.PaidServiceId); Assert.Null(hidden.PurchasedItemId); Assert.Null(hidden.CreditsAmount);
        Assert.Null(hidden.CreditsBalance); Assert.Equal("fireball", FogOfWar.ObserveEvent(result.State, "blue", e)!.PaidServiceId);
        restored.MachineInteraction = restored.MachineInteraction! with { PaidServices = new(["forged"]) };
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateRestoredState(restored));
    }

    [Fact]
    public void TypedCustomMachineBehaviorDoesNotDependOnNameOrTypeIdentity()
    {
        var machine = MachineTypes.DelveCare with { Id = "custom", Name = "Meteor-O-Mat" };
        var state = Open(Fixture(machine));
        Assert.Equal(4, GameEngine.ConfirmPaidMachine(state, "minor-treatment", new Dice(), false).State.Units[0].CurrentHp);
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("wall")]
    [InlineData("exhausted")]
    [InlineData("source")]
    [InlineData("defeated")]
    public void ConfirmRechecksOrdinaryOperateLegalityWithoutSpending(string changed)
    {
        var state = Open(Fixture(MachineTypes.DelveCare));
        switch (changed)
        {
            case "owner": state.Physical.Board.Machines[0] = state.Physical.Board.Machines[0] with { OwnerSide = "red" }; break;
            case "wall": state.Physical.Board.Edges.Add(new(new(1, 1), new(2, 1), EdgeKind.Wall)); break;
            case "exhausted": state.Physical.Board.Machines[0] = state.Physical.Board.Machines[0] with
                { Type = MachineTypes.DelveCare with { Operation = MachineTypes.DelveCare.Operation with { MaxUsesTotal = 0 } } }; break;
            case "source": state.Types[0] = state.Types[0] with { LocalAbilities = [] }; break;
            case "defeated": state.Units[0] = state.Units[0] with { CurrentHp = 0 }; break;
        }
        var before = Json(state);
        Assert.Throws<InvalidOperationException>(() => GameEngine.ConfirmPaidMachine(state, "minor-treatment", new Dice(), false));
        Assert.Equal(before, Json(state));
    }

    [Fact]
    public void BuyingTwiceCreatesDistinctReceiptsAndRejectsAnUnaffordableThirdPurchase()
    {
        var scene = Fixture(MachineTypes.MeteorOMat);
        scene.UnitTypes![0] = scene.UnitTypes[0] with { ActionCountModifier = 2 };
        var state = Open(scene, credits: 44);
        var dice = new Dice();
        var itemIds = new HashSet<string>();
        for (var purchase = 0; purchase < 2; purchase++)
        {
            var result = GameEngine.ConfirmPaidMachine(state, "fireball", dice, false);
            var receipt = Assert.Single(result.Events, e => e.Kind == "MachineOperated");
            Assert.True(itemIds.Add(Assert.IsType<string>(receipt.PurchasedItemId)));
            var nextOperate = result.State.Pending!.Candidates.Single(c => c.Action == UnitAction.Operate);
            state = GameEngine.Advance(result.State, new Choice(nextOperate.Key), dice, false).State;
        }
        Assert.Equal(14, state.SideCredits["blue"]);
        Assert.Equal(2, state.ActionsUsedThisActivation);
        Assert.Equal(itemIds.Order(), state.Units[0].Inventory.Where(o => o.ItemTypeId == "last-resort-fireball").Select(o => o.Id).Order());
        var offer = Assert.Single(GameEngine.PreviewPaidMachine(state).Offers);
        Assert.Contains(offer.Diagnostics, d => d.Code == "insufficient-credits");
        var before = Json(state);
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmPaidMachine(state, "fireball", dice, false));
        Assert.Equal(before, Json(state));
        Assert.Equal(0, dice.Calls);
    }

    [Fact]
    public void OnePointTreatmentChargesFullPriceWhileLeavingOtherSpentUsesAndPostureAlone()
    {
        var scene = Fixture(MachineTypes.DelveCare);
        scene.EquipmentTypes![0] = scene.EquipmentTypes[0] with { LocalAbilities = [Operate with { Key = "crank" }] };
        scene.Units[0] = scene.Units[0] with { InitialHp = 7, Inventory = [new("capacity-1", "capacity")
            { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("crank", 2) }] };
        var ready = Ready(scene);
        var native = ready.Pending!.Candidates.Single(c => c.Action == UnitAction.Operate && c.Ability!.SourceKind == AbilitySourceKind.Unit);
        var dice = new Dice();
        var pending = GameEngine.Advance(ready, new Choice(native.Key), dice, false).State;
        var result = GameEngine.ConfirmPaidMachine(pending, "minor-treatment", dice, false);
        Assert.Equal(8, result.State.Units[0].CurrentHp);
        Assert.Equal(1, Assert.Single(result.Events, e => e.Kind == "HealResolved").Healing);
        Assert.Equal(90, result.State.SideCredits["blue"]);
        Assert.Equal(2, result.State.Units[0].Inventory.Single().AbilityUseCounts["crank"]);
        Assert.True(result.State.IsUpright("operator"));
        Assert.Equal(0, dice.Calls);
    }

    [Fact]
    public void FullPhysicalCounterProducesFailedOfferAndFreeCancel()
    {
        var state = Open(Fixture(MachineTypes.DelveCare));
        state.Physical.Board.Machines[0] = state.Physical.Board.Machines[0] with
            { UseCounts = ImmutableDictionary<string, int>.Empty.Add("blue", int.MaxValue) };
        Assert.All(GameEngine.PreviewPaidMachine(state).Offers, o => Assert.Contains(o.Diagnostics, d => d.Code == "machine-counter-full"));
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmPaidMachine(state, "minor-treatment", new Dice(), false));
        Assert.Null(GameEngine.CancelMachineInteraction(state, new Dice(), false).State.MachineInteraction);
    }

    [Fact]
    public void LayDownOperateCostResolvesAlongsideHealing()
    {
        var scene = Fixture(MachineTypes.DelveCare);
        scene.UnitTypes![0] = scene.UnitTypes[0] with { LocalAbilities = [Operate with { Costs = [new LayDownSelf()] }] };
        var state = Open(scene); var result = GameEngine.ConfirmPaidMachine(state, "minor-treatment", new Dice(), false);
        Assert.False(result.State.IsUpright("operator")); Assert.Equal(4, result.State.Units[0].CurrentHp);
        Assert.Single(result.Events, e => e.Kind == "HealResolved" && e.Healing == 2);
        Assert.Equal(0, result.Events.Single(e => e.Kind == "MachineOperated").Healing);
    }

    [Fact]
    public void ScenarioJsonAndTransportRetainServicesAndTypedItemDependency()
    {
        var scene = Fixture(MachineTypes.MeteorOMat);
        var json = ScenarioDefinitionJson.ToJson(scene);
        var restored = ScenarioDefinitionJson.FromJson(json);
        var state = Open(restored);
        Assert.True(GameEngine.PreviewPaidMachine(state, "fireball").CanConfirm);
        Assert.True(GameEngine.PreviewPaidMachine(Open(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scene))), "fireball").CanConfirm);
        Assert.Contains(ContentDefinitions.Dependencies(MachineTypes.MeteorOMat), d => d.Target == new ContentIdentity(ContentFamily.Item, "last-resort-fireball"));
    }

    [Theory]
    [InlineData("negative-cost")]
    [InlineData("no-effect")]
    [InlineData("mixed-effect")]
    [InlineData("zero-heal")]
    [InlineData("empty-item")]
    [InlineData("duplicate")]
    [InlineData("empty-services")]
    [InlineData("null-service")]
    [InlineData("resource-output")]
    public void TypedPaidServicesRejectMalformedDefinitions(string invalid)
    {
        var service = new PaidMachineService("service", "Service", 10, HealAmount: 2);
        var services = ImmutableArray.Create(service);
        switch (invalid)
        {
            case "negative-cost": services = [service with { Cost = -1 }]; break;
            case "no-effect": services = [service with { HealAmount = null }]; break;
            case "mixed-effect": services = [service with { FullHeal = true }]; break;
            case "zero-heal": services = [service with { HealAmount = 0 }]; break;
            case "empty-item": services = [service with { HealAmount = null, ItemTypeId = " " }]; break;
            case "duplicate": services = [service, service]; break;
            case "empty-services": services = []; break;
            case "null-service": services = [null!]; break;
        }
        var operation = new MachineOperation([]) { PaidServices = new(services) };
        if (invalid == "resource-output") operation = operation with { OutputType = ResourceTypes.Diamond };
        var machine = new MachineType("invalid", "Invalid", null, operation);
        Assert.NotEmpty(ContentDefinitions.Validate(new(machine)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredPendingInvocationMustRetainItsOpeningSourceBinding(bool nullDefinition)
    {
        var state = Open(Fixture(MachineTypes.DelveCare));
        var pending = state.Pending!;
        var interaction = pending.MachineInteraction!;
        var altered = interaction.Operation.Ability! with { SourceInstanceId = "other" };
        if (nullDefinition) altered = altered with { Definition = null! };
        state.Pending = pending with { MachineInteraction = interaction with { Operation = interaction.Operation with
            { Ability = altered } } };
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateRestoredState(state));
    }

    [Fact]
    public void NonUniqueOperatorRetainsOneHpCapWithEquipmentAndCannotBuyEitherTreatment()
    {
        var scene = Fixture(MachineTypes.DelveCare);
        scene.UnitTypes![0] = scene.UnitTypes[0] with { Unique = false, Hp = 1 };
        scene.Units[0] = scene.Units[0] with { InitialHp = 1 };
        scene.Units[1] = scene.Units[1] with { InitialHp = 1 };
        var state = Open(scene);
        var preview = GameEngine.PreviewPaidMachine(state);
        Assert.Equal(1, preview.EffectiveMaxHp);
        Assert.Contains(state.Units[0].Inventory, o => o.EquipmentTypeId == "capacity");
        Assert.Equal(2, preview.Offers.Length);
        var before = Json(state);
        var dice = new Dice();
        foreach (var offer in preview.Offers)
        {
            Assert.False(offer.CanConfirm);
            Assert.Equal(1, offer.ResultingHp);
            Assert.Contains(offer.Diagnostics, d => d.Code == "no-healing");
            Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmPaidMachine(state, offer.ServiceId, dice, false));
            Assert.Equal(before, Json(state));
        }
        Assert.Equal(100, state.SideCredits["blue"]);
        Assert.Equal(0, state.Physical.Board.Machines[0].TotalUses);
        Assert.Empty(state.Units[0].AbilityUseCounts);
        Assert.Equal(0, dice.Calls);
    }

}
