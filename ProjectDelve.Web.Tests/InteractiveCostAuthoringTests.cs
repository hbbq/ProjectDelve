using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class InteractiveCostAuthoringTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-interactive-costs-" + Guid.NewGuid().ToString("N"));
    private static LocalAbilityDefinition Door(ImmutableArray<AbilityCost> costs, int? uses = null) =>
        new("try-open-door", "Try Open Door", AbilityTiming.Action, new TryOpenDoor(2)) { Costs = costs, Usage = new(uses) };
    private static EquipmentDefinition Knocker(ImmutableArray<AbilityCost>? costs = null) =>
        new("diamond-door-knocker", "Diamond Door Knocker")
        { LocalAbilities = [Door(costs ?? [new SpendResource(ResourceTypeIds.Diamond, 1)])] };
    private static ContentEditorResponse Validate(string family, object definition) =>
        ContentEditorApi.Validate(new(family, JsonSerializer.SerializeToElement(definition, definition.GetType(), Json)));
    private static T Restore<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    [Theory]
    [InlineData("units")]
    [InlineData("equipment")]
    [InlineData("items")]
    public void AllSharedAbilityOwnersValidateRoundTripAndDescribeOrderedCosts(string family)
    {
        var ability = Door([new SpendResource("diamond", 1), new LayDownSelf(), new SpendResource("diamond", 2)], family == "items" ? null : 2);
        object owner = family switch
        {
            "units" => new UnitType("cost-owner", 1, 0, 0, 0, 1) { LocalAbilities = [ability] },
            "items" => new ItemDefinition("cost-owner", "Cost owner") { LocalAbilities = [ability] },
            _ => new EquipmentDefinition("cost-owner", "Cost owner") { LocalAbilities = [ability] }
        };
        var response = Validate(family, owner);
        Assert.True(response.Valid, string.Join("; ", response.Diagnostics));
        var row = Assert.Single(response.Entry!.Abilities);
        Assert.Equal(ability.Key, row.Key);
        Assert.Equal(ability.CardEntry().Description, row.Description);
        var serialized = response.Definition!.Value.GetProperty("localAbilities")[0];
        Assert.Equal(new[] { "spendResource", "layDownSelf", "spendResource" }, serialized.GetProperty("costs").EnumerateArray().Select(c => c.GetProperty("kind").GetString()));
        var restored = serialized.Deserialize<LocalAbilityDefinition>(Json)!;
        Assert.Equal(ability.Costs.ToArray(), restored.Costs.ToArray());
        Assert.Equal(ability.Usage, restored.Usage);
        var dependencies = response.Entry.References.Where(reference => reference.Family == "resources").ToArray();
        Assert.Contains(dependencies, reference => reference.Id == "diamond" && reference.Exists);
    }

    [Fact]
    public void InvalidCostFieldsReturnPathsForTheRecursiveEditor()
    {
        void Error(LocalAbilityDefinition ability, string path)
        {
            var response = Validate("equipment", Knocker() with { LocalAbilities = [ability] });
            Assert.False(response.Valid);
            Assert.Contains(response.Diagnostics, d => d.Path == "definition.localAbilities[0]." + path);
        }
        Error(Door([new LayDownSelf(), new LayDownSelf()]), "costs[1]");
        Error(Door([new SpendResource("diamond", 0)]), "costs[0].amount");
        Error(Door([new SpendResource("diamond", -1)]), "costs[0].amount");
        Error(Door([new SpendResource("absent-resource", 1)]), "costs[0].resourceTypeId");
        Error(Door([new LayDownSelf()]) with { Timing = AbilityTiming.Passive }, "costs[0]");
        Error(Door([new LayDownSelf()]) with { Timing = AbilityTiming.AfterAttack, Mechanic = new Cleave() }, "costs[0]");
        var item = new ItemDefinition("invalid-counter", "Invalid counter") { LocalAbilities = [Door([new LayDownSelf()], 2)] };
        var invalid = Validate("items", item);
        Assert.False(invalid.Valid);
        Assert.Contains(invalid.Diagnostics, d => d.Path.StartsWith("definition.localAbilities[0].costs"));
    }

    [Fact]
    public void WorkspaceCanonicalOverridePromotionAndScenarioTransportKeepCostOrder()
    {
        var canonical = new CanonicalContent(Path.Combine(root, "canonical"));
        var workspace = new ContentWorkspace(Path.Combine(root, "workspace"), () => canonical.Bundle);
        var equipment = Knocker([new SpendResource("diamond", 1), new LayDownSelf()]);
        var response = Validate("equipment", equipment);
        Assert.True(response.Valid);
        equipment = response.Definition!.Value.Deserialize<EquipmentDefinition>(Json)!;
        var saved = workspace.Save(new() { Equipment = [equipment, new("unrelated", "Unrelated")] }, workspace.Snapshot().Token);
        var reopened = new ContentWorkspace(Path.Combine(root, "workspace"), () => canonical.Bundle).Snapshot();
        Assert.Equal(saved.Token, reopened.Token);
        Assert.Equal(equipment.LocalAbilities[0].Costs.ToArray(), reopened.Definitions.Equipment[0].LocalAbilities[0].Costs.ToArray());
        var scenario = Fixture(equipment) with { EquipmentTypes = [] };
        var captured = WorkspaceScenarioExport.Capture(scenario, reopened.Definitions, canonical.Bundle);
        var portable = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(captured));
        Assert.Equal(equipment.LocalAbilities[0].Costs.ToArray(), portable.EquipmentTypes!.Single(e => e.Id == equipment.Id).LocalAbilities[0].Costs.ToArray());
        var promotion = new WorkspacePromotionService(workspace, canonical);
        var selected = ImmutableArray.Create(new ContentIdentity(ContentFamily.Equipment, equipment.Id));
        promotion.Publish(new(promotion.Preview(new(saved.Token, selected)), true));
        Assert.Equal("unrelated", Assert.Single(workspace.Snapshot().Definitions.Equipment).Id);
        var overrideDefinition = equipment with { LocalAbilities = [Door([new LayDownSelf(), new SpendResource("diamond", 2)])] };
        var current = workspace.Snapshot();
        var edited = workspace.Save(current.Definitions with { Equipment = current.Definitions.Equipment.Add(overrideDefinition) }, current.Token);
        var running = GameEngine.CreateGame(scenario, workspace.Resolve().Definitions);
        var runningJson = JsonSerializer.Serialize(running, Json);
        var preview = promotion.Preview(new(edited.Token, selected));
        Assert.Single(preview.Replacements);
        promotion.Publish(new(preview, true));
        var published = new CanonicalContent(Path.Combine(root, "canonical")).Bundle.Equipment.Single(e => e.Id == equipment.Id);
        Assert.Equal(overrideDefinition.LocalAbilities[0].Costs.ToArray(), published.LocalAbilities[0].Costs.ToArray());
        Assert.Equal(runningJson, JsonSerializer.Serialize(running, Json));
        var local = equipment with { LocalAbilities = [Door([new SpendResource("diamond", 3)])] };
        var overridden = GameEngine.CreateGame(scenario with { EquipmentTypes = [local] }, canonical.Bundle);
        Assert.Equal(local.LocalAbilities[0].Costs.ToArray(), overridden.EquipmentTypes.Single(e => e.Id == local.Id).LocalAbilities[0].Costs.ToArray());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, false)]
    public void DiamondDoorKnockerPaysForEitherResultAndResumesWithoutRepayment(int roll, bool opens)
    {
        var equipment = Knocker();
        var canonical = ContentBundle.Canonical();
        var workspace = new ContentWorkspace(Path.Combine(root, "workspace"), () => canonical);
        var response = Validate("equipment", equipment);
        Assert.True(response.Valid);
        workspace.Save(new() { Equipment = [response.Definition!.Value.Deserialize<EquipmentDefinition>(Json)!] }, workspace.Snapshot().Token);
        var state = Ready(Fixture(equipment) with { EquipmentTypes = [] }, workspace.Resolve().Definitions);
        var candidate = Paid(state);
        var initial = JsonSerializer.Serialize(state, Json);
        Assert.Throws<ArgumentException>(() => Use(state, "not-a-door", roll));
        Assert.Throws<ArgumentException>(() => Use(state, null, roll));
        Assert.Equal(initial, JsonSerializer.Serialize(state, Json));
        var cancelled = Use(state, "end-turn", roll);
        Assert.Equal(1, cancelled.State.StoredQuantity("blue", "bearer", "diamond"));
        var unaffordable = Restore(state);
        unaffordable.StoredResources[0] = unaffordable.StoredResources[0] with { Quantity = 0 };
        Assert.DoesNotContain(GameEngine.RefreshChoices(unaffordable, new Dice(roll), false).NextInput!.Candidates, c => c.Ability?.Key == candidate.Ability!.Key);
        var unpaid = JsonSerializer.Serialize(unaffordable, Json);
        Assert.Throws<ArgumentException>(() => Use(unaffordable, candidate.Key, roll));
        Assert.Equal(unpaid, JsonSerializer.Serialize(unaffordable, Json));
        var committed = Use(Restore(state), candidate.Key, roll);
        Assert.Equal(0, committed.State.StoredQuantity("blue", "bearer", "diamond"));
        Assert.Single(committed.Events.Where(e => e.Kind == "ResourceSpent"));
        Assert.NotNull(committed.State.DoorInProgress);
        Assert.Equal(EdgeKind.ClosedDoor, Assert.Single(committed.State.Physical.Board.Edges).Kind);
        Assert.Equal(DecisionKind.RollDice, committed.NextInput!.Kind);
        var result = Use(Restore(committed.State), "roll-dice", roll);
        Assert.Equal(JsonSerializer.Serialize(Use(committed.State, "roll-dice", roll), Json), JsonSerializer.Serialize(result, Json));
        Assert.Equal(opens, Assert.Single(result.Events.Where(e => e.Kind == "DoorOpeningAttemptResolved")).Succeeded);
        Assert.Equal(opens ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor, Assert.Single(result.State.Physical.Board.Edges).Kind);
        Assert.DoesNotContain(result.Events, e => e.Kind == "ResourceSpent");
        Assert.Equal(0, result.State.StoredQuantity("blue", "bearer", "diamond"));
        Assert.Equal("physical-knocker", Assert.Single(result.State.Units.Single(u => u.Id == "actor").Inventory).Id);
        Assert.Equal(AbilitySourceKind.Equipment, committed.State.DoorInProgress!.AbilitySource!.Kind);
        Assert.Equal(JsonSerializer.Serialize(Use(state, candidate.Key, roll), Json), JsonSerializer.Serialize(committed, Json));
    }

    [Fact]
    public void CumulativeCostsRejectPartialPaymentAndPhysicalSourcesKeepTheirOwnLifecycle()
    {
        foreach (var source in new[] { AbilitySourceKind.Equipment, AbilitySourceKind.Item })
        {
            var ability = Door([new SpendResource("diamond", 1), new SpendResource("diamond", 1)], source == AbilitySourceKind.Equipment ? 1 : null);
            var equipment = Knocker() with { LocalAbilities = [ability] };
            var scenario = Fixture(equipment) with { StoredResources = [new("blue", "bearer", ResourceTypes.Diamond, 2)] };
            if (source == AbilitySourceKind.Item)
                scenario = scenario with
                {
                    EquipmentTypes = [], ItemTypes = [new(equipment.Id, equipment.Name) { LocalAbilities = [ability] }],
                    Units = [scenario.Units[0] with { Inventory = [new("physical-knocker") { ItemTypeId = equipment.Id }] }, scenario.Units[1]]
                };
            var state = Ready(scenario);
            var insufficient = Restore(state);
            insufficient.StoredResources[0] = insufficient.StoredResources[0] with { Quantity = 1 };
            Assert.DoesNotContain(GameEngine.RefreshChoices(insufficient, new Dice(1), false).NextInput!.Candidates, c => c.Ability?.Key == ability.Key);
            var candidate = Paid(state);
            var started = Use(state, candidate.Key, 1);
            Assert.Equal(new int?[] { 1, 0 }, started.Events.Where(e => e.Kind == "ResourceSpent").Select(e => e.StoredQuantity));
            if (source == AbilitySourceKind.Item)
            {
                Assert.Empty(started.State.Units[0].Inventory);
                Assert.Equal("physical-knocker", Assert.Single(started.State.LootDeck!.DiscardPile).Id);
            }
            else Assert.Equal(new AbilityUses(1, 0), started.State.AbilityUsesOf("actor", candidate.Ability!));
            var resumed = Use(Restore(started.State), "roll-dice", 1);
            Assert.Contains(resumed.Events, e => e.Kind == "DoorOpened");
            Assert.DoesNotContain(resumed.Events, e => e.Kind is "ResourceSpent" or "ItemUsed");
        }
    }

    [Fact]
    public void HiddenKnockerPaymentAndDoorResolutionRemainOutsideTheObserversProjection()
    {
        var state = Ready(Fixture(Knocker()) with { FogOfWarSides = ["red"] });
        Assert.False(FogOfWar.IsUnitObservable(state, "red", "actor"));
        var started = Use(state, Paid(state).Key, 6);
        var payment = Assert.Single(started.ResolutionSteps.Where(s => started.Events[s.EventIndex].Kind == "ResourceSpent"));
        Assert.Null(FogOfWar.ObserveEvent(payment.StateAfter, "red", started.Events[payment.EventIndex]));
        var visible = FogOfWar.Observe(started.State, "red");
        Assert.DoesNotContain(visible.Units, unit => unit.Id == "actor");
        Assert.DoesNotContain(visible.StoredResources, resource => resource.SideId == "blue");
        Assert.DoesNotContain("physical-knocker", JsonSerializer.Serialize(visible, Json));
        var failed = Use(Restore(started.State), "roll-dice", 6);
        foreach (var step in failed.ResolutionSteps.Where(s => failed.Events[s.EventIndex].Kind == "DoorOpeningAttemptResolved"))
            Assert.Null(FogOfWar.ObserveEvent(step.StateAfter, "red", failed.Events[step.EventIndex]));
        Assert.Equal(0, failed.State.StoredQuantity("blue", "bearer", "diamond"));
    }

    private static ScenarioDefinition Fixture(EquipmentDefinition equipment) => new()
    {
        Board = new() { Width = 8, Height = 4, Edges = [new(new(1, 0), EdgeDirection.Down, EdgeKind.ClosedDoor)] },
        UnitTypeIds = ["bearer", "observer"],
        UnitTypes = [new("bearer", 1, 0, 0, 0, 1, Actions: UnitAction.None)
            {
                CanHaveEquipment = true, CanHaveItems = true, ResourceStorage = new([new(ResourceTypes.Diamond, 3)]),
                LocalAbilities = [new("reserve", "Reserve", AbilityTiming.BonusAction, new SelfModifiersMechanic([new(Stat.Def, 1)]))]
            }, new("observer", 0, 0, 0, 0, 1)],
        EquipmentTypes = [equipment],
        Units = [new("bearer", "blue", new(1, 1), UnitId: "actor") { Inventory = [new("physical-knocker", equipment.Id)] },
            new("observer", "red", new(7, 3), UnitId: "observer")],
        Agency = [new("bearer", "blue", ControllerKind.Human), new("observer", "red", ControllerKind.Human)],
        StoredResources = [new("blue", "bearer", ResourceTypes.Diamond, 1)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };
    private static GameState Ready(ScenarioDefinition definition, ContentBundle? content = null)
    {
        content ??= ContentBundle.Canonical();
        var state = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition), null, content), content);
        state.Round = 1; state.ActiveToken = new("bearer", "blue"); state.CurrentUnitId = "actor"; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "bearer", "actor", [], false);
        return GameEngine.RefreshChoices(state, new Dice(1), false).State;
    }
    private static Candidate Paid(GameState state) => state.Pending!.Candidates.First(c => c.Ability?.Key == "try-open-door");
    private static EngineResult Use(GameState state, string? key, int roll) => GameEngine.Advance(state, new Pick(key), new Dice(roll), false);
    private sealed class Pick(string? key) : IDecisionProvider { public string? Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice(int roll) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => roll;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
