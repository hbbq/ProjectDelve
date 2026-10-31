using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SpecializedSourceExecutionTests
{
    public static TheoryData<AbilitySourceKind, string> Contexts
    {
        get
        {
            var data = new TheoryData<AbilitySourceKind, string>();
            foreach (var source in Enum.GetValues<AbilitySourceKind>())
            foreach (var mechanic in new[] { "breath", "claw", "shell", "pickup", "give-unit", "give-machine", "operate", "banana", "boost", "worker", "troll" })
                data.Add(source, mechanic);
            return data;
        }
    }

    private static AbilityMechanic Mechanic(string kind) => kind switch
    {
        "breath" => new FireBreath(), "claw" => new ClawAttack(3), "shell" => new ShootShell(2, 4, 4),
        "pickup" => new PickupResource(), "give-unit" or "give-machine" => new GiveResource(), "operate" => new Operate(),
        "banana" => new PlaceBananaPeel(2), "boost" => new Boost(2),
        "worker" => new SummonWorker(2), "troll" => new SummonTroll(2),
        _ => throw new InvalidOperationException()
    };

    private static GameState Ready(AbilitySourceKind source, string kind, params LocalAbilityDefinition[] additionalNative)
    {
        var mechanic = Mechanic(kind);
        var ability = new LocalAbilityDefinition("use", "Authored Effect", mechanic is Boost or PlaceBananaPeel
            ? AbilityTiming.BonusAction : AbilityTiming.Action, mechanic) { Usage = new(source == AbilitySourceKind.Item ? null : 1) };
        var reserve = new LocalAbilityDefinition("reserve", "Reserve Guard", AbilityTiming.BonusAction,
            new SelfModifiersMechanic([new(Stat.Def, 1)]));
        var storage = new ResourceStorage([new(ResourceTypes.Coal, 9), new(ResourceTypes.Diamond, 9),
            new(ResourceTypes.Banana, 9), new(ResourceTypes.Mushroom, 9), new(ResourceTypes.Shell, 9)]);
        var actor = new UnitType("actor", 2, 4, 2, 0, 8, Actions: UnitAction.None)
        {
            Unique = true, CanHaveItems = true, CanHaveEquipment = true, ResourceStorage = storage,
            LocalAbilities = source == AbilitySourceKind.Unit ? [ability, reserve, .. additionalNative] : [reserve, .. additionalNative]
        };
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 5 },
            UnitTypeIds = ["actor", "friend", "near", "far", UnitTypeIds.Worker, UnitTypeIds.Troll],
            UnitTypes = [actor, new("friend", 0, 0, 0, 0, 8) { Unique = true, ResourceStorage = storage },
                new("near", 0, 1, 0, 0, 20) { Unique = true }, new("far", 0, 1, 0, 0, 20) { Unique = true }],
            EquipmentTypes = source == AbilitySourceKind.Equipment ? [new("equipment", "Physical Equipment") { LocalAbilities = [ability] }] : [],
            ItemTypes = source == AbilitySourceKind.Item ? [new("item", "Physical Item") { LocalAbilities = [ability] }] : [],
            Units = [new("actor", "blue", new(1, 1), UnitId: "actor") { Inventory = source switch
                {
                    AbilitySourceKind.Equipment => [new("physical-source", "equipment")],
                    AbilitySourceKind.Item => [new("physical-source") { ItemTypeId = "item" }], _ => []
                } },
                new("friend", "blue", new(1, 2), UnitId: "friend"),
                new("near", "red", new(2, 2), UnitId: "near"), new("far", "red", new(4, 1), UnitId: "far")],
            Agency = [new("actor", "blue", ControllerKind.Human), new("friend", "blue", ControllerKind.Human),
                new("near", "red", ControllerKind.Human), new("far", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            FogOfWarSides = ["blue"], Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario)));
        state.Physical.Board.Machines.Add(new("press", new(0, 1), MachineTypes.DiamondPress)
        { StoredResources = kind == "operate" ? [new(ResourceTypes.Coal, 3)] : [] });
        foreach (var resource in storage.Capacities.Select(c => c.Type)) state.StoredResources.Add(new("blue", "actor", resource, 3));
        if (kind is "pickup" or "give-unit" or "give-machine") state.Resources.Add(new(ResourceTypes.Coal, new(1, 1)));
        state.Round = 1; state.ActiveToken = new("actor", "blue"); state.CurrentUnitId = "actor"; state.MoveDone = true;
        state.Pending = new(DecisionKind.Activation, "actor", "actor", [], false);
        GameEngine.ValidateScenario(state);
        return Refresh(state);
    }

    private static GameState Refresh(GameState state) => GameEngine.RefreshChoices(state, new Dice(), false).State;
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static EngineResult Use(GameState state, string key) => GameEngine.Advance(state, new Choice(key), new Dice(), false);

    [Theory, MemberData(nameof(Contexts))]
    public void SelectedSourceSurvivesLastChargeOrItemConsumption(AbilitySourceKind source, string kind)
    {
        var state = Ready(source, kind);
        var candidate = state.Pending!.Candidates.First(c => c.Ability?.Key == "use" && (kind switch
        {
            "give-machine" or "operate" => c.MachineId == "press",
            "give-unit" => c.TargetId == "friend",
            "claw" => c.TargetId == "near", "shell" => c.TargetId == "far", _ => true
        }));
        var result = Use(state, candidate.Key);
        if (kind is "breath" or "claw" or "shell")
        {
            Assert.Equal(kind == "claw" ? 5 : kind == "shell" ? 4 : 2, result.State.AttackInProgress!.AttackDice);
            if (kind == "shell") Assert.Equal(4, result.State.AttackInProgress.RangeOverride);
            var resumed = Use(Restore(result.State), "roll-dice");
            Assert.Equal(JsonSerializer.Serialize(Use(result.State, "roll-dice")), JsonSerializer.Serialize(resumed));
            result = resumed;
        }
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Ability?.Key == "use");
        if (source == AbilitySourceKind.Item)
        {
            Assert.Empty(result.State.Units.Single(u => u.Id == "actor").Inventory);
            Assert.Equal("physical-source", Assert.Single(result.State.LootDeck!.DiscardPile).Id);
        }
        else Assert.Equal(new AbilityUses(1, 0), result.State.AbilityUsesOf("actor", candidate.Ability!));
        switch (kind)
        {
            case "breath":
                Assert.Equal(18, result.State.Units.Single(u => u.Id == "near").CurrentHp);
                Assert.Equal(18, result.State.Units.Single(u => u.Id == "far").CurrentHp);
                Assert.Equal(8, result.State.Units.Single(u => u.Id == "friend").CurrentHp);
                break;
            case "claw": Assert.Equal(15, result.State.Units.Single(u => u.Id == "near").CurrentHp); break;
            case "shell":
                Assert.Equal(16, result.State.Units.Single(u => u.Id == "far").CurrentHp);
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor", ResourceTypes.Shell.Id));
                break;
            case "pickup":
                Assert.Empty(result.State.Resources);
                Assert.Equal(4, result.State.StoredQuantity("blue", "actor", ResourceTypes.Coal.Id));
                break;
            case "give-unit":
                Assert.Empty(result.State.Resources);
                Assert.Equal(1, result.State.StoredQuantity("blue", "friend", ResourceTypes.Coal.Id));
                break;
            case "give-machine":
                Assert.Empty(result.State.Resources);
                Assert.Equal(1, result.State.Physical.Board.Machines.Single().StoredQuantity(ResourceTypes.Coal.Id));
                break;
            case "operate":
                Assert.Equal(ResourceTypes.Diamond, Assert.Single(result.State.Resources).Type);
                Assert.Equal(0, result.State.Physical.Board.Machines.Single().StoredQuantity(ResourceTypes.Coal.Id));
                break;
            case "banana":
                Assert.Single(result.State.CellMarkers);
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor", ResourceTypes.Banana.Id));
                break;
            case "boost":
                Assert.Equal(1, result.State.MoveOpportunityModifierThisActivation);
                Assert.False(result.State.MoveDone);
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor", ResourceTypes.Mushroom.Id));
                break;
            case "worker": Assert.Single(result.State.Units.Where(u => u.TypeId == UnitTypeIds.Worker)); break;
            case "troll":
                Assert.Single(result.State.Units.Where(u => u.TypeId == UnitTypeIds.Troll));
                Assert.Equal(1, result.State.StoredQuantity("blue", "actor", ResourceTypes.Diamond.Id));
                break;
        }
        GameEngine.ValidateScenario(Restore(result.State));
    }

    [Theory]
    [InlineData(AbilitySourceKind.Equipment)]
    [InlineData(AbilitySourceKind.Item)]
    public void SelectedClawAndShellParametersOverrideUnrelatedNativeDeclarations(AbilitySourceKind source)
    {
        var claw = Ready(source, "claw",
            new LocalAbilityDefinition("native-claw", "Unrelated Claw", AbilityTiming.Action, new ClawAttack(0)));
        var clawCandidate = claw.Pending!.Candidates.Single(c => c.Ability?.Key == "use" && c.TargetId == "near");
        Assert.Equal(5, new GameplayQueries(claw).EffectiveAttackDice("actor", clawCandidate));
        Assert.Equal(5, Use(claw, clawCandidate.Key).State.AttackInProgress!.AttackDice);

        var shell = Ready(source, "shell",
            new LocalAbilityDefinition("native-shell", "Unrelated Shell", AbilityTiming.Action, new ShootShell(3, 1, 1)));
        Assert.DoesNotContain(shell.Pending!.Candidates, c => c.Ability?.Key == "native-shell" && c.TargetId == "far");
        var shellCandidate = shell.Pending!.Candidates.Single(c => c.Ability?.Key == "use" && c.TargetId == "far");
        Assert.Equal(4, new GameplayQueries(shell).EffectiveAttackDice("actor", shellCandidate));
        var started = Use(shell, shellCandidate.Key);
        Assert.Equal(4, started.State.AttackInProgress!.AttackDice);
        Assert.Equal(4, started.State.AttackInProgress.RangeOverride);
        Assert.Equal(1, started.State.StoredQuantity("blue", "actor", ResourceTypes.Shell.Id));
        var resumed = Use(Restore(started.State), "roll-dice");
        Assert.Equal(16, resumed.State.Units.Single(u => u.Id == "far").CurrentHp);
    }

    [Fact]
    public void MultipleBananaDeclarationsUseSelectedCostsAndIndependentAllowances()
    {
        var state = Ready(AbilitySourceKind.Unit, "banana",
            new LocalAbilityDefinition("cheap", "Cheap Banana", AbilityTiming.BonusAction, new PlaceBananaPeel(1)) { Usage = new(1) });
        var expensive = state.Pending!.Candidates.First(c => c.Ability?.Key == "use");
        state = Use(state, expensive.Key).State;
        Assert.Equal(1, state.StoredQuantity("blue", "actor", ResourceTypes.Banana.Id));
        var cheap = state.Pending!.Candidates.First(c => c.Ability?.Key == "cheap");
        state = Use(state, cheap.Key).State;
        Assert.Equal(0, state.StoredQuantity("blue", "actor", ResourceTypes.Banana.Id));
        Assert.Equal(2, state.CellMarkers.Count);
        Assert.Equal(1, state.BonusActionUsesThisActivation(expensive.Ability!.AccountingId));
        Assert.Equal(1, state.BonusActionUsesThisActivation(cheap.Ability!.AccountingId));
    }

    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
