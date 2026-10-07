using System.Net;
using System.Net.Http.Json;
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

public sealed class PlaytestApiTests
{
    // Discover fixture Units by content and initial placement, independent of generated IDs.
    private static readonly GameState InitialSetup = CourtyardFixture.Create();
    private static string InitialUnit(string typeId, Cell? anchor = null) => InitialSetup.Units.Single(u =>
        u.TypeId == typeId && (anchor is null || InitialSetup.Physical.Figures.Single(f => f.Id == u.Id).Position == anchor)).Id;
    private static readonly string Barbarian = InitialUnit(UnitTypeIds.Barbarian);
    private static readonly string Rogue = InitialUnit(UnitTypeIds.Rogue);
    private static readonly string Cleric = InitialUnit(UnitTypeIds.Cleric);
    private static readonly string Wizard = InitialUnit(UnitTypeIds.Wizard);
    private static readonly string Grunt1 = InitialUnit(UnitTypeIds.Grunt, new(7, 11));
    private static readonly string Grunt2 = InitialUnit(UnitTypeIds.Grunt, new(7, 8));
    private static readonly string Zombie1 = InitialUnit(UnitTypeIds.Zombie, new(2, 3));
    private static readonly string Zombie2 = InitialUnit(UnitTypeIds.Zombie, new(1, 5));
    private static readonly string Archer1 = InitialUnit(UnitTypeIds.SkeletonArcher, new(6, 7));
    private static readonly string Archer2 = InitialUnit(UnitTypeIds.SkeletonArcher, new(9, 3));
    private static readonly string Shaman = InitialUnit(UnitTypeIds.Shaman);
    private static readonly string Troll1 = InitialUnit(UnitTypeIds.Troll, new(11, 0));
    private static readonly string Troll2 = InitialUnit(UnitTypeIds.Troll, new(13, 0));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("basic-combat")]
    [InlineData("full-party-trolls")]
    public async Task ImportedScenarioUsesNormalCreationGameplayAndRestart(string id)
    {
        await using var catalogHost = await Host.Start(hit: true);
        await using var importHost = await Host.Start(hit: true);
        var definition = PlaytestScenarios.Definition(id);
        var catalog = await Start(catalogHost, "scenario", new { expectedRevision = 0, scenarioId = id });
        var imported = await Start(importHost, "scenario/import", new
        {
            expectedRevision = 0, transport = ScenarioDefinitionTransport.Encode(definition)
        });
        Assert.Equal("imported", imported.ScenarioId);
        Assert.Equal(1, imported.Revision);
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(GameEngine.CreateGame(definition), imported.Result.State);
        Assert.Empty(imported.Result.Events);
        Assert.Empty(imported.Result.ResolutionSteps);
        Assert.Null(imported.Result.NextInput);

        catalog = await Start(catalogHost, "preferences", new { expectedRevision = catalog.Revision, autoChooseSingleRelevantChoice = false });
        imported = await Start(importHost, "preferences", new { expectedRevision = imported.Revision, autoChooseSingleRelevantChoice = false });
        catalog = await Start(catalogHost, "round", new { expectedRevision = catalog.Revision });
        imported = await Start(importHost, "round", new { expectedRevision = imported.Revision });
        var sawDice = false;
        for (var decisions = 0; ; decisions++)
        {
            Assert.Equal(JsonSerializer.Serialize(catalog.Result, Json), JsonSerializer.Serialize(imported.Result, Json));
            Assert.Equal(JsonSerializer.Serialize(catalog.Presentation, Json), JsonSerializer.Serialize(imported.Presentation, Json));
            Assert.Equal(catalog.Revision, imported.Revision);
            if (imported.Result.NextInput is not { } pending) break;
            Assert.True(decisions < 128, "The imported and catalog games must finish a round.");
            Assert.Equal(ControllerKind.Human, imported.Result.State.ControllerFor(pending));
            sawDice |= pending.Kind == DecisionKind.RollDice;
            // Basic Combat walks into range and attacks to exercise explicit dice continuations.
            var key = id == "basic-combat" ? pending.Candidates.FirstOrDefault(c =>
                c.Key == "3,4" || c.Action == UnitAction.NormalAttack)?.Key : null;
            key ??= pending.Candidates.FirstOrDefault(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn or ActivationChoiceKind.RollDice)?.Key;
            catalog = await Start(catalogHost, "decision", new { expectedRevision = catalog.Revision, candidateKey = key });
            imported = await Start(importHost, "decision", new { expectedRevision = imported.Revision, candidateKey = key });
        }
        Assert.True(imported.Result.State.RoundComplete);
        if (id == "basic-combat") Assert.True(sawDice);
        var fresh = await Start(importHost, "restart", new { expectedRevision = imported.Revision });
        Assert.Equal("imported", fresh.ScenarioId);
        Assert.False(fresh.AutoChooseSingleRelevantChoice);
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(GameEngine.CreateGame(definition), fresh.Result.State);

        // Switching back to the catalog also replaces the retained restart definition.
        var selected = await Start(importHost, "scenario", new { expectedRevision = fresh.Revision, scenarioId = "archers" });
        var restarted = await Start(importHost, "restart", new { expectedRevision = selected.Revision });
        Assert.Equal("archers", restarted.ScenarioId);
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(PlaytestScenarios.Create("archers"), restarted.Result.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a scenario")]
    [InlineData("DELVE2:abc")]
    [InlineData("DELVE1:!")]
    [InlineData("DELVE1:YWJj")]
    [InlineData("invalid-definition")]
    public async Task RejectedImportReturnsClientErrorAndPreservesActiveGame(string? transport)
    {
        await using var host = await Host.Start();
        if (transport == "invalid-definition")
        {
            var definition = PlaytestScenarios.Definition("full-party-trolls");
            transport = ScenarioDefinitionTransport.Encode(definition with { Board = definition.Board with { Width = 0 } });
        }
        var initial = await Start(host, "scenario/import", new
        {
            expectedRevision = 0, transport = ScenarioDefinitionTransport.Encode(PlaytestScenarios.Definition("basic-combat"))
        });
        var active = await host.Round(initial.Revision);
        var before = JsonSerializer.Serialize(await host.Read(), Json);
        using var rejected = await host.Post("scenario/import", new { expectedRevision = active.Revision, transport });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("Could not load scenario. Paste a valid DELVE1 scenario string.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(before, JsonSerializer.Serialize(await host.Read(), Json));
        var continued = await host.Decide(active.Revision, "stay");
        Assert.True(continued.Revision > active.Revision);
        var restarted = await Start(host, "restart", new { expectedRevision = continued.Revision });
        ScenarioDefinitionJsonTests.AssertEquivalentRoundZeroStates(initial.Result.State, restarted.Result.State);
    }

    [Fact]
    public async Task StaleImportKeepsRevisionConflictSemantics()
    {
        await using var host = await Host.Start();
        var active = await host.Round(0);
        using var rejected = await host.Post("scenario/import", new
        {
            expectedRevision = 0, transport = ScenarioDefinitionTransport.Encode(PlaytestScenarios.Definition("full-party-trolls"))
        });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(active.Result.State, Json), JsonSerializer.Serialize((await host.Read()).Result.State, Json));
    }

    private static async Task<GameResponse> Start(Host host, string operation, object body)
    {
        using var response = await host.Post(operation, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
    }

    [Fact]
    public async Task HttpSuppliesDomainCardsAndCompleteDirectChoiceWithProgressivePresentation()
    {
        await using var host = await Host.Start(hit: true);
        using var initialJson = JsonDocument.Parse(await host.Client.GetStringAsync("/api/game"));
        var cards = initialJson.RootElement.GetProperty("presentation").GetProperty("cards");
        Assert.Equal("Cleric", cards.GetProperty(Cleric).GetProperty("displayName").GetString());
        var telekinesis = cards.GetProperty(Wizard).GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("content").GetProperty("id").GetString() == "telekinesis");
        Assert.Equal("Telekinesis", telekinesis.GetProperty("content").GetProperty("name").GetString());
        Assert.Equal("Action", telekinesis.GetProperty("content").GetProperty("category").GetString());
        Assert.Equal("Lay down an upright enemy within RNG and LOS.", telekinesis.GetProperty("content").GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, telekinesis.GetProperty("uses").ValueKind);
        Assert.Equal(JsonValueKind.Null, telekinesis.GetProperty("content").GetProperty("maxUses").ValueKind);
        var cardWave = cards.GetProperty(Cleric).GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("content").GetProperty("id").GetString() == "holy-wave");
        Assert.Equal("Lay down all adjacent upright enemies.\nThen lay down this Unit.", cardWave.GetProperty("content").GetProperty("description").GetString());
        Assert.Equal(2, cardWave.GetProperty("uses").GetProperty("remainingUses").GetInt32());
        using var preference = await host.Post("preferences", new { expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var result = await host.Round(changed.Revision);
        foreach (var id in new[] { Barbarian, Rogue })
        {
            Assert.Equal(id, result.Result.NextInput!.UnitId);
            result = await host.Decide(result.Revision, "stay");
            result = await host.Decide(result.Revision, "end-turn");
        }
        Assert.Equal(Cleric, result.Result.NextInput!.UnitId);
        result = await host.Decide(result.Revision, "6,9");
        var wave = Assert.Single(result.Result.NextInput!.Candidates, c => c.Action == UnitAction.HolyWave);
        Assert.Equal(new[] { Grunt2, Shaman }.OrderBy(id => id), wave.TargetIds.OrderBy(id => id));
        using var choicesJson = JsonDocument.Parse(await host.Client.GetStringAsync("/api/game"));
        var projectedWave = choicesJson.RootElement.GetProperty("presentation").GetProperty("decision").GetProperty("candidates")
            .EnumerateArray().Single(c => c.GetProperty("key").GetString() == wave.Key);
        Assert.Equal("Direct", projectedWave.GetProperty("interaction").GetProperty("kind").GetString());
        Assert.Equal(wave.TargetIds, projectedWave.GetProperty("affectedUnitIds").EnumerateArray().Select(t => t.GetString()));
        var resolved = await host.Decide(result.Revision, wave.Key);
        var changes = resolved.Result.Events.Where(e => e.Kind == "PostureChanged" && e.Posture == Posture.Lying).ToArray();
        Assert.Equal(wave.TargetIds.Append(Cleric), changes.Select(e => e.UnitId));
        Assert.DoesNotContain(resolved.Result.Events, e => e.AbilityName == "Holy Wave" && e.Attack is not null);
        Assert.Equal(Posture.Lying, resolved.Result.State.Physical.Figures.Single(f => f.Id == Cleric).Posture);
        using var resolvedJson = JsonDocument.Parse(await host.Client.GetStringAsync("/api/game"));
        var figureJson = resolvedJson.RootElement.GetProperty("result").GetProperty("state")
            .GetProperty("physical").GetProperty("figures").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == Cleric);
        Assert.Equal("Lying", figureJson.GetProperty("posture").GetString());
        Assert.Equal(resolved.Result.Events.Count, resolved.Presentation.Events.Count);
        Assert.Equal(resolved.Result.ResolutionSteps.Select(s => s.EventIndex), resolved.Presentation.ResolutionSteps.Select(s => s.EventIndex));
        foreach (var step in resolved.Result.ResolutionSteps)
            Assert.Equal(BrowserProjection.Cards(step.StateAfter)[Cleric].Entries,
                resolved.Presentation.ResolutionSteps.Single(s => s.EventIndex == step.EventIndex).Cards[Cleric].Entries);
        Assert.Equal(1, resolved.Presentation.Cards[Cleric].Entries.Single(e => e.Content.Id == "holy-wave").Uses!.RemainingUses);
    }

    [Fact]
    public async Task CardDataIncludesNamedAuraAndAuthoritativeEffectiveDefence()
    {
        await using var host = await Host.Start();
        using var json = JsonDocument.Parse(await host.Client.GetStringAsync("/api/game"));
        var state = json.RootElement.GetProperty("result").GetProperty("state");
        var types = state.GetProperty("types").EnumerateArray().ToArray();
        var barbarian = types.Single(t => t.GetProperty("id").GetString() == "barbarian-type");
        Assert.Equal(3, barbarian.GetProperty("mov").GetInt32());
        Assert.Equal(1, barbarian.GetProperty("rng").GetInt32());
        Assert.Equal(4, barbarian.GetProperty("atk").GetInt32());
        Assert.Equal(3, barbarian.GetProperty("def").GetInt32());
        Assert.Equal(5, barbarian.GetProperty("hp").GetInt32());
        Assert.Equal("Rage", barbarian.GetProperty("bonusActions")[0].GetProperty("name").GetString());
        Assert.Equal("Fury", barbarian.GetProperty("fury").GetProperty("name").GetString());
        var fury = barbarian.GetProperty("passives")[0];
        Assert.Equal("Fury", fury.GetProperty("name").GetString());
        Assert.Equal("ATK +1 while adjacent to 2 or more enemies", fury.GetProperty("displayText").GetString());
        Assert.Equal(4, state.GetProperty("effectiveAtk").GetProperty(Barbarian).GetInt32());
        var rogue = types.Single(t => t.GetProperty("id").GetString() == "rogue-type");
        Assert.Equal(new[] { "Dash", "Throwing Knife" }, rogue.GetProperty("bonusActions").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString()));
        var cleric = types.Single(t => t.GetProperty("id").GetString() == "cleric-type");
        var aura = cleric.GetProperty("adjacentFriendlyUnitsDefenceBonus");
        Assert.Equal("Aura", aura.GetProperty("name").GetString());
        Assert.Equal(1, aura.GetProperty("amount").GetInt32());
        Assert.Equal("Aura", cleric.GetProperty("passives")[0].GetProperty("name").GetString());
        Assert.Equal("Adjacent friendly Units get DEF +1", cleric.GetProperty("passives")[0].GetProperty("displayText").GetString());
        // Rogue starts adjacent to Cleric; base DEF remains unchanged in content.
        Assert.Equal(2, rogue.GetProperty("def").GetInt32());
        Assert.Equal(3, state.GetProperty("effectiveDef").GetProperty(Rogue).GetInt32());
        Assert.Equal(3, state.GetProperty("effectiveDef").GetProperty(Barbarian).GetInt32());
    }

    [Fact]
    public async Task ReadIsSideEffectFree_AndHostServesBrowserAssets()
    {
        await using var host = await Host.Start();
        var first = await host.Read();
        var second = await host.Read();
        Assert.Equal(0, first.Revision);
        Assert.Equal(0, second.Result.State.Round);
        Assert.Empty(second.Result.Events);
        Assert.Null(second.Result.NextInput);
        Assert.Equal(15, second.Result.State.Physical.Board.Width);
        Assert.Equal(15, second.Result.State.Physical.Board.Height);
        Assert.Equal(13, second.Result.State.Physical.Figures.Count);
        var heroes = second.Result.State.Units.Where(u => u.SideId == "blue").ToArray();
        Assert.Equal(new[] { Barbarian, Rogue, Cleric, Wizard }, heroes.Select(u => u.Id));
        Assert.Equal(4, heroes.Select(u => u.TypeId).Distinct().Count());
        Assert.All(second.Result.State.Types.Where(t => heroes.Any(u => u.TypeId == t.Id)),
            type => Assert.Equal(type.Id == "cleric-type" ? UnitAction.NormalAttack | UnitAction.Heal | UnitAction.HolyWave
                : type.Id == "wizard-type" ? UnitAction.NormalAttack | UnitAction.Fireball | UnitAction.Telekinesis
                : UnitAction.NormalAttack, type.Actions));
        Assert.Equal(new[] { 2, 2, 2, 1, 2 }, second.Result.State.Units.Where(u => u.SideId == "red")
            .GroupBy(u => u.TypeId).Select(group => group.Count()));
        Assert.Equal(4, second.Result.State.Physical.Board.Edges.Count(e => e.Kind == EdgeKind.ClosedDoor));
        Assert.Contains(second.Result.State.Physical.Board.Edges, e => e.Kind == EdgeKind.OpenDoor);
        var board = second.Result.State.Physical.Board;
        Assert.Contains(board.Edges, e => e.Kind == EdgeKind.WallWithWindow);
        Assert.Equal(Enum.GetValues<TerrainKind>().OrderBy(kind => kind),
            Enumerable.Range(0, board.Height).SelectMany(y => Enumerable.Range(0, board.Width)
                .Select(x => board.TerrainAt(new Cell(x, y)))).Distinct().OrderBy(kind => kind));
        Assert.All(second.Result.State.Physical.Figures, figure => Assert.True(board.TerrainAt(figure.Position).Passable()));
        Assert.Contains("visual playtest", await host.Client.GetStringAsync("/"));
        Assert.Contains("case \"Movement\"", await host.Client.GetStringAsync("/app.js"));
    }

    [Theory]
    [InlineData("barbarian-type", 3, 1, 4, 3, 5)]
    [InlineData("rogue-type", 4, 1, 3, 2, 4)]
    [InlineData("cleric-type", 3, 1, 3, 3, 4)]
    [InlineData("grunt-type", 3, 1, 3, 3, 1)]
    public async Task BasicContentHasSpecifiedStatsAndExplicitActions(string id, int mov, int rng, int atk, int def, int hp)
    {
        await using var host = await Host.Start();
        var type = (await host.Read()).Result.State.Types.Single(t => t.Id == id);
        Assert.Equal((mov, rng, atk, def, hp), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(id == "cleric-type" ? UnitAction.NormalAttack | UnitAction.Heal | UnitAction.HolyWave
            : UnitAction.NormalAttack, type.Actions);
        if (id == "cleric-type")
        {
            Assert.Equal(new Heal(2), type.Heal);
            Assert.Equal(new AbilityUses(2, 2),
                (await host.Read()).Result.State.Units.Single(u => u.TypeId == id).HealUses);
        }
        Assert.Equal(id == "grunt-type" ? UnitFreeAction.None : UnitFreeAction.OpenDoor, type.FreeActions);
        Assert.Equal(UnitBehavior.None, type.Behaviors);
        Assert.Null(type.TryOpenDoor);
        Assert.Null(type.MoveAfterAttack);
    }

    [Fact]
    public async Task HeroDoorChoiceIsSuppliedByEngine_AndOpeningBeforeMoveKeepsMoveAndAttackAvailable()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var moved = await host.Decide(started.Revision, "4,4");
        var afterMove = Assert.Single(moved.Result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        Assert.Equal(ActivationChoiceKind.FreeAction, afterMove.Kind);
        Assert.Null(afterMove.Action);
        Assert.Equal("open-door:3,4:4,4", afterMove.Key);
        // Decline it for this activation so the next activation can open before Move.
        var completed = await FinishRound(host, await host.Decide(moved.Revision, "end-turn"));
        var next = await host.Round(completed.Revision);
        var beforeMove = Assert.Single(next.Result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
        var opened = await host.Decide(next.Revision, beforeMove.Key);
        Assert.Equal(next.Revision + 1, opened.Revision);
        Assert.Equal("DoorOpened", Assert.Single(opened.Result.Events, e => e.Kind == "DoorOpened").Kind);
        Assert.Equal(Barbarian, opened.Result.NextInput!.UnitId);
        Assert.False(opened.Result.State.MoveDone);
        Assert.False(opened.Result.State.ActionDone);
        Assert.Empty(opened.Result.State.BonusActionsUsedThisActivation);
        Assert.Contains(opened.Result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Stay);
        Assert.DoesNotContain(opened.Result.NextInput.Candidates, c => c.Key == beforeMove.Key);
        Assert.Equal(EdgeKind.OpenDoor, opened.Result.State.Physical.Board.EdgeBetween(new(3, 4), new(4, 4)));
        var stayed = await host.Decide(opened.Revision, "stay");
        var attack = Assert.Single(stayed.Result.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        var attacked = await host.Decide(stayed.Revision, attack.Key);
        Assert.Contains(attacked.Result.Events, e => e.Kind == "AttackResolved" && e.UnitId == Barbarian);
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("selection.kind === \"Door\"", script);
        Assert.Contains("Open Door (Free Action)", moved.Presentation.Decision!.Candidates.Single(c => c.Key == afterMove.Key).Label);
    }

    [Fact]
    public async Task StayRunsMonstersInOrder_AndNextRoundPreservesPositions()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        Assert.Equal(1, started.Revision);
        Assert.Equal("barbarian-type", started.Result.NextInput!.TypeId);
        Assert.Equal(DecisionKind.Activation, started.Result.NextInput.Kind);
        var completed = await FinishRound(host, started);
        Assert.True(completed.Result.State.RoundComplete);
        Assert.Null(completed.Result.NextInput);
        var moves = completed.Result.Events.Where(e => e.Kind == "MovementCompleted").ToArray();
        Assert.Equal(new[] { Barbarian, Rogue, Cleric, Wizard, Grunt2, Grunt1, Zombie1, Zombie2, Archer2, Archer1, Shaman, Troll1, Troll2 }, moves.Select(e => e.UnitId));
        Assert.Equal(new[] { "barbarian-type", "rogue-type", "cleric-type", "wizard-type", "grunt-type", "zombie-type", "skeleton-archer-type", "shaman-type", "troll-type" },
            completed.Result.Events.Where(e => e.Kind == "TokenDrawn").Select(e => e.TypeId));
        Assert.Contains(moves, e => e.Path!.Count > 1);
        // Each Unit finishes before the next Unit of that Type moves.
        foreach (var typeId in new[] { "grunt-type", "zombie-type", "skeleton-archer-type", "troll-type" })
        {
            var ids = completed.Result.State.Units.Where(u => u.TypeId == typeId).Select(u => u.Id).ToArray();
            var unitEvents = completed.Result.Events.Where(e => ids.Contains(e.UnitId)).Select(e => e.UnitId).ToArray();
            foreach (var id in ids)
            {
                var indices = unitEvents.Select((value, index) => (value, index)).Where(x => x.value == id).Select(x => x.index).ToArray();
                if (indices.Length > 0) Assert.Equal(indices.Length, indices[^1] - indices[0] + 1);
            }
        }
        Assert.Equal("RoundCompleted", completed.Result.Events[^1].Kind);
        Assert.Empty((await host.Read()).Result.Events); // Refresh must not replay effects.
        var next = await host.Round(completed.Revision);
        Assert.Equal(2, next.Result.State.Round);
        Assert.Equal(completed.Result.State.Physical.Figures, next.Result.State.Physical.Figures);
    }

    [Fact]
    public async Task IllegalChoiceAndWrongLifecycleDoNotChangeState_AndOldRevisionCannotBeReused()
    {
        await using var host = await Host.Start();
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("decision", new { expectedRevision = 0, candidateKey = "4,6" })).StatusCode);
        var started = await host.Round(0);
        var before = JsonSerializer.Serialize(await host.Read(), Json);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("round", new { expectedRevision = started.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "99,99" })).StatusCode);
        // Missing a key is malformed, not an implicit choice to do nothing.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new { expectedRevision = started.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("round", new { })).StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(await host.Read(), Json));
        await host.Decide(started.Revision, "4,6");
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("decision", new { expectedRevision = started.Revision, candidateKey = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task RosterAndSealedCryptUseOnlyCurrentContent()
    {
        await using var host = await Host.Start();
        var state = (await host.Read()).Result.State;
        Assert.Equal(JsonSerializer.Serialize(new[] { UnitType.Barbarian(), UnitType.Rogue(), UnitType.Cleric(), UnitType.Wizard(), UnitType.Grunt(), UnitType.Zombie(),
            UnitType.SkeletonArcher(), UnitType.Goblin(), UnitType.Shaman(), UnitType.Troll() }, Json), JsonSerializer.Serialize(state.Types, Json));
        Assert.Contains(state.Units, u => u.TypeId == "shaman-type");
        Assert.DoesNotContain(state.Units, u => u.TypeId == "goblin-type");
        var rogue = state.Units.Single(u => u.Id == Rogue);
        Assert.Equal(new AbilityUses(2, 2), rogue.BonusActionUses["Dash"]);
        Assert.Equal(new AbilityUses(2, 2), rogue.BonusActionUses["Throwing Knife"]);
        var board = state.Physical.Board;
        var room = Enumerable.Range(1, 3).SelectMany(x => Enumerable.Range(2, 4).Select(y => new Cell(x, y))).ToHashSet();
        var zombies = state.Units.Where(u => u.TypeId == "zombie-type").ToArray();
        Assert.Equal(2, zombies.Length);
        Assert.All(zombies, u => Assert.Contains(state.Physical.Figures.Single(f => f.Id == u.Id).Position, room));
        var boundary = room.SelectMany(c => new[] { new Cell(c.X - 1, c.Y), new Cell(c.X + 1, c.Y),
                new Cell(c.X, c.Y - 1), new Cell(c.X, c.Y + 1) }
            .Where(n => !room.Contains(n)).Select(n => board.EdgeBetween(c, n))).ToArray();
        Assert.Equal(14, boundary.Length);
        Assert.Single(boundary, kind => kind == EdgeKind.ClosedDoor);
        Assert.All(boundary, kind => Assert.Contains(kind, new[] { EdgeKind.Wall, EdgeKind.ClosedDoor }));
        Assert.Equal(state.Physical.Figures.Count, state.Physical.Figures.Select(f => f.Position).Distinct().Count());
        Assert.All(state.Physical.Figures, f => {
            Assert.InRange(f.Position.X, 0, 14); Assert.InRange(f.Position.Y, 0, 14);
            Assert.True(board.TerrainAt(f.Position).Passable());
            var unit = state.Units.Single(u => u.Id == f.Id);
            Assert.Equal(unit.Id == Wizard ? 2 : state.Types.Single(t => t.Id == unit.TypeId).Hp, unit.CurrentHp);
        });
        var started = await host.Round(0);
        var moved = await host.Decide(started.Revision, "4,6");
        Assert.DoesNotContain(moved.Result.NextInput!.Candidates, c => c.FreeAction == UnitFreeAction.OpenDoor);
    }

    [Fact]
    public async Task CombinedMonsterResolutionTransportsProgressiveHpSnapshots()
    {
        await using var host = await Host.Start(hit: true);
        var result = await host.Read();
        Assert.Empty(result.Result.ResolutionSteps);
        var found = false;
        for (var round = 0; round < 10 && !found; round++)
        {
            result = await host.Round(result.Revision);
            while (!result.Result.State.RoundComplete)
            {
                var before = result.Result.State;
                result = await host.Decide(result.Revision, null);
                Assert.Equal(Enumerable.Range(0, result.Result.Events.Count),
                    result.Result.ResolutionSteps.Select(s => s.EventIndex));
                var attacks = result.Result.ResolutionSteps
                    .Where(s => result.Result.Events[s.EventIndex] is { Kind: "AttackResolved", Damage: > 0 })
                    .GroupBy(s => result.Result.Events[s.EventIndex].TargetId);
                foreach (var group in attacks.Where(g => g.Count() > 1))
                {
                    var hp = before.Units.Single(u => u.Id == group.Key).CurrentHp;
                    var values = new List<int>();
                    foreach (var step in group)
                    {
                        hp = Math.Max(0, hp - result.Result.Events[step.EventIndex].Damage);
                        var supplied = step.StateAfter.Units.Single(u => u.Id == group.Key).CurrentHp;
                        Assert.Equal(hp, supplied);
                        values.Add(supplied);
                    }
                    Assert.True(values[0] > values[^1]);
                    Assert.Equal(values[^1], result.Result.State.Units.Single(u => u.Id == group.Key).CurrentHp);
                    found = true;
                }
                if (found) break;
            }
        }
        Assert.True(found, "Expected several Monster attacks on the same Hero in one API response.");
        Assert.Empty((await host.Read()).Result.ResolutionSteps);
    }

    [Fact]
    public async Task MonsterAttacksAndDefeatAreReturnedInOrder_WithAuthoritativeHpAndFigureRemoval()
    {
        await using var host = await Host.Start(hit: true);
        var result = await host.Read();
        var events = new List<RulesEvent>();
        for (var round = 0; round < 10 && events.All(e => e.Kind != "UnitDefeated"); round++)
        {
            result = await FinishRound(host, await host.Round(result.Revision));
            events.AddRange(result.Result.Events);
        }
        var died = events.First(e => e.Kind == "UnitDefeated");
        Assert.Contains(died.UnitId, new[] { Barbarian, Rogue });
        var deathIndex = events.IndexOf(died);
        Assert.Equal("AttackResolved", events[deathIndex - 1].Kind);
        Assert.Equal(died.UnitId, events[deathIndex - 1].TargetId);
        Assert.True(events[deathIndex - 1].Damage > 0);
        Assert.Equal(0, result.Result.State.Units.Single(u => u.Id == died.UnitId).CurrentHp);
        Assert.DoesNotContain(result.Result.State.Physical.Figures, f => f.Id == died.UnitId);
        Assert.True(result.Result.State.RoundComplete);
        var next = await host.Round(result.Revision);
        var deadType = result.Result.State.Units.Single(u => u.Id == died.UnitId).TypeId;
        Assert.DoesNotContain(next.Result.State.Bag, token => token.TypeId == deadType);
        Assert.DoesNotContain(next.Result.Events, e => e.Kind == "TokenDrawn" && e.TypeId == deadType);
    }

    [Fact]
    public async Task PlayerAttackUsesSuppliedCandidate_AndReturnsDamageThenDefeat()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var stayedFirst = await host.Decide(started.Revision, null);
        var skipped = await host.Decide(stayedFirst.Revision, null);
        var completed = await FinishRound(host, skipped);
        host.Hits = true;
        var next = await host.Round(completed.Revision);
        // Move toward a supplied melee target rather than assuming a specific Monster's route.
        var destination = next.Result.NextInput!.Candidates.First(c => c.Destination is not null &&
            next.Result.State.Physical.Figures.Any(f => next.Result.State.Units.Single(u => u.Id == f.Id).TypeId == UnitTypeIds.Grunt &&
                Math.Abs(f.Position.X - c.Destination.X) <= 1 && Math.Abs(f.Position.Y - c.Destination.Y) <= 1));
        var moved = await host.Decide(next.Revision, destination.Key);
        var attack = moved.Result.NextInput!.Candidates.First(c => c.Action == UnitAction.NormalAttack &&
            moved.Result.State.Units.Any(u => u.Id == c.TargetId && u.TypeId == UnitTypeIds.Grunt));
        var result = await host.Decide(moved.Revision, attack.Key);
        Assert.Equal("AttackResolved", result.Result.Events.First(e => e.Kind == "AttackResolved").Kind);
        Assert.Equal(Barbarian, result.Result.Events.First(e => e.Kind == "AttackResolved").UnitId);
        Assert.Equal(4, result.Result.Events.First(e => e.Kind == "AttackResolved").Damage);
        Assert.Equal("UnitDefeated", result.Result.Events.First(e => e.Kind == "UnitDefeated").Kind);
        Assert.Equal(attack.TargetId, result.Result.Events.First(e => e.Kind == "UnitDefeated").UnitId);
        Assert.Equal(0, result.Result.State.Units.Single(u => u.Id == attack.TargetId).CurrentHp);
        Assert.DoesNotContain(result.Result.State.Physical.Figures, f => f.Id == attack.TargetId);
    }

    [Fact]
    public async Task MonsterFirstToken_AdvancesBeforeExposingPlayerDecision()
    {
        await using var host = await Host.Start(monstersFirst: true);
        var result = await host.Round(0);
        Assert.Equal("grunt-type", result.Result.Events.First(e => e.Kind == "TokenDrawn").TypeId);
        Assert.Equal("barbarian-type", result.Result.NextInput!.TypeId);
        Assert.Equal(new[] { Grunt2, Grunt1, Zombie1, Zombie2, Archer2, Archer1, Shaman, Troll1, Troll2 }, result.Result.Events
            .Where(e => e.Kind == "MovementCompleted").Select(e => e.UnitId));
        Assert.Equal("barbarian-type", result.Result.Events.Last(e => e.Kind == "TokenDrawn").Token!.TypeId);
        Assert.False(result.Result.State.RoundComplete);
    }

    [Fact]
    public async Task ConcurrentSubmissionsWithSameRevision_CommitOnlyOnce()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        var responses = await Task.WhenAll(
            host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "4,6" }),
            host.Post("decision", new { expectedRevision = started.Revision, candidateKey = "4,6" }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(started.Revision + 1, (await host.Read()).Revision);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task ZombieApproachesDoorAndReportsOutcome(int roll, bool succeeds)
    {
        await using var host = await Host.Start(doorRoll: roll);
        var result = await FinishRound(host, await host.Round(0));
        var move = Assert.Single(result.Result.Events, e => e.Kind == "MovementCompleted" && e.UnitId == Zombie1);
        Assert.Equal(new[] { new Cell(2, 3), new Cell(3, 3), new Cell(3, 4) }, move.Path);
        var attempt = Assert.Single(result.Result.Events, e => e.Kind == "DoorOpeningAttemptResolved" && e.UnitId == Zombie1);
        Assert.Equal(roll, attempt.DieRoll);
        Assert.Equal(2, attempt.SuccessCount);
        Assert.Equal(succeeds, attempt.Succeeded);
        Assert.Equal(succeeds ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor,
            result.Result.State.Physical.Board.EdgeBetween(new(3, 4), new(4, 4)));
        Assert.Equal(succeeds, result.Result.Events.Any(e => e.Kind == "DoorOpened" && e.UnitId == Zombie1));
        var next = await FinishRound(host, await host.Round(result.Revision));
        Assert.Equal(succeeds ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor,
            next.Result.State.Physical.Board.EdgeBetween(new(3, 4), new(4, 4)));
        foreach (var id in new[] { Zombie1, Zombie2 })
        {
            var position = next.Result.State.Physical.Figures.Single(f => f.Id == id).Position;
            Assert.Equal(succeeds, position.X >= 4);
        }
        if (!succeeds)
            Assert.Contains(next.Result.Events, e => e.Kind == "DoorOpeningAttemptResolved" && e.UnitId == Zombie1);
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("case \"DoorAttempt\"", script);
        var projectedAttempt = Assert.Single(result.Presentation.Events, e => e.Role == OutcomeRole.DoorAttempt && e.UnitId == Zombie1);
        Assert.Contains(succeeds ? "success" : "failed; door stays closed", projectedAttempt.Text);
    }

    [Fact]
    public async Task ArcherRetreatsFromHeroAndStillAttacksAutomatically()
    {
        await using var host = await Host.Start();
        var initial = await host.Read();
        Assert.Equal(UnitType.SkeletonArcher(),
            initial.Result.State.Types.Single(t => t.Id == "skeleton-archer-type"));
        var completed = await FinishRound(host, await host.Round(0));
        var move = Assert.Single(completed.Result.Events,
            e => e.Kind == "MovementCompleted" && e.UnitId == Archer1);
        Assert.Equal(new Cell(6, 7), move.Path![0]);
        Assert.True(move.Path.Count > 1);
        var attack = Assert.Single(completed.Result.Events,
            e => e.Kind == "AttackResolved" && e.UnitId == Archer1);
        Assert.Equal(Barbarian, attack.TargetId);
        var end = move.Path[^1];
        // The courtyard permits retreat to the full firing range.
        Assert.Equal(4, Math.Abs(end.X - 4) + Math.Abs(end.Y - 7));
    }

    [Fact]
    public async Task ShamanFleesSpawnsFirstGoblinAndNextRoundOnlyStandsItUp()
    {
        await using var host = await Host.Start();
        var initial = (await host.Read()).Result.State;
        Assert.Equal(UnitType.Shaman(), initial.Types.Single(t => t.Id == "shaman-type"));
        Assert.DoesNotContain(initial.Units, u => u.TypeId == "goblin-type");
        var started = await host.Round(0);
        Assert.DoesNotContain("goblin-type", started.Result.State.Bag.Select(t => t.TypeId));
        var completed = await FinishRound(host, started);
        var move = Assert.Single(completed.Result.Events, e => e.UnitId == Shaman && e.Kind == "MovementCompleted");
        Assert.Equal(new Cell(7, 10), move.Path![0]);
        Assert.InRange(move.Path.Count - 1, 1, 2);
        var createdIndex = completed.Result.Events.FindIndex(e => e.Kind == "UnitCreated");
        Assert.True(createdIndex >= 0);
        var created = completed.Result.ResolutionSteps.Single(s => s.EventIndex == createdIndex).StateAfter;
        var goblin = Assert.Single(created.Units, u => u.TypeId == "goblin-type");
        Assert.Equal(UnitType.Goblin().CreateUnit(goblin.Id, "red"), goblin);
        Assert.Equal(Posture.Lying, created.Physical.Figures.Single(f => f.Id == goblin.Id).Posture);
        Assert.DoesNotContain("goblin-type", created.Bag.Select(t => t.TypeId));
        Assert.DoesNotContain(completed.Result.Events, e => e.Kind == "TokenDrawn" && e.TypeId == "goblin-type");
        Assert.True(completed.Result.State.RoundComplete);
        var next = await host.Round(completed.Revision);
        Assert.Contains("goblin-type", next.Result.State.Bag.Select(t => t.TypeId));
        Assert.Equal(Posture.Lying, next.Result.State.Physical.Figures.Single(f => f.Id == goblin.Id).Posture);
        var second = await FinishRound(host, next);
        Assert.Equal(new RulesEvent("PostureChanged", goblin.Id, Posture: Posture.Upright),
            Assert.Single(second.Result.Events, e => e.UnitId == goblin.Id && e.Kind == "PostureChanged"));
        var third = await FinishRound(host, await host.Round(second.Revision));
        Assert.Contains(third.Result.Events, e => e.UnitId == goblin.Id && e.Kind == "MovementCompleted");
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("decision.prompt", script);
        Assert.Contains("event.text", script);
    }

    [Fact]
    public async Task RelevanceAutoChoicePreferenceIsBackendVisibleRevisionedAndOutsideGameState()
    {
        await using var host = await Host.Start();
        var started = await host.Round(0);
        Assert.True(started.AutoChooseSingleRelevantChoice);
        Assert.All(started.Result.NextInput!.Candidates, c =>
            Assert.Equal(c.Kind != ActivationChoiceKind.BonusAction, c.Relevant));
        using var snapshot = JsonDocument.Parse(await host.Client.GetStringAsync("/api/game"));
        Assert.All(snapshot.RootElement.GetProperty("result").GetProperty("nextInput")
            .GetProperty("candidates").EnumerateArray(), c =>
                Assert.Equal(c.GetProperty("kind").GetString() != "BonusAction", c.GetProperty("relevant").GetBoolean()));
        var stateBefore = JsonSerializer.Serialize(started.Result.State, Json);
        using var response = await host.Post("preferences", new {
            expectedRevision = started.Revision, autoChooseSingleRelevantChoice = false });
        response.EnsureSuccessStatusCode();
        var changed = (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        Assert.False(changed.AutoChooseSingleRelevantChoice);
        Assert.Equal(started.Revision + 1, changed.Revision);
        Assert.Equal(stateBefore, JsonSerializer.Serialize(changed.Result.State, Json));
        Assert.False((await host.Read()).AutoChooseSingleRelevantChoice);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Post("preferences", new {
            expectedRevision = started.Revision, autoChooseSingleRelevantChoice = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("preferences", new {
            expectedRevision = changed.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new {
            expectedRevision = changed.Revision, candidateKey = (string?)null })).StatusCode);
        var moved = await host.Decide(changed.Revision, "4,6");
        Assert.False(moved.AutoChooseSingleRelevantChoice);
        Assert.Contains(moved.Result.Events, e => e.Kind == "MovementCompleted");
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.Contains("autoChooseSingleRelevantChoice", script);
        Assert.Contains("candidate.relevant === false", script);
        Assert.DoesNotContain("state.phase", script);
        var page = await host.Client.GetStringAsync("/");
        Assert.Contains("Filter irrelevant choices (display only)", page);
        Assert.Contains("Auto-choose single relevant choice", page);
    }

    [Fact]
    public async Task DisabledRelevanceAutoChoice_PreservesOrdinarySoleLegalChoiceResolution()
    {
        await using var host = await Host.Start();
        using var preference = await host.Post("preferences", new {
            expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var started = await host.Round(changed.Revision);
        Assert.False(started.AutoChooseSingleRelevantChoice);
        Assert.Equal(DecisionKind.Activation, started.Result.NextInput!.Kind);
        Assert.Equal(Barbarian, started.Result.NextInput.UnitId);
        Assert.True(started.Result.NextInput.Candidates.Count > 1);
        Assert.All(started.Result.NextInput.Candidates, c =>
            Assert.Equal(c.Kind != ActivationChoiceKind.BonusAction, c.Relevant));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new {
            expectedRevision = started.Revision, candidateKey = "forged" })).StatusCode);
        var raging = await host.Decide(started.Revision,
            started.Result.NextInput.Candidates.Single(c => c.Kind == ActivationChoiceKind.BonusAction).Key);
        var stayed = await host.Decide(raging.Revision, "stay");
        // Sole End Turn and the next sole Unit selection still resolve automatically.
        Assert.Equal(DecisionKind.Activation, stayed.Result.NextInput!.Kind);
        Assert.Equal(Rogue, stayed.Result.NextInput.UnitId);
        Assert.False(stayed.AutoChooseSingleRelevantChoice);
    }

    [Fact]
    public async Task RageIsSuppliedWithUsesEffectiveAtkAndAuthoritativeRelevance()
    {
        await using var host = await Host.Start(hit: true);
        var initial = await host.Read();
        Assert.Equal(new AbilityUses(2, 2), initial.Result.State.Units.Single(u => u.Id == Barbarian).BonusActionUses["Rage"]);
        using var preference = await host.Post("preferences", new {
            expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var started = await host.Round(changed.Revision);
        var rage = Assert.Single(started.Result.NextInput!.Candidates, c => c.BonusAction is not null);
        Assert.False(rage.Relevant);
        Assert.Equal("Rage", rage.BonusAction!.Name);
        var moved = await host.Decide(started.Revision, "5,7");
        rage = Assert.Single(moved.Result.NextInput!.Candidates, c => c.BonusAction is not null);
        Assert.True(rage.Relevant);
        var raging = await host.Decide(moved.Revision, rage.Key);
        Assert.Equal(new AbilityUses(2, 1), raging.Result.State.Units.Single(u => u.Id == Barbarian).BonusActionUses["Rage"]);
        Assert.Equal("Rage", Assert.Single(raging.Result.State.BonusActionsUsedThisActivation));
        Assert.Equal(6, raging.Result.State.EffectiveAtk[Barbarian]);
        Assert.Equal(4, raging.Result.State.Types.Single(t => t.Id == "barbarian-type").Atk);
        Assert.Equal(new ModifierThisTurn(Stat.Atk, 2), Assert.Single(raging.Result.State.ModifiersThisTurn));
        Assert.Equal(6, raging.Result.ResolutionSteps.Last().StateAfter.EffectiveAtk[Barbarian]);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new {
            expectedRevision = raging.Revision, candidateKey = rage.Key })).StatusCode);
        var attacked = await host.Decide(raging.Revision, $"attack:{Archer1}");
        Assert.Equal(6, Assert.Single(attacked.Result.Events, e => e.Kind == "AttackResolved").Hits);
        Assert.Empty(attacked.Result.State.ModifiersThisTurn);
        Assert.Equal(4, attacked.Result.State.EffectiveAtk[Barbarian]);
        Assert.Equal(new AbilityUses(2, 1), attacked.Result.State.Units.Single(u => u.Id == Barbarian).BonusActionUses["Rage"]);
    }

    [Fact]
    public async Task LegalIrrelevantRageCanBeSubmittedOverHttpAfterAction()
    {
        await using var host = await Host.Start();
        using var preference = await host.Post("preferences", new {
            expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var moved = await host.Decide((await host.Round(changed.Revision)).Revision, "5,7");
        var attacked = await host.Decide(moved.Revision, $"attack:{Archer1}");
        var rage = Assert.Single(attacked.Result.NextInput!.Candidates, c => c.BonusAction is not null);
        Assert.True(attacked.Result.State.ActionDone);
        Assert.False(rage.Relevant);
        var used = await host.Decide(attacked.Revision, rage.Key);
        Assert.Contains(used.Result.Events, e => e.Kind == "AbilityUsed" && e.AbilityName == "Rage");
        Assert.Equal(new AbilityUses(2, 1), used.Result.State.Units.Single(u => u.Id == Barbarian).BonusActionUses["Rage"]);
        Assert.Empty(used.Result.State.ModifiersThisTurn);
        Assert.Equal(Rogue, used.Result.NextInput!.UnitId);
    }

    [Fact]
    public async Task WizardAndFocusUseGenericCardsChoicesCountersAndEffectiveStatsOverHttp()
    {
        await using var host = await Host.Start(hit: true);
        var initial = await host.Read();
        var wizard = initial.Result.State.Units.Single(u => u.Id == Wizard);
        var type = initial.Result.State.Types.Single(t => t.Id == wizard.TypeId);
        Assert.Equal((2, 4, 3, 2, 4), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        var position = initial.Result.State.Physical.Figures.Single(f => f.Id == wizard.Id).Position;
        Assert.True(initial.Result.State.Physical.Board.TerrainAt(position).Passable());
        Assert.Single(initial.Result.State.Physical.Figures, f => f.Position == position);
        var card = initial.Presentation.Cards[Wizard];
        Assert.Equal("Wizard", card.DisplayName);
        Assert.Equal(type.CardEntries(), card.Entries.Select(e => e.Content).ToArray());
        var focusCard = Assert.Single(card.Entries, e => e.Content.Name == "Focus");
        Assert.Equal("Bonus Action", focusCard.Content.Category);
        Assert.Equal("+1 ATK this turn", focusCard.Content.Description);
        Assert.Equal("2/game", focusCard.Content.UseLimitText);
        Assert.Equal(new AbilityUses(2, 2), focusCard.Uses);

        using var preference = await host.Post("preferences", new { expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var result = await host.Round(changed.Revision);
        foreach (var id in new[] { Barbarian, Rogue, Cleric })
        {
            Assert.Equal(id, result.Result.NextInput!.UnitId);
            result = await host.Decide(result.Revision, "stay");
            if (result.Result.NextInput?.UnitId == id)
                result = await host.Decide(result.Revision, "end-turn");
        }
        Assert.Equal(Wizard, result.Result.NextInput!.UnitId);
        result = await host.Decide(result.Revision, "4,8");
        var focus = Assert.Single(result.Result.NextInput!.Candidates, c => c.BonusAction?.Name == "Focus");
        Assert.True(focus.Relevant);
        var projected = Assert.Single(result.Presentation.Decision!.Candidates, c => c.Key == focus.Key);
        Assert.Equal(focusCard.Content.Id, projected.EntryId);
        Assert.Equal("Focus (Bonus Action)", projected.Label);
        Assert.Equal(InteractionKind.Direct, projected.Interaction.Kind);
        Assert.Equal(focus.Relevant, projected.Relevant);
        var focused = await host.Decide(result.Revision, projected.Key);
        Assert.Equal(4, focused.Result.State.EffectiveAtk[Wizard]);
        Assert.False(focused.Result.State.ActionDone);
        Assert.Equal(new AbilityUses(2, 1), focused.Presentation.Cards[Wizard].Entries.Single(e => e.Content.Name == "Focus").Uses);
        Assert.Equal(OutcomeRole.Notice, focused.Presentation.Events.Last().Role);
        Assert.Equal($"{Wizard} used Focus", focused.Presentation.Events.Last().Text);
        Assert.Equal(new AbilityUses(2, 1), focused.Presentation.ResolutionSteps.Last().Cards[Wizard].Entries.Single(e => e.Content.Name == "Focus").Uses);
        Assert.DoesNotContain(focused.Presentation.Decision!.Candidates, c => c.EntryId == focusCard.Content.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post("decision", new { expectedRevision = focused.Revision, candidateKey = focus.Key })).StatusCode);
        var attack = focused.Result.NextInput!.Candidates.First(c => c.Action == UnitAction.NormalAttack);
        var attacked = await host.Decide(focused.Revision, attack.Key);
        Assert.Equal(4, attacked.Result.Events.Single(e => e.Kind == "AttackResolved" && e.UnitId == Wizard).Hits);
        Assert.Equal(3, attacked.Result.State.EffectiveAtk[Wizard]);
        var script = await host.Client.GetStringAsync("/app.js");
        Assert.DoesNotContain("wizard", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Focus", script);
        Assert.DoesNotContain("bonus:focus", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TelekinesisExposesAndResolvesAuthoritativeUnitChoiceOverHttp()
    {
        await using var host = await Host.Start();
        using var preference = await host.Post("preferences", new { expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        preference.EnsureSuccessStatusCode();
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var result = await host.Round(changed.Revision);
        foreach (var id in new[] { Barbarian, Rogue, Cleric })
        {
            Assert.Equal(id, result.Result.NextInput!.UnitId);
            result = await host.Decide(result.Revision, "stay");
            if (result.Result.NextInput?.UnitId == id)
                result = await host.Decide(result.Revision, "end-turn");
        }
        Assert.Equal(Wizard, result.Result.NextInput!.UnitId);
        result = await host.Decide(result.Revision, "4,8");
        var action = result.Result.NextInput!.Candidates.First(c => c.Action == UnitAction.Telekinesis);
        var projected = result.Presentation.Decision!.Candidates.Single(c => c.Key == action.Key);
        Assert.Equal("telekinesis", projected.EntryId);
        Assert.Equal(new ChoiceInteraction(InteractionKind.Unit, UnitId: action.TargetId), projected.Interaction);
        Assert.Equal(new[] { action.TargetId! }, projected.AffectedUnitIds);
        var before = result.Result.State.Physical.Figures.Single(f => f.Id == action.TargetId);
        var hp = result.Result.State.Units.Single(u => u.Id == action.TargetId).CurrentHp;
        var resolved = await host.Decide(result.Revision, projected.Key);
        Assert.Equal(new RulesEvent("PostureChanged", action.TargetId, Posture: Posture.Lying) { SourceUnitId = Wizard, ActionId = "telekinesis" }, Assert.Single(resolved.Result.Events, e => e.Kind == "PostureChanged"));
        Assert.Equal(before with { Posture = Posture.Lying }, resolved.Result.State.Physical.Figures.Single(f => f.Id == action.TargetId));
        Assert.Equal(hp, resolved.Result.State.Units.Single(u => u.Id == action.TargetId).CurrentHp);
        Assert.True(resolved.Result.State.ActionDone);
        Assert.DoesNotContain(resolved.Result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Action);
        Assert.Equal(Posture.Lying, resolved.Result.ResolutionSteps.Last().StateAfter.Physical.Figures.Single(f => f.Id == action.TargetId).Posture);
        Assert.Equal(OutcomeRole.Notice, resolved.Presentation.Events.Last().Role);
        Assert.Null(resolved.Presentation.Cards[Wizard].Entries.Single(e => e.Content.Id == "telekinesis").Uses);
        using var repeated = await host.Post("decision", new { expectedRevision = resolved.Revision, candidateKey = projected.Key });
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
    }

    [Fact]
    public async Task TrollVaultRequiresAllThreeGatesAndDelaysEvenSuccessfulAttempts()
    {
        await using var host = await Host.Start(doorRoll: 1);
        var result = await host.Read();
        Assert.Equal(UnitType.Troll(), result.Result.State.Types.Single(t => t.Id == "troll-type"));
        Assert.Equal("Undying", result.Presentation.Cards[Troll1].Entries.Single(e => e.Content.Id == "undying").Content.Name);
        var gates = new[] { new Edge(new(11, 1), new(11, 2), EdgeKind.ClosedDoor),
            new Edge(new(11, 2), new(11, 3), EdgeKind.ClosedDoor),
            new Edge(new(11, 4), new(11, 5), EdgeKind.ClosedDoor) };
        // Each horizontal partition spans the vault; the western boundary is solid.
        var board = result.Result.State.Physical.Board;
        foreach (var y in new[] { 1, 2, 4 })
            for (var x = 10; x <= 14; x++)
                Assert.Equal(x == 11 ? EdgeKind.ClosedDoor : EdgeKind.Wall, board.EdgeBetween(new(x, y), new(x, y + 1)));
        for (var y = 0; y <= 4; y++)
            Assert.Equal(EdgeKind.Wall, board.EdgeBetween(new(9, y), new(10, y)));
        var opened = new List<Edge>();
        for (var round = 1; round <= 4; round++)
        {
            result = await FinishRound(host, await host.Round(result.Revision));
            Assert.Null(result.Result.NextInput); // Trolls are fully automated by the host.
            var trollEvents = result.Result.Events.Where(e => result.Result.State.Units.Any(u =>
                u.Id == e.UnitId && u.TypeId == UnitTypeIds.Troll)).ToArray();
            Assert.All(trollEvents.Where(e => e.Kind == "DoorOpeningAttemptResolved"), e =>
            {
                Assert.Equal(4, e.SuccessCount);
                Assert.True(e.Succeeded);
            });
            opened.AddRange(trollEvents.Where(e => e.Kind == "DoorOpened").Select(e => e.Door!));
            if (round <= 2)
            {
                Assert.All(result.Result.State.Physical.Figures.Where(f => result.Result.State.Units.Any(u =>
                    u.Id == f.Id && u.TypeId == UnitTypeIds.Troll)), f =>
                {
                    Assert.InRange(f.Position.X, 10, 14);
                    Assert.InRange(f.Position.Y, 0, 4);
                });
                Assert.DoesNotContain(trollEvents, e => e.Kind == "AttackResolved");
            }
        }
        Assert.Equal(gates.Select(gate => gate with { Kind = EdgeKind.OpenDoor }), opened);
        Assert.Contains(result.Result.State.Physical.Figures, f => f.Position.Y >= 5 && result.Result.State.Units.Any(u =>
            u.Id == f.Id && u.TypeId == UnitTypeIds.Troll));
    }

    [Fact]
    public async Task FailedTrollDoorAttemptsKeepBothTrollsInsideFirstChamber()
    {
        await using var host = await Host.Start(doorRoll: 5);
        var result = await host.Read();
        for (var round = 0; round < 3; round++)
        {
            result = await FinishRound(host, await host.Round(result.Revision));
            var attempts = result.Result.Events.Where(e => e.Kind == "DoorOpeningAttemptResolved" && result.Result.State.Units.Any(u =>
                u.Id == e.UnitId && u.TypeId == UnitTypeIds.Troll)).ToArray();
            Assert.NotEmpty(attempts);
            Assert.All(attempts, e => Assert.False(e.Succeeded));
            Assert.All(result.Result.State.Physical.Figures.Where(f => result.Result.State.Units.Any(u =>
                u.Id == f.Id && u.TypeId == UnitTypeIds.Troll)), f => Assert.InRange(f.Position.Y, 0, 1));
            Assert.DoesNotContain(result.Result.Events, e => e.Kind == "DoorOpened" && result.Result.State.Units.Any(u =>
                u.Id == e.UnitId && u.TypeId == UnitTypeIds.Troll));
        }
    }

    [Fact]
    public async Task CourtyardOffersFuryAndCleaveOnFirstBarbarianActivation()
    {
        await using var host = await Host.Start(hit: true);
        var started = await host.Round(0);
        var moved = await host.Decide(started.Revision, "6,8");
        Assert.Equal(5, moved.Result.State.EffectiveAtkOf(Barbarian));
        var attacked = await host.Decide(moved.Revision, $"attack:{Grunt2}");
        Assert.Equal(5, attacked.Result.Events.First(e => e.Kind == "AttackResolved").Damage);
        Assert.Equal(DecisionKind.Cleave, attacked.Result.NextInput!.Kind);
        Assert.Contains(attacked.Result.NextInput.Candidates, c => c.Key == $"cleave:{Archer1}");
        var cleaved = await host.Decide(attacked.Revision, $"cleave:{Archer1}");
        Assert.Contains(cleaved.Result.Events, e => e.Kind == "CleaveResolved" && e.TargetId == Archer1);
    }

    [Fact]
    public async Task CourtyardOffersRogueBackstabWithBarbarianSupportingFlank()
    {
        await using var host = await Host.Start();
        var moved = await host.Decide((await host.Round(0)).Revision, "6,8");
        var rogue = await host.Decide(moved.Revision, "end-turn");
        Assert.Equal(Rogue, rogue.Result.NextInput!.UnitId);
        var flanked = await host.Decide(rogue.Revision, "6,9");
        Assert.Contains(flanked.Result.NextInput!.Candidates, c => c.Key == $"attack:{Grunt2}");
        Assert.Equal(4, flanked.Result.State.EffectiveAtkAgainst(Rogue, Grunt2));
        Assert.Equal(3, flanked.Result.State.EffectiveAtkOf(Rogue));
    }

    [Fact]
    public async Task ClericCanHealWoundedWizardAndWizardCanFireballCourtyardPair()
    {
        await using var host = await Host.Start(hit: true);
        using var preference = await host.Post("preferences", new { expectedRevision = 0, autoChooseSingleRelevantChoice = false });
        var changed = (await preference.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        var result = await host.Round(changed.Revision);
        foreach (var id in new[] { Barbarian, Rogue })
        {
            Assert.Equal(id, result.Result.NextInput!.UnitId);
            result = await host.Decide(result.Revision, "stay");
            result = await host.Decide(result.Revision, "end-turn");
        }
        Assert.Equal(Cleric, result.Result.NextInput!.UnitId);
        result = await host.Decide(result.Revision, "stay");
        result = await host.Decide(result.Revision, $"heal:{Wizard}");
        Assert.Contains(result.Result.Events, e => e.Kind == "HealResolved" && e.TargetId == Wizard && e.Healing == 2);
        Assert.Equal(4, result.Result.State.Units.Single(u => u.Id == Wizard).CurrentHp);
        result = await host.Decide(result.Revision, "4,8");
        var fireball = result.Result.NextInput!.Candidates.Single(c => c.Key == "fireball:6,8");
        Assert.Equal(new[] { Archer1, Grunt2 }.OrderBy(id => id), fireball.TargetIds.OrderBy(id => id));
        var resolved = await host.Decide(result.Revision, fireball.Key);
        Assert.All(new[] { Archer1, Grunt2 }, id => Assert.Contains(resolved.Result.Events, e => e.Kind == "UnitDefeated" && e.UnitId == id));
    }

    private static async Task<GameResponse> FinishRound(Host host, GameResponse result)
    {
        var events = new List<RulesEvent>(result.Result.Events);
        var steps = new List<ResolutionStep>(result.Result.ResolutionSteps);
        for (var decisions = 0; result.Result.NextInput is not null; decisions++)
        {
            Assert.True(decisions < 18, "A round must stop after the four Heroes' choices.");
            Assert.Contains(result.Result.NextInput.TypeId, new[] { "barbarian-type", "rogue-type", "cleric-type", "wizard-type" });
            result = await host.Decide(result.Revision, null);
            steps.AddRange(result.Result.ResolutionSteps.Select(s => s with { EventIndex = s.EventIndex + events.Count }));
            events.AddRange(result.Result.Events);
        }
        return result with { Result = result.Result with { Events = events, ResolutionSteps = steps } };
    }

    private sealed class FixedRandom(bool hit, bool monstersFirst, int doorRoll) : IRandomProvider
    {
        public bool Hits { get; set; } = hit;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => monstersFirst
            ? bag.FirstOrDefault(token => token.TypeId is "grunt-type" or "zombie-type" or "skeleton-archer-type" or "goblin-type" or "shaman-type" or "troll-type") ?? bag[0]
            : bag[0];
        public AttackFace RollAttackDie() => Hits ? AttackFace.Hit : AttackFace.Miss;
        public int RollD6() => doorRoll;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    // Real loopback HTTP tests, without adding a test-server package or persistence layer.
    private sealed class Host(WebApplication app, HttpClient client, FixedRandom random) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public bool Hits { set => random.Hits = value; }
        public static async Task<Host> Start(bool hit = false, bool monstersFirst = false, int doorRoll = 6)
        {
            var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var random = new FixedRandom(hit, monstersFirst, doorRoll);
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", contentRoot], random, CourtyardFixture.Create());
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) }, random);
        }
        public async Task<GameResponse> Read() => (await Client.GetFromJsonAsync<GameResponse>("/api/game", Json))!;
        public Task<HttpResponseMessage> Post(string operation, object body) => Client.PostAsJsonAsync($"/api/game/{operation}", body);
        public Task<GameResponse> Round(long revision) => Mutation("round", new { expectedRevision = revision });
        public async Task<GameResponse> Decide(long revision, string? key)
        {
            if (key is null)
                key = (await Read()).Result.NextInput!.Candidates.Single(c => c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn or ActivationChoiceKind.RollDice).Key;
            return await Mutation("decision", new { expectedRevision = revision, candidateKey = key });
        }
        private async Task<GameResponse> Mutation(string operation, object body)
        {
            using var response = await Post(operation, body);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
            // Legacy completed-operation tests explicitly continue each supplied pool over HTTP.
            var events = new List<RulesEvent>(result.Result.Events);
            var steps = new List<ResolutionStep>(result.Result.ResolutionSteps);
            while (result.Result.NextInput?.Kind == DecisionKind.RollDice)
            {
                using var rolled = await Post("decision", new { expectedRevision = result.Revision, candidateKey = "roll-dice" });
                rolled.EnsureSuccessStatusCode();
                result = (await rolled.Content.ReadFromJsonAsync<GameResponse>(Json))!;
                steps.AddRange(result.Result.ResolutionSteps.Select(step => step with { EventIndex = step.EventIndex + events.Count }));
                events.AddRange(result.Result.Events);
            }
            return result with { Result = result.Result with { Events = events, ResolutionSteps = steps } };
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }
}
