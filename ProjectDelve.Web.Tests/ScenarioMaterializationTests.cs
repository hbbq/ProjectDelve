using System.Text.Json;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioMaterializationTests
{
    private static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 12, Height = 10 }, UnitTypeIds = [UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Grunt, "red", new(8, 8), UnitId: "enemy", Dormant: true)],
        Agency = [new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Objectives = [new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };

    [Fact]
    public void MaterializationRetainsNormalValidationAndOwnedState()
    {
        var definition = Definition();
        var json = ScenarioDefinitionJson.ToJson(definition);
        var visits = 0;
        var materialized = ScenarioDefinitionJson.Materialize(json, d => { visits++; Assert.Single(d.Units); });
        Assert.Equal(1, visits);
        Assert.Equal(JsonSerializer.Serialize(GameEngine.CreateGame(definition)), JsonSerializer.Serialize(materialized.State));
        Assert.Equal(ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(json)), ScenarioDefinitionJson.ToJson(materialized.Definition));
        materialized.State.Units.Clear();
        materialized.State.Physical.Board.Terrain.Add(new(new(0, 0), TerrainKind.SolidRock));
        Assert.Single(materialized.Definition.Units);
        Assert.Empty(materialized.Definition.Board.Cells);
        var other = ScenarioDefinitionTransport.Materialize(ScenarioDefinitionTransport.Encode(definition));
        Assert.Single(other.State.Units);
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(other.Definition));
    }

    [Fact]
    public void LimitsRunBeforeCreationAndInvalidPlacementIsStillRejected()
    {
        var bad = Definition();
        bad.Units.Add(bad.Units[0] with { UnitId = "overlap" });
        var json = ScenarioDefinitionJson.ToJson(bad);
        var before = Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(json));
        var after = Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.Materialize(json));
        Assert.Equal(before.Message, after.Message);
        Assert.Throws<InvalidOperationException>(() => ScenarioDefinitionTransport.Materialize(
            ScenarioDefinitionTransport.Encode(bad), _ => throw new InvalidOperationException("size rejected before creation")));
        Assert.Throws<FormatException>(() => ScenarioDefinitionTransport.Materialize("DELVE2:bad"));
    }

    [Fact]
    public void QuickPlayReuseMatchesIndependentPreparationAndDoesNotMutateItsInput()
    {
        var content = ContentBundle.Canonical();
        var materialized = ScenarioDefinitionJson.Materialize(ScenarioDefinitionJson.ToJson(Definition()), content: content);
        var raw = JsonSerializer.Serialize(materialized.State);
        var original = ScenarioDefinitionJson.ToJson(materialized.Definition);
        var expected = WorldQuickPlay.Prepare(materialized.Definition, content);
        var actual = WorldQuickPlay.PrepareValidated(materialized, content);
        Assert.Equal(ScenarioDefinitionJson.ToJson(expected), ScenarioDefinitionJson.ToJson(actual));
        Assert.Equal(raw, JsonSerializer.Serialize(materialized.State));
        Assert.Equal(original, ScenarioDefinitionJson.ToJson(materialized.Definition));
        Assert.NotEmpty(GameEngine.CreateGame(actual, content).Units);
        actual.Units.Clear();
        Assert.Single(materialized.Definition.Units);
    }

    [Fact]
    public void ImportExecutionUsesCurrentCanonicalContentRatherThanAdmissionState()
    {
        var (definition, content) = ImportFixture();
        var changed = content with { Units = [content.Units[0] with { Mov = 3 }] };
        var calls = 0;
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(definition, content),
            canonicalContent: content, canonicalProvider: () => ++calls == 1 ? content : changed);

        var imported = game.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(definition));

        Assert.Equal(2, calls);
        Assert.Equal(3, Assert.Single(imported.Result.State.Types).Mov);
        Assert.Equal(1, content.Units[0].Mov);
        Assert.Equal(JsonSerializer.Serialize(GameEngine.CreateGame(definition, changed)),
            JsonSerializer.Serialize(imported.Result.State));
    }

    [Fact]
    public void ImportExecutionRejectsRemovedCanonicalTypeAndRestoresPublishedState()
    {
        var (definition, content) = ImportFixture();
        var removed = content with { Units = [] };
        var calls = 0;
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(definition, content),
            canonicalContent: content, canonicalProvider: () => ++calls == 1 ? content : removed);
        var before = game.Snapshot();

        Assert.ThrowsAny<ArgumentException>(() => game.StartTransportScenario(0,
            ScenarioDefinitionTransport.Encode(definition)));

        Assert.Equal(2, calls);
        var after = game.Snapshot();
        Assert.Equal(before.SessionId, after.SessionId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(JsonSerializer.Serialize(before.Result.State), JsonSerializer.Serialize(after.Result.State));
        Assert.Equal(before.ControllerAssignments, after.ControllerAssignments);
    }

    [Theory]
    [InlineData("missing-group", "Human")]
    [InlineData("camp", "unknown-controller")]
    public void ReusedImportStillValidatesControllerAssignmentsBeforeChangingTheGame(string group, string controller)
    {
        var (definition, content) = ImportFixture();
        var calls = 0;
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(definition, content),
            canonicalContent: content, canonicalProvider: () => { calls++; return content; });
        var before = JsonSerializer.Serialize(game.Snapshot());
        var transport = ScenarioDefinitionTransport.Encode(definition);

        var error = Assert.Throws<PlaytestRequestException>(() => game.SetupImport(0, transport, null,
            [new("materialization-unit", "red", controller, group)]));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal(before, JsonSerializer.Serialize(game.Snapshot()));
        var imported = game.SetupImport(0, transport, null,
            [new("materialization-unit", "red", "Default Behavior", "camp")]);
        Assert.Equal(3, calls);
        Assert.Equal(new ControllerSelection("materialization-unit", "red", "Default Behavior", "camp"),
            Assert.Single(imported.ControllerAssignments));
    }

    private static (ScenarioDefinition Definition, ContentBundle Content) ImportFixture()
    {
        var content = new ContentBundle
        {
            Units = [new("materialization-unit", 1, 0, 0, 0, 1)],
            Terrain = [new("stone-floor", "Test floor", true, false) { LegacyKind = TerrainKind.StoneFloor }]
        };
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 6, Height = 6 }, UnitTypeIds = ["materialization-unit"],
            Units = [new("materialization-unit", "red", new(2, 2), UnitId: "fixture-unit", GroupId: "camp")],
            Agency = [new("materialization-unit", "red", ControllerKind.Human, "camp")],
            Objectives = [new("red", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        return (definition, content);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }
}
