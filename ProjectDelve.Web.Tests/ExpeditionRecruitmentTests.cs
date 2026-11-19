using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionRecruitmentTests : IDisposable
{
    private const ulong Seed = 889;
    private const string Station = "recruitment-fixture-station";
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-recruitment-" + Guid.NewGuid().ToString("N"));

    internal static ScenarioDefinition RecruitmentRegion(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
    {
        var area = plan.ExitArea;
        var capacity = plan.DeploymentCapacity;
        var horizontal = plan.ExitEdge is ExpeditionEdge.East or ExpeditionEdge.West;
        var cells = (from u in Enumerable.Range(0, capacity) from v in Enumerable.Range(0, 2)
                     select horizontal ? new Cell(area.X + v, area.Y - capacity + u)
                         : new Cell(area.X - capacity + u, area.Y + v)).ToList();
        var departureFrom = cells[^1];
        var departure = horizontal ? departureFrom with { Y = departureFrom.Y + 1 }
            : departureFrom with { X = departureFrom.X + 1 };
        var order = new[] { departureFrom }.Concat(cells.Where(cell => cell != departureFrom)
            .OrderByDescending(cell => Math.Abs(cell.X - departureFrom.X) + Math.Abs(cell.Y - departureFrom.Y))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X)).ToList();
        var actor = order[0];
        var machine = new[] { new Cell(actor.X - 1, actor.Y), new(actor.X, actor.Y - 1),
            new(actor.X + 1, actor.Y), new(actor.X, actor.Y + 1) }.First(cell =>
                !cells.Contains(cell) && cell != departure &&
                !(cell.X >= area.X && cell.Y >= area.Y && cell.X < area.X + area.W && cell.Y < area.Y + area.H));
        return new()
        {
            Board = new() { Width = ExpeditionWorld.Stride, Height = ExpeditionWorld.Stride },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty,
            Setup = new(1, new(capacity, cells, cells, order, departureFrom, departure,
                2 * capacity, (3 * capacity + 1) / 2), []),
            Machines = [new(Station, machine, MachineTypes.EquipmentStation)],
            CreditsPiles = plan.Region == default ? [new(actor, 75)] : []
        };
    }

    private static EngineResult OpenStation(ExpeditionRunner runner)
    {
        var result = runner.StartRound();
        var actor = ExpeditionParty.Originals[0].InstanceId;
        for (var i = 0; i < 24 && result.NextInput?.UnitId != actor; i++)
            result = runner.Decide(Pass(result.NextInput!));
        Assert.Equal(actor, result.NextInput!.UnitId);
        result = runner.Decide(Assert.Single(result.NextInput.Candidates, c => c.Kind == ActivationChoiceKind.Stay).Key);
        return runner.Decide(Assert.Single(result.NextInput!.Candidates, c => c.MachineId == Station).Key);
    }

    private static string Pass(DecisionRequest request) => request.Candidates.FirstOrDefault(c =>
        c.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn)?.Key ?? request.Candidates[0].Key;

    private static string Recruit(ExpeditionRunner runner)
    {
        OpenStation(runner);
        var preview = GameEngine.PreviewRecruitment(runner.Snapshot(), UnitTypeIds.Tycoon);
        Assert.True(preview.CanConfirm, JsonSerializer.Serialize(preview.Diagnostics));
        runner.ResolveBoundary((state, random) => GameEngine.ConfirmRecruitment(state, UnitTypeIds.Tycoon, random, false), false);
        return runner.Snapshot().Party!.MemberIds[^1];
    }

    [Fact]
    public void PurchasedMemberSurvivesSaveReplayAndHandoffWithStableGearAndCredits()
    {
        const string id = "six-members";
        var store = new ExpeditionStore(root, id);
        var runner = ExpeditionRunner.Create(store, Seed, ContentBundle.Canonical, RecruitmentRegion);
        var recruit = Recruit(runner);
        var settled = runner.Snapshot();
        Assert.Equal(6, settled.Party!.MemberIds.Length);
        Assert.Contains("every Party member", Assert.Throws<ArgumentException>(() =>
            ExpeditionParty.ResolveContent(ContentBundle.Canonical(), settled, ExpeditionParty.Originals)).Message);
        Assert.Equal(0, settled.SideCredits[ExpeditionParty.SideId]);
        Assert.Equal(1, settled.ActionsUsedThisActivation);
        runner.ResolveBoundary((state, _) =>
        {
            var copy = GameEngine.UpgradeUnit(state, ExpeditionParty.Originals[0].InstanceId);
            var index = copy.Units.FindIndex(u => u.Id == recruit);
            copy.Units[index] = copy.Units[index] with
            {
                Inventory = [new("retained-recruit-boots", "lucky-boots") { Level = 2,
                    AbilityUseCounts = ImmutableDictionary<string, int>.Empty.Add("second-step", 1) }]
            };
            return new(copy, [], copy.Pending);
        }, false);
        var saved = runner.Save();
        var resumed = ExpeditionRunner.Open(store, ContentBundle.Canonical, RecruitmentRegion);
        Assert.Equal(JsonSerializer.Serialize(runner.Snapshot()), JsonSerializer.Serialize(resumed.Snapshot()));
        Assert.Equal(saved.ActiveCheckpoint, resumed.Head.ActiveCheckpoint);
        FinishWithFounderAndRecruit(resumed, recruit);
        var archived = resumed.Head.ActiveCheckpoint;
        var bytes = File.ReadAllBytes(Path.Combine(root, "expedition-" + id, archived.Digest + ".json"));
        var receipt = resumed.Continue(resumed.Region.TransitionId);
        Assert.Equal(receipt, resumed.Continue(receipt.Id));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "expedition-" + id, archived.Digest + ".json")));
        var next = store.ReadCheckpoint(receipt.Successor);
        Assert.Equal(6, next.Scenario.Setup!.Deployment.Capacity);
        Assert.Equal(settled.Party.MemberIds.ToArray(), next.State.Party!.MemberIds.ToArray());
        Assert.Equal(6, next.State.Units.Count(u => u.SideId == ExpeditionParty.SideId));
        var member = next.State.Units.Single(u => u.Id == recruit);
        Assert.Equal(UnitTypeIds.Tycoon, member.TypeId);
        Assert.Equal(6, member.CurrentHp);
        var boots = Assert.Single(member.Inventory);
        Assert.Equal("retained-recruit-boots", boots.Id);
        Assert.Equal(2, boots.Level);
        Assert.Empty(boots.AbilityUseCounts);
        Assert.Equal(0, next.State.SideCredits[ExpeditionParty.SideId]);
        Assert.Equal(2, next.State.Units.Single(u => u.Id == ExpeditionParty.Originals[0].InstanceId).Level);
        GameEngine.ValidateRestoredState(next.State);
    }

    [Fact]
    public void SixMemberHandoffCanRetryAfterHeadFailureWithoutDuplicatingMembersOrSpendingAgain()
    {
        var armed = false;
        var store = new ExpeditionStore(root, "retry-recruitment", point =>
        {
            if (armed && point == ExpeditionWritePoint.HeadBeforeReplace) throw new IOException("injected head failure");
        });
        var runner = ExpeditionRunner.Create(store, Seed, ContentBundle.Canonical, RecruitmentRegion);
        var recruit = Recruit(runner);
        FinishWithFounderAndRecruit(runner, recruit);
        var source = runner.Head.ActiveCheckpoint;
        var transition = runner.Region.TransitionId;
        armed = true;
        Assert.Equal(ExpeditionFailureStage.HeadCommit, Assert.Throws<ExpeditionOperationException>(() =>
            runner.Continue(transition)).Stage);
        Assert.Equal(source, store.ReadHead().ActiveCheckpoint);
        armed = false;
        var receipt = runner.Continue(transition);
        Assert.Equal(receipt, runner.Continue(transition));
        var next = runner.Snapshot();
        Assert.Equal(6, next.Party!.MemberIds.Length);
        Assert.Single(next.Units, u => u.Id == recruit);
        Assert.Equal(0, next.SideCredits[ExpeditionParty.SideId]);
        Assert.Equal(6, next.Physical.Figures.Count);
    }

    [Fact]
    public void DefeatedRecruitedMemberRemainsInRosterAndReturnsUnderTheEstablishedHandoffRules()
    {
        var store = new ExpeditionStore(root, "defeated-member");
        var runner = ExpeditionRunner.Create(store, Seed, ContentBundle.Canonical, RecruitmentRegion);
        var recruit = Recruit(runner);
        runner.ResolveBoundary((state, _) =>
        {
            var copy = PlaytestGame.Detach(state);
            var index = copy.Units.FindIndex(u => u.Id == recruit);
            copy.Units[index] = copy.Units[index] with { CurrentHp = 0 };
            copy.Physical.Figures.RemoveAll(f => f.Id == recruit);
            copy.DefeatedUnitIds.Add(recruit);
            return new(copy, [], copy.Pending);
        }, false);
        FinishWithFounderAndRecruit(runner, ExpeditionParty.Originals[1].InstanceId);
        var receipt = runner.Continue(runner.Region.TransitionId);
        var next = store.ReadCheckpoint(receipt.Successor).State;
        Assert.Contains(recruit, next.Party!.MemberIds);
        Assert.Equal(5, next.Units.Single(u => u.Id == recruit).CurrentHp);
        Assert.Contains(next.Physical.Figures, f => f.Id == recruit && f.Posture == Posture.Upright);
        Assert.DoesNotContain(recruit, next.DefeatedUnitIds);
    }

    [Fact]
    public void LegacyCheckpointMigratesOnlyTheActivePartyAndPreservesTheHistoricalBlob()
    {
        var store = new ExpeditionStore(root, "legacy-members");
        var runner = ExpeditionRunner.Create(store, Seed, ContentBundle.Canonical, RecruitmentRegion);
        var checkpoint = store.ReadCheckpoint(runner.Head.ActiveCheckpoint);
        SideObjective Legacy(SideObjective objective) => objective.SideId == ExpeditionParty.SideId
            ? objective with { Selectors = [.. ExpeditionParty.Originals.Select(h => UnitSelector.ForUnit(h.InstanceId))] } : objective;
        checkpoint.State.Party = null;
        checkpoint.State.Objectives = checkpoint.State.Objectives.Select(Legacy).ToList();
        var legacy = checkpoint with { Version = ExpeditionStore.LegacyCheckpointVersion,
            Scenario = checkpoint.Scenario with { Party = null, Objectives = checkpoint.Scenario.Objectives!.Select(Legacy).ToList() } };
        var reference = store.WriteCheckpoint(legacy);
        var head = runner.Head with { ActiveCheckpoint = reference, Revision = runner.Head.Revision + 1 };
        store.ReplaceHead(runner.Head, head);
        var path = Path.Combine(root, "expedition-legacy-members", reference.Digest + ".json");
        var bytes = File.ReadAllBytes(path);
        var reopened = ExpeditionRunner.Open(store, ContentBundle.Canonical, RecruitmentRegion);
        Assert.Equal(ExpeditionParty.Originals.Select(h => h.InstanceId), reopened.Snapshot().Party!.MemberIds);
        Assert.Equal(UnitSelectorKind.Party, Assert.Single(reopened.Snapshot().Objectives.Single(o =>
            o.SideId == ExpeditionParty.SideId).Selectors).Kind);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Null(store.ReadCheckpoint(reference).State.Party);
        Assert.Equal(checkpoint.GameplayRandomState, store.ReadCheckpoint(reference).GameplayRandomState);
    }

    [Fact]
    public void RecruitmentSelectionRoundTripsSeparatelyAndRejectsMixedOrUnboundDrafts()
    {
        var store = new ExpeditionStore(root, "recruitment-draft");
        var runner = ExpeditionRunner.Create(store, Seed, ContentBundle.Canonical, RecruitmentRegion);
        Assert.Throws<ExpeditionInputException>(() => runner.Save(null, UnitTypeIds.Tycoon));
        OpenStation(runner);
        var untouched = JsonSerializer.Serialize(runner.Snapshot());
        var reference = runner.Save(null, UnitTypeIds.Tycoon).ActiveCheckpoint;
        var checkpoint = store.ReadCheckpoint(reference);
        Assert.Equal(UnitTypeIds.Tycoon, checkpoint.RecruitmentTypeId);
        Assert.Null(checkpoint.EquipmentDraft);
        Assert.Equal(untouched, JsonSerializer.Serialize(checkpoint.State));
        var resumed = ExpeditionRunner.Open(store, ContentBundle.Canonical, RecruitmentRegion);
        Assert.Equal(UnitTypeIds.Tycoon, resumed.RecruitmentDraft);
        Assert.Equal(untouched, JsonSerializer.Serialize(resumed.Snapshot()));
        var allocation = resumed.Snapshot().MachineInteraction!.EquipmentStation!.OriginalAllocation;
        Assert.Throws<ExpeditionInputException>(() => resumed.Save(allocation, UnitTypeIds.Tycoon));
        Assert.Throws<InvalidDataException>(() => store.WriteCheckpoint(checkpoint with { EquipmentDraft = allocation }));
        Assert.Throws<InvalidDataException>(() => store.WriteCheckpoint(checkpoint with { RecruitmentTypeId = "  " }));
        resumed.Save(allocation);
        Assert.Null(resumed.RecruitmentDraft);
        Assert.NotNull(resumed.EquipmentDraft);
        resumed.Save(null, "unavailable-after-content-change");
        Assert.Equal("unavailable-after-content-change", resumed.RecruitmentDraft);
        resumed.ResolveBoundary((state, random) => GameEngine.CancelMachineInteraction(state, random, false), false);
        Assert.Null(resumed.RecruitmentDraft);
        Assert.Equal(75, resumed.Snapshot().SideCredits[ExpeditionParty.SideId]);
        Assert.Equal(1, resumed.Snapshot().ActionsRemaining);
    }

    [Fact]
    public void SeedBrowserRecruitmentExpeditionsWhenRequested()
    {
        var requested = Environment.GetEnvironmentVariable("PROJECTDELVE_RECRUITMENT_QA_ROOT");
        if (string.IsNullOrWhiteSpace(requested)) return;
        var qaRoot = Path.GetFullPath(requested);
        var artifactsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.playwright-cli")) +
            Path.DirectorySeparatorChar;
        if (!qaRoot.StartsWith(artifactsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recruitment browser fixtures must be written below the repository's .playwright-cli directory.");
        foreach (var mode in new[] { "desktop", "mobile" })
        {
            var settings = ExpeditionWorld.Configure(Seed, new(new(), new(0, 0, 0), Enrichment: new(Enabled: false),
                Civilization: new(Enabled: false), Routes: new(Enabled: false), Population: new(PopulationDensity.Off)));
            var runner = ExpeditionRunner.Create(new ExpeditionStore(qaRoot, "phase6-" + mode), Seed,
                ContentBundle.Canonical, RecruitmentRegion, settings.ToString(), ExpeditionWorldProfile.Create(settings.NaturalTerrain),
                "Recruitment " + mode);
            Assert.Contains(runner.Snapshot().Types, type => type.Id == UnitTypeIds.Tycoon && type.RecruitmentCost == 75);
            runner.ResolveBoundary((state, _) =>
            {
                var copy = PlaytestGame.Detach(state);
                copy.Units = copy.Units.Select((unit, index) => unit with { Dormant = index != 0 }).ToList();
                return new(copy, [], copy.Pending);
            }, false);
            runner.Save();
        }
    }

    internal static void FinishWithFounderAndRecruit(ExpeditionRunner runner, string second)
    {
        var targets = new[] { ExpeditionParty.Originals[0].InstanceId, second };
        var area = runner.Region.ExitArea;
        Cell[] destinations = [new(area.X, area.Y), new(area.X + 1, area.Y)];
        var result = new EngineResult(runner.Snapshot(), [], runner.Snapshot().Pending);
        for (var step = 0; step < 180 && !result.State.IsCompleted; step++)
        {
            if (result.NextInput is not { } decision) { result = runner.StartRound(); continue; }
            var key = Pass(decision);
            var index = Array.IndexOf(targets, decision.UnitId);
            if (index >= 0)
            {
                var destination = destinations[index];
                var start = result.State.Physical.Figures.Single(f => f.Id == decision.UnitId).Position;
                var move = decision.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move && c.Destination is not null)
                    .OrderBy(c => Distance(c.Destination!, destination)).FirstOrDefault();
                if (move is not null && Distance(move.Destination!, destination) < Distance(start, destination))
                    key = move.Key;
            }
            result = runner.Decide(key);
        }
        Assert.True(result.State.IsCompleted);
        Assert.Contains(ExpeditionParty.SideId, result.State.Completion!.AchievedSideIds);
        static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
