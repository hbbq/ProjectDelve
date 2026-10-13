using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GambitHttpTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly JsonSerializerOptions Gambit = GambitJson.CreateOptions();
    [Fact]
    public async Task IsolatedHttpContractAuthenticatesAdmitsReconcilesAndRejectsResetResponse()
    {
        var old = Environment.GetEnvironmentVariable("PROJECTDELVE_GAMBIT_TOKEN");
        Environment.SetEnvironmentVariable("PROJECTDELVE_GAMBIT_TOKEN","isolated-gambit-test");
        await using var host=PlaytestHost.Build(["--urls","http://127.0.0.1:0","--contentRoot",Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../ProjectDelve.Web"))],new First());
        try
        {
            await host.StartAsync();
            using var http=new HttpClient { BaseAddress=new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            Assert.Equal(HttpStatusCode.Unauthorized,(await http.GetAsync("/api/controllers/gambit/status")).StatusCode);
            var initial=(await http.GetFromJsonAsync<GameResponse>("/api/game",Json))!;
            var sides=initial.Result.State.Objectives.Select(o=>o.SideId).ToArray();
            var bindings=new Dictionary<string,string> { [sides[0]]="Gambit",[sides[1]]="Human" };
            var setup=(await (await http.PostAsJsonAsync("/api/game/restart",new { expectedRevision=initial.Revision,controllers=bindings })).Content.ReadFromJsonAsync<GameResponse>(Json))!;
            Assert.False(setup.AutoChooseSingleRelevantChoice);
            var round=await http.PostAsJsonAsync("/api/game/round",new { expectedRevision=setup.Revision }); round.EnsureSuccessStatusCode();
            http.DefaultRequestHeaders.Authorization=new("Bearer","isolated-gambit-test");
            var request=(await http.GetFromJsonAsync<GambitRequest>("/api/controllers/gambit/request",Gambit))!;
            Assert.Equal(1,request.SchemaVersion); Assert.Equal("gambit",request.Binding.BindingId);
            Assert.True(request.Request.Candidates.Count+(request.Request.AllowsNone ? 1:0)>1);
            Assert.NotNull(request.Objective); Assert.NotEmpty(request.Events);
            var serialized=await http.GetStringAsync("/api/controllers/gambit/request");
            Assert.DoesNotContain("commentar",serialized,StringComparison.OrdinalIgnoreCase);
            using (var document = JsonDocument.Parse(serialized))
            {
                var root = document.RootElement;
                var observation = root.GetProperty("observation");
                Assert.Equal(JsonValueKind.Array, observation.GetProperty("units")[0].GetProperty("position").ValueKind);
                Assert.Equal(request.Request.Candidates.Count,
                    root.GetProperty("request").GetProperty("candidates").GetArrayLength());
            }
            Assert.True((await http.PostAsJsonAsync("/api/controllers/gambit/admit",request.Binding)).IsSuccessStatusCode);
            var selection=new GambitSubmission("http-stable",request.Binding,request.Request.Candidates.First().Key,"gambit-private-marker");
            var receipt=(await (await http.PostAsJsonAsync("/api/controllers/gambit/selection",selection,Json)).Content.ReadFromJsonAsync<GambitReceipt>(Json))!;
            Assert.Equal("queued",receipt.Status);
            for(var n=0;n<200;n++)
            {
                receipt=(await http.GetFromJsonAsync<GambitReceipt>("/api/controllers/gambit/receipts/http-stable",Json))!;
                if(receipt.Status!="queued") break; await Task.Delay(10);
            }
            Assert.Equal("applied",receipt.Status);
            var duplicate=(await (await http.PostAsJsonAsync("/api/controllers/gambit/selection",selection,Json)).Content.ReadFromJsonAsync<GambitReceipt>(Json))!;
            Assert.Equal("applied",duplicate.Status);
            var game=(await http.GetFromJsonAsync<GameResponse>("/api/game",Json))!;
            // Replay delivers deliberate commentary through the ordinary browser stream.
            // Fresh baseline deliberately has no historical commentary. Resume from baseline
            // sequence zero to replay the committed decision commentary and gameplay events.
            using var browser = new HttpClient { BaseAddress = http.BaseAddress };
            browser.DefaultRequestHeaders.Add("Last-Event-ID", $"{game.SessionId}:0");
            using var resumed = await browser.GetAsync("/api/game/updates", HttpCompletionOption.ResponseHeadersRead);
            using var reader = new StreamReader(await resumed.Content.ReadAsStreamAsync());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string? line;
            do { line = await reader.ReadLineAsync(deadline.Token); }
            while (line is not null && !line.Contains("gambit-private-marker"));
            Assert.NotNull(line);
            Assert.Contains("gambit-private-marker", line);
            Assert.DoesNotContain("gambit-private-marker", await http.GetStringAsync("/api/controllers/gambit/request"));
            var reset=await http.PostAsJsonAsync("/api/game/restart",new { expectedRevision=game.Revision }); reset.EnsureSuccessStatusCode();
            var stale=(await (await http.PostAsJsonAsync("/api/controllers/gambit/selection",selection with { SubmissionId="http-late" },Json)).Content.ReadFromJsonAsync<GambitReceipt>(Json))!;
            Assert.Equal("rejected",stale.Status);
            Assert.Equal("applied",(await http.GetFromJsonAsync<GambitReceipt>("/api/controllers/gambit/receipts/http-stable",Json))!.Status);
            Assert.Equal("unknown",(await http.GetFromJsonAsync<GambitReceipt>("/api/controllers/gambit/receipts/missing",Json))!.Status);
            Assert.Equal(HttpStatusCode.NotFound,(await http.PostAsJsonAsync("/api/game/owner",new { token="unused" })).StatusCode);
        }
        finally
        {
            await host.StopAsync();
            Environment.SetEnvironmentVariable("PROJECTDELVE_GAMBIT_TOKEN",old);
        }
    }
    private sealed class First : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag)=>bag[0];
        public AttackFace RollAttackDie()=>AttackFace.Miss;
        public DefenceFace RollDefenceDie()=>DefenceFace.Miss;
        public int RollD6()=>6;
    }
}
