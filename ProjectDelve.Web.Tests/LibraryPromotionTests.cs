using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class LibraryPromotionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "delve-promotion-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(directory, "local");
    private string Bundle => Path.Combine(directory, "bundle");
    private ScenarioLibrary Store => new(Local, Bundle);
    private static SaveLibraryScenario Draft(string name, string? cover = null)
    {
        var definition = Scenario.Define(Scenario.Map(5, 5),
            [Scenario.Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, Scenario.At(0, 0))]);
        return new(name, cover, JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(definition)));
    }

    [Fact]
    public void PreviewDoesNotWriteAndApplyShipsCampaignScenariosAndImagesWithStableIds()
    {
        var cover = new LibraryCoverStore(Local, Bundle).Import(LibraryCoverTests.Png(), "image/png");
        var first = Store.SaveScenario(null, Draft("First", cover.CoverArt));
        var second = Store.SaveScenario(null, Draft("Second"));
        var campaign = Store.SaveCampaign(null, new("Finished", "Public introduction", cover.CoverArt, [second.Id, first.Id]));
        var before = File.ReadAllBytes(Path.Combine(Local, "library.json"));
        var preview = LibraryPromotion.Promote(Local, Bundle, campaign.Id);
        Assert.False(preview.Applied);
        Assert.False(Directory.Exists(Bundle));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Local, "library.json")));
        var result = LibraryPromotion.Promote(Local, Bundle, campaign.Id, apply: true);
        Assert.Equal(3, result.ReleasedLocalRecords);
        Assert.Equal(new[] { second.Id, first.Id }, result.ScenarioIds);
        Assert.Single(result.Covers);
        Assert.Equal("bundled", Store.Scenario(first.Id).Source);
        Assert.Single(Store.Catalog().Scenarios, s => s.Id == first.Id);
        Assert.Equal(first.Revision, Store.Scenario(first.Id).Revision);
        // A clean installation loads the exact same content and URL without the authoring directory.
        var clean = new ScenarioLibrary(Path.Combine(directory, "new-install"), Bundle);
        Assert.Equal(new[] { second.Id, first.Id }, clean.Campaign(campaign.Id).ScenarioIds);
        Assert.True(JsonElement.DeepEquals(first.Definition, clean.Scenario(first.Id).Definition));
        Assert.Equal(cover.CoverArt, clean.Scenario(first.Id).CoverArt);
        Assert.NotNull(new LibraryCoverStore(Path.Combine(directory, "new-install"), Bundle).Read(result.Covers[0]));
        var bundledBytes = File.ReadAllBytes(Path.Combine(Bundle, "library.json"));
        var again = LibraryPromotion.Promote(Local, Bundle, campaign.Id, apply: true);
        Assert.Equal(0, again.ReleasedLocalRecords);
        Assert.Equal(bundledBytes, File.ReadAllBytes(Path.Combine(Bundle, "library.json")));
    }

    [Fact]
    public void PromotionRefusesMissingDependenciesAndUnmanagedImagesBeforeWritingBundle()
    {
        var scenario = Store.SaveScenario(null, Draft("Missing later"));
        var campaign = Store.SaveCampaign(null, new("Unfinished", null, null, [scenario.Id]));
        Store.DeleteScenario(scenario.Id, scenario.Revision);
        Assert.Throws<LibraryException>(() => LibraryPromotion.Promote(Local, Bundle, campaign.Id));
        Assert.False(Directory.Exists(Bundle));
        var withExternalCover = Store.SaveCampaign(null, new("Unmanaged image", null, "https://example.org/cover.png", []));
        Assert.Throws<ArgumentException>(() => LibraryPromotion.Promote(Local, Bundle, withExternalCover.Id));
        Assert.False(Directory.Exists(Bundle));
    }

    [Fact]
    public void PromotionPreservesOtherBundlesAndRejectsOldAuthoringTokens()
    {
        var scenario = Store.SaveScenario(null, Draft("First"));
        var campaign = Store.SaveCampaign(null, new("One", null, null, [scenario.Id]));
        LibraryPromotion.Promote(Local, Bundle, campaign.Id, true);
        var other = Store.SaveCampaign(null, new("Two", null, null, []));
        LibraryPromotion.Promote(Local, Bundle, other.Id, true);
        Assert.Equal("One", Store.Campaign(campaign.Id).Name);
        Assert.Equal("Two", Store.Campaign(other.Id).Name);
        Assert.Throws<LibraryException>(() => Store.SaveScenario(scenario.Id,
            Draft("Stale") with { Revision = scenario.Revision, ChangeToken = scenario.ChangeToken }));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
