using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class CostAuthoringBoundaryTests
{
    [Fact]
    public void AuthoringMetadataExposesTheSameContractsForInteractiveCostEditingAndExecution()
    {
        var response = ContentBrowserCatalog.Canonical();
        Assert.Equal(CostCatalog.All, response.Costs);
        Assert.True(response.CostExecutionAvailable);
        Assert.True(response.CostEditingAvailable);
        var json = JsonSerializer.SerializeToElement(response, ContentPackageJson.CreateOptions());
        Assert.True(json.GetProperty("costExecutionAvailable").GetBoolean());
        Assert.True(json.GetProperty("costEditingAvailable").GetBoolean());
        Assert.Equal(2, json.GetProperty("costs").GetArrayLength());
    }

    [Fact]
    public void NormalAuthoringAcceptsExecutableCostsForTheSharedEditor()
    {
        var equipment = new EquipmentDefinition("cost-test", "Cost Test")
        {
            LocalAbilities = [new("use", "Use", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 2))
            { Costs = [new LayDownSelf()] }]
        };
        var result = ContentEditorApi.Validate(new("equipment",
            JsonSerializer.SerializeToElement(equipment, ContentPackageJson.CreateOptions())));
        Assert.True(result.Valid);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("Lay down this Unit.", Assert.Single(result.Entry!.Abilities).Description);
    }

    [Fact]
    public void NewCostBearingLibraryRevisionRoundTripsWithoutRewritingEarlierRevision()
    {
        var directory = Path.Combine(Path.GetTempPath(), "delve-cost-library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ContentLibrary(directory);
            var ability = new LocalAbilityDefinition("use", "Use", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 2));
            ContentPackage Package(LocalAbilityDefinition entry) => new()
            {
                Id = "paid-kit", Definitions = new()
                { Equipment = [new("paid-gear", "Paid Gear") { LocalAbilities = [entry] }] }
            };
            var original = store.Create(Package(ability));
            var paid = store.Save("paid-kit", new(Package(ability with { Costs = [new LayDownSelf()] }), original.Revision));
            Assert.Equal(original.Digest, store.Read("paid-kit", original.Revision).Digest);
            Assert.Empty(store.Read("paid-kit", original.Revision).Package.Definitions.Equipment[0].LocalAbilities[0].Costs);
            var reopened = new ContentLibrary(directory).Read("paid-kit", paid.Revision);
            Assert.IsType<LayDownSelf>(Assert.Single(reopened.Package.Definitions.Equipment[0].LocalAbilities[0].Costs));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
