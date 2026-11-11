using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GeneratedMachineImportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedBeaconAndCivilizationOffControlCrossDesignerAndQuickPlayBoundaries(bool civilization)
    {
        await using var host = await Host.Start();
        var recipe = new WorldGenerationRecipe(new(Width: 40, Height: 30),
            new(ForestCoverage: 0, WaterCoverage: 0, RockCoverage: 0), 0x3039,
            Civilization: new(Enabled: civilization, Campsites: ContentDensity.None, Settlements: ContentDensity.None));
        using var generatedResponse = await host.Client.PostAsJsonAsync("/api/world-generation", new { code = recipe.ToString() });
        await Success(generatedResponse);
        var generated = await generatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var definition = ScenarioDefinitionJson.FromJson(generated.GetProperty("definition").GetRawText());
        if (civilization)
        {
            Assert.NotEmpty(definition.Machines!);
            Assert.All(definition.Machines!, machine =>
            {
                Assert.Equal(MachineTypes.Beacon.Id, machine.Type.Id);
                AssertBeacon(machine);
            });
        }
        else Assert.Empty(definition.Machines ?? []);
        var transport = generated.GetProperty("transport").GetString()!;
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        var before = await host.Client.GetStringAsync("/api/game");
        var exported = await DesignerRoundTrip(host.Client, definition, transport);
        using var quickPlay = await host.PostDraft("/api/world-generation/quick-play", ScenarioDefinitionTransport.Decode(exported));
        await Success(quickPlay);
        var preparedTransport = (await quickPlay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        var prepared = ScenarioDefinitionTransport.Decode(preparedTransport);
        AssertMachines(definition.Machines ?? [], prepared.Machines ?? []);
        var materialized = GameEngine.CreateGame(prepared);
        AssertMachines(definition.Machines ?? [], materialized.Physical.Board.Machines);
        Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
        using var imported = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new
        {
            expectedRevision = JsonDocument.Parse(before).RootElement.GetProperty("revision").GetInt64(),
            transport = preparedTransport
        });
        await Success(imported);
        var state = host.App.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State;
        AssertMachines(definition.Machines ?? [], state.Physical.Board.Machines);
        Assert.Equal(materialized.Units.Count, state.Units.Count);
    }

    [Fact]
    public async Task StorageFreeRevealAndResourceMachinesRetainLimitsCountersAndStorageThroughJsonDelveAndPlaytest()
    {
        await using var host = await Host.Start();
        // Test-owned content exercises both optional storage and both independent use limits.
        var limitedReveal = MachineTypes.Beacon with
        {
            Id = "fixture-limited-reveal", Name = "Limited Reveal",
            Operation = new([]) { Reveal = new(7), MaxUsesTotal = 4, MaxUsesPerSide = 2 }
        };
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 8, Height = 4 },
            UnitTypeIds = ["fixture-operator"],
            UnitTypes = [new("fixture-operator", 1, 0, 0, 1, 1)],
            Units = [new("fixture-operator", "blue", new(0, 0)), new("fixture-operator", "red", new(7, 3))],
            Agency = [new("fixture-operator", "blue", ControllerKind.Human), new("fixture-operator", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty,
            Machines =
            [
                new("beacon", new(1, 1), MachineTypes.Beacon)
                { UseCounts = ImmutableDictionary<string, int>.Empty.Add("blue", 1) },
                new("limited", new(3, 1), limitedReveal)
                { UseCounts = ImmutableDictionary<string, int>.Empty.Add("blue", 2).Add("red", 1) },
                new("press", new(5, 1), MachineTypes.DiamondPress)
                { OwnerSide = "blue", StoredResources = [new(ResourceTypes.Coal, 3)] },
                new("slot", new(6, 1), MachineTypes.SlotMachine)
                { StoredResources = [new(ResourceTypes.Fruit, 2)] }
            ]
        };
        var json = ScenarioDefinitionJson.ToJson(definition);
        var fromJson = ScenarioDefinitionJson.FromJson(json);
        AssertMachines(definition.Machines, fromJson.Machines!);
        var exported = await DesignerRoundTrip(host.Client, fromJson, ScenarioDefinitionTransport.Encode(fromJson));
        var fromDelve = ScenarioDefinitionTransport.Decode(exported);
        AssertMachines(definition.Machines, fromDelve.Machines!);
        var state = GameEngine.CreateGame(fromDelve);
        AssertCountersAndControls(state.Physical.Board.Machines);
        using var imported = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport = exported });
        await Success(imported);
        var played = host.App.Services.GetRequiredService<PlaytestGame>().Snapshot().Result.State;
        AssertMachines(definition.Machines, played.Physical.Board.Machines);
        AssertCountersAndControls(played.Physical.Board.Machines);
    }

    private static void AssertCountersAndControls(IReadOnlyList<Machine> machines)
    {
        var beacon = machines.Single(m => m.Id == "beacon");
        AssertBeacon(beacon);
        Assert.False(beacon.HasUsesFor("blue"));
        Assert.True(beacon.HasUsesFor("red"));
        var limited = machines.Single(m => m.Id == "limited");
        Assert.Null(limited.Type.Storage);
        Assert.Equal(7, limited.Type.Operation.Reveal!.Distance);
        Assert.Equal(4, limited.Type.Operation.MaxUsesTotal);
        Assert.Equal(2, limited.Type.Operation.MaxUsesPerSide);
        Assert.Equal(1, limited.RemainingUsesTotal);
        Assert.Equal(0, limited.RemainingUsesFor("blue"));
        Assert.Equal(1, limited.RemainingUsesFor("red"));
        Assert.False(limited.HasUsesFor("blue"));
        Assert.True(limited.HasUsesFor("red"));
        Assert.Equal(3, machines.Single(m => m.Id == "press").StoredQuantity("coal"));
        Assert.Equal(2, machines.Single(m => m.Id == "slot").StoredQuantity("fruit"));
    }

    private static void AssertBeacon(Machine machine)
    {
        Assert.Null(machine.Type.Storage);
        Assert.Empty(machine.StoredResources);
        Assert.Empty(machine.Type.Operation.Costs);
        Assert.Null(machine.Type.Operation.OutputType);
        Assert.Null(machine.Type.Operation.OutputTable);
        Assert.Equal(15, machine.Type.Operation.Reveal!.Distance);
        Assert.Null(machine.Type.Operation.MaxUsesTotal);
        Assert.Equal(1, machine.Type.Operation.MaxUsesPerSide);
    }

    private static void AssertMachines(IEnumerable<Machine> expected, IEnumerable<Machine> actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, ContentPackageJson.CreateOptions()),
            JsonSerializer.Serialize(actual, ContentPackageJson.CreateOptions()));

    private static async Task<string> DesignerRoundTrip(HttpClient client, ScenarioDefinition definition, string transport)
    {
        using var imported = await client.PostAsJsonAsync("/api/designer/import", new { transport });
        await Success(imported);
        var json = await imported.Content.ReadAsStringAsync();
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), json);
        using var validated = await client.PostAsync("/api/designer/validate", new StringContent(json, Encoding.UTF8, "application/json"));
        await Success(validated);
        Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid, await validated.Content.ReadAsStringAsync());
        using var exported = await client.PostAsync("/api/designer/export", new StringContent(json, Encoding.UTF8, "application/json"));
        await Success(exported);
        var result = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.StartsWith("DELVE1:", result);
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(result)));
        return result;
    }

    private static async Task Success(HttpResponseMessage response) =>
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

    private sealed class Host(WebApplication app, HttpClient client, string directory) : IAsyncDisposable
    {
        public WebApplication App => app;
        public HttpClient Client => client;
        public Task<HttpResponseMessage> PostDraft(string path, ScenarioDefinition definition) =>
            client.PostAsync(path, new StringContent(ScenarioDefinitionJson.ToJson(definition), Encoding.UTF8, "application/json"));
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var directory = Path.Combine(Path.GetTempPath(), "delve-generated-machine-" + Guid.NewGuid().ToString("N"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root,
                "--CanonicalContent:Directory", Path.Combine(directory, "canonical"),
                "--ContentWorkspace:Path", Path.Combine(directory, "workspace"),
                "--ContentLibrary:Path", Path.Combine(directory, "content-library"), "--Library:Path", Path.Combine(directory, "library")]);
            await app.StartAsync();
            return new(app, new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) }, directory);
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
