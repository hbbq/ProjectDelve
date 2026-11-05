using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class LargeMapAuthoringTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-large-authoring-" + Guid.NewGuid().ToString("N"));
    private const string Recipe = "WLD1-120x100-X-17-Y-13-F35-W12-R15-KBalanced-HMixed-S0123456789ABCDEF";

    [Theory]
    [InlineData(100)]
    [InlineData(120)]
    public async Task WorldTerrainSurvivesDesignerEditingExportLibraryAndDurableReopen(int height)
    {
        string id, expected;
        await using (var host = await Host.Start(directory))
        {
            var before = await host.Client.GetStringAsync("/api/game");
            using var generated = await host.Client.PostAsJsonAsync("/api/world-generation", new { code = Recipe.Replace("120x100", $"120x{height}") });
            generated.EnsureSuccessStatusCode();
            var preview = await generated.Content.ReadFromJsonAsync<JsonElement>();
            var transport = preview.GetProperty("transport").GetString()!;
            using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
            imported.EnsureSuccessStatusCode();
            var definition = ScenarioDefinitionJson.FromJson(await imported.Content.ReadAsStringAsync());
            Assert.Equal(120, definition.Board.Width);
            Assert.Equal(height, definition.Board.Height);
            // Edit a known cell while retaining every generated terrain placement.
            var editedCell = new Cell(119, height - 1);
            var neighbor = new Cell(118, height - 1);
            definition = definition with { Board = definition.Board with
            {
                Cells = definition.Board.Cells.Where(c => c.Position != editedCell && c.Position != neighbor)
                    .Append(new CellDefinition(editedCell, TerrainKind.Grass) { TerrainTypeId = "dirt" })
                    .Append(new CellDefinition(neighbor, TerrainKind.Grass) { TerrainTypeId = "dirt" }).ToList(),
                Edges = [new(neighbor, EdgeDirection.Right, EdgeKind.Wall)]
            } };
            expected = ScenarioDefinitionJson.ToJson(definition);
            using var draft = JsonDocument.Parse(expected);
            using var validated = await host.Client.PostAsJsonAsync("/api/designer/validate", draft.RootElement);
            Assert.True((await validated.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid, await validated.Content.ReadAsStringAsync());
            using var inspected = await host.Client.PostAsJsonAsync("/api/content/inspect", draft.RootElement);
            inspected.EnsureSuccessStatusCode();
            using var exported = await host.Client.PostAsJsonAsync("/api/designer/export", draft.RootElement);
            exported.EnsureSuccessStatusCode();
            var exportedTransport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
            Assert.Equal(expected, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(exportedTransport)));
            using var saved = await host.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("Large terrain", null, draft.RootElement));
            saved.EnsureSuccessStatusCode();
            id = (await saved.Content.ReadFromJsonAsync<LibraryScenario>())!.Id;
            Assert.Equal(before, await host.Client.GetStringAsync("/api/game"));
            using var played = await host.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, transport = exportedTransport });
            played.EnsureSuccessStatusCode();
        }
        await using var reopened = await Host.Start(directory);
        var stored = await reopened.Client.GetFromJsonAsync<LibraryScenario>($"/api/library/scenarios/{id}");
        Assert.Equal(expected, ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(stored!.Definition.GetRawText())));
        var portable = await reopened.Client.GetFromJsonAsync<JsonElement>($"/api/library/scenarios/{id}/transport");
        Assert.Equal(expected, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(portable.GetProperty("transport").GetString()!)));
    }

    [Fact]
    public async Task DenseMaximumBoardWithAuthoredEdgesFitsLibraryAndTransportBudgets()
    {
        await using var host = await Host.Start(directory);
        var cells = new List<CellDefinition>();
        var edges = new List<EdgeDefinition>();
        for (var y = 0; y < 120; y++)
        for (var x = 0; x < 120; x++)
        {
            cells.Add(new(new(x, y), TerrainKind.Grass) { TerrainTypeId = "dirt" });
            if (x < 119) edges.Add(new(new(x, y), EdgeDirection.Right, EdgeKind.Wall));

        }
        var definition = Scenario.Define(Scenario.Map(120, 120), []) with
        { Board = new() { Width = 120, Height = 120, Cells = cells, Edges = edges } };
        var json = ScenarioDefinitionJson.ToJson(definition);
        Assert.True(Encoding.UTF8.GetByteCount(json) > 2_000_000);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= AuthoringLimits.MaxScenarioJsonBytes);
        var transport = ScenarioDefinitionTransport.Encode(definition);
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        using var document = JsonDocument.Parse(json);
        using var saved = await host.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("Dense edges", null, document.RootElement));
        saved.EnsureSuccessStatusCode();
        var stored = (await saved.Content.ReadFromJsonAsync<LibraryScenario>())!;
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(stored.Definition.GetRawText())));
    }

    [Fact]
    public async Task LibraryReportsPayloadBudgetIndependentlyOfBoardDimensions()
    {
        await using var host = await Host.Start(directory);
        var json = ScenarioDefinitionJson.ToJson(Scenario.Define(Scenario.Map(1, 1), []));
        json = json[..^1] + ",\"padding\":\"" + new string('a', AuthoringLimits.MaxScenarioJsonBytes) + "\"}";
        using var draft = JsonDocument.Parse(json);
        using var rejected = await host.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("Payload budget", null, draft.RootElement));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("too large", await rejected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IndependentHostsApplyLowerPolicyAcrossAllAuthoringBoundaries()
    {
        await using var lower = await Host.Start(Path.Combine(directory, "lower"), 20);
        await using var normal = await Host.Start(Path.Combine(directory, "normal"));
        foreach (var route in new[] { "/api/designer/catalog", "/api/world-generation/catalog" })
        {
            Assert.Equal(20, (await lower.Client.GetFromJsonAsync<JsonElement>(route)).GetProperty("maxBoardSize").GetInt32());
            Assert.Equal(120, (await normal.Client.GetFromJsonAsync<JsonElement>(route)).GetProperty("maxBoardSize").GetInt32());
        }
        using var rejectedWorld = await lower.Client.PostAsJsonAsync("/api/world-generation", new { code = Recipe });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedWorld.StatusCode);
        Assert.Contains("20", await rejectedWorld.Content.ReadAsStringAsync());
        var definition = Scenario.Define(Scenario.Map(21, 20), []);
        using var draft = JsonDocument.Parse(ScenarioDefinitionJson.ToJson(definition));
        using var invalid = await lower.Client.PostAsJsonAsync("/api/designer/validate", draft.RootElement);
        Assert.False((await invalid.Content.ReadFromJsonAsync<DesignerValidation>())!.Valid);
        foreach (var route in new[] { "/api/designer/export", "/api/content/inspect" })
        {
            using var rejected = await lower.Client.PostAsJsonAsync(route, draft.RootElement);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var accepted = await normal.Client.PostAsJsonAsync(route, draft.RootElement);
            accepted.EnsureSuccessStatusCode();
        }
        var transport = ScenarioDefinitionTransport.Encode(definition);
        foreach (var route in new[] { "/api/designer/import", "/api/game/scenario/import" })
        {
            using var rejected = await lower.Client.PostAsJsonAsync(route, new { transport, expectedRevision = 0 });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using var rejectedSave = await lower.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("Oversized", null, draft.RootElement));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedSave.StatusCode);
        using var acceptedSave = await normal.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("Allowed", null, draft.RootElement));
        acceptedSave.EnsureSuccessStatusCode();
        Assert.Equal(0, (await lower.Client.GetFromJsonAsync<JsonElement>("/api/game")).GetProperty("revision").GetInt64());
    }

    [Theory]
    [InlineData("DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QFun-S000000000000002A")]
    public async Task LowerHostPolicyRejectsDungeonPreviewAndDirectPlayBeforeGeneration(string code)
    {
        await using var lower = await Host.Start(directory, 20);
        var before = await lower.Client.GetStringAsync("/api/game");
        using var preview = await lower.Client.PostAsJsonAsync("/api/generation", new { code });
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Contains("20", await preview.Content.ReadAsStringAsync());
        using var play = await lower.Client.PostAsJsonAsync("/api/game/scenario/import", new { expectedRevision = 0, generationCode = code });
        Assert.Equal(HttpStatusCode.BadRequest, play.StatusCode);
        Assert.Contains("20", await play.Content.ReadAsStringAsync());
        Assert.Equal(before, await lower.Client.GetStringAsync("/api/game"));
    }

    [Fact]
    public async Task LoweringHostPolicyPreservesLibraryAccessAndDeletionOfExistingLargeRecords()
    {
        LibraryScenario large, small;
        using var largeDraft = JsonDocument.Parse(ScenarioDefinitionJson.ToJson(Scenario.Define(Scenario.Map(120, 100), [])));
        using var smallDraft = JsonDocument.Parse(ScenarioDefinitionJson.ToJson(Scenario.Define(Scenario.Map(5, 5), [])));
        await using (var original = await Host.Start(directory))
        {
            using var savedLarge = await original.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("Existing large", null, largeDraft.RootElement));
            savedLarge.EnsureSuccessStatusCode();
            large = (await savedLarge.Content.ReadFromJsonAsync<LibraryScenario>())!;
            using var savedSmall = await original.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("Existing small", null, smallDraft.RootElement));
            savedSmall.EnsureSuccessStatusCode();
            small = (await savedSmall.Content.ReadFromJsonAsync<LibraryScenario>())!;
        }
        await using var lower = await Host.Start(directory, 20);
        var catalog = (await lower.Client.GetFromJsonAsync<LibraryCatalog>("/api/library/"))!;
        Assert.Contains(catalog.Scenarios, entry => entry.Id == large.Id);
        Assert.Contains(catalog.Scenarios, entry => entry.Id == small.Id);
        var openedSmall = (await lower.Client.GetFromJsonAsync<LibraryScenario>($"/api/library/scenarios/{small.Id}"))!;
        Assert.Equal(small.Definition.GetRawText(), openedSmall.Definition.GetRawText());
        using var smallExport = await lower.Client.GetAsync($"/api/library/scenarios/{small.Id}/transport");
        smallExport.EnsureSuccessStatusCode();
        // Stored data remains inspectable, but materialization/export and edits use current policy.
        var openedLarge = (await lower.Client.GetFromJsonAsync<LibraryScenario>($"/api/library/scenarios/{large.Id}"))!;
        Assert.Equal(120, openedLarge.Definition.GetProperty("board").GetProperty("width").GetInt32());
        using var largeExport = await lower.Client.GetAsync($"/api/library/scenarios/{large.Id}/transport");
        Assert.Equal(HttpStatusCode.BadRequest, largeExport.StatusCode);
        using var rejectedSave = await lower.Client.PostAsJsonAsync("/api/library/scenarios", new SaveLibraryScenario("New large", null, largeDraft.RootElement));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedSave.StatusCode);
        using var rejectedEdit = await lower.Client.PutAsJsonAsync($"/api/library/scenarios/{large.Id}",
            new SaveLibraryScenario("Edited large", null, largeDraft.RootElement, openedLarge.Revision, openedLarge.ChangeToken));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedEdit.StatusCode);
        using var deleted = await lower.Client.DeleteAsync($"/api/library/scenarios/{large.Id}?revision={openedLarge.Revision}&changeToken={Uri.EscapeDataString(openedLarge.ChangeToken!)}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var retainedSmall = await lower.Client.GetAsync($"/api/library/scenarios/{small.Id}");
        retainedSmall.EnsureSuccessStatusCode();
        var remaining = (await lower.Client.GetFromJsonAsync<LibraryCatalog>("/api/library/"))!;
        Assert.DoesNotContain(remaining.Scenarios, entry => entry.Id == large.Id);
        Assert.Contains(remaining.Scenarios, entry => entry.Id == small.Id);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("invalid")]
    public void InvalidPolicyFailsAtHostConstruction(string max)
    {
        Assert.ThrowsAny<Exception>(() => PlaytestHost.Build(["--contentRoot", Host.Root, "--Authoring:MaxBoardSize", max]));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        public HttpClient Client => client;
        public static async Task<Host> Start(string directory, int max = 120)
        {
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", Root,
                "--Authoring:MaxBoardSize", max.ToString(),
                "--CanonicalContent:Directory", Path.Combine(directory, "canonical"),
                "--ContentWorkspace:Path", Path.Combine(directory, "workspace"),
                "--ContentLibrary:Path", Path.Combine(directory, "content-library"),
                "--Library:BundlePath", Path.Combine(directory, "bundle"), "--Library:Path", Path.Combine(directory, "library")]);
            await app.StartAsync();
            return new(app, new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) });
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
    }
}
