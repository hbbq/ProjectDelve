using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class WorkspaceScenarioExportTests : IDisposable
{
    private const string Caller = "export-caller";
    private const string Gear = "export-boots";
    private const string Gift = "export-gift";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-workspace-export-" + Guid.NewGuid().ToString("N"));

    private static ContentBundle Draft(string name = "Workspace Caller") => new()
    {
        Units = [new(Caller, 1, 0, 0, 0, 2) { DisplayName = name, Unique = true, CanHaveEquipment = true,
            LocalAbilities = [new("call", "Call", AbilityTiming.Action, new SummonAdjacent(UnitTypeIds.Goblin, Posture.Lying))] }],
        Equipment = [new(Gear, "Export Boots") { LocalAbilities =
            [new("stride", "Stride", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Mov, 2)]))] }],
        LootCards = [new(Gift, "Export Gift") { EquipmentTypeId = Gear }]
    };
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 4, Height = 4 }, UnitTypeIds = [Caller],
        Units = [new(Caller, "blue", new(1, 1), UnitId: "caller")],
        Agency = [new(Caller, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        Loot = new() { Composition = [new() { LootCardId = Gift }] }, LootMarkers = [new("gift", new(1, 1))]
    };

    [Fact]
    public void DelveExportCapturesWorkspaceAndCanonicalSummonClosureWithoutMutatingDraft()
    {
        var canonical = ContentBundle.Canonical(); var workspace = Draft(); var scenario = Scenario();
        var authored = ScenarioDefinitionJson.ToJson(scenario);
        var captured = WorkspaceScenarioExport.Capture(scenario, workspace, canonical);
        Assert.Equal(authored, ScenarioDefinitionJson.ToJson(scenario));
        Assert.Contains(captured.UnitTypes!, unit => unit.Id == Caller);
        Assert.Contains(captured.UnitTypes!, unit => unit.Id == UnitTypeIds.Goblin);
        Assert.Contains(captured.EquipmentTypes!, gear => gear.Id == Gear);
        Assert.Contains(captured.LootCardDefinitions!, card => card.Id == Gift && card.EquipmentTypeId == Gear);
        Assert.True(captured.ContentReferences is null or { Count: 0 });
        var transport = ScenarioDefinitionTransport.Encode(captured);
        Assert.StartsWith("DELVE1:", transport);
        // Default decode and game creation have no access to the application Workspace.
        var portable = ScenarioDefinitionTransport.Decode(transport);
        var state = GameEngine.CreateGame(portable);
        Assert.Equal("Workspace Caller", state.Types.Single(unit => unit.Id == Caller).DisplayName);
        Assert.Contains(state.Types, unit => unit.Id == UnitTypeIds.Goblin);
        Assert.DoesNotContain(state.Units, unit => unit.TypeId == UnitTypeIds.Goblin);
        Assert.Equal(new[] { Caller }, state.ActivationTypeOrder);
        var started = GameEngine.StartRound(state, new Dice(), false);
        var inventory = Assert.Single(started.State.Units.Single(unit => unit.Id == "caller").Inventory);
        Assert.Equal(Gear, inventory.EquipmentTypeId); Assert.Equal(Gift, inventory.LootCardId);
        Assert.Equal(3, started.State.EffectiveMovOf("caller"));
        var action = GameEngine.Advance(started.State, new Choice("stay"), new Dice(), false);
        var summon = action.NextInput!.Candidates.First(candidate => candidate.Action == UnitAction.SummonAdjacent);
        var summoned = GameEngine.Advance(action.State, new Choice(summon.Key), new Dice(), false);
        var helper = Assert.Single(summoned.State.Units.Where(unit => unit.TypeId == UnitTypeIds.Goblin));
        Assert.Equal(Posture.Lying, summoned.State.Physical.Figures.Single(figure => figure.Id == helper.Id).Posture);
    }

    [Fact]
    public void NewPlaytestsUseCurrentWorkspaceWhileRunningGamesAndExportedDefinitionsStayCaptured()
    {
        var canonical = ContentBundle.Canonical();
        var workspace = new ContentWorkspace(directory, () => canonical);
        var first = workspace.Save(Draft(), workspace.Snapshot().Token);
        var captured = WorkspaceScenarioExport.Capture(Scenario(), first.Definitions, canonical);
        var portableTransport = ScenarioDefinitionTransport.Encode(captured);
        using var playtest = new PlaytestGame(new Dice(), GameEngine.CreateGame(WebScenarioFixtures.Combat()),
            canonicalProvider: () => workspace.Resolve().Definitions);
        var started = playtest.StartTransportScenario(0, ScenarioDefinitionTransport.Encode(Scenario()));
        Assert.Equal("Workspace Caller", started.Result.State.Types.Single(unit => unit.Id == Caller).DisplayName);
        var running = JsonSerializer.Serialize(playtest.Snapshot().Result.State);
        var changed = workspace.Save(Draft("Later Caller"), first.Token);
        Assert.Equal(running, JsonSerializer.Serialize(playtest.Snapshot().Result.State));
        var newer = playtest.StartTransportScenario(started.Revision, ScenarioDefinitionTransport.Encode(Scenario()));
        Assert.Equal("Later Caller", newer.Result.State.Types.Single(unit => unit.Id == Caller).DisplayName);
        workspace.Clear(changed.Token);
        Assert.Equal("Later Caller", playtest.Snapshot().Result.State.Types.Single(unit => unit.Id == Caller).DisplayName);
        using var cleanPlaytest = new PlaytestGame(new Dice(), GameEngine.CreateGame(WebScenarioFixtures.Combat()));
        var reopened = cleanPlaytest.StartTransportScenario(0, portableTransport);
        Assert.Equal("Workspace Caller", reopened.Result.State.Types.Single(unit => unit.Id == Caller).DisplayName);
        Assert.Equal("Export Boots", reopened.Result.State.Equipment(Gear).Name);
        Assert.Equal(Gear, reopened.Result.State.LootCardDefinitions.Single(card => card.Id == Gift).EquipmentTypeId);
    }

    [Fact]
    public void ExplicitScenarioLocalDefinitionsRemainAboveCapturedWorkspaceDefinitions()
    {
        var workspace = Draft();
        var scenario = Scenario() with
        {
            UnitTypes = [workspace.Units[0] with { DisplayName = "Scenario Caller" }],
            EquipmentTypes = [workspace.Equipment[0] with { Name = "Scenario Boots" }]
        };
        var portable = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(
            WorkspaceScenarioExport.Capture(scenario, workspace, ContentBundle.Canonical())));
        var state = GameEngine.CreateGame(portable);
        Assert.Equal("Scenario Caller", state.Types.Single(unit => unit.Id == Caller).DisplayName);
        Assert.Equal("Scenario Boots", state.Equipment(Gear).Name);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
