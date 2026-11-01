using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class CanonicalPackageIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "package-bearer") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    [Fact]
    public async Task CheckedInJsonContentIsCanonicalEditableAsReferencesAndPlayableWithoutRosterChanges()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        var directory = Path.Combine(root, "Content", "Canonical");
        var example = ContentPackageCatalog.ReadDirectory(directory).Single(p => p.Package.Id == "json-provider-example").Package;
        var equipment = Assert.Single(example.Definitions.Equipment);
        var card = Assert.Single(example.Definitions.LootCards);
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], new Dice());
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var canonical = app.Services.GetRequiredService<CanonicalContent>().Bundle;
        var game = app.Services.GetRequiredService<PlaytestGame>();

        var browser = (await client.GetFromJsonAsync<ContentBrowserResponse>("/api/content/catalog", Json))!;
        Assert.Equal("canonical", browser.Entries.Single(e => e.Family == "equipment" && e.Id == equipment.Id).Origin);
        Assert.Equal("canonical", browser.Entries.Single(e => e.Family == "lootCards" && e.Id == card.Id).Origin);
        var metadata = await client.GetFromJsonAsync<JsonElement>("/api/designer/catalog");
        Assert.Contains(metadata.GetProperty("equipmentTypes").EnumerateArray(), e => e.GetProperty("id").GetString() == equipment.Id);
        Assert.Contains(metadata.GetProperty("lootCardDefinitions").EnumerateArray(), e => e.GetProperty("id").GetString() == card.Id);
        using var validate = await client.PostAsJsonAsync("/api/content/validate", new ContentEditorRequest("lootCards",
            JsonSerializer.SerializeToElement(new LootCardDefinition("test-reference", "Reference") { EquipmentTypeId = equipment.Id }, Json)), Json);
        Assert.True((await validate.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!.Valid);

        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 4, Height = 3 }, UnitTypeIds = ["package-bearer", "package-friend"],
            UnitTypes = [new("package-bearer", 1, 0, 0, 0, 1, Actions: UnitAction.None) { CanHaveEquipment = true }, new("package-friend", 0, 0, 0, 0, 1)],
            Units = [new("package-bearer", "blue", new(1, 1), UnitId: "bearer"), new("package-friend", "blue", new(2, 1), Posture.Lying, UnitId: "friend")],
            Agency = [new("package-bearer", "blue", ControllerKind.Human), new("package-friend", "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)],
            Loot = new() { Composition = [new() { LootCardId = card.Id }] }, LootMarkers = [new("gift", new(1, 1))]
        };
        using var exported = await client.PostAsync("/api/designer/export", new StringContent(ScenarioDefinitionJson.ToJson(scenario), Encoding.UTF8, "application/json"));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(scenario), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport, null, canonical)));
        async Task<GameResponse> Command(string name, object payload)
        {
            using var response = await client.PostAsJsonAsync("/api/game/" + name, payload, Json);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        }
        var current = await Command("scenario/import", new { expectedRevision = 0, transport });
        current = await Command("preferences", new { expectedRevision = current.Revision, autoChooseSingleRelevantChoice = false });
        current = await Command("round", new { expectedRevision = current.Revision });
        var stay = current.Result.NextInput!.Candidates.Single(c => c.Key == "stay");
        current = await Command("decision", new { expectedRevision = current.Revision, candidateKey = stay.Key });
        Assert.Equal(equipment.Id, Assert.Single(current.Result.State.Units.Single(u => u.Id == "bearer").Inventory).EquipmentTypeId);
        var action = current.Result.NextInput!.Candidates.Single(c => c.Ability?.SourceTypeId == equipment.Id);
        current = await Command("decision", new { expectedRevision = current.Revision, candidateKey = action.Key });
        var authoritative = game.Snapshot().Result.State;
        Assert.True(authoritative.IsUpright("friend"));
        Assert.Equal(1, Assert.Single(authoritative.Units.Single(u => u.Id == "bearer").Inventory).AbilityUseCounts[equipment.LocalAbilities[0].Key]);
        var saved = JsonSerializer.Serialize(authoritative);

        var changedDirectory = Path.Combine(Path.GetTempPath(), "delve-provider-changed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(changedDirectory);
        try
        {
            var changed = example with { Definitions = example.Definitions with { Equipment = [equipment with
                { Name = "Changed after save", LocalAbilities = [equipment.LocalAbilities[0] with { Usage = new(9) }] }] } };
            File.WriteAllText(Path.Combine(changedDirectory, "example.json"), ContentPackageJson.ToJson(changed));
            var replacement = ContentPackageCatalog.LoadCanonical(changedDirectory).Definitions;
            Assert.Equal("Changed after save", GameEngine.CreateGame(scenario, replacement).Equipment(equipment.Id).Name);
            Assert.Equal(equipment.Name, canonical.Equipment.Single(e => e.Id == equipment.Id).Name);
            var restored = JsonSerializer.Deserialize<GameState>(saved)!;
            Assert.Equal(equipment.Name, restored.Equipment(equipment.Id).Name);
            Assert.Equal(2, restored.Equipment(equipment.Id).LocalAbilities[0].Usage.MaxUses);
            Assert.Equal(9, replacement.Equipment.Single(e => e.Id == equipment.Id).LocalAbilities[0].Usage.MaxUses);
            ContentComposition.ValidateSnapshot(restored);
            var expected = GameEngine.RefreshChoices(authoritative, new Dice(), false);
            var replay = GameEngine.RefreshChoices(restored, new Dice(), false);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(replay));
        }
        finally { Directory.Delete(changedDirectory, true); }
    }

    [Fact]
    public void InvalidConfiguredProviderFailsBeforeTheHostCanStart()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        var directory = Path.Combine(Path.GetTempPath(), "delve-provider-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "duplicate.json"), ContentPackageJson.ToJson(new() { Id = "duplicate",
                Definitions = new() { Equipment = [EquipmentCatalog.All[0]] } }));
            var error = Assert.Throws<ContentCompositionException>(() => PlaytestHost.Build(["--contentRoot", root, "--CanonicalContent:Directory", directory]));
            Assert.Equal("conflictingDefinition", Assert.Single(error.Diagnostics).Code);
        }
        finally { Directory.Delete(directory, true); }
    }
}
