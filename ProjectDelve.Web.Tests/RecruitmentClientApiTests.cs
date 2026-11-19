using System.Net;
using System.Net.Http.Json;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed partial class ExpeditionClientApiTests
{
    [Fact]
    public async Task RecruitmentPreviewSavedSelectionAndRetryStayBoundToTheSameStationAndOnePayment()
    {
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: ExpeditionRecruitmentTests.RecruitmentRegion);
        string expeditionId;
        RecruitmentPreviewRequest oldBinding;
        var founder = ExpeditionParty.Originals[0].InstanceId;
        await using (var host = await Host.Start(root, catalog))
        {
            expeditionId = (await host.Send(Create("Recruit test", "889"))).Snapshot.Expedition!.Id;
            await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
            await host.Send(new() { Operation = "round" });
            while (host.Current.Snapshot.Result.NextInput?.UnitId != founder)
                await host.Send(new() { Operation = "decision", CandidateKey = Pass(host.Current.Snapshot.Result.NextInput!).Key });
            var stay = Assert.Single(host.Current.Snapshot.Result.NextInput!.Candidates, c => c.Kind == ActivationChoiceKind.Stay);
            await host.Send(new() { Operation = "decision", CandidateKey = stay.Key });
            var operate = Assert.Single(host.Current.Snapshot.Result.NextInput!.Candidates, c => c.MachineId is not null);
            var station = await host.Send(new() { Operation = "decision", CandidateKey = operate.Key });
            oldBinding = new(station.SessionId, station.Revision, station.DecisionId!, UnitTypeIds.Tycoon);
            using var response = await host.PostRecruit(oldBinding);
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var quote = (await response.Content.ReadFromJsonAsync<RecruitmentPreviewResponse>(Json))!.Preview;
            Assert.True(quote.CanConfirm);
            Assert.Equal(5, quote.PartySize);
            Assert.Equal(6, quote.PartyCapacity);
            Assert.Equal(75, quote.CurrentCredits);
            Assert.Equal(0, quote.ResultingCredits);
            Assert.NotNull(quote.SpawnCell);
            Assert.Equal(HttpStatusCode.BadRequest, await host.Reject(new() { Operation = "machine/recruit",
                RecruitTypeId = UnitTypeIds.Tycoon, EquipmentAllocation = new([]) }));
            var invalid = await host.Send(new() { Operation = "machine/recruit", RecruitTypeId = "not-an-offer" }, "error");
            Assert.Equal(station.Revision, invalid.Revision);
            Assert.Equal(5, invalid.Snapshot.Result.State.Party!.MemberIds.Length);
            Assert.Equal(0, invalid.Snapshot.Result.State.ActionsUsedThisActivation);
            await host.Send(new() { Operation = "expedition/save", RecruitTypeId = UnitTypeIds.Tycoon });
        }

        await using var resumed = await Host.Start(root, catalog);
        await resumed.Send(new() { Operation = "expedition/resume", ExpeditionId = expeditionId });
        using var stale = await resumed.PostRecruit(oldBinding);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(UnitTypeIds.Tycoon, (await resumed.PreviewStation(resumed.PreviewBinding())).SavedRecruitTypeId);
        var confirm = resumed.Prepare(new() { Operation = "machine/recruit", RecruitTypeId = UnitTypeIds.Tycoon });
        var result = (await resumed.Send(confirm, prepared: true)).Snapshot.Result.State;
        var tycoon = Assert.Single(result.Units, u => u.TypeId == UnitTypeIds.Tycoon);
        Assert.Contains(tycoon.Id, result.Party!.MemberIds);
        Assert.Equal(6, result.Party.MemberIds.Length);
        Assert.Equal(0, result.SideCredits[ExpeditionParty.SideId]);
        Assert.Equal(1, result.ActionsUsedThisActivation);
        Assert.Equal(HttpStatusCode.Accepted, await resumed.PostStatus(confirm));
        Assert.Equal(HttpStatusCode.Conflict, await resumed.PostStatus(confirm with { RecruitTypeId = "different" }));
        Assert.Equal(HttpStatusCode.Conflict, await resumed.PostStatus(confirm with { CommandId = "stale-recruit" }));
        await resumed.Send(new() { Operation = "expedition/save" });
        var store = new ExpeditionStore(root, expeditionId);
        var saved = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
        Assert.Null(saved.RecruitmentTypeId);
        Assert.Equal(6, saved.State.Party!.MemberIds.Length);
        Assert.Equal(0, saved.State.SideCredits[ExpeditionParty.SideId]);
    }
}
