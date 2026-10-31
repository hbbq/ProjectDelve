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

public sealed class ContentEditorWorkflowTests
{
    private const string HeroId = "content-collector";
    private const string FriendId = "content-patient";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task CreatedEquipmentAndLootPersistThroughDesignerLibraryPlaytestAndPendingSaveResume()
    {
        await using var host = await Host.Start();
        var equipment = await host.Validate<EquipmentDefinition>("equipment", new EquipmentDefinition("fieldwork-cape", "Fieldwork Cape")
        {
            LocalAbilities =
            [
                new("stride", "Wind at Your Back", AbilityTiming.Passive,
                    new SelfModifiersMechanic([new(Stat.Mov, 1), new(Stat.Def, -1)])),
                new("mend", "A Quiet Remedy", AbilityTiming.Action, new HealMechanic(2)) { Usage = new(MaxUses: null) },
                new("passage", "Paper Passage", AbilityTiming.Passive, new Phase()),
                new("door", "A Polite Knock", AbilityTiming.FreeAction, new OpenDoor())
            ]
        });
        var reward = await host.Validate<LootCardDefinition>("lootCards",
            new LootCardDefinition("fieldwork-reward", "A Folded Gift") { EquipmentTypeId = equipment.Id },
            new() { EquipmentTypes = [equipment] });
        Assert.DoesNotContain(EquipmentCatalog.All, type => type.Id == equipment.Id);
        Assert.DoesNotContain(LootCardCatalog.All, card => card.Id == reward.Id);

        var authored = Fixture(equipment, reward);
        var authoredJson = ScenarioDefinitionJson.ToJson(authored);
        using var exported = await host.Client.PostAsync("/api/designer/export", JsonBody(authoredJson));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        Assert.StartsWith("DELVE1:", transport);
        using var imported = await host.Client.PostAsJsonAsync("/api/designer/import", new { transport });
        imported.EnsureSuccessStatusCode();
        var restoredDefinition = ScenarioDefinitionJson.FromJson(await imported.Content.ReadAsStringAsync());
        Assert.Equal(authoredJson, ScenarioDefinitionJson.ToJson(restoredDefinition));
        AssertContent(equipment, reward, restoredDefinition);

        using var saved = await host.Client.PostAsJsonAsync("/api/library/scenarios",
            new SaveLibraryScenario("Content editor workflow fixture", null, JsonSerializer.SerializeToElement(authored, Json)), Json);
        saved.EnsureSuccessStatusCode();
        var savedEntry = (await saved.Content.ReadFromJsonAsync<LibraryScenario>(Json))!;
        var opened = (await host.Client.GetFromJsonAsync<LibraryScenario>($"/api/library/scenarios/{savedEntry.Id}", Json))!;
        Assert.Equal(authoredJson, ScenarioDefinitionJson.ToJson(ScenarioDefinitionJson.FromJson(opened.Definition.GetRawText())));
        var fromDisk = new ScenarioLibrary(host.LibraryDirectory).Scenario(savedEntry.Id);
        Assert.True(JsonElement.DeepEquals(opened.Definition, fromDisk.Definition));
        var libraryTransport = (await host.Client.GetFromJsonAsync<JsonElement>($"/api/library/scenarios/{savedEntry.Id}/transport"))
            .GetProperty("transport").GetString()!;
        Assert.Equal(authoredJson, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(libraryTransport)));

        var game = await host.Command("scenario/import", new { expectedRevision = 0, transport = libraryTransport });
        Assert.Empty(game.Result.State.Units.Single(unit => unit.Id == HeroId).Inventory);
        Assert.Equal(3, game.Result.State.EffectiveMovOf(HeroId));
        Assert.Equal(3, game.Result.State.EffectiveDefOf(HeroId));
        game = await host.Command("preferences", new { expectedRevision = game.Revision, autoChooseSingleRelevantChoice = false });
        game = await host.Command("round", new { expectedRevision = game.Revision });
        // Initial markers under Units resolve at the first Round Start. Stay then
        // exercises the ordinary choice boundary with the newly acquired source.
        var owned = Assert.Single(game.Result.State.Units.Single(unit => unit.Id == HeroId).Inventory);
        Assert.Equal(equipment.Id, owned.EquipmentTypeId);
        Assert.Equal(reward.Id, owned.LootCardId);
        Assert.Empty(game.Result.State.LootMarkers);
        Assert.Contains(game.Result.Events, occurrence => occurrence.Kind == "LootCardDrawn" && occurrence.ObjectCard?.Id == owned.Id);
        Assert.Equal(4, game.Result.State.EffectiveMovOf(HeroId));
        Assert.Equal(2, game.Result.State.EffectiveDefOf(HeroId));
        Assert.Contains(game.Result.State.AbilitiesOf(HeroId), ability => ability.SourceKind == AbilitySourceKind.Equipment && ability.Definition.Mechanic is Phase);
        Assert.Contains(game.Result.State.AbilitiesOf(HeroId), ability => ability.SourceKind == AbilitySourceKind.Equipment && ability.Definition.Mechanic is OpenDoor);
        if (game.Result.NextInput!.Kind == DecisionKind.SelectUnit)
            game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = HeroId });
        Assert.Equal(HeroId, game.Result.NextInput!.UnitId);
        var stay = Assert.Single(game.Result.NextInput!.Candidates, candidate => candidate.Kind == ActivationChoiceKind.Stay);
        game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = stay.Key });
        var heal = Assert.Single(game.Result.NextInput!.Candidates, candidate => candidate.Ability is
            { SourceKind: AbilitySourceKind.Equipment, Key: "mend" } && candidate.TargetId == FriendId);
        Assert.Equal(owned.Id, heal.Ability!.SourceInstanceId);
        Assert.Null(heal.Ability.Definition.Usage.MaxUses);
        Assert.Equal("A Quiet Remedy", heal.Ability.Definition.Name);

        // Save executable state, rather than the restricted browser projection.
        var authoritative = host.Game.Snapshot().Result.State;
        Assert.Empty(authoritative.LootDeck!.DrawPile);
        var savedState = JsonSerializer.Serialize(authoritative, Json);
        var resumedState = JsonSerializer.Deserialize<GameState>(savedState, Json)!;
        AbilityContent.ValidateState(resumedState);
        Assert.Equal(authoritative.Pending!.Candidates.Select(candidate => candidate.Key),
            resumedState.Pending!.Candidates.Select(candidate => candidate.Key));
        Assert.Equal(owned.Id, Assert.Single(resumedState.Units.Single(unit => unit.Id == HeroId).Inventory).Id);
        using var resumed = new PlaytestGame(new Dice(), resumedState);
        var resume = resumed.SetRelevanceAutoChoice(0, false);
        var resumedHeal = Assert.Single(resume.Result.NextInput!.Candidates, candidate => candidate.Ability is
            { SourceKind: AbilitySourceKind.Equipment, Key: "mend" } && candidate.TargetId == FriendId);
        Assert.Equal(heal.Key, resumedHeal.Key);
        Assert.Equal(heal.Ability.AccountingId, resumedHeal.Ability!.AccountingId);
        var afterResume = resumed.Decide(resume.Revision, resumedHeal.Key);
        game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = heal.Key });
        Assert.Equal(3, game.Result.State.Units.Single(unit => unit.Id == FriendId).CurrentHp);
        Assert.Contains(game.Result.Events, occurrence => occurrence.Kind == "ActionUsed" && occurrence.ActionId == heal.Ability.ActionId);
        Assert.Equal(owned.Id, Assert.Single(game.Result.State.Units.Single(unit => unit.Id == HeroId).Inventory).Id);
        Assert.Equal(JsonSerializer.Serialize(host.Game.Snapshot().Result.State, Json), JsonSerializer.Serialize(afterResume.Result.State, Json));

        var restarted = await host.Command("restart", new { expectedRevision = game.Revision });
        Assert.Empty(restarted.Result.State.Units.Single(unit => unit.Id == HeroId).Inventory);
        Assert.Single(restarted.Result.State.LootMarkers);
        Assert.Equal(3, restarted.Result.State.EffectiveMovOf(HeroId));
        AssertContent(equipment, reward, ScenarioDefinitionTransport.Decode(libraryTransport));
        Assert.Equal(authoredJson, ScenarioDefinitionJson.ToJson(authored));
    }

    private static void AssertContent(EquipmentDefinition equipment, LootCardDefinition reward, ScenarioDefinition definition)
    {
        Assert.Equal(JsonSerializer.Serialize(equipment, Json), JsonSerializer.Serialize(Assert.Single(definition.EquipmentTypes!), Json));
        Assert.Equal(reward, Assert.Single(definition.LootCardDefinitions!));
        Assert.Equal(reward.Id, Assert.Single(definition.Loot!.Composition!).LootCardId);
    }

    [Fact]
    public async Task KnightlyBootsLootGrantsTwoPhysicalSummonUsesWithPlacementPreviewAndPendingResume()
    {
        await using var host = await Host.Start();
        var boots = await host.Validate<EquipmentDefinition>("equipment", new EquipmentDefinition("knightly-boots", "Knightly Boots")
        { LocalAbilities = [new("call", "Call a Knight", AbilityTiming.Action,
            new SummonAdjacent(UnitTypeIds.HobbyKnight, Posture.Lying)) { Usage = new(2) }] });
        var reward = await host.Validate<LootCardDefinition>("lootCards", new LootCardDefinition("knightly-gift", "Knightly Gift")
        { EquipmentTypeId = boots.Id }, new() { EquipmentTypes = [boots] });
        var bearer = new UnitType("knightly-bearer", 1, 0, 0, 0, 4)
        {
            CanHaveEquipment = true, Unique = true,
            LocalAbilities =
            [
                new("practice", "Practice Time", AbilityTiming.BonusAction, new ExtraActionsMechanic(2)),
                new("rest", "Rest", AbilityTiming.Action, new HealMechanic(1))
            ]
        };
        var patient = new UnitType("knightly-patient", 0, 0, 0, 0, 2) { Unique = true };
        var scenario = Scenario.Define(Scenario.Map(5, 4),
            [
                Scenario.Group(bearer.Id, "blue", ControllerKind.Human, Scenario.At(1, 1, unitId: HeroId)),
                Scenario.Group(patient.Id, "blue", ControllerKind.Human, Scenario.At(3, 1, initialHp: 1, unitId: FriendId))
            ]) with
        {
            UnitTypes = [bearer, patient], EquipmentTypes = [boots], LootCardDefinitions = [reward],
            Loot = new() { Composition = [new(Count: 1) { LootCardId = reward.Id }] },
            LootMarkers = [new("boots-marker", new(2, 1))],
            Objectives = [new("blue", ScenarioObjective.NoObjective)]
        };
        using var exported = await host.Client.PostAsync("/api/designer/export", JsonBody(ScenarioDefinitionJson.ToJson(scenario)));
        exported.EnsureSuccessStatusCode();
        var transport = (await exported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transport").GetString()!;
        var game = await host.Command("scenario/import", new { expectedRevision = 0, transport });
        game = await host.Command("preferences", new { expectedRevision = game.Revision, autoChooseSingleRelevantChoice = false });
        game = await host.Command("round", new { expectedRevision = game.Revision });
        if (game.Result.NextInput!.Kind == DecisionKind.SelectUnit)
            game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = HeroId });
        var move = game.Result.NextInput!.Candidates.Single(candidate => candidate.Kind == ActivationChoiceKind.Move && candidate.Destination == new Cell(2, 1));
        game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = move.Key });
        var owned = Assert.Single(game.Result.State.Units.Single(unit => unit.Id == HeroId).Inventory);
        Assert.Equal(boots.Id, owned.EquipmentTypeId);
        Assert.Equal(reward.Id, owned.LootCardId);
        Assert.Empty(game.Result.State.LootMarkers);
        Assert.Equal(new AbilityUses(2, 2), Assert.Single(game.Presentation.Cards[HeroId].Entries.Where(entry => entry.SourceTypeId == boots.Id)).Uses);
        var practice = Assert.Single(game.Result.NextInput!.Candidates, candidate => candidate.Ability?.Key == "practice");
        game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = practice.Key });

        Candidate Summon(GameResponse response) => response.Result.NextInput!.Candidates.First(candidate => candidate.Ability is
            { SourceKind: AbilitySourceKind.Equipment, Key: "call" });
        var first = Summon(game);
        var preview = Assert.Single(game.Presentation.Decision!.Candidates, candidate => candidate.Key == first.Key);
        Assert.Equal(new[] { first.Destination! }, preview.PlacementCells);
        game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = first.Key });
        Assert.Equal(new AbilityUses(2, 1), Assert.Single(game.Presentation.Cards[HeroId].Entries.Where(entry => entry.SourceTypeId == boots.Id)).Uses);

        var authoritative = host.Game.Snapshot().Result.State;
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(authoritative, Json), Json)!;
        using var resumed = new PlaytestGame(new Dice(), restored);
        var afterResume = resumed.SetRelevanceAutoChoice(0, false);
        var second = Summon(game);
        Assert.Contains(afterResume.Result.NextInput!.Candidates, candidate => candidate.Key == second.Key);
        Assert.Equal(owned.Id, second.Ability!.SourceInstanceId);
        afterResume = resumed.Decide(afterResume.Revision, second.Key);
        game = await host.Command("decision", new { expectedRevision = game.Revision, candidateKey = second.Key });
        Assert.Equal(JsonSerializer.Serialize(host.Game.Snapshot().Result.State, Json), JsonSerializer.Serialize(afterResume.Result.State, Json));

        var knights = game.Result.State.Units.Where(unit => unit.TypeId == UnitTypeIds.HobbyKnight).ToArray();
        Assert.Equal(2, knights.Length);
        Assert.All(knights, unit =>
        {
            Assert.Equal("blue", unit.SideId);
            Assert.Equal(Posture.Lying, game.Result.State.Physical.Figures.Single(figure => figure.Id == unit.Id).Posture);
        });
        Assert.Equal(ControllerKind.Human, game.Result.State.ControllerFor(new ActivationToken(UnitTypeIds.HobbyKnight, "blue")));
        Assert.DoesNotContain(game.Result.State.Bag, token => token.TypeId == UnitTypeIds.HobbyKnight);
        Assert.Equal(1, game.Result.State.ActionsRemaining);
        Assert.Contains(game.Result.NextInput!.Candidates, candidate => candidate.Ability?.Key == "rest" && candidate.TargetId == FriendId);
        Assert.DoesNotContain(game.Result.NextInput!.Candidates, candidate => candidate.Ability?.Key == "call");
        Assert.Equal(new AbilityUses(2, 0), Assert.Single(game.Presentation.Cards[HeroId].Entries.Where(entry => entry.SourceTypeId == boots.Id)).Uses);
        Assert.Equal(owned.Id, Assert.Single(game.Result.State.Units.Single(unit => unit.Id == HeroId).Inventory).Id);
        AssertContent(boots, reward, ScenarioDefinitionTransport.Decode(transport));
    }

    private static ScenarioDefinition Fixture(EquipmentDefinition? equipment = null, LootCardDefinition? reward = null)
    {
        var friend = new UnitType("content-patient-type", 0, 0, 0, 0, 4) { DisplayName = "Practice patient", Unique = true };
        return Scenario.Define(Scenario.Map(4, 3),
        [
            Scenario.Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, Scenario.At(1, 1, unitId: HeroId)),
            Scenario.Group(friend.Id, "blue", ControllerKind.Human, Scenario.At(2, 1, initialHp: 1, unitId: FriendId))
        ]) with
        {
            UnitTypes = [friend], Objectives = [new("blue", ScenarioObjective.NoObjective)],
            EquipmentTypes = equipment is null ? [] : [equipment], LootCardDefinitions = reward is null ? [] : [reward],
            Loot = reward is null ? LootSettings.Empty : new() { Composition = [new(Count: 1) { LootCardId = reward.Id }] },
            LootMarkers = reward is null ? [] : [new("authored-reward-marker", new(1, 1))]
        };
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(token => token.TypeId == UnitTypeIds.Barbarian) ?? bag[0];
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private sealed class Host(WebApplication app, HttpClient client, string libraryDirectory) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public string LibraryDirectory => libraryDirectory;
        public PlaytestGame Game => app.Services.GetRequiredService<PlaytestGame>();
        public static async Task<Host> Start()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var library = Path.Combine(Path.GetTempPath(), "delve-content-workflow-" + Guid.NewGuid().ToString("N"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root, "--Library:Path", library],
                new Dice(), GameEngine.CreateGame(Fixture()));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new Uri(address) }, library);
        }
        public async Task<T> Validate<T>(string family, T definition, ContentEditorContext? context = null)
        {
            using var response = await client.PostAsJsonAsync("/api/content/validate",
                new ContentEditorRequest(family, JsonSerializer.SerializeToElement(definition, Json), Context: context), Json);
            response.EnsureSuccessStatusCode();
            var validation = (await response.Content.ReadFromJsonAsync<ContentEditorResponse>(Json))!;
            Assert.True(validation.Valid, JsonSerializer.Serialize(validation.Diagnostics, Json));
            Assert.Empty(validation.Diagnostics);
            return validation.Definition!.Value.Deserialize<T>(Json)!;
        }
        public async Task<GameResponse> Command(string action, object payload)
        {
            using var response = await client.PostAsJsonAsync("/api/game/" + action, payload, Json);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<GameResponse>(Json))!;
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
            if (Directory.Exists(libraryDirectory)) Directory.Delete(libraryDirectory, true);
        }
    }
}
