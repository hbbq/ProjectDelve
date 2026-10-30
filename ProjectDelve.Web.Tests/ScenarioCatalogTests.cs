using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioCatalogTests
{
    public static TheoryData<string> Catalog => new(PlaytestScenarios.Catalog.Select(scenario => scenario.Id));

    [Theory, MemberData(nameof(Catalog))]
    public void AuthoredScenarioIsValidAndLoadable(string id)
    {
        _ = PlaytestScenarios.Create(id);
        var json = ScenarioDefinitionJson.ToJson(PlaytestScenarios.Definition(id));
        _ = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json));
        var export = ScenarioDefinitionTransport.Encode(PlaytestScenarios.Definition(id));
        _ = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(export));
    }

    [Fact]
    public void AuthoredAndShippedLibrariesAreValidAndLoadable()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../campaigns"));
        var shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web/Content/Library"));
        foreach (var directory in Directory.EnumerateDirectories(root).Append(shipped).Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, "library.json");
            if (!File.Exists(path)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var library = new ScenarioLibrary(
                Path.Combine(Path.GetTempPath(), "delve-catalog-unused-" + Guid.NewGuid().ToString("N")),
                directory, readOnly: true);
            _ = library.Catalog();
            var scenarioIds = document.RootElement.GetProperty("scenarios").EnumerateArray()
                .Select(scenario => scenario.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
            foreach (var id in scenarioIds)
            {
                var definition = ScenarioDefinitionJson.FromJson(library.Scenario(id).Definition.GetRawText());
                _ = GameEngine.CreateGame(definition);
                _ = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
                _ = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
            }
            foreach (var entry in document.RootElement.GetProperty("campaigns").EnumerateArray())
            {
                var campaign = library.Campaign(entry.GetProperty("id").GetString()!);
                foreach (var reference in campaign.ScenarioIds)
                    _ = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(library.Scenario(reference).Definition.GetRawText()));
            }
        }
    }

    [Fact]
    public async Task EveryCatalogEntryCanBeLoadedThroughTheWebHost()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root]);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        long revision = 0;
        foreach (var entry in PlaytestScenarios.Catalog)
        {
            using var response = await client.PostAsJsonAsync("/api/game/scenario", new { expectedRevision = revision, scenarioId = entry.Id });
            response.EnsureSuccessStatusCode();
            using var data = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            revision = data.RootElement.GetProperty("revision").GetInt64();
        }
    }
}
