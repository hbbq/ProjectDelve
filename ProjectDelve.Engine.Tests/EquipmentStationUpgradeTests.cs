using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed partial class EquipmentStationTests
{
    private static ScenarioDefinition PaidFixture()
    {
        var scene = SalesFixture();
        scene = scene with
        {
            SideCredits = new() { ["blue"] = 40 },
            UnitTypes = [.. scene.UnitTypes!.Select(type => type.Id == "incapable"
                ? type with { AdditionalLevels = [new(2, 1, 1, 1, 1) { UpgradeCost = 0 }] }
                : type with { AdditionalLevels = [new(type.Mov + 1, type.Rng, type.Atk + 1, type.Def, type.Hp + 1)
                    { UpgradeCost = 24, LocalAbilities = type.LocalAbilities }] })],
            EquipmentTypes = [.. scene.EquipmentTypes!.Select(type => type.Id == "boots"
                ? type with { AdditionalLevels = [new()
                    {
                        UpgradeCost = 12, MaxHpModifier = 2,
                        LocalAbilities = [Passive("move", new SelfModifiersMechanic([new(Stat.Mov, 3)])),
                            new("sprint", "Sprint", AbilityTiming.BonusAction, new SelfExtraMovesMechanic(1)) { Usage = new(2) }]
                    }] }
                : type.Id == "bell" ? type with { AdditionalLevels = [new() { UpgradeCost = 5,
                    LocalAbilities = [new("new-ring", "New Ring", AbilityTiming.BonusAction, new SelfExtraMovesMechanic(1)) { Usage = new(4) }] }] }
                : type)]
        };
        return scene;
    }

    private static EquipmentAllocation Upgrades(GameState state, string[]? units = null, string[]? equipment = null) =>
        Allocation(state) with { UpgradedUnitIds = [.. units ?? []], UpgradedEquipmentIds = [.. equipment ?? []] };

    [Fact]
    public void UpgradeOnlyUnitRestoresHpAndNativeUsesWithOneActionAndNoRandomness()
    {
        var scene = PaidFixture();
        scene.UnitTypes![0] = scene.UnitTypes[0] with { LocalAbilities = [.. scene.UnitTypes[0].LocalAbilities,
            new("dash", "Dash", AbilityTiming.BonusAction, new SelfExtraMovesMechanic(1)) { Usage = new(2) }],
            AdditionalLevels = [scene.UnitTypes[0].AdditionalLevels[0] with
            {
                LocalAbilities = [new("operate", "Operate", AbilityTiming.Action, new Operate()),
                    new("renew", "Renewed Sprint", AbilityTiming.BonusAction, new SelfExtraMovesMechanic(1)) { Usage = new(3) }]
            }]
        };
        var charged = scene.Units[1].Inventory![0];
        scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!, charged] };
        scene.Units[1] = scene.Units[1] with { Inventory = [] };
        var state = Ready(scene);
        var dash = state.AbilitiesOf("a").Single(a => a.Key == "dash");
        state.Units[0] = state.Units[0] with { CurrentHp = 1, AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add(dash.AccountingId, 2) };
        state = Open(state).State;
        var before = Json(state);
        var draft = Upgrades(state, ["a"]);
        var preview = GameEngine.PreviewEquipmentStation(state, draft);
        Assert.True(preview.CanConfirm);
        Assert.Equal(24, preview.TotalUpgradeCost);
        Assert.Equal(16, preview.ResultingCredits);
        var row = preview.Units.Single(u => u.UnitId == "a");
        Assert.Equal(1, row.CurrentHp);
        Assert.Equal(7, row.ResultingHp);
        Assert.Equal(2, row.ResultingLevel);
        Assert.Equal(3, row.AbilityPreviews.Single(a => a.Ability.Key == "renew").RemainingUses);
        Assert.Equal(before, Json(state));
        var dice = new Dice();
        var result = GameEngine.ConfirmEquipmentStation(state, draft, dice, false);
        Assert.Equal(0, dice.Calls);
        Assert.Equal(16, result.State.SideCredits["blue"]);
        Assert.Equal(7, result.State.Units.Single(u => u.Id == "a").CurrentHp);
        Assert.Equal(1, result.State.ActionsUsedThisActivation);
        Assert.Empty(result.State.Units.Single(u => u.Id == "a").AbilityUseCounts);
        Assert.Equal(2, Object(result.State, "a", "bell-b").AbilityUseCounts["ring"]);
        Assert.Equal(before, Json(state));
        Assert.Throws<InvalidOperationException>(() => GameEngine.ConfirmEquipmentStation(result.State, draft, dice, false));
        GameEngine.ValidateScenario(result.State);
    }

    [Fact]
    public void EquipmentOnlyRefreshesSelectedPhysicalCopyWithoutHealingBearerOrOtherSources()
    {
        var state = Open(Ready(PaidFixture())).State;
        var draft = Upgrades(state, equipment: ["bell-b", "boots-a"]);
        var preview = GameEngine.PreviewEquipmentStation(state, draft);
        Assert.True(preview.CanConfirm);
        Assert.Equal(17, preview.TotalUpgradeCost);
        Assert.Equal(6, preview.Units.Single(u => u.UnitId == "a").ResultingHp);
        Assert.Equal(8, preview.Units.Single(u => u.UnitId == "a").Hp);
        Assert.Equal(4, preview.Units.Single(u => u.UnitId == "b").AbilityPreviews.Single(a => a.Ability.Key == "new-ring").RemainingUses);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false).State;
        Assert.Equal(2, Object(result, "a", "boots-a").Level);
        Assert.Equal(1, Object(result, "c", "boots-c").Level);
        Assert.Empty(Object(result, "b", "bell-b").AbilityUseCounts);
        Assert.Equal(2, result.Units.Single(u => u.Id == "b").CurrentHp);
        Assert.False(result.IsUpright("b"));
        Assert.Equal(23, result.SideCredits["blue"]);
    }

    [Fact]
    public void SalesFundACompleteUpgradeAndTransferWithHealingToFinalMaximum()
    {
        var scene = PaidFixture();
        scene = scene with { SideCredits = new() { ["blue"] = 32 } };
        var state = Open(Ready(scene)).State;
        var draft = Sell(state, ["boots-c"], ("boots-a", "b")) with
            { UpgradedUnitIds = ["b"], UpgradedEquipmentIds = ["boots-a"] };
        var preview = GameEngine.PreviewEquipmentStation(state, draft);
        Assert.True(preview.CanConfirm);
        Assert.Equal(4, preview.TotalProceeds);
        Assert.Equal(36, preview.TotalUpgradeCost);
        Assert.Equal(0, preview.ResultingCredits);
        Assert.Equal(9, preview.Units.Single(u => u.UnitId == "b").ResultingHp);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false);
        Assert.Equal(9, result.State.Units.Single(u => u.Id == "b").CurrentHp);
        Assert.Equal(2, Object(result.State, "b", "boots-a").Level);
        Assert.Equal(0, result.State.SideCredits["blue"]);
        Assert.DoesNotContain(result.State.Units.SelectMany(u => u.Inventory), o => o.Id == "boots-c");
        var operated = Assert.Single(result.Events, e => e.Kind == "MachineOperated");
        Assert.Equal(-32, operated.CreditsAmount);
        Assert.Equal(36, operated.UpgradeCost);
        Assert.Equal("b", Assert.Single(operated.UnitUpgrades).TargetId);
        Assert.Equal("boots-a", Assert.Single(operated.EquipmentUpgrades).TargetId);
        foreach (var step in result.ResolutionSteps)
        {
            Assert.Equal(0, step.StateAfter.SideCredits["blue"]);
            Assert.Equal(9, step.StateAfter.Units.Single(u => u.Id == "b").CurrentHp);
        }
        var reversed = draft with { Assignments = [.. draft.Assignments.Reverse()] };
        Assert.Equal(Json(result), Json(GameEngine.ConfirmEquipmentStation(JsonSerializer.Deserialize<GameState>(Json(state))!, reversed, new Dice(), false)));
        var hidden = FogOfWar.ObserveEvent(result.State, "red", operated)!;
        Assert.Empty(hidden.UnitUpgrades);
        Assert.Empty(hidden.EquipmentUpgrades);
        Assert.Null(hidden.UpgradeCost);
        Assert.Null(hidden.CreditsBalance);
        Assert.Null(hidden.CreditsAmount);
    }

    [Fact]
    public void FreeUpgradeAllowsNonUniqueUnitWithoutInventoryCapability()
    {
        var state = Open(Ready(PaidFixture())).State;
        Assert.DoesNotContain("incapable", state.MachineInteraction!.EquipmentStation!.EligibleUnitIds);
        Assert.Contains("incapable", state.MachineInteraction.EquipmentStation!.EligibleUpgradeUnitIds!.Value);
        var draft = Upgrades(state, ["incapable"]);
        var preview = GameEngine.PreviewEquipmentStation(state, draft);
        Assert.True(preview.CanConfirm);
        Assert.Equal(0, preview.TotalUpgradeCost);
        Assert.Equal(1, preview.Units.Single(u => u.UnitId == "incapable").Hp);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false).State;
        Assert.Equal(2, result.Units.Single(u => u.Id == "incapable").Level);
        Assert.Equal(1, result.EffectiveMaxHpOf("incapable"));
        Assert.Equal(40, result.SideCredits["blue"]);
        Assert.Equal(1, result.ActionsUsedThisActivation);
    }

    [Fact]
    public void InvalidUpgradeSetsAndUnaffordableDraftsLeaveEveryGameplayEffectUncommitted()
    {
        var state = Open(Ready(PaidFixture())).State;
        var drafts = new[]
        {
            Upgrades(state, ["a", "a"]),
            Upgrades(state, ["enemy"]),
            Upgrades(state, equipment: ["foreign"]),
            Upgrades(state, equipment: ["item-one"]),
            Upgrades(state, equipment: ["boots-a", "boots-a"]),
            Upgrades(state, equipment: ["boots-a"]) with { SoldObjectIds = default },
            Upgrades(state, equipment: ["ground"]),
            Sell(state, ["boots-a"]) with { UpgradedEquipmentIds = ["boots-a"] },
            Upgrades(state, ["a", "b"])
        };
        var before = Json(state);
        foreach (var draft in drafts)
        {
            var preview = GameEngine.PreviewEquipmentStation(state, draft);
            Assert.False(preview.CanConfirm);
            Assert.NotEmpty(preview.Diagnostics);
            Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false));
            Assert.Equal(before, Json(state));
        }
        var allocation = Upgrades(state, ["a"]);
        state.SideCredits["blue"] = 0;
        Assert.Contains(GameEngine.PreviewEquipmentStation(state, allocation).Diagnostics, d => d.Code == "insufficient-credits");
        Assert.Equal(1, state.Units[0].Level);
    }

    [Fact]
    public void MissingPricePreservesAuthoredLevelWithoutOfferingItForPurchase()
    {
        var scene = PaidFixture();
        scene.UnitTypes![0] = scene.UnitTypes[0] with { AdditionalLevels = [scene.UnitTypes[0].AdditionalLevels[0] with { UpgradeCost = null }] };
        scene.EquipmentTypes![0] = scene.EquipmentTypes[0] with { AdditionalLevels = [scene.EquipmentTypes[0].AdditionalLevels[0] with { UpgradeCost = null }] };
        var state = Open(Ready(scene)).State;
        var offers = GameEngine.PreviewEquipmentStation(state, null);
        Assert.DoesNotContain(offers.UnitUpgrades, o => o.TargetId == "a");
        Assert.DoesNotContain(offers.EquipmentUpgrades, o => o.TargetId == "boots-a");
        Assert.False(GameEngine.PreviewEquipmentStation(state, Upgrades(state, ["a"], ["boots-a"])).CanConfirm);
        scene.Units[0] = scene.Units[0] with { Level = 2, Inventory = [new("boots-a", "boots") { Level = 2 }] };
        var restored = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scene)));
        Assert.Equal(2, restored.Units[0].Level);
        Assert.Equal(2, restored.Units[0].Inventory[0].Level);
        Assert.Equal(9, restored.Units[0].CurrentHp);
    }

    [Fact]
    public void PricesValidateAndCombinedArithmeticCanExceedIntWhileFinalBalanceRemainsLegal()
    {
        var scene = PaidFixture();
        var unit = scene.UnitTypes![0];
        var gear = scene.EquipmentTypes![0];
        Assert.Contains(ContentDefinitions.Validate(new(unit with { AdditionalLevels = [unit.AdditionalLevels[0] with { UpgradeCost = -1 }] })),
            d => d.Path == "additionalLevels[0].upgradeCost");
        Assert.Contains(ContentDefinitions.Validate(new(gear with { AdditionalLevels = [gear.AdditionalLevels[0] with { UpgradeCost = -1 }] })),
            d => d.Path == "additionalLevels[0].upgradeCost");
        scene.UnitTypes[0] = unit with { AdditionalLevels = [unit.AdditionalLevels[0] with { UpgradeCost = int.MaxValue }] };
        scene.EquipmentTypes[0] = gear with { SellValue = int.MaxValue, AdditionalLevels = [gear.AdditionalLevels[0] with { UpgradeCost = int.MaxValue }] };
        scene = scene with { SideCredits = new() { ["blue"] = int.MaxValue } };
        var state = Open(Ready(scene)).State;
        var draft = Sell(state, ["boots-c"]) with { UpgradedUnitIds = ["a"], UpgradedEquipmentIds = ["boots-a"] };
        var preview = GameEngine.PreviewEquipmentStation(state, draft);
        Assert.True(preview.CanConfirm);
        Assert.Equal(2L * int.MaxValue, preview.TotalUpgradeCost);
        Assert.Equal(0, preview.ResultingCredits);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false);
        Assert.Equal(0, result.State.SideCredits["blue"]);
        Assert.Equal(-int.MaxValue, Assert.Single(result.Events, e => e.Kind == "MachineOperated").CreditsAmount);
    }

    [Theory]
    [InlineData(false, "operate", true)]
    [InlineData(true, "operate", true)]
    [InlineData(false, "console", true)]
    [InlineData(true, "console", true)]
    [InlineData(false, "operate", false)]
    [InlineData(true, "operate", false)]
    public void UpgradingOpeningSourcePaysOnceAndPreservesOnlyContinuingLimitedOperate(bool equipment, string destinationKey, bool initiallyLimited)
    {
        var scene = PaidFixture();
        var opening = new LocalAbilityDefinition("operate", "Crank", AbilityTiming.Action, new Operate()) { Usage = new(initiallyLimited ? 2 : null) };
        var destination = new LocalAbilityDefinition(destinationKey, "Console", AbilityTiming.Action, new Operate()) { Usage = new(3) };
        if (equipment)
        {
            scene.UnitTypes![0] = scene.UnitTypes[0] with { LocalAbilities = [] };
            var gear = scene.EquipmentTypes![0];
            scene.EquipmentTypes[0] = gear with { LocalAbilities = [opening],
                AdditionalLevels = [gear.AdditionalLevels[0] with { LocalAbilities = [destination] }] };
        }
        else
        {
            var type = scene.UnitTypes![0];
            scene.UnitTypes[0] = type with { LocalAbilities = [opening],
                AdditionalLevels = [type.AdditionalLevels[0] with { LocalAbilities = [destination] }] };
        }
        var state = Open(Ready(scene)).State;
        var draft = Upgrades(state, equipment ? [] : ["a"], equipment ? ["boots-a"] : []);
        if (equipment) draft = draft with { Assignments = [.. draft.Assignments.Select(a => a.ObjectId == "boots-a" ? a with { UnitId = "b" } : a)] };
        var row = GameEngine.PreviewEquipmentStation(state, draft).Units.Single(u => u.UnitId == (equipment ? "b" : "a"));
        int remaining = initiallyLimited && destinationKey == "operate" ? 2 : 3;
        Assert.Equal(remaining, row.AbilityPreviews.Single(a => a.Ability.Key == destinationKey &&
            (!equipment || a.Ability.SourceInstanceId == "boots-a")).RemainingUses);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false).State;
        var grant = result.AbilitiesOf(equipment ? "b" : "a").Single(a => a.Key == destinationKey &&
            (!equipment || a.SourceInstanceId == "boots-a"));
        Assert.Equal(remaining, result.AbilityUsesOf(equipment ? "b" : "a", grant)!.RemainingUses);
        Assert.Equal(1, result.ActionsUsedThisActivation);
        GameEngine.ValidateScenario(result);
    }

    [Fact]
    public void UpgradeMayRemoveOpeningOperateAndSingleUseSourceIsConsumedOnlyOnSuccessfulConfirm()
    {
        var scene = PaidFixture();
        var type = scene.UnitTypes![0];
        var opening = new LocalAbilityDefinition("operate", "Crank", AbilityTiming.Action, new Operate()) { Usage = new(1) };
        scene.UnitTypes[0] = type with { LocalAbilities = [opening],
            AdditionalLevels = [type.AdditionalLevels[0] with { LocalAbilities = [] }] };
        var state = Open(Ready(scene)).State;
        var result = GameEngine.ConfirmEquipmentStation(state, Upgrades(state, ["a"]), new Dice(), false).State;
        Assert.Empty(result.Units[0].AbilityUseCounts);
        Assert.DoesNotContain(result.AbilitiesOf("a"), a => a.Definition.Mechanic is Operate);
        GameEngine.ValidateScenario(result);

        scene.UnitTypes[0] = scene.UnitTypes[0] with { LocalAbilities = [] };
        scene = scene with { ItemTypes = [new("pass", "Pass") { LocalAbilities = [new("open", "Open", AbilityTiming.Action, new Operate())] }] };
        scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!, new("ticket") { ItemTypeId = "pass" }] };
        state = Open(Ready(scene)).State;
        var draft = Upgrades(state, ["a"]);
        Assert.DoesNotContain(GameEngine.PreviewEquipmentStation(state, draft).Units.SelectMany(u => u.Allocation), o => o.Id == "ticket");
        var cancel = GameEngine.CancelMachineInteraction(state, new Dice(), false).State;
        Assert.Contains(cancel.Units.SelectMany(u => u.Inventory), o => o.Id == "ticket");
        result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false).State;
        Assert.DoesNotContain(result.Units.SelectMany(u => u.Inventory), o => o.Id == "ticket");
        Assert.Equal("ticket", Assert.Single(result.LootDeck!.DiscardPile).Id);
        Assert.Equal(2, result.Units[0].Level);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OpeningChargeRetiresWhenSameKeyBecomesAnotherMechanicOrUnlimited(bool equipment, bool unlimited)
    {
        var scene = PaidFixture();
        var opening = new LocalAbilityDefinition("operate", "Crank", AbilityTiming.Action, new Operate()) { Usage = new(1) };
        var destination = unlimited
            ? new LocalAbilityDefinition("operate", "Console", AbilityTiming.Action, new Operate())
            : new LocalAbilityDefinition("operate", "Sprint", AbilityTiming.BonusAction, new SelfExtraMovesMechanic(1)) { Usage = new(3) };
        if (equipment)
        {
            scene.UnitTypes![0] = scene.UnitTypes[0] with { LocalAbilities = [] };
            var gear = scene.EquipmentTypes![0];
            scene.EquipmentTypes[0] = gear with { LocalAbilities = [opening],
                AdditionalLevels = [gear.AdditionalLevels[0] with { LocalAbilities = [destination] }] };
        }
        else
        {
            var type = scene.UnitTypes![0];
            scene.UnitTypes[0] = type with { LocalAbilities = [opening],
                AdditionalLevels = [type.AdditionalLevels[0] with { LocalAbilities = [destination] }] };
        }
        var state = Open(Ready(scene)).State;
        var result = GameEngine.ConfirmEquipmentStation(state, Upgrades(state, equipment ? [] : ["a"], equipment ? ["boots-a"] : []), new Dice(), false).State;
        var grant = result.AbilitiesOf("a").Single(a => a.Key == "operate");
        if (unlimited) Assert.Null(result.AbilityUsesOf("a", grant));
        else Assert.Equal(3, result.AbilityUsesOf("a", grant)!.RemainingUses);
        Assert.Empty(equipment ? Object(result, "a", "boots-a").AbilityUseCounts : result.Units[0].AbilityUseCounts);
        GameEngine.ValidateScenario(result);
    }

    [Fact]
    public void CancelAndLegacyPendingRoundTripPreserveUpgradeChoicesWithoutRestoringOrSpending()
    {
        var state = Open(Ready(PaidFixture())).State;
        var before = Json(state);
        var draft = Upgrades(state, ["a"], ["boots-a"]);
        Assert.Equal(Json(draft), Json(JsonSerializer.Deserialize<EquipmentAllocation>(Json(draft))));
        var saved = JsonSerializer.Deserialize<GameState>(before)!;
        var interaction = saved.MachineInteraction!;
        saved.MachineInteraction = interaction with { EquipmentStation = interaction.EquipmentStation! with { EligibleUpgradeUnitIds = null } };
        saved.Pending = saved.Pending! with { MachineInteraction = saved.MachineInteraction };
        GameEngine.ValidateScenario(saved);
        Assert.True(GameEngine.PreviewEquipmentStation(saved, draft).CanConfirm);
        Assert.Equal(Json(GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false)),
            Json(GameEngine.ConfirmEquipmentStation(saved, draft, new Dice(), false)));
        var canceled = GameEngine.CancelMachineInteraction(state, new Dice(), false).State;
        Assert.Equal(40, canceled.SideCredits["blue"]);
        Assert.Equal(1, canceled.Units[0].Level);
        Assert.Equal(1, Object(canceled, "a", "boots-a").Level);
        Assert.Equal(0, canceled.ActionsUsedThisActivation);
        Assert.Equal(before, Json(state));
    }

    [Fact]
    public void EveryConfirmUsesTheLiveDestinationPriceAndAdvancesExactlyOneLevel()
    {
        var scene = PaidFixture();
        var boots = scene.EquipmentTypes![0];
        scene.EquipmentTypes[0] = boots with { AdditionalLevels = [boots.AdditionalLevels[0],
            new() { UpgradeCost = 9, MaxHpModifier = 3 }] };
        var first = Open(Ready(scene)).State;
        var draft = Upgrades(first, equipment: ["boots-a"]);
        Assert.Equal(12, GameEngine.PreviewEquipmentStation(first, draft).TotalUpgradeCost);
        int typeIndex = first.EquipmentTypes.FindIndex(t => t.Id == "boots");
        var live = first.EquipmentTypes[typeIndex];
        first.EquipmentTypes[typeIndex] = live with { AdditionalLevels =
            [live.AdditionalLevels[0] with { UpgradeCost = 15 }, live.AdditionalLevels[1]] };
        Assert.Equal(15, GameEngine.PreviewEquipmentStation(first, draft).TotalUpgradeCost);
        var result = GameEngine.ConfirmEquipmentStation(first, draft, new Dice(), false).State;
        Assert.Equal(2, Object(result, "a", "boots-a").Level);
        Assert.Equal(25, result.SideCredits["blue"]);

        scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!.Select(o => o.Id == "boots-a" ? o with { Level = 2 } : o)] };
        var second = Open(Ready(scene)).State;
        var next = Upgrades(second, equipment: ["boots-a"]);
        Assert.Equal(9, GameEngine.PreviewEquipmentStation(second, next).TotalUpgradeCost);
        result = GameEngine.ConfirmEquipmentStation(second, next, new Dice(), false).State;
        Assert.Equal(3, Object(result, "a", "boots-a").Level);
        Assert.Equal(31, result.SideCredits["blue"]);
        scene.Units[0] = scene.Units[0] with { Inventory = [.. scene.Units[0].Inventory!.Select(o => o.Id == "boots-a" ? o with { Level = 3 } : o)] };
        var highest = Open(Ready(scene)).State;
        Assert.DoesNotContain(GameEngine.PreviewEquipmentStation(highest, null).EquipmentUpgrades, o => o.TargetId == "boots-a");
        Assert.False(GameEngine.PreviewEquipmentStation(highest, Upgrades(highest, equipment: ["boots-a"])).CanConfirm);
    }

    [Fact]
    public void OpeningCostsUseOpeningConfigurationOnceAndFailedUpgradePaysNothing()
    {
        var scene = PaidFixture();
        var type = scene.UnitTypes![0];
        var source = new LocalAbilityDefinition("operate", "Service", AbilityTiming.Action, new Operate())
            { Usage = new(1), Costs = [new LayDownSelf()] };
        scene.UnitTypes[0] = type with { LocalAbilities = [source],
            AdditionalLevels = [type.AdditionalLevels[0] with { LocalAbilities = [] }] };
        var state = Open(Ready(scene)).State;
        var draft = Upgrades(state, ["a"]);
        var rejected = state.DetachedCopy();
        rejected.SideCredits["blue"] = 0;
        Assert.Throws<EquipmentAllocationException>(() => GameEngine.ConfirmEquipmentStation(rejected, draft, new Dice(), false));
        Assert.True(rejected.IsUpright("a"));
        Assert.Empty(rejected.Units[0].AbilityUseCounts);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false);
        Assert.Single(result.Events, e => e.Kind == "PostureChanged");
        Assert.Single(result.Events, e => e.Kind == "ActionUsed");
        Assert.False(result.State.IsUpright("a"));
        Assert.Equal(7, result.State.Units[0].CurrentHp);
        Assert.Equal(1, result.ResolutionSteps[0].StateAfter.ActionsUsedThisActivation);
        Assert.True(state.IsUpright("a"));
        GameEngine.ValidateScenario(result.State);
    }

    [Fact]
    public void CrankToConsoleRetiresOldCounterAndStartsTwoNewUses()
    {
        var scene = PaidFixture();
        var gear = scene.EquipmentTypes![0];
        scene.UnitTypes![0] = scene.UnitTypes[0] with { LocalAbilities = [] };
        scene.EquipmentTypes[0] = gear with
        {
            LocalAbilities = [new("crank", "Crank", AbilityTiming.Action, new Operate()) { Usage = new(1) }],
            AdditionalLevels = [gear.AdditionalLevels[0] with
            {
                LocalAbilities = [new("console", "Console", AbilityTiming.Action, new Operate()) { Usage = new(2) }]
            }]
        };
        var state = Open(Ready(scene)).State;
        var draft = Upgrades(state, equipment: ["boots-a"]);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false).State;
        var grant = result.AbilitiesOf("a").Single(a => a.Key == "console");
        Assert.Equal(2, result.AbilityUsesOf("a", grant)!.RemainingUses);
        Assert.Empty(Object(result, "a", "boots-a").AbilityUseCounts);
        Assert.DoesNotContain(result.AbilitiesOf("a"), a => a.Key == "crank");
        GameEngine.ValidateScenario(result);
    }

    [Fact]
    public void PreviewShowsTimingAndUsesChangesUnderTheSameNativeSourceKey()
    {
        var scene = PaidFixture();
        var type = scene.UnitTypes![0];
        var opening = new LocalAbilityDefinition("swap", "Swap", AbilityTiming.Action, UnitAuthoring.Swap(RelationshipSelector.Friendlies))
            { Usage = new(2) };
        scene.UnitTypes[0] = type with { LocalAbilities = [.. type.LocalAbilities, opening],
            AdditionalLevels = [type.AdditionalLevels[0] with { LocalAbilities = [.. type.LocalAbilities,
                opening with { Timing = AbilityTiming.BonusAction, Usage = new(3) }] }] };
        var ready = Ready(scene);
        var grant = ready.AbilitiesOf("a").Single(a => a.Key == "swap");
        ready.Units[0] = ready.Units[0] with { AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add(grant.AccountingId, 1) };
        var state = Open(ready).State;
        var row = GameEngine.PreviewEquipmentStation(state, Upgrades(state, ["a"])).Units.Single(u => u.UnitId == "a");
        var before = row.OpeningAbilityPreviews.Single(p => p.Ability.Key == "swap");
        var after = row.AbilityPreviews.Single(p => p.Ability.Key == "swap");
        Assert.Equal(before.Ability.SourceReference, after.Ability.SourceReference);
        Assert.Equal(AbilityTiming.Action, before.Ability.Definition.Timing);
        Assert.Equal(AbilityTiming.BonusAction, after.Ability.Definition.Timing);
        Assert.Equal(1, before.RemainingUses);
        Assert.Equal(3, after.RemainingUses);
    }

    [Fact]
    public void SettlementAccumulatesOnlyFinalVisibilityAndPreviewLeavesExplorationUntouched()
    {
        var scene = PaidFixture();
        scene = scene with { FogOfWarSides = ["blue"],
            UnitTypes = [.. scene.UnitTypes!.Select(t => t with { SightDistance = t.Id == "carrier" ? 4 : 0,
                AdditionalLevels = t.Id == "carrier" ? [t.AdditionalLevels[0] with { SightDistance = 0 }] : t.AdditionalLevels })] };
        var state = Open(Ready(scene)).State;
        state.ExploredCells["blue"] = [new(1, 1), new(2, 1)];
        var draft = Upgrades(state, ["a"]);
        var before = Json(state);
        Assert.True(GameEngine.PreviewEquipmentStation(state, draft).CanConfirm);
        Assert.Equal(before, Json(state));
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false);
        Assert.DoesNotContain(new Cell(0, 0), result.State.ExploredCells["blue"]);
        Assert.Equal(before, Json(state));
    }

    [Fact]
    public void UnitWithoutInventoryCanOperateAndUpgradeWhenTheInventoryPoolIsEmpty()
    {
        var scene = PaidFixture();
        scene.UnitTypes![0] = scene.UnitTypes[0] with { CanHaveEquipment = false, CanHaveItems = false };
        scene = scene with
        {
            Units = [scene.Units[0] with { Inventory = [] }],
            Agency = [new("carrier", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)]
        };
        var state = Open(Ready(scene)).State;
        Assert.Empty(state.MachineInteraction!.EquipmentStation!.EligibleUnitIds);
        var draft = Upgrades(state, ["a"]);
        Assert.Empty(draft.Assignments);
        Assert.True(GameEngine.PreviewEquipmentStation(state, draft).CanConfirm);
        var result = GameEngine.ConfirmEquipmentStation(state, draft, new Dice(), false).State;
        Assert.Equal(2, result.Units[0].Level);
        Assert.Equal(7, result.Units[0].CurrentHp);
        Assert.Equal(16, result.SideCredits["blue"]);
        Assert.Empty(result.Units[0].Inventory);
    }

    [Fact]
    public void FinalOccupancyCanUseReceivedUpgradedEquipmentWithoutIntermediateRejection()
    {
        var scene = PaidFixture();
        var type = scene.UnitTypes![1];
        scene.UnitTypes[1] = type with { Amphibious = true, AdditionalLevels = [type.AdditionalLevels[0] with { Amphibious = false }] };
        scene = scene with { Board = scene.Board with { Cells = [new(new(8, 1), TerrainKind.Water)] } };
        var boots = scene.EquipmentTypes![0];
        scene.EquipmentTypes[0] = boots with { AdditionalLevels = [boots.AdditionalLevels[0] with
            { LocalAbilities = [new("swim", "Swim", AbilityTiming.Passive, new Amphibious())] }] };
        var state = Open(Ready(scene)).State;
        var failed = Upgrades(state, ["b"]);
        Assert.Contains(GameEngine.PreviewEquipmentStation(state, failed).Diagnostics, d => d.Code == "illegal-occupancy");
        var success = failed with { Assignments = [.. failed.Assignments.Select(a => a.ObjectId == "boots-a" ? a with { UnitId = "b" } : a)],
            UpgradedEquipmentIds = ["boots-a"] };
        Assert.True(GameEngine.PreviewEquipmentStation(state, success).CanConfirm);
        var result = GameEngine.ConfirmEquipmentStation(state, success, new Dice(), false).State;
        Assert.Equal(2, result.Units.Single(u => u.Id == "b").Level);
        Assert.Contains("water", result.EffectiveMovementCapabilitiesOf("b"));
        GameEngine.ValidateScenario(result);
    }
}
