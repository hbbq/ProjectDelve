using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ContentCompositionTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
        public int RollD8() => 1;
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static GameState Restore(GameState state) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 5, Height = 5 }, UnitTypeIds = [UnitTypeIds.Shaman],
        Units = [new(UnitTypeIds.Shaman, "blue", new(2, 2), UnitId: "actor")],
        Agency = [new(UnitTypeIds.Shaman, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };

    [Fact]
    public void ExplicitSourcesComposeAndScenarioDefinitionsReplaceProviders()
    {
        var canonical = ContentBundle.Canonical();
        var local = new ContentBundle { Units = [UnitRoster.Shaman() with { DisplayName = "Local Shaman" }] };
        var result = ContentComposition.Compose([canonical], local);
        Assert.Equal("Local Shaman", result.Definitions.Units.Single(u => u.Id == UnitTypeIds.Shaman).DisplayName);
        Assert.Equal("Shaman", canonical.Units.Single(u => u.Id == UnitTypeIds.Shaman).DisplayName);
        Assert.Throws<ContentCompositionException>(() => ContentComposition.Compose([canonical, canonical]));
        Assert.Equal(canonical.Equipment[0], ContentComposition.Compose([canonical], new() { Equipment = [canonical.Equipment[0]] }).Definitions.Equipment.Last());
        Assert.Empty(ContentComposition.Compose([new ContentBundle()]).Definitions.Units);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario(), new ContentBundle()));
        Assert.Equal("Local Shaman", GameEngine.CreateGame(Scenario(), result).Types.Single(t => t.Id == UnitTypeIds.Shaman).DisplayName);
        var assembly = new ContentBundle { Assemblies = [new("composed", "Composed", "stone-floor")] };
        var composed = ContentComposition.Compose([canonical, assembly]);
        Assert.Empty(composed.Definitions.Assemblies);
        Assert.NotNull(GameEngine.CreateGame(Scenario(), composed).Physical.Board.TerrainTypes.Single(t => t.Id == "composed").Assembly);
        var conflictingComponent = canonical with { FixedComponents = [.. canonical.FixedComponents.Select(c => c.Id == "table" ? c with { Name = "Conflicting Table" } : c)] };
        Assert.Throws<ContentCompositionException>(() => ContentComposition.Compose([conflictingComponent]));
        var resources = new ContentBundle { Resources = [ResourceTypes.Coal, ResourceTypes.Diamond] };
        var conflict = Assert.Throws<ContentCompositionException>(() => ContentComposition.Compose([resources,
            new() { Resources = [ResourceTypes.Diamond], Source = new("resources.json", "second") }]));
        Assert.Equal(new(ContentFamily.Resource, "diamond"), Assert.Single(conflict.Diagnostics).Dependency);
        Assert.Equal("resources.json", conflict.Diagnostics[0].Source!.Document);
    }

    [Fact]
    public void ScopePrecedenceReplacesWholeDefinitionsAndValidatesEffectiveReferences()
    {
        var canonical = ContentBundle.Canonical();
        var workspace = new ContentBundle
        {
            Units = [UnitRoster.Goblin() with { Atk = 17, DisplayName = "Workspace Goblin" }],
            Equipment = [new("lucky-boots", "Workspace Boots") { LocalAbilities =
                [new("summon", "Summon", AbilityTiming.Action, new SummonAdjacent(UnitTypeIds.Goblin, Posture.Lying))] }]
        };
        var scenario = new ContentBundle { Units = [UnitRoster.Goblin() with { Atk = 23 }] };
        var effective = ContentComposition.ComposeScopes(canonical, workspace, scenario);
        Assert.Equal(23, effective.Definitions.Units.Single(u => u.Id == UnitTypeIds.Goblin).Atk);
        Assert.Single(effective.Definitions.Equipment.Single(e => e.Id == "lucky-boots").LocalAbilities);
        Assert.Contains(new ContentIdentity(ContentFamily.Unit, UnitTypeIds.Goblin),
            effective.Dependencies.Closure([new(ContentFamily.Equipment, "lucky-boots")]));
        var invalidShadowed = workspace with { Equipment = [new("lucky-boots", "Shadowed") { LocalAbilities =
            [new("summon", "Summon", AbilityTiming.Action, new SummonAdjacent("missing", Posture.Lying))] }] };
        _ = ContentComposition.ComposeScopes(canonical, invalidShadowed, new() { Equipment = [canonical.Equipment.Single(e => e.Id == "lucky-boots")] });
        Assert.Throws<ContentCompositionException>(() => ContentComposition.ComposeScopes(canonical,
            workspace with { Units = [workspace.Units[0], workspace.Units[0]] }, scenario));
        Assert.Equal("Goblin", canonical.Units.Single(u => u.Id == UnitTypeIds.Goblin).DisplayName);
    }

    [Fact]
    public void ScopedLootAndEquipmentUseFinalDefinitionsAndSnapshotsStayIsolated()
    {
        var canonical = ContentBundle.Canonical();
        var workspace = new ContentBundle { Equipment = [new("lucky-boots", "Workspace Boots") { LocalAbilities =
            [new("stride", "Stride", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 4)]))] }] };
        var provider = ContentComposition.ComposeScopes(canonical, workspace).Definitions;
        var scenario = Scenario() with
        {
            UnitTypes = [UnitRoster.Shaman() with { CanHaveEquipment = true }],
            LootCardDefinitions = [new("pocket-breakfast", "Boots Reward") { EquipmentTypeId = "lucky-boots" }],
            Loot = new() { Composition = [new(LootCardKind.PocketBreakfast)] }, LootMarkers = [new("loot", new(2, 2))]
        };
        var state = Restore(GameEngine.CreateGame(scenario, provider));
        var started = GameEngine.StartRound(state, new Dice(), false);
        var collected = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        Assert.Equal(6, collected.State.EffectiveMovOf("actor"));
        Assert.Equal("Workspace Boots", collected.State.Equipment("lucky-boots").Name);
        var other = GameEngine.CreateGame(Scenario(), canonical);
        Assert.Equal("Lucky Boots", other.Equipment("lucky-boots").Name);
        Assert.Equal("pocket-breakfast", Assert.Single(collected.State.Units[0].Inventory).LootCardId);
    }

    [Fact]
    public void DependencyOnlyLocalUnitsRemainOutsideInitialActivationAndPlacementSelection()
    {
        var summoned = new UnitType("portable-helper", 1, 0, 0, 0, 1);
        var summoner = new UnitType(UnitTypeIds.Shaman, 1, 0, 0, 0, 1) { LocalAbilities =
            [new("call", "Call", AbilityTiming.Action, new SummonAdjacent(summoned.Id, Posture.Lying))] };
        var scenario = Scenario() with { UnitTypes = [summoner, summoned] };
        // An exported closure may define an unplaced dependency without selecting it for setup.
        var initial = Restore(GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scenario))));
        Assert.Contains(initial.Types, type => type.Id == summoned.Id);
        Assert.DoesNotContain(initial.Units, unit => unit.TypeId == summoned.Id);
        Assert.Equal(new[] { summoner.Id }, initial.ActivationTypeOrder);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario with
            { Units = [.. scenario.Units, new(summoned.Id, "blue", new(0, 0))] }));
        var started = GameEngine.StartRound(initial, new Dice(), false);
        Assert.DoesNotContain(started.State.Bag, token => token.TypeId == summoned.Id);
        var action = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        var summon = action.NextInput!.Candidates.First(candidate => candidate.Action == UnitAction.SummonAdjacent);
        var result = GameEngine.Advance(Restore(action.State), new Choice(summon.Key), new Dice(), false);
        Assert.Contains(result.State.Units, unit => unit.TypeId == summoned.Id);
        Assert.Equal(new[] { summoner.Id, summoned.Id }, result.State.ActivationTypeOrder);
        Assert.DoesNotContain(result.State.Bag, token => token.TypeId == summoned.Id);
    }

    [Fact]
    public void MissingDependencyHasIdentityFieldAndProviderLocation()
    {
        var source = new ContentBundle { Source = new("cards.json", "provider"),
            LootCards = [new("card", "Card") { EquipmentTypeId = "missing" }] };
        var error = Assert.Throws<ContentCompositionException>(() => ContentComposition.Compose([source]));
        var diagnostic = Assert.Single(error.Diagnostics);
        Assert.Equal(new(ContentFamily.Equipment, "missing"), diagnostic.Dependency);
        Assert.Equal("equipmentTypeId", diagnostic.Path);
        Assert.Equal("cards.json", diagnostic.Source!.Document);
        Assert.Equal("lootCardDefinitions[0]", diagnostic.Source.Path);
    }

    [Fact]
    public void UnplacedSummonSurvivesSaveAndAChangedProviderWithoutChangingBagOrder()
    {
        var first = ContentBundle.Canonical();
        first = ContentComposition.ComposeScopes(first, new()
        { Units = [UnitRoster.Goblin() with { Atk = 17, DisplayName = "Captured Goblin" }] }).Definitions;
        var state = GameEngine.CreateGame(Scenario(), first);
        Assert.Contains(state.Types, t => t.Id == UnitTypeIds.Goblin && t.Atk == 17);
        Assert.DoesNotContain(state.Units, u => u.TypeId == UnitTypeIds.Goblin);
        var saved = JsonSerializer.Serialize(state);
        var second = first with { Units = [.. first.Units.Select(t => t.Id == UnitTypeIds.Goblin ? t with { Atk = 99 } : t)] };
        Assert.Equal(99, GameEngine.CreateGame(Scenario(), second).Types.Single(t => t.Id == UnitTypeIds.Goblin).Atk);
        var restored = JsonSerializer.Deserialize<GameState>(saved)!;
        var started = GameEngine.StartRound(restored, new Dice(), false);
        var action = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        var summon = action.NextInput!.Candidates.First(c => c.Action == UnitAction.SummonAdjacent);
        var result = GameEngine.Advance(Restore(action.State), new Choice(summon.Key), new Dice(), false);
        Assert.Contains(result.Events, e => e.Kind == "UnitCreated");
        Assert.Equal(17, result.State.Types.Single(t => t.Id == UnitTypeIds.Goblin).Atk);
        Assert.Equal(new[] { UnitTypeIds.Shaman, UnitTypeIds.Goblin }, result.State.ActivationTypeOrder);
        Assert.DoesNotContain(result.State.Bag, t => t.TypeId == UnitTypeIds.Goblin);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(GameEngine.Advance(Restore(action.State), new Choice(summon.Key), new Dice(), false)));
        result.State.Types.RemoveAll(t => t.Id == UnitTypeIds.Goblin);
        Assert.Throws<ArgumentException>(() => result.State.PlaceUnit(UnitTypeIds.Goblin, "missing", "blue", new(4, 4)));
    }

    [Fact]
    public void EmptyCatalogDoesNotAcquireCanonicalContentAndMissingSnapshotIsRejected()
    {
        var unit = new UnitType("only", 1, 0, 0, 0, 1);
        var bundle = new ContentBundle { Units = [unit], Terrain = [new("stone-floor", "Floor", true, false)] };
        var scenario = Scenario() with { UnitTypeIds = [unit.Id], Units = [new(unit.Id, "blue", new(0, 0))], Agency = [new(unit.Id, "blue", ControllerKind.Human)] };
        var state = GameEngine.CreateGame(scenario, bundle);
        var restored = Restore(state);
        Assert.Empty(restored.EquipmentTypes); Assert.Empty(restored.ItemTypes); Assert.Empty(restored.LootCardDefinitions);
        Assert.Empty(restored.WorldCardDefinitions); Assert.Empty(restored.ResourceDefinitions);
        Assert.Single(restored.Physical.Board.TerrainTypes);
        Assert.Null(UnitContent.Find(UnitTypeIds.Goblin, restored.Types));
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(state))!.AsObject();
        json.Remove("EquipmentTypes");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameState>(json.ToJsonString()));
    }

    [Fact]
    public void LootWorldTerrainAndResourcesUseCapturedPayloadsAfterResume()
    {
        var bundle = ContentBundle.Canonical();
        bundle = bundle with
        {
            Equipment = [.. bundle.Equipment.Select(e => e.Id == "lucky-boots" ? e with { Name = "Captured Boots" } : e)],
            Items = [.. bundle.Items.Select(i => i.Id == "second-wind" ? i with { Name = "Captured Item" } : i)],
            WorldCards = [new("captured-world", "Captured World", WorldCardLifecycle.Immediate) { Immediate = new LayDownAllUnitsMechanic() }],
            DefaultWorldDeck = [new("captured-world")],
            Resources = [.. bundle.Resources.Select(r => r.Id == "coal" ? r with { Name = "Captured Coal" } : r)]
        };
        var scenario = Scenario() with
        {
            Board = new() { Width = 5, Height = 5, Cells = [new(new(3, 3), TerrainKind.Lava)] },
            WorldEffects = new(1, 1), Loot = new() { Composition = [new(LootCardKind.LuckyBoots), new(LootCardKind.SecondWind)] }
        };
        var state = Restore(GameEngine.CreateGame(scenario, bundle));
        Assert.Equal("Captured Boots", state.Equipment("lucky-boots").Name);
        Assert.Equal("Captured Item", state.Item("second-wind").Name);
        Assert.Equal(new[] { "lucky-boots", "second-wind" }, state.LootDeck!.DrawPile.Select(o => o.LootCardId));
        Assert.Equal(new[] { "captured-world" }, state.WorldEffects!.Composition!.Select(r => r.TypeId));
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Contains(result.Events, e => e.Kind == "ResourceCreated" && e.ResourceType!.Name == "Captured Coal");
        Assert.Contains(result.Events, e => e.Kind == "PostureChanged" && e.Posture == Posture.Lying && e.ActionId == "world:captured-world");
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(GameEngine.StartRound(Restore(state), new Dice(), false)));
        var observed = FogOfWar.Observe(result.State, null);
        Assert.DoesNotContain(observed.ResourceDefinitions, r => r.Id == "diamond"); Assert.Empty(observed.EquipmentTypes); Assert.Empty(observed.LootCardDefinitions);
    }

    [Fact]
    public void CapturedLootAcquisitionAndPhysicalEquipmentCountersSurviveResume()
    {
        var bundle = ContentBundle.Canonical();
        bundle = bundle with { Equipment = [.. bundle.Equipment.Select(e => e.Id == "lucky-boots" ? e with
        { LocalAbilities = [new("stride", "Stride", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 4)]))] } : e)] };
        var scenario = Scenario() with
        {
            UnitTypes = [UnitRoster.Shaman().Without<SummonAdjacent>() with { CanHaveEquipment = true }],
            EquipmentTypes = [new("charged-bell", "Captured Bell") { LocalAbilities =
                [new("call", "Call", AbilityTiming.Action, new SummonAdjacent(UnitTypeIds.Goblin, Posture.Lying)) { Usage = new(2) }] }],
            Units = [Scenario().Units[0] with { Inventory = [new("physical-bell", "charged-bell")] }],
            Loot = new() { Composition = [new(LootCardKind.LuckyBoots)] }, LootMarkers = [new("marker", new(2, 2))]
        };
        var initial = Restore(GameEngine.CreateGame(scenario, bundle));
        var started = GameEngine.StartRound(initial, new Dice(), false);
        var action = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        Assert.Equal(6, action.State.EffectiveMovOf("actor"));
        Assert.Contains(action.State.Units[0].Inventory, o => o.EquipmentTypeId == "lucky-boots");
        var summon = action.NextInput!.Candidates.First(c => c.Ability?.SourceInstanceId == "physical-bell");
        var used = GameEngine.Advance(Restore(action.State), new Choice(summon.Key), new Dice(), false).State;
        var restored = Restore(used);
        Assert.Equal(1, restored.Units[0].Inventory.Single(o => o.Id == "physical-bell").AbilityUseCounts["call"]);
        Assert.Equal("Captured Bell", restored.Equipment("charged-bell").Name);
        GameEngine.ValidateScenario(restored);
    }

    [Fact]
    public void ProductionReferencesResolveAgainstAnExplicitNoncanonicalResourceList()
    {
        var unit = new UnitType("only", 0, 0, 0, 0, 1);
        var bundle = new ContentBundle
        {
            Units = [unit], Resources = [new("crystal", "Captured Crystal")],
            Terrain = [new("stone-floor", "Floor", true, false), new("crystal-source", "Source", true, false)
            { Production = new DirectionalResourceProduction("crystal", 1) }]
        };
        var scenario = Scenario() with
        {
            UnitTypeIds = [unit.Id], Units = [new(unit.Id, "blue", new(0, 0))], Agency = [new(unit.Id, "blue", ControllerKind.Human)],
            Board = new() { Width = 5, Height = 5, Cells = [new(new(3, 3), TerrainKind.StoneFloor) { TerrainTypeId = "crystal-source" }] }
        };
        var state = Restore(GameEngine.CreateGame(scenario, bundle));
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(new ResourceType("crystal", "Captured Crystal"), Assert.Single(result.State.Resources).Type);
        state.ResourceDefinitions.Clear();
        Assert.Throws<ArgumentException>(() => ContentComposition.ValidateSnapshot(state));
    }
}
