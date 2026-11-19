using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

public sealed partial class ExpeditionClientApiTests(ITestOutputHelper output) : IDisposable
{
    private const ulong Seed = 889;
    private const string Boots = "fixture-carried-boots";
    private const string HiddenItem = "fixture-undiscovered-potion";
    private const string Station = "fixture-equipment-station";
    private const string StationItem = "fixture-station-item";
    private const string HiddenFailure = "private generator diagnostic at (41,49)";
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-expedition-client-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EquipmentStationPreviewInvalidDraftResumeAndIdempotentConfirmPreserveTheExpeditionBoundary(bool sellItem, bool upgrade)
    {
        ScenarioDefinition Region(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            var scenario = StationFixture(seed, plan, content);
            return upgrade && plan.Region == default
                ? scenario with { CreditsPiles = [new(scenario.Setup!.Deployment.PlacementOrder[0], 35)] }
                : scenario;
        }
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: Region);
        var owner = ExpeditionParty.Originals[0].InstanceId;
        var recipient = ExpeditionParty.Originals[1].InstanceId;
        var invalid = new EquipmentAllocation([new(Boots, recipient), new(Boots, owner)]);
        var valid = sellItem
            ? new EquipmentAllocation([new(Boots, recipient)]) { SoldObjectIds = [StationItem] }
            : new EquipmentAllocation([new(Boots, recipient), new(StationItem, recipient)]);
        if (sellItem) invalid = invalid with { SoldObjectIds = [StationItem] };
        if (upgrade)
        {
            invalid = invalid with { UpgradedUnitIds = [owner], UpgradedEquipmentIds = [Boots] };
            valid = valid with { UpgradedUnitIds = [owner], UpgradedEquipmentIds = [Boots] };
        }
        ExpeditionStore store;
        ExpeditionCheckpoint saved;
        EquipmentStationPreviewRequest previousBinding;
        string invalidPreview;

        await using (var host = await Host.Start(root, catalog))
        {
            var created = await host.Send(Create("Station fixture", "889"));
            store = new ExpeditionStore(root, created.Snapshot.Expedition!.Id);
            await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
            await host.Send(new() { Operation = "round" });
            for (var i = 0; i < 12 && host.Current.Snapshot.Result.NextInput?.UnitId != owner; i++)
                await host.Send(new() { Operation = "decision", CandidateKey = Pass(host.Current.Snapshot.Result.NextInput!).Key });

            Assert.Equal(owner, host.Current.Snapshot.Result.NextInput!.UnitId);
            foreach (var id in new[] { Boots, StationItem })
            {
                var pickup = Assert.Single(host.Current.Snapshot.Result.NextInput!.Candidates,
                    candidate => candidate.ObjectId == id);
                await host.Send(new() { Operation = "decision", CandidateKey = pickup.Key });
            }
            var stay = Assert.Single(host.Current.Snapshot.Result.NextInput!.Candidates,
                candidate => candidate.Kind == ActivationChoiceKind.Stay);
            await host.Send(new() { Operation = "decision", CandidateKey = stay.Key });
            var operate = Assert.Single(host.Current.Snapshot.Result.NextInput!.Candidates,
                candidate => candidate.MachineId == Station);
            await host.Send(new() { Operation = "decision", CandidateKey = operate.Key });
            var cancelled = await host.Send(new() { Operation = "machine/cancel" });
            Assert.Null(cancelled.Snapshot.Result.State.MachineInteraction);
            Assert.Equal(1, cancelled.Snapshot.Result.State.ActionsRemaining);
            Assert.Equal(0L, cancelled.Snapshot.Result.State.Physical.Board.Machines.Single().TotalUses);
            var opened = await host.Send(new() { Operation = "decision", CandidateKey = operate.Key });
            Assert.Equal(DecisionKind.MachineInteraction, opened.Snapshot.Result.NextInput!.Kind);
            Assert.Equal(0, opened.Snapshot.Result.State.ActionsUsedThisActivation);
            Assert.Equal("Manage Equipment and Items", opened.Snapshot.Presentation.Decision!.DisplayPrompt);
            Assert.Equal("Cancel", Assert.Single(opened.Snapshot.Presentation.Decision.Candidates).Label);
            var stationEditor = Assert.IsType<EquipmentStationInteraction>(opened.Snapshot.Presentation.Decision.EquipmentStation);
            Assert.Contains(recipient, stationEditor.EligibleUnitIds);
            Assert.Contains(new EquipmentAssignment(Boots, owner), stationEditor.OriginalAllocation.Assignments);
            previousBinding = host.PreviewBinding(invalid);

            var head = store.ReadHead();
            invalidPreview = Encode((await host.PreviewStation(previousBinding)).Preview);
            Assert.Equal(invalidPreview, Encode((await host.PreviewStation(previousBinding)).Preview));
            Assert.DoesNotContain(HiddenItem, invalidPreview);
            Assert.Equal(Encode(head), Encode(store.ReadHead()));
            var preview = (await host.PreviewStation(host.PreviewBinding(valid))).Preview;
            Assert.True(preview.CanConfirm);
            Assert.Equal(sellItem ? 2L : 0L, preview.TotalProceeds);
            Assert.Equal(upgrade ? 36L : 0L, preview.TotalUpgradeCost);
            Assert.Equal(preview.CurrentCredits + preview.TotalProceeds - preview.TotalUpgradeCost, preview.ResultingCredits);
            var bootsAbility = Assert.Single(preview.Units.Single(u => u.UnitId == recipient).AbilityPreviews,
                a => a.Ability.SourceInstanceId == Boots && a.Ability.Key == "movement");
            Assert.Equal(bootsAbility.Ability.CardEntry(), bootsAbility.Content);
            using (var stale = await host.PostPreview(previousBinding with { ExpectedRevision = opened.Revision - 1 }))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var rejected = await host.Send(new() { Operation = "machine/confirm", EquipmentAllocation = invalid }, "error");
            Assert.NotEmpty(rejected.EquipmentDiagnostics!.Value);
            Assert.Equal("waiting-human", rejected.Status);
            Assert.Equal(opened.Revision, rejected.Revision);
            Assert.Equal(opened.DecisionId, rejected.DecisionId);
            Assert.Equal(0, rejected.Snapshot.Result.State.ActionsUsedThisActivation);
            Assert.Equal(Encode(head), Encode(store.ReadHead()));

            await host.Send(new() { Operation = "expedition/save", EquipmentAllocation = invalid });
            saved = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
            Assert.Equal(invalid.Assignments.ToArray(), saved.EquipmentDraft!.Assignments.ToArray());
            Assert.Equal(invalid.SoldObjectIds.ToArray(), saved.EquipmentDraft.SoldObjectIds.ToArray());
            Assert.Equal(invalid.UpgradedUnitIds.ToArray(), saved.EquipmentDraft.UpgradedUnitIds.ToArray());
            Assert.Equal(invalid.UpgradedEquipmentIds.ToArray(), saved.EquipmentDraft.UpgradedEquipmentIds.ToArray());
            Assert.Equal(DecisionKind.MachineInteraction, saved.State.Pending!.Kind);
        }

        await using var resumedHost = await Host.Start(root, catalog);
        var resumed = await resumedHost.Send(new() { Operation = "expedition/resume", ExpeditionId = saved.ExpeditionId });
        Assert.NotEqual(previousBinding.SessionId, resumed.SessionId);
        Assert.NotEqual(previousBinding.DecisionId, resumed.DecisionId);
        using (var stale = await resumedHost.PostPreview(previousBinding))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var restored = await resumedHost.PreviewStation(resumedHost.PreviewBinding());
        Assert.Equal(invalid.Assignments.ToArray(), restored.SavedDraft!.Assignments.ToArray());
        Assert.Equal(invalid.SoldObjectIds.ToArray(), restored.SavedDraft.SoldObjectIds.ToArray());
        Assert.Equal(invalid.UpgradedUnitIds.ToArray(), restored.SavedDraft.UpgradedUnitIds.ToArray());
        Assert.Equal(invalid.UpgradedEquipmentIds.ToArray(), restored.SavedDraft.UpgradedEquipmentIds.ToArray());
        Assert.Equal(invalidPreview, Encode(restored.Preview));

        var random = new ExpeditionRandom(saved.GameplayRandomState);
        var expected = GameEngine.ConfirmEquipmentStation(saved.State, valid, random, false).State;
        var confirm = resumedHost.Prepare(new() { Operation = "machine/confirm", EquipmentAllocation = valid });
        var committed = await resumedHost.Send(confirm, prepared: true);
        Assert.Null(committed.Snapshot.Result.State.MachineInteraction);
        Assert.Contains(committed.Snapshot.Result.State.Units.Single(unit => unit.Id == recipient).Inventory, obj => obj.Id == Boots);
        Assert.Equal(HttpStatusCode.Accepted, await resumedHost.PostStatus(confirm with
            { EquipmentAllocation = valid with { Assignments = [.. valid.Assignments.Reverse()] } }));
        if (sellItem)
            Assert.Equal(HttpStatusCode.Conflict, await resumedHost.PostStatus(confirm with
                { EquipmentAllocation = valid with { SoldObjectIds = [] } }));
        if (upgrade)
        {
            Assert.Equal(HttpStatusCode.Conflict, await resumedHost.PostStatus(confirm with
                { EquipmentAllocation = valid with { UpgradedUnitIds = [] } }));
            Assert.Equal(HttpStatusCode.Conflict, await resumedHost.PostStatus(confirm with
                { EquipmentAllocation = valid with { UpgradedEquipmentIds = [] } }));
        }
        Assert.Equal(HttpStatusCode.Conflict, await resumedHost.PostStatus(confirm with { EquipmentAllocation = invalid }));
        Assert.Equal(HttpStatusCode.Conflict, await resumedHost.PostStatus(confirm with { CommandId = "new-stale-confirm" }));
        Assert.Equal(HttpStatusCode.Conflict, await resumedHost.PostStatus(confirm with
            { CommandId = "new-stale-cancel", Operation = "machine/cancel", EquipmentAllocation = null }));

        await resumedHost.Send(new() { Operation = "expedition/save" });
        var after = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
        Assert.Null(after.EquipmentDraft);
        Assert.Equal(1L, after.State.Physical.Board.Machines.Single().TotalUses);
        Assert.Equal(random.State, after.GameplayRandomState);
        Assert.Equal(Encode(expected), Encode(after.State));
        Assert.Equal(random.RollD6(), new ExpeditionRandom(after.GameplayRandomState).RollD6());
        if (sellItem)
        {
            var runner = ExpeditionRunner.Open(store, ContentBundle.Canonical, Region);
            var region = runner.Snapshot();
            for (var step = 0; step < 100 && !region.IsCompleted; step++)
                region = region.Pending is { } decision
                    ? runner.Decide(AdvanceTowardExit(region, decision, runner.Region.ExitArea).Key).State
                    : runner.StartRound().State;
            Assert.True(region.IsCompleted);
            var transitionId = runner.Region.TransitionId;
            var receipt = runner.Continue(transitionId);
            Assert.Equal(receipt, runner.Continue(transitionId));
            foreach (var reference in new[] { receipt.Source, receipt.Successor })
            {
                var checkpoint = store.ReadCheckpoint(reference);
                Assert.Equal(upgrade ? 1 : 2, checkpoint.State.SideCredits[ExpeditionParty.SideId]);
                Assert.Equal(upgrade ? 2 : 1, checkpoint.State.Units.Single(u => u.Id == owner).Level);
                var boots = Assert.Single(checkpoint.State.Units.Single(u => u.Id == recipient).Inventory, o => o.Id == Boots);
                Assert.Equal(upgrade ? 2 : 1, boots.Level);
                Assert.DoesNotContain(checkpoint.State.Units.SelectMany(u => u.Inventory), o => o.Id == StationItem);
                Assert.DoesNotContain(checkpoint.State.GroundObjects, o => o.Object.Id == StationItem);
            }
        }
    }

    [Fact]
    public async Task EquipmentStationInteractionIsPrivateToTheOperatingSide()
    {
        var state = GameEngine.CreateGame(new()
        {
            Board = new() { Width = 12, Height = 12 },
            UnitTypeIds = [UnitTypeIds.Barbarian, UnitTypeIds.Cleric, UnitTypeIds.Wizard],
            Units = [new(UnitTypeIds.Barbarian, "red", new(1, 1), UnitId: "operator") { Inventory = [new(Boots, "lucky-boots")] },
                new(UnitTypeIds.Cleric, "red", new(1, 2), UnitId: "recipient"), new(UnitTypeIds.Wizard, "blue", new(10, 10), UnitId: "viewer")],
            Agency = [new(UnitTypeIds.Barbarian, "red", ControllerKind.Human), new(UnitTypeIds.Cleric, "red", ControllerKind.Human),
                new(UnitTypeIds.Wizard, "blue", ControllerKind.Human)],
            Machines = [new(Station, new(2, 1), MachineTypes.EquipmentStation)],
            SideCredits = new() { ["red"] = 20 },
            FogOfWarSides = ["blue"]
        });
        var random = new ExpeditionRandom(889);
        state = GameEngine.StartRound(state, random, false).State;
        for (var i = 0; i < 8 && state.Pending?.UnitId != "operator"; i++)
            state = GameEngine.Advance(state, new Choice(Pass(state.Pending!).Key), random, false).State;
        Assert.Equal("operator", state.Pending!.UnitId);
        state = GameEngine.Advance(state, new Choice(Pass(state.Pending).Key), random, false).State;
        var operate = Assert.Single(state.Pending!.Candidates, candidate => candidate.MachineId == Station);
        state = GameEngine.Advance(state, new Choice(operate.Key), random, false).State;

        await using var host = await Host.Start(root, initialState: state);
        Assert.Null(host.Current.Snapshot.Result.State.MachineInteraction);
        Assert.Null(host.Current.Snapshot.Result.NextInput);
        Assert.Null(host.Current.Snapshot.Presentation.Decision);
        Assert.DoesNotContain(Boots, await host.GameSnapshot());
        using (var preview = await host.PostPreview(host.PreviewBinding()))
            Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await host.Reject(new() { Operation = "machine/cancel" }));
        var hiddenBinding = host.PreviewBinding();
        using (var recruit = await host.PostRecruit(new(hiddenBinding.SessionId, hiddenBinding.ExpectedRevision, hiddenBinding.DecisionId, UnitTypeIds.Tycoon)))
            Assert.Equal(HttpStatusCode.Forbidden, recruit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await host.Reject(new() { Operation = "machine/recruit", RecruitTypeId = UnitTypeIds.Tycoon }));
        Assert.Equal(HttpStatusCode.Forbidden, await host.Reject(new() { Operation = "machine/confirm",
            EquipmentAllocation = new([new(Boots, "recipient")]) }));

        var sale = GameEngine.ConfirmEquipmentStation(state, new([]) { SoldObjectIds = [Boots], UpgradedUnitIds = ["operator"] }, random, false);
        var ownerEvent = Assert.Single(BrowserProjection.Observe(sale, "red").Events, e => e.Kind == "MachineOperated");
        var controllerEvent = GambitProjection.Event(ownerEvent);
        Assert.Equal(Boots, Assert.Single(controllerEvent.EquipmentSales!).ObjectId);
        Assert.Equal("operator", Assert.Single(controllerEvent.UnitUpgrades!).TargetId);
        Assert.Equal(24, controllerEvent.UpgradeCost);
        Assert.Equal(0, controllerEvent.CreditsBalance);
        Assert.Contains("upgraded", BrowserProjection.Outcome(ownerEvent, sale.State).Text);
        Assert.DoesNotContain(BrowserProjection.Observe(sale, "blue").Events, e => e.Kind == "MachineOperated");
        var publicBoard = PlaytestGame.Detach(sale.State);
        publicBoard.FogOfWarSides.Clear();
        var observed = FogOfWar.ObserveEvent(publicBoard, "blue", ownerEvent)!;
        Assert.Null(GambitProjection.Event(observed).EquipmentSales);
        Assert.Null(GambitProjection.Event(observed).UnitUpgrades);
        Assert.Null(GambitProjection.Event(observed).UpgradeCost);
        Assert.Null(observed.CreditsAmount);
        Assert.Null(observed.CreditsBalance);
        Assert.DoesNotContain("sold", BrowserProjection.Outcome(observed, publicBoard).Text);
    }

    [Fact]
    public async Task CreationValidatesInputsAndPublishesAFullWld1Region()
    {
        await using var host = await Host.Start(root);
        var watch = Stopwatch.StartNew();
        foreach (var input in new[]
        {
            Create(" ", "123"), Create(new string('x', 81), "123"), Create("split\nname", "123"),
            Create("Bad seed", "18446744073709551616"), Create("Bad seed", "-1"), Create("Bad seed", "0xBADH"),
            Create("Bad recipe", "889", "WLD1-invalid")
        })
        {
            Assert.Contains(await host.Reject(input), new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
        }
        Assert.Empty((await host.Catalog()).Expeditions);
        Assert.Null(host.Current.Snapshot.Expedition);

        watch.Restart();
        var first = await host.Send(Create("Seed 889", "889", "Grassland"), timeout: TimeSpan.FromMinutes(2));
        output.WriteLine($"Full seed-889 Grassland creation and first browser publication: {watch.Elapsed.TotalSeconds:F2}s.");
        var view = Assert.IsType<ExpeditionPlayView>(first.Snapshot.Expedition);
        Assert.Equal("Seed 889", view.Name);
        Assert.Equal("0x0000000000000379", view.Seed);
        Assert.Equal(default, view.Region);
        Assert.Equal(2, view.ExitRequired);
        Assert.Null(view.TransitionId);
        Assert.False(view.HasUnsavedChanges);
        var store = new ExpeditionStore(root, view.Id);
        var head = store.ReadHead();
        Assert.NotNull(head.WorldProfile);
        Assert.Equal(ExpeditionWorldProfile.CurrentCivilizationVersion, head.WorldProfile.CivilizationVersion);
        Assert.Equal(ExpeditionWorldProfile.CurrentConstructionVersion, head.WorldProfile.ConstructionVersion);
        var checkpoint = store.ReadCheckpoint(head.ActiveCheckpoint);
        Assert.Equal(60, checkpoint.Scenario.Board.Width);
        Assert.Equal(60, checkpoint.Scenario.Board.Height);
        Assert.Contains(checkpoint.State.Units, unit => unit.SideId != ExpeditionParty.SideId);
        Assert.True(first.Snapshot.Result.State.Units.Count < checkpoint.State.Units.Count);
        Assert.Equal(5, first.Snapshot.Result.State.Units.Count(unit => unit.SideId == ExpeditionParty.SideId));
        var summary = Assert.Single((await host.Catalog()).Expeditions);
        Assert.Equal(view.Id, summary.Id);
        Assert.Equal(view.SavedAt, summary.SavedAt);
    }

    [Fact]
    public async Task ScenarioExportCopiesTheSavedRegionWithoutChangingPlayOrTheCheckpoint()
    {
        var builds = 0;
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: (seed, plan, content) =>
        {
            builds++;
            return Fixture(seed, plan, content);
        });
        await using var host = await Host.Start(root, catalog);
        using (var inactive = await host.ExportScenario())
            Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);

        var active = await host.Send(Create("Export test", "889"));
        var id = active.Snapshot.Expedition!.Id;
        var store = new ExpeditionStore(root, id);
        await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
        await host.Send(new() { Operation = "round" });
        var saved = await host.Send(new() { Operation = "expedition/save" });
        var head = store.ReadHead();
        var checkpoint = store.ReadCheckpoint(head.ActiveCheckpoint);
        var directory = Path.Combine(root, "expedition-" + id);
        var headBytes = File.ReadAllBytes(Path.Combine(directory, "head.json"));
        var checkpointPath = Path.Combine(directory, head.ActiveCheckpoint.Digest + ".json");
        var checkpointBytes = File.ReadAllBytes(checkpointPath);
        var files = Directory.GetFiles(directory).Order().ToArray();

        var legal = Pass(saved.Snapshot.Result.NextInput!);
        var random = new ExpeditionRandom(checkpoint.GameplayRandomState);
        var expected = GameEngine.Advance(checkpoint.State, new Choice(legal.Key), random, false);
        await host.Send(new() { Operation = "decision", CandidateKey = legal.Key });
        Assert.True(host.Current.Snapshot.Expedition!.HasUnsavedChanges);
        var liveBefore = await host.GameSnapshot();
        Assert.DoesNotContain(HiddenItem, liveBefore);

        using (var stale = await host.ExportScenario("stale-session"))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var response = await host.ExportScenario(active.SessionId);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var transport = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("DELVE1:", transport);
        var scenario = ScenarioDefinitionTransport.Decode(transport, null, checkpoint.Content);
        Assert.Equal(ScenarioDefinitionJson.ToJson(checkpoint.Scenario), ScenarioDefinitionJson.ToJson(scenario));
        Assert.Contains(scenario.GroundObjects!, item => item.Object.Id == HiddenItem);
        Assert.Equal(liveBefore, await host.GameSnapshot());
        Assert.Equal(headBytes, File.ReadAllBytes(Path.Combine(directory, "head.json")));
        Assert.Equal(checkpointBytes, File.ReadAllBytes(checkpointPath));
        Assert.Equal(files, Directory.GetFiles(directory).Order().ToArray());
        Assert.Equal(1, builds);

        await host.Send(new() { Operation = "expedition/save" });
        var after = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
        Assert.Equal(random.State, after.GameplayRandomState);
        Assert.Equal(Encode(expected.State), Encode(after.State));
    }

    [Fact]
    public async Task PendingChoiceRngAndFogSurviveSwitchAndServerRestart()
    {
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: Fixture);
        ExpeditionPlayView first;
        ExpeditionCheckpoint saved;
        string pendingJson;
        await using (var host = await Host.Start(root, catalog))
        {
            first = (await host.Send(Create("First", "889"))).Snapshot.Expedition!;
            Assert.Equal(ExpeditionWorldProfile.CurrentCivilizationVersion,
                new ExpeditionStore(root, first.Id).ReadHead().WorldProfile!.CivilizationVersion);
            await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
            await host.Send(new() { Operation = "round" });
            var playing = host.Current.Snapshot;
            Assert.True(playing.Expedition!.HasUnsavedChanges);
            pendingJson = Encode(Assert.IsType<DecisionRequest>(playing.Result.NextInput));
            Assert.NotEmpty(playing.Result.State.ExploredCells[ExpeditionParty.SideId]);
            Assert.DoesNotContain(HiddenItem, Encode(playing));
            var saveClock = Stopwatch.StartNew();
            await host.Send(new() { Operation = "expedition/save" });
            saveClock.Stop();
            Assert.False(host.Current.Snapshot.Expedition!.HasUnsavedChanges);
            var store = new ExpeditionStore(root, first.Id);
            saved = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
            output.WriteLine($"Pending fixture Save and browser acknowledgement: {saveClock.Elapsed.TotalMilliseconds:F0}ms.");
            Assert.False(saved.AutoChooseSingleRelevantChoice);
            Assert.Contains(saved.State.GroundObjects, item => item.Object.Id == HiddenItem);
            var second = (await host.Send(Create("Second", null))).Snapshot.Expedition!;
            Assert.NotEqual(first.Id, second.Id);
            Assert.Matches("^0x[0-9A-F]{16}$", second.Seed);
            var listed = await host.Catalog();
            Assert.Equal(2, listed.Expeditions.Count);
            Assert.DoesNotContain(HiddenItem, Encode(listed));
            await host.Send(new() { Operation = "round" });
            Assert.True(host.Current.Snapshot.Expedition!.HasUnsavedChanges);
            var switched = await host.Send(new() { Operation = "expedition/resume", ExpeditionId = first.Id });
            Assert.Equal(pendingJson, Encode(switched.Snapshot.Result.NextInput));
            var secondStore = new ExpeditionStore(root, second.Id);
            Assert.NotNull(secondStore.ReadCheckpoint(secondStore.ReadHead().ActiveCheckpoint).State.Pending);
            Assert.Equal(saved.GameplayRandomState, store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint).GameplayRandomState);
        }

        await using var reopened = await Host.Start(root, catalog);
        Assert.Null(reopened.Current.Snapshot.Expedition);
        var resumeClock = Stopwatch.StartNew();
        var resumed = await reopened.Send(new() { Operation = "expedition/resume", ExpeditionId = first.Id });
        output.WriteLine($"Pending fixture Resume from a fresh host: {resumeClock.Elapsed.TotalMilliseconds:F0}ms.");
        Assert.Equal(pendingJson, Encode(resumed.Snapshot.Result.NextInput));
        Assert.False(resumed.Snapshot.AutoChooseSingleRelevantChoice);
        Assert.DoesNotContain(HiddenItem, Encode(resumed));
        var firstStore = new ExpeditionStore(root, first.Id);
        var random = new ExpeditionRandom(saved.GameplayRandomState);
        var legal = Pass(resumed.Snapshot.Result.NextInput!);
        var direct = GameEngine.Advance(saved.State, new Choice(legal.Key), random, false);
        await reopened.Send(new() { Operation = "decision", CandidateKey = legal.Key });
        await reopened.Send(new() { Operation = "expedition/leave" });
        Assert.Null(reopened.Current.Snapshot.Expedition);
        var checkpoint = firstStore.ReadCheckpoint(firstStore.ReadHead().ActiveCheckpoint);
        Assert.Equal(random.State, checkpoint.GameplayRandomState);
        Assert.Equal(Encode(direct.State), Encode(checkpoint.State));
        Assert.Equal(2, (await reopened.Catalog()).Expeditions.Count);
    }

    [Fact]
    public async Task ContinueKeepsCompletedRegionAcrossGenerationFailureAndDuplicates()
    {
        ExpeditionStore? store = null;
        ExpeditionHead? completed = null;
        var builds = 0;
        using var generatorEntered = new ManualResetEventSlim();
        using var allowFailure = new ManualResetEventSlim();
        ScenarioDefinition Generate(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            Assert.Equal(Seed, seed);
            if (plan.Region != default)
            {
                Assert.Equal(completed!.ActiveCheckpoint, store!.ReadHead().ActiveCheckpoint);
                Assert.True(store.ReadCheckpoint(completed.ActiveCheckpoint).State.IsCompleted);
                if (++builds == 1)
                {
                    generatorEntered.Set();
                    Assert.True(allowFailure.Wait(TimeSpan.FromSeconds(10)));
                    throw new InvalidOperationException(HiddenFailure);
                }
            }
            return Fixture(seed, plan, content);
        }
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: Generate);
        await using var host = await Host.Start(root, catalog);
        var origin = (await host.Send(Create("Handoff", "889"))).Snapshot.Expedition!;
        store = new(root, origin.Id);
        var initial = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
        await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
        var traveled = Stopwatch.StartNew();
        var steps = 0;
        for (; steps < 100 && !host.Current.Snapshot.Result.State.IsCompleted; steps++)
        {
            var result = host.Current.Snapshot.Result;
            if (result.NextInput is not { } request)
            {
                await host.Send(new() { Operation = "round" });
                continue;
            }

            var choice = AdvanceTowardExit(result.State, request, initial.Plan.ExitArea);
            await host.Send(new() { Operation = "decision", CandidateKey = choice.Key });
        }
        output.WriteLine($"Fixture completed in {steps} browser commands and {traveled.Elapsed.TotalSeconds:F2}s.");
        var sourceView = host.Current.Snapshot.Expedition!;
        Assert.NotNull(sourceView.TransitionId);
        Assert.Equal(2, sourceView.ExitCount);
        Assert.Equal(1, sourceView.CompletedRegions);
        Assert.False(sourceView.HasUnsavedChanges);
        completed = store.ReadHead();
        var source = store.ReadCheckpoint(completed.ActiveCheckpoint);
        Assert.Contains(ExpeditionParty.SideId, source.State.Completion!.AchievedSideIds);
        Assert.Empty(completed.Transitions);
        var originalCheckpoint = Encode(source);
        Assert.Contains(source.State.Units.SelectMany(unit => unit.Inventory), item => item.Id == Boots);

        var command = new GameCommand { Operation = "expedition/continue", TransitionId = sourceView.TransitionId };
        var failing = host.Send(command, kind: "error");
        try
        {
            Assert.True(await Task.Run(() => generatorEntered.Wait(TimeSpan.FromSeconds(10))));
            Assert.Equal(HttpStatusCode.Conflict, await host.Reject(new() { Operation = "scenario", ScenarioId = PlaytestScenarios.DefaultId }));
            Assert.Equal(HttpStatusCode.Conflict, await host.Reject(command));
            using var sourceExport = await host.ExportScenario(host.Current.SessionId);
            Assert.Equal(ScenarioDefinitionTransport.Encode(source.Scenario), await sourceExport.Content.ReadAsStringAsync());
        }
        finally { allowFailure.Set(); }
        var failed = await failing;
        Assert.NotNull(failed.Error);
        Assert.Contains("generate", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(HiddenFailure, Encode(failed));
        Assert.Equal(sourceView, failed.Snapshot.Expedition);
        Assert.Equal(completed.ActiveCheckpoint, store.ReadHead().ActiveCheckpoint);

        var requestForRetry = host.Prepare(command);
        var success = await host.Send(requestForRetry, prepared: true);
        Assert.Equal(initial.Plan.NextRegion, success.Snapshot.Expedition!.Region);
        Assert.Equal(1, success.Snapshot.Expedition.CompletedRegions);
        Assert.Null(success.Snapshot.Expedition.TransitionId);
        Assert.Null(success.Snapshot.Result.NextInput);
        Assert.False(success.Snapshot.Result.State.IsCompleted);
        Assert.Equal(2, builds);
        Assert.Equal(HttpStatusCode.Accepted, await host.PostStatus(requestForRetry));
        Assert.Equal(HttpStatusCode.Conflict, await host.PostStatus(requestForRetry with { CommandId = Guid.NewGuid().ToString("N") }));
        var head = store.ReadHead();
        var transition = Assert.Single(head.Transitions);
        Assert.Equal(completed.ActiveCheckpoint, transition.Source);
        Assert.Equal(originalCheckpoint, Encode(store.ReadCheckpoint(transition.Source)));
        Assert.Equal(completed.OriginalHeroIds.Order(), success.Snapshot.Result.State.Units.Select(unit => unit.Id).Order());
        Assert.Contains(success.Snapshot.Result.State.Units.SelectMany(unit => unit.Inventory), item => item.Id == Boots);
        Assert.DoesNotContain(HiddenItem, Encode(success));
        using (var staleExport = await host.ExportScenario(requestForRetry.SessionId))
            Assert.Equal(HttpStatusCode.Conflict, staleExport.StatusCode);
        using var successorExport = await host.ExportScenario();
        Assert.Equal(ScenarioDefinitionTransport.Encode(store.ReadCheckpoint(transition.Successor).Scenario),
            await successorExport.Content.ReadAsStringAsync());
        Assert.Equal(2, builds);
    }

    [Fact]
    public void BrowserHandoffFixtureRetainsALegalPendingEndTurn()
    {
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: Fixture);
        var runner = catalog.Create("Near Exit Fixture", "889", "Grassland");
        var result = runner.ResolveBoundary((state, rng) => GameEngine.StartRound(state, rng, false), false);
        var pad = runner.Region.ExitArea;
        for (var step = 0; step < 100; step++)
        {
            if (result.NextInput is not { } request) { result = runner.StartRound(); continue; }
            if (request.Candidates.Any(candidate => candidate.Kind == ActivationChoiceKind.EndTurn) &&
                GameEngine.ObjectiveQualifiedCount(result.State, result.State.Objectives.Single(objective => objective.SideId == ExpeditionParty.SideId)) == 2)
                break;
            result = runner.Decide(AdvanceTowardExit(result.State, request, pad).Key);
        }

        Assert.False(result.State.IsCompleted);
        var pending = Assert.IsType<DecisionRequest>(result.NextInput);
        var end = Assert.Single(pending.Candidates, candidate => candidate.Kind == ActivationChoiceKind.EndTurn);
        var head = runner.Save();
        var saved = new ExpeditionStore(root, head.Id).ReadCheckpoint(head.ActiveCheckpoint);
        Assert.True(GameEngine.Advance(saved.State, new Choice(end.Key), new ExpeditionRandom(saved.GameplayRandomState), false).State.IsCompleted);

        // Opt-in fixture export for the reviewed data-only Edge plan.
        if (Environment.GetEnvironmentVariable("PROJECTDELVE_EXPEDITION_BROWSER_FIXTURE") is { Length: > 0 } destination)
        {
            var source = Path.Combine(root, "expedition-" + head.Id);
            var target = Path.Combine(destination, "expedition-" + head.Id);
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*.json"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            output.WriteLine($"Browser handoff fixture exported to {target}.");
        }
    }

    private static GameCommand Create(string name, string? seed, string? recipe = null) =>
        new() { Operation = "expedition/create", ExpeditionName = name, ExpeditionSeed = seed, ExpeditionRecipe = recipe };

    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static Candidate Pass(DecisionRequest request) => request.Candidates.FirstOrDefault(candidate =>
        candidate.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn) ?? request.Candidates[0];

    private static Candidate AdvanceTowardExit(GameState state, DecisionRequest request, ObjectiveArea area)
    {
        var pickup = request.Candidates.FirstOrDefault(candidate => candidate.Key == "pickup-object:" + Boots);
        if (pickup is not null) return pickup;
        var index = ExpeditionParty.Originals.ToList().FindIndex(hero => hero.InstanceId == request.UnitId);
        if (index is < 0 or > 1) return Pass(request);
        var target = new Cell(area.X + index, area.Y);
        var from = state.Physical.Figures.Single(figure => figure.Id == request.UnitId).Position;
        var move = request.Candidates.Where(candidate => candidate.Kind == ActivationChoiceKind.Move && candidate.Destination is not null)
            .OrderBy(candidate => Distance(candidate.Destination!, target)).FirstOrDefault();
        return move is not null && Distance(move.Destination!, target) < Distance(from, target) ? move : Pass(request);
    }

    private static ScenarioDefinition Fixture(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
    {
        var area = plan.ExitArea;
        var horizontal = plan.ExitEdge is ExpeditionEdge.East or ExpeditionEdge.West;
        var cells = (from u in Enumerable.Range(0, 5) from v in Enumerable.Range(0, 2)
                     select horizontal ? new Cell(area.X + v, area.Y - 5 + u) : new Cell(area.X - 5 + u, area.Y + v)).ToList();
        var edge = cells[^1];
        var departure = horizontal ? edge with { Y = edge.Y + 1 } : edge with { X = edge.X + 1 };
        return new()
        {
            Board = new() { Width = ExpeditionWorld.Stride, Height = ExpeditionWorld.Stride },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty,
            Setup = new(1, new(5, cells, cells.Take(8).ToList(), cells, edge, departure, 10, 8), []),
            GroundObjects = plan.Region == default ?
                [new(new OwnedObject(Boots, "lucky-boots"), cells[0]),
                    new(new OwnedObject(HiddenItem) { ItemTypeId = "second-wind" }, new(30, 30))] : []
        };
    }

    private static ScenarioDefinition StationFixture(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
    {
        var scenario = Fixture(seed, plan, content);
        var deployment = scenario.Setup!.Deployment;
        var start = deployment.PlacementOrder[0];
        var position = new[] { new Cell(start.X - 1, start.Y), new(start.X, start.Y - 1),
            new(start.X + 1, start.Y), new(start.X, start.Y + 1) }.First(cell =>
            cell.X >= 0 && cell.Y >= 0 && cell.X < scenario.Board.Width && cell.Y < scenario.Board.Height &&
            !deployment.Cells.Contains(cell) && cell != deployment.Departure);
        return scenario with
        {
            Machines = [new(Station, position, MachineTypes.EquipmentStation)],
            GroundObjects = plan.Region == default
                ? [.. scenario.GroundObjects!, new(new OwnedObject(StationItem) { ItemTypeId = "second-wind" }, start)]
                : scenario.GroundObjects
        };
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class Host(WebApplication app, HttpClient client, HttpResponseMessage stream, StreamReader reader) : IAsyncDisposable
    {
        public GameUpdate Current { get; private set; } = null!;

        public static async Task<Host> Start(string root, ExpeditionCatalog? catalog = null, GameState? initialState = null)
        {
            var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", contentRoot, "--Expeditions:Path", root],
                null, initialState ?? CourtyardFixture.Create(), expeditionCatalog: catalog);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient { BaseAddress = new Uri(address) };
            var stream = await client.GetAsync("/api/game/updates", HttpCompletionOption.ResponseHeadersRead);
            stream.EnsureSuccessStatusCode();
            var host = new Host(app, client, stream, new(await stream.Content.ReadAsStreamAsync()));
            host.Current = await host.Read(_ => true);
            return host;
        }

        public async Task<ExpeditionCatalogView> Catalog() => (await client.GetFromJsonAsync<ExpeditionCatalogView>("/api/expeditions", Json))!;
        public Task<string> GameSnapshot() => client.GetStringAsync("/api/game");
        public EquipmentStationPreviewRequest PreviewBinding(EquipmentAllocation? allocation = null) =>
            new(Current.SessionId, Current.Revision, Current.DecisionId!, allocation);
        public Task<HttpResponseMessage> PostPreview(EquipmentStationPreviewRequest request) =>
            client.PostAsJsonAsync("/api/game/machine/preview", request, Json);
        public Task<HttpResponseMessage> PostRecruit(RecruitmentPreviewRequest request) =>
            client.PostAsJsonAsync("/api/game/machine/recruitment/preview", request, Json);
        public async Task<EquipmentStationPreviewResponse> PreviewStation(EquipmentStationPreviewRequest request)
        {
            using var response = await PostPreview(request);
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            return (await response.Content.ReadFromJsonAsync<EquipmentStationPreviewResponse>(Json))!;
        }
        public Task<HttpResponseMessage> ExportScenario(string? sessionId = null) => client.GetAsync(
            "/api/expeditions/current/scenario" + (sessionId is null ? "" : "?sessionId=" + Uri.EscapeDataString(sessionId)));

        public GameCommand Prepare(GameCommand request) => request with
        {
            CommandId = Guid.NewGuid().ToString("N"), SessionId = Current.SessionId,
            ExpectedRevision = Current.Revision, DecisionId = Current.DecisionId
        };

        public async Task<HttpStatusCode> Reject(GameCommand request) => await PostStatus(Prepare(request));

        public async Task<HttpStatusCode> PostStatus(GameCommand request)
        {
            using var response = await client.PostAsJsonAsync("/api/game/commands", request, Json);
            return response.StatusCode;
        }

        public async Task<GameUpdate> Send(GameCommand request, string? kind = null, bool prepared = false, TimeSpan? timeout = null)
        {
            var command = prepared ? request : Prepare(request);
            using var accepted = await client.PostAsJsonAsync("/api/game/commands", command, Json);
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            Current = await Read(update => update.CommandId == command.CommandId &&
                (update.Kind is "error" or "fault" || update.Kind is "baseline" or "boundary" && update.Status != "resolving"), timeout);
            if (kind is not null) Assert.Equal(kind, Current.Kind);
            else Assert.True(Current.Kind is "baseline" or "boundary", Current.Error);
            return Current;
        }

        private async Task<GameUpdate> Read(Func<GameUpdate, bool> match, TimeSpan? wait = null)
        {
            using var timeout = new CancellationTokenSource(wait ?? TimeSpan.FromSeconds(30));
            var received = new List<string>();
            try
            {
                while (await reader.ReadLineAsync(timeout.Token) is { } line)
                {
                    if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                    var update = JsonSerializer.Deserialize<GameUpdate>(line[6..], Json)!;
                    if (match(update)) return update;
                    received.Add($"{update.Kind}/{update.Status} ({update.CommandId})");
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            { throw new TimeoutException("No matching game update. Received " + string.Join(", ", received)); }
            throw new EndOfStreamException("The game publication stream closed before the expected update.");
        }

        public async ValueTask DisposeAsync()
        {
            reader.Dispose();
            stream.Dispose();
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
