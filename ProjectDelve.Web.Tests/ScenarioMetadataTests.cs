using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioMetadataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-scenario-metadata-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void WholeResponsePreservesLegacyMetadataValuesAndScenarioOrder()
    {
        var (definition, content) = Fixture();
        var entries = new[] { new ScenarioDescription("second", "Second", "Second description"),
            new ScenarioDescription("first", "First", "First description") };
        var state = GameEngine.CreateGame(definition, content);
        var response = new GameResponse(17, new(state, [], null), false, "second")
        {
            SessionId = "metadata-test-session", VisualSeed = "metadata-test-seed",
            Scenarios = entries.Select(entry => ScenarioDescription.Create(entry, definition, content)).ToArray()
        };
        var expected = JsonSerializer.SerializeToNode(response, Json)!;
        expected["scenarios"] = JsonSerializer.SerializeToNode(entries.Select(entry => Legacy(entry, definition, state)).ToArray(), Json);

        Assert.Equal(expected.ToJsonString(), JsonSerializer.Serialize(response, Json));
        Assert.Equal(new[] { "second", "first" }, response.Scenarios.Select(entry => entry.Id));
        Assert.Equal(new[] { "declared-z", "declared-a", "equipment-child", "item-child", "grandchild" },
            response.Scenarios[0].UnitTypeNames.Keys);
        Assert.Equal("Scenario local name", response.Scenarios[0].UnitTypeNames["declared-z"]);
        Assert.Equal("grandchild", response.Scenarios[0].UnitTypeNames["grandchild"]);
        Assert.Equal(new[] { "west", "east", "reserve" }, response.Scenarios[0].SideIds);
        Assert.Equal("Default Behavior", response.Scenarios[0].Controllers["west"]);
        Assert.Equal("Human", response.Scenarios[0].Controllers["east"]);
        Assert.Equal(new[] { new ActivationToken("declared-z", "west"), new ActivationToken("declared-a", "east") },
            response.Scenarios[0].InitiallyPresentTokens);
    }

    [Fact]
    public void CapturedCollectionsRejectMutationAndMetadataCaptureDoesNotInstantiateTheBoard()
    {
        var (definition, content) = Fixture();
        // Metadata resolution needs typed content, not a playable physical setup.
        definition = definition with { Board = new() { Width = 0, Height = 0 } };
        var captured = ScenarioDescription.Create(new("fixture", "Fixture", "Metadata only"), definition, content);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition, content));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)captured.SideHues).Add("new-side", 10));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)captured.Controllers).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)captured.UnitTypeNames).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ControllerSelection>)captured.ControllerAssignments).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ActivationToken>)captured.InitiallyPresentTokens).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)captured.SideIds).Clear());
    }

    [Fact]
    public async Task CapturedMetadataSurvivesSourceMutationRepeatedSerializationAndRoundtrip()
    {
        var (definition, content) = Fixture();
        var hues = new Dictionary<string, int> { ["west"] = 123 };
        var entry = new ScenarioDescription("fixture", "Fixture", "Captured metadata") { SideHues = hues };
        var captured = ScenarioDescription.Create(entry, definition, content);
        var response = new GameResponse(1, new(GameEngine.CreateGame(definition, content), [], null), ScenarioId: entry.Id)
        { Scenarios = [captured] };
        var before = JsonSerializer.Serialize(response, Json);

        // Invalidated authoring inputs would fail any attempt to rebuild metadata during serialization.
        definition.UnitTypeIds.Clear();
        definition.Units.Clear();
        definition.Agency.Clear();
        definition.UnitTypes!.Clear();
        hues["west"] = 321;
        hues["new-side"] = 10;

        Assert.Equal(123, captured.SideHues["west"]);
        Assert.False(captured.SideHues.ContainsKey("new-side"));
        Assert.All(await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => JsonSerializer.Serialize(response, Json)))), json => Assert.Equal(before, json));
        var roundtrip = JsonSerializer.Deserialize<GameResponse>(before, Json)!;
        Assert.Equal(before, JsonSerializer.Serialize(roundtrip, Json));
        Assert.Equal(before, JsonSerializer.Serialize(response with { Revision = 1 }, Json));
    }

    [Fact]
    public async Task SameScenarioIdentityDoesNotShareMetadataAcrossContentSnapshots()
    {
        var (definition, content) = Fixture();
        definition = definition with { UnitTypes = [] };
        var entry = new ScenarioDescription("shared-id", "Fixture", "Independent content contexts");
        var replacement = content with
        {
            Units = [.. content.Units.Select(type => type.Id == "declared-z" ? type with { DisplayName = "Replacement name" } : type)]
        };
        var first = ScenarioDescription.Create(entry, definition, content);
        var next = ScenarioDescription.Create(entry, definition, replacement);

        Assert.Equal("Canonical Z", first.UnitTypeNames["declared-z"]);
        Assert.Equal("Replacement name", next.UnitTypeNames["declared-z"]);
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
            ScenarioDescription.Create(entry, definition, index % 2 == 0 ? content : replacement))));
        for (var index = 0; index < snapshots.Length; index++)
            Assert.Equal(index % 2 == 0 ? "Canonical Z" : "Replacement name", snapshots[index].UnitTypeNames["declared-z"]);
        Assert.Equal("Canonical Z", first.UnitTypeNames["declared-z"]);
    }

    [Fact]
    public void NewExplicitWorkspaceSnapshotsFollowEditsAndPromotionWhileExistingSnapshotsAndCompiledCatalogRemainFixed()
    {
        var canonical = new CanonicalContent(Path.Combine(root, "canonical"));
        var workspace = new ContentWorkspace(Path.Combine(root, "workspace"), () => canonical.Bundle);
        var compiledCatalog = PlaytestScenarios.Catalog;
        var compiledJson = JsonSerializer.Serialize(compiledCatalog, Json);
        var type = Type("metadata-workspace-unit", "First workspace name");
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 2, Height = 2 }, UnitTypeIds = [type.Id],
            Units = [new(type.Id, "test-side", new(0, 0))],
            Agency = [new(type.Id, "test-side", ControllerKind.Human)], Loot = LootSettings.Empty
        };
        var entry = new ScenarioDescription("workspace-fixture", "Workspace fixture", "Explicit provider snapshot");
        var saved = workspace.Save(new() { Units = [type] }, workspace.Snapshot().Token);
        var initial = ScenarioDescription.Create(entry, definition, workspace.Resolve().Definitions);
        var initialJson = JsonSerializer.Serialize(initial, Json);
        saved = workspace.Save(new() { Units = [type with { DisplayName = "Edited workspace name" }] }, saved.Token);
        var edited = ScenarioDescription.Create(entry, definition, workspace.Resolve().Definitions);
        Assert.Equal("Edited workspace name", edited.UnitTypeNames[type.Id]);

        var promotion = new WorkspacePromotionService(workspace, canonical);
        promotion.Publish(new(promotion.Preview(new(saved.Token, [new(ContentFamily.Unit, type.Id)])), true));
        var promoted = ScenarioDescription.Create(entry, definition, new CanonicalContent(Path.Combine(root, "canonical")).Bundle);
        Assert.Equal("Edited workspace name", promoted.UnitTypeNames[type.Id]);
        Assert.Equal(initialJson, JsonSerializer.Serialize(initial, Json));
        Assert.Same(compiledCatalog, PlaytestScenarios.Catalog);
        Assert.Equal(compiledJson, JsonSerializer.Serialize(PlaytestScenarios.Catalog, Json));
    }

    private static UnitType Type(string id, string name) => UnitType.Define(id, name,
        UnitAuthoring.Stats(2, 0, 0, 1, 1), UnitAuthoring.CantAttack());

    private static (ScenarioDefinition Definition, ContentBundle Content) Fixture()
    {
        var z = Type("declared-z", "Canonical Z");
        var a = Type("declared-a", "Canonical A");
        var equipmentChild = Type("equipment-child", "Equipment child") with
        {
            LocalAbilities = [new("summon-grandchild", "Summon Grandchild", AbilityTiming.Action,
                new AdjacentUnitSummonMechanic("grandchild", Posture.Lying))]
        };
        var content = new ContentBundle
        {
            Units = [z, a, equipmentChild, Type("item-child", "Item child"), Type("grandchild", "Grandchild") with { DisplayName = null }, Type("unused", "Unused")],
            Equipment = [new("fixture-equipment", "Fixture equipment")
            {
                LocalAbilities = [new("summon", "Summon", AbilityTiming.Action,
                    new AdjacentUnitSummonMechanic("equipment-child", Posture.Upright))]
            }],
            Items = [new("fixture-item", "Fixture item")
            {
                LocalAbilities = [new("summon", "Summon", AbilityTiming.Action,
                    new AdjacentUnitSummonMechanic("item-child", Posture.Upright))]
            }],
            Terrain = TerrainCatalog.All, Resources = ResourceTypes.All
        };
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 4, Height = 3 }, UnitTypeIds = [z.Id, a.Id],
            UnitTypes = [z with { DisplayName = "Scenario local name" }],
            Units = [new(z.Id, "west", new(0, 0)), new(z.Id, "west", new(0, 1)), new(a.Id, "east", new(3, 2))],
            Agency = [new(z.Id, "west", ControllerKind.Human), new(a.Id, "west", ControllerKind.Automated),
                new(a.Id, "east", ControllerKind.Human), new(z.Id, "reserve", ControllerKind.Automated)],
            Loot = LootSettings.Empty
        };
        return (definition, content);
    }

    private static object Legacy(ScenarioDescription entry, ScenarioDefinition definition, GameState state) => new
    {
        entry.Id, entry.Name, entry.Description, SideHues = new Dictionary<string, int>(),
        Controllers = definition.Agency.GroupBy(a => a.SideId).ToDictionary(g => g.Key,
            g => g.All(a => a.Controller == ControllerKind.Human) ? "Human" : "Default Behavior"),
        ControllerAssignments = definition.Agency.Select(a => new ControllerSelection(a.UnitTypeId, a.SideId,
            a.Controller == ControllerKind.Human ? "Human" : "Default Behavior")).ToArray(),
        UnitTypeNames = state.Types.ToDictionary(type => type.Id, type => type.DisplayName ?? type.Id),
        InitiallyPresentTokens = definition.Units.Select(unit => new ActivationToken(unit.UnitTypeId, unit.SideId)).Distinct().ToArray(),
        SideIds = definition.Agency.Select(a => a.SideId).Distinct().ToArray()
    };

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
