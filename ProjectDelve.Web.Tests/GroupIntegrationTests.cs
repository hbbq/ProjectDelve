using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GroupIntegrationTests
{
    private static ScenarioDefinition Definition() => new()
    {
        Board = new() { Width = 16, Height = 4 }, UnitTypeIds = [UnitTypeIds.Goblin, UnitTypeIds.Barbarian],
        Units = [new(UnitTypeIds.Barbarian, "blue", new(0, 0), UnitId: "hero"),
            new(UnitTypeIds.Goblin, "red", new(2, 0), UnitId: "guard", GroupId: "camp"),
            new(UnitTypeIds.Goblin, "red", new(14, 3), UnitId: "patrol", GroupId: "road")],
        Agency = [new(UnitTypeIds.Barbarian, "blue", ControllerKind.Human),
            new(UnitTypeIds.Goblin, "red", ControllerKind.Automated),
            new(UnitTypeIds.Goblin, "red", ControllerKind.Human, "camp")],
        Orders = [new(UnitTypeIds.Goblin, "red", OrderKind.HoldPosition)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)]
    };

    [Fact]
    public void CatalogAndHostExposeIndependentResolvedGroupControllers()
    {
        var definition = Definition();
        var description = ScenarioDescription.Create(new("fixture", "Fixture", ""), definition, ContentBundle.Canonical());
        Assert.Contains(description.ControllerAssignments, a => a.GroupId == "camp" && a.Controller == "Human");
        Assert.Contains(description.ControllerAssignments, a => a.GroupId == "road" && a.Controller == "Default Behavior");
        Assert.Equal(3, description.InitiallyPresentTokens.Count);
        using var game = new PlaytestGame(new Dice(), GameEngine.CreateGame(definition));
        var assignments = game.Snapshot().ControllerAssignments;
        Assert.Contains(assignments, a => a.GroupId == "camp" && a.Controller == "Human");
        Assert.Contains(assignments, a => a.GroupId == "road" && a.Controller == "Default Behavior");
    }

    [Fact]
    public void DelveTransportAndGambitKeepGroupIdentityAndAgency()
    {
        var definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Definition()));
        var state = GameEngine.CreateGame(definition);
        var camp = new ActivationToken(UnitTypeIds.Goblin, "red", "camp");
        var road = new ActivationToken(UnitTypeIds.Goblin, "red", "road");
        var observation = GambitProjection.Observation(state, "red", new Dictionary<ActivationToken, string>
            { [camp] = "Gambit", [road] = "Default Behavior" });
        Assert.Contains(observation.Units, u => u.Id == "guard" && u.GroupId == "camp" && u.Agency == GambitAgency.Direct);
        Assert.Contains(observation.Units, u => u.Id == "patrol" && u.GroupId == "road" && u.Agency == GambitAgency.Allied);
        Assert.Equal(OrderKind.HoldPosition, state.OrderFor(camp).Kind);
        state.AssignOrder(camp, OrderKind.Normal);
        Assert.Equal(OrderKind.HoldPosition, state.OrderFor(road).Kind);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(OrderKind.Normal, restored.OrderFor(camp).Kind);
        Assert.Equal(OrderKind.HoldPosition, restored.OrderFor(road).Kind);
    }

    [Fact]
    public void VisiblePeerDoesNotRevealHiddenGroupsControllerOrToken()
    {
        var state = GameEngine.CreateGame(Definition() with { FogOfWarSides = ["blue"] });
        var hidden = new ActivationToken(UnitTypeIds.Goblin, "red", "road");
        var observed = FogOfWar.Observe(state, "blue");
        Assert.Contains(observed.Units, u => u.Id == "guard");
        Assert.DoesNotContain(observed.Units, u => u.Id == "patrol");
        Assert.DoesNotContain(observed.Controllers, c => c.Token == hidden);
        Assert.Null(FogOfWar.ObserveEvent(state, "blue", new("TokenDrawn", TypeId: hidden.TypeId)
            { Token = hidden, SideId = hidden.SideId }));
    }

    [Fact]
    public void ObservedResolvingMembershipOmitsHiddenCapturedIds()
    {
        var definition = Definition() with { FogOfWarSides = ["blue"] };
        definition.Units[2] = definition.Units[2] with { GroupId = "camp" };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new CampFirst(), false);
        Assert.Contains("patrol", result.State.ResolvingTokenUnitIds);
        var observation = BrowserProjection.Observe(result, "blue");
        Assert.Contains("guard", observation.State.ResolvingTokenUnitIds);
        Assert.DoesNotContain("patrol", observation.State.ResolvingTokenUnitIds);
        Assert.DoesNotContain("\"patrol\"", JsonSerializer.Serialize(observation));
    }

    [Fact]
    public void VisibleDormantPeerDoesNotRevealHiddenAwakeGroupActivation()
    {
        var definition = Definition() with { FogOfWarSides = ["blue"] };
        definition.Units[1] = definition.Units[1] with { Dormant = true, WakeDistance = 0 };
        definition.Units[2] = definition.Units[2] with { GroupId = "camp" };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new CampFirst(), false);
        var observed = BrowserProjection.Observe(result, "blue");
        Assert.Contains(observed.State.Units, u => u.Id == "guard" && u.Dormant);
        Assert.Empty(observed.State.ResolvingTokenUnitIds);
        Assert.Null(observed.State.ActiveToken);
        Assert.DoesNotContain(observed.Events, e => e.Kind == "TokenDrawn");
    }

    [Fact]
    public async Task DesignerApiValidatesExportsAndImportsDormantAreaAndPatrolGroups()
    {
        var definition = Definition() with
        {
            Waypoints = [new("west", new(2, 0, 2, 2)), new("east", new(13, 2, 2, 2))],
            Routes = [new("road", ["west", "east"], RouteMode.PingPong)],
            Orders = [new(UnitTypeIds.Goblin, "red", OrderKind.GuardArea, GroupId: "camp", Area: new(1, 0, 4, 3)),
                new(UnitTypeIds.Goblin, "red", OrderKind.PatrolRoute, Route: new("road", 1, -1), GroupId: "road")]
        };
        definition.Units[1] = definition.Units[1] with { Dormant = true, WakeDistance = 0 };
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root]);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var document = JsonDocument.Parse(ScenarioDefinitionJson.ToJson(definition));
        using var validated = await client.PostAsJsonAsync("/api/designer/validate", document.RootElement);
        validated.EnsureSuccessStatusCode();
        using var validation = JsonDocument.Parse(await validated.Content.ReadAsStringAsync());
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
        using var exported = await client.PostAsJsonAsync("/api/designer/export", document.RootElement);
        exported.EnsureSuccessStatusCode();
        using var transport = JsonDocument.Parse(await exported.Content.ReadAsStringAsync());
        using var imported = await client.PostAsJsonAsync("/api/designer/import", new { transport = transport.RootElement.GetProperty("transport").GetString() });
        imported.EnsureSuccessStatusCode();
        var restored = ScenarioDefinitionJson.FromJson(await imported.Content.ReadAsStringAsync());
        Assert.Equal("camp", restored.Units[1].GroupId);
        Assert.True(restored.Units[1].Dormant);
        Assert.Equal(0, restored.Units[1].WakeDistance);
        var state = GameEngine.CreateGame(restored);
        Assert.Equal(new ObjectiveArea(1, 0, 4, 3), state.OrderFor(new(UnitTypeIds.Goblin, "red", "camp")).Area);
        Assert.Equal(new FollowRoute("road", 1, -1), state.OrderFor(new(UnitTypeIds.Goblin, "red", "road")).Route);
        Assert.Equal(OrderKind.PatrolRoute, state.OrderFor(new(UnitTypeIds.Goblin, "red", "road")).Kind);
    }

    private sealed class CampFirst : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.First(t => t.GroupId == "camp");
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 6;
    }
}
