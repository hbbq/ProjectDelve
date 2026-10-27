using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class MiniatureHostingTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ProductionModelsAreAvailableWithoutFlags(string environment)
    {
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", Path.Combine(Root, "ProjectDelve.Web"), "--environment", environment], null, GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        foreach (var unit in new[] { "zombie", "troll" })
        {
            using var response = await http.GetAsync($"/art/models/units/{unit}/v1/{unit}.glb");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("model/gltf-binary", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(File.ReadAllBytes(Path.Combine(Root, $"ProjectDelve.Web/wwwroot/art/models/units/{unit}/v1/{unit}.glb")), await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/assets/source/models/units/{unit}/original.glb")).StatusCode);
        }
    }

    [Fact]
    public async Task StaticMiddlewareServesGlbWithExplicitMimeAndRejectsUnknownFiles()
    {
        var directory = Path.Combine(Root, ".playwright-cli", "glb-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "wwwroot"));
        var source = Path.Combine(Root, "ProjectDelve.Web.Tests/fixtures/miniatures/static-figure.glb");
        File.Copy(source, Path.Combine(directory, "wwwroot/fixture.glb"));
        File.WriteAllText(Path.Combine(directory, "wwwroot/fixture.unknown-extension"), "not public");
        try
        {
            await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", directory], null,
                GameEngine.CreateGame(WebScenarioFixtures.Combat()));
            await app.StartAsync();
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using var response = await http.GetAsync("/fixture.glb");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("model/gltf-binary", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(File.ReadAllBytes(source), await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/fixture.unknown-extension")).StatusCode);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    public async Task FixtureRegistrationRequiresExplicitDevelopmentOptIn(string environment, bool option, bool enabled)
    {
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", Path.Combine(Root, "ProjectDelve.Web"),
            "--environment", environment, "--MiniatureQa", option.ToString()], null, GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Assert.Contains(enabled ? "true" : "false", await http.GetStringAsync("/api/presentation/miniatures-qa"));
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await http.GetAsync("/__qa/miniatures/static-figure.glb")).StatusCode);
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await http.GetAsync("/__qa/miniatures/registration.js")).StatusCode);
    }
}
