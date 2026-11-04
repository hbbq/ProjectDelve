using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class LootManagementTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-loot-management-" + Guid.NewGuid().ToString("N"));
    private readonly CanonicalContent canonical;
    private readonly ContentWorkspace workspace;
    private readonly LootManagementService service;
    public LootManagementTests()
    {
        canonical = new(Path.Combine(root, "canonical"));
        workspace = new(Path.Combine(root, "workspace"), () => canonical.Bundle);
        service = new(workspace, canonical);
    }
    [Fact]
    public void RelationshipsRetainMultipleCardsAndTypedPresetQuantities()
    {
        workspace.Save(new()
        {
            Equipment = [new("test-reward", "Reward")],
            LootCards = [new("test-card-a", "A") { EquipmentTypeId = "test-reward" }, new("test-card-b", "B") { EquipmentTypeId = "test-reward" }],
            LootPresets = [new("test-preset", "Test", "", [new() { LootCardId = "test-card-a", Count = 3 }])]
        }, workspace.Snapshot().Token);
        var relationships = service.Inspect(null);
        Assert.Equal(2, relationships.Rewards.Single(r => r.TypeId == "test-reward").Cards.Length);
        var card = relationships.Cards.Single(c => c.TypeId == "test-card-a");
        Assert.Equal("equipment", card.RewardFamily);
        Assert.Equal("workspace", card.RewardOrigin);
        Assert.Contains(card.Presets, p => p.TypeId == "test-preset" && p.Quantity == 3 && !p.Dynamic);
        Assert.Contains(card.Presets, p => p.TypeId == "All" && p.Quantity == 1 && p.Dynamic);
        Assert.DoesNotContain(relationships.Cards.Single(c => c.TypeId == "test-card-b").Presets, p => p.TypeId == "test-preset");
    }
    [Fact]
    public void BatchCreationIsAtomicAndPreservesRewardIdsAndNames()
    {
        var saved = workspace.Save(new() { Equipment = [new("test-a", "First"), new("test-b", "Second")] }, workspace.Snapshot().Token);
        var request = new LootBatchRequest(saved.Token, [new(ContentFamily.Equipment, "test-a"), new(ContentFamily.Equipment, "test-b")]);
        var preview = service.Preview(request);
        Assert.True(preview.CanCreate);
        Assert.Equal(new[] { "test-a", "test-b" }, preview.Definitions.LootCards.Select(c => c.Id));
        Assert.Throws<LibraryException>(() => service.Create(new(preview with { Definitions = new() }, true)));
        Assert.Equal(saved.Token, workspace.Snapshot().Token);
        workspace.SaveReady = () => throw new IOException("Interrupted");
        Assert.Throws<IOException>(() => service.Create(new(preview, true)));
        Assert.Equal(saved.Token, workspace.Snapshot().Token);
        workspace.SaveReady = null;
        var result = service.Create(new(preview, true));
        Assert.Equal(result.Workspace.Token, workspace.Snapshot().Token);
        Assert.Equal(2, result.Workspace.Definitions.Equipment.Length);
        Assert.Equal(new[] { "First", "Second" }, result.Definitions.LootCards.Select(c => c.Name));
        Assert.All(result.Definitions.LootCards, c => Assert.Equal(c.Id, c.EquipmentTypeId));
        Assert.Equal(409, Assert.Throws<LibraryException>(() => service.Create(new(preview, true))).StatusCode);
    }
    [Fact]
    public void CollidingRewardFamiliesAndExistingCardsBlockWholeBatch()
    {
        var saved = workspace.Save(new()
        {
            Equipment = [new("shared-reward", "Equipment"), new("other-reward", "Other")],
            Items = [new("shared-reward", "Item") { LocalAbilities = [new("extra", "Extra", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))] }],
            LootCards = [new("unrelated-card", "Existing") { EquipmentTypeId = "other-reward" }]
        }, workspace.Snapshot().Token);
        var collision = service.Preview(new(saved.Token, [new(ContentFamily.Equipment, "shared-reward"), new(ContentFamily.Item, "shared-reward")]));
        Assert.False(collision.CanCreate);
        Assert.Contains(collision.Diagnostics, d => d.Code == "identityCollision");
        Assert.Throws<ArgumentException>(() => service.Create(new(collision, true)));
        var existing = service.Preview(new(saved.Token, [new(ContentFamily.Equipment, "other-reward")]));
        Assert.Contains(existing.Diagnostics, d => d.Code == "existingCard");
        Assert.Equal(saved.Token, workspace.Snapshot().Token);
    }
    [Fact]
    public void InspectionShowsMissingRewardsWithoutHidingOtherRelationships()
    {
        var context = new ContentEditorContext
        {
            LootCardDefinitions = [new("test-missing-reward", "Missing") { EquipmentTypeId = "not-defined" }]
        };
        var result = service.Inspect(context);
        var card = result.Cards.Single(c => c.TypeId == "test-missing-reward");
        Assert.Null(card.RewardOrigin);
        Assert.Contains(card.Diagnostics, d => d.Code == "missingReference" && d.Path == "equipmentTypeId");
        Assert.Contains(result.Diagnostics, d => d.Owner.TypeId == "test-missing-reward");
        Assert.Contains(result.Rewards, r => r.Origin == "canonical");
    }
    [Fact]
    public void InspectionReportsMissingPresetCardReferences()
    {
        var result = LootManagementService.Relationships(canonical.Bundle, new(), new()
        {
            LootPresets = [new("test-broken-preset", "Broken", "", [new() { LootCardId = "not-defined", Count = 2 }])]
        });
        Assert.Contains(result.Diagnostics, d => d.Owner == new ContentIdentity(ContentFamily.LootPreset, "test-broken-preset") &&
            d.Dependency == new ContentIdentity(ContentFamily.LootCard, "not-defined") && d.Code == "missingReference");
        Assert.NotEmpty(result.Cards);
    }
    [Fact]
    public void CanonicalChangesInvalidateConfirmedBatch()
    {
        var saved = workspace.Save(new() { Equipment = [new("test-batch-reward", "Reward"), new("test-publish-reward", "Publish")] }, workspace.Snapshot().Token);
        var preview = service.Preview(new(saved.Token, [new(ContentFamily.Equipment, "test-batch-reward")]));
        File.WriteAllText(Path.Combine(root, "canonical", "new-provider.json"), ContentPackageJson.ToJson(new()
        {
            Id = "test-new-provider", Definitions = new() { Equipment = [new("test-external-reward", "External")] }
        }));
        Assert.Equal(409, Assert.Throws<LibraryException>(() => service.Create(new(preview, true))).StatusCode);
        Assert.Equal(saved.Token, workspace.Snapshot().Token);
        Assert.Empty(workspace.Snapshot().Definitions.LootCards);
    }
    [Fact]
    public void ScenarioDestinationRevalidatesWithoutWritingWorkspaceAndRejectsScopeLeaks()
    {
        var token = workspace.Snapshot().Token;
        var context = new ContentEditorContext { EquipmentTypes = [new("local-reward", "Local")] };
        var selection = new ContentIdentity[] { new(ContentFamily.Equipment, "local-reward") };
        var rejected = service.Preview(new(token, [.. selection], Context: context));
        Assert.Contains(rejected.Diagnostics, d => d.Code == "scopeDependency");
        var preview = service.Preview(new(token, [.. selection], "scenario", context));
        Assert.True(preview.CanCreate);
        var result = service.Create(new(preview, true));
        Assert.Equal(token, workspace.Snapshot().Token);
        Assert.Equal("local-reward", Assert.Single(result.Definitions.LootCards).EquipmentTypeId);
        var canonicalToken = canonical.Snapshot.Revision;
        var saved = workspace.Save(new() { Equipment = [new("later", "Later")] }, token);
        Assert.NotEqual(token, saved.Token);
        Assert.Equal(canonicalToken, canonical.Snapshot.Revision);
        Assert.Throws<LibraryException>(() => service.Create(new(preview, true)));
    }
    [Fact]
    public void PromotionUsesCanonicalDependenciesUnlessWorkspaceReplacementIsSelected()
    {
        var canonicalReward = canonical.Bundle.Equipment[0];
        var saved = workspace.Save(new()
        {
            Equipment = [canonicalReward with { Name = "Optional override" }],
            LootCards = [new("test-promotion-card", "Card") { EquipmentTypeId = canonicalReward.Id }]
        }, workspace.Snapshot().Token);
        var promotion = new WorkspacePromotionService(workspace, canonical);
        var preview = promotion.Preview(new(saved.Token, [new(ContentFamily.LootCard, "test-promotion-card")]));
        Assert.True(preview.CanPublish);
        Assert.Contains(preview.Dependencies, d => d.Identity == new ContentIdentity(ContentFamily.Equipment, canonicalReward.Id) && d.Status == "canonical" && d.HasWorkspaceOverride);
        promotion.Publish(new(preview, true));
        Assert.Equal("Optional override", Assert.Single(workspace.Snapshot().Definitions.Equipment).Name);
        Assert.Equal(canonicalReward.Name, canonical.Bundle.Equipment.Single(e => e.Id == canonicalReward.Id).Name);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
