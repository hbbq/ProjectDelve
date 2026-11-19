using System.Net;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed partial class ExpeditionClientApiTests
{
    [Fact]
    public async Task PaidMachineResumesAtTheOfferAndARepeatedPurchaseCarriesOnlyOneItemAcrossTheSeam()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web/Content/Canonical"));
        var published = new CanonicalContent(directory).Bundle;
        ScenarioDefinition Region(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            var fixture = ExpeditionRecruitmentTests.RecruitmentRegion(seed, plan, content);
            return fixture with { Machines = [fixture.Machines!.Single() with { Type = MachineTypes.MeteorOMat }] };
        }
        var catalog = new ExpeditionCatalog(root, () => published, generate: Region);
        var founderId = ExpeditionParty.Originals[0].InstanceId;
        string expeditionId;
        GameCommand oldConfirm;
        await using (var host = await Host.Start(root, catalog))
        {
            expeditionId = (await host.Send(Create("Vending fixture", "889"))).Snapshot.Expedition!.Id;
            await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
            await host.Send(new() { Operation = "round" });
            while (host.Current.Snapshot.Result.NextInput?.UnitId != founderId)
                await host.Send(new() { Operation = "decision", CandidateKey = Pass(host.Current.Snapshot.Result.NextInput!).Key });
            var stay = host.Current.Snapshot.Result.NextInput!.Candidates.Single(c => c.Kind == ActivationChoiceKind.Stay);
            var actions = await host.Send(new() { Operation = "decision", CandidateKey = stay.Key });
            var operate = actions.Snapshot.Result.NextInput!.Candidates.Single(c => c.MachineId is not null);
            var pending = await host.Send(new() { Operation = "decision", CandidateKey = operate.Key });
            var preview = pending.Snapshot.Presentation.Decision!.PaidMachine!;
            Assert.Equal(75, preview.CurrentCredits);
            Assert.Equal(60, Assert.Single(preview.Offers).ResultingCredits);
            Assert.True(preview.Offers[0].CanConfirm);
            Assert.Null(pending.Snapshot.Presentation.Decision.EquipmentStation);
            Assert.Equal(HttpStatusCode.Conflict, await host.Reject(new() { Operation = "machine/confirm", EquipmentAllocation = new([]) }));
            Assert.Equal(HttpStatusCode.BadRequest, await host.Reject(new() { Operation = "machine/service", ServiceId = "fireball", EquipmentAllocation = new([]) }));
            var rejected = await host.Send(new() { Operation = "machine/service", ServiceId = "unknown" }, "error");
            Assert.Equal(pending.Revision, rejected.Revision);
            Assert.Equal(0, rejected.Snapshot.Result.State.ActionsUsedThisActivation);
            oldConfirm = host.Prepare(new() { Operation = "machine/service", ServiceId = "fireball" });
            await host.Send(new() { Operation = "expedition/save" });
        }

        string itemId;
        await using (var host = await Host.Start(root, catalog))
        {
            await host.Send(new() { Operation = "expedition/resume", ExpeditionId = expeditionId });
            Assert.Equal("last-resort-fireball", Assert.Single(host.Current.Snapshot.Presentation.Decision!.PaidMachine!.Offers).ItemTypeId);
            Assert.Equal(HttpStatusCode.Conflict, await host.PostStatus(oldConfirm));
            var confirm = host.Prepare(new() { Operation = "machine/service", ServiceId = "fireball" });
            var result = (await host.Send(confirm, prepared: true)).Snapshot.Result.State;
            itemId = Assert.Single(result.Units.Single(u => u.Id == founderId).Inventory).Id;
            Assert.Equal(60, result.SideCredits[ExpeditionParty.SideId]);
            Assert.Equal(1, result.ActionsUsedThisActivation);
            Assert.Equal(HttpStatusCode.Accepted, await host.PostStatus(confirm));
            Assert.Equal(HttpStatusCode.Conflict, await host.PostStatus(confirm with { ServiceId = "different" }));
            Assert.Equal(HttpStatusCode.Conflict, await host.PostStatus(confirm with { CommandId = "new-stale-id" }));
            await host.Send(new() { Operation = "expedition/save" });
        }

        var store = new ExpeditionStore(root, expeditionId);
        var runner = ExpeditionRunner.Open(store, () => published, Region);
        ExpeditionRecruitmentTests.FinishWithFounderAndRecruit(runner, ExpeditionParty.Originals[1].InstanceId);
        var receipt = runner.Continue(runner.Region.TransitionId);
        Assert.Equal(receipt, runner.Continue(receipt.Id));
        var next = store.ReadCheckpoint(receipt.Successor).State;
        Assert.Equal(60, next.SideCredits[ExpeditionParty.SideId]);
        var carried = Assert.Single(next.Units.Single(u => u.Id == founderId).Inventory);
        Assert.Equal(itemId, carried.Id);
        Assert.Equal("last-resort-fireball", carried.ItemTypeId);
    }
}
