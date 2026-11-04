using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ContentWorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-workspace-" + Guid.NewGuid().ToString("N"));
    private string CanonicalPath => Path.Combine(root, "canonical");
    private string WorkspacePath => Path.Combine(root, "workspace");
    private CanonicalContent Canonical() => new(CanonicalPath);
    private ContentWorkspace Workspace(CanonicalContent canonical) => new(WorkspacePath, () => canonical.Bundle);
    private static readonly ContentIdentity Bell = new(ContentFamily.Equipment, "workspace-bell");
    private static readonly ContentIdentity Gift = new(ContentFamily.LootCard, "workspace-gift");
    private static ContentBundle Kit() => new()
    {
        Equipment = [new(Bell.TypeId, "Workspace bell"), new("unrelated-workspace", "Keep local")],
        LootCards = [new(Gift.TypeId, "Gift") { EquipmentTypeId = Bell.TypeId }]
    };

    [Fact]
    public void MutableScopePersistsWithoutHistoryAndRejectsStaleOrInvalidSaves()
    {
        var canonical = Canonical(); var workspace = Workspace(canonical);
        var empty = workspace.Snapshot();
        var saved = workspace.Save(Kit(), empty.Token);
        Assert.NotEqual(empty.Token, saved.Token);
        Assert.Equal(saved.Token, Workspace(canonical).Snapshot().Token);
        Assert.Single(Directory.GetFiles(WorkspacePath, "*.json"));
        Assert.Equal(409, Assert.Throws<LibraryException>(() => workspace.Save(new(), empty.Token)).StatusCode);
        var before = File.ReadAllBytes(Path.Combine(WorkspacePath, "workspace.json"));
        Assert.Throws<ContentCompositionException>(() => workspace.Save(new()
        { LootCards = [new("bad-gift", "Missing") { EquipmentTypeId = "absent" }] }, saved.Token));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(WorkspacePath, "workspace.json")));
        var cleared = workspace.Clear(saved.Token);
        Assert.Empty(cleared.Definitions.Equipment);
        Assert.NotEqual(saved.Token, cleared.Token);
    }

    [Fact]
    public void PreviewRequiresExplicitDependencySelectionAndBatchPromotionKeepsUnselectedWorkspaceRows()
    {
        var canonical = Canonical(); var workspace = Workspace(canonical);
        var saved = workspace.Save(Kit(), workspace.Snapshot().Token);
        var promotion = new WorkspacePromotionService(workspace, canonical);
        var incomplete = promotion.Preview(new(saved.Token, [Gift]));
        Assert.False(incomplete.CanPublish);
        Assert.Contains(Bell, incomplete.MissingSelections);
        Assert.Throws<ArgumentException>(() => promotion.Publish(new(incomplete, true)));
        Assert.Equal(saved.Token, workspace.Snapshot().Token);
        Assert.DoesNotContain(canonical.Bundle.LootCards, c => c.Id == Gift.TypeId);
        var preview = promotion.Preview(new(saved.Token, [Gift, Bell]));
        Assert.True(preview.CanPublish);
        Assert.Throws<ArgumentException>(() => promotion.Publish(new(preview, false)));
        var result = promotion.Publish(new(preview, true));
        Assert.Equal(2, result.Published.Length);
        Assert.Equal("unrelated-workspace", Assert.Single(result.Workspace.Definitions.Equipment).Id);
        Assert.Empty(result.Workspace.Definitions.LootCards);
        var restarted = Canonical();
        Assert.Equal(result.CanonicalRevision, restarted.Snapshot.Revision);
        Assert.Contains(restarted.Bundle.Equipment, e => e.Id == Bell.TypeId);
        Assert.Contains(restarted.Bundle.LootCards, c => c.Id == Gift.TypeId && c.EquipmentTypeId == Bell.TypeId);
    }

    [Fact]
    public void PromotionReplacesCSharpAndJsonOwnershipWithoutDuplicateIdentitiesOrSourceMutation()
    {
        Directory.CreateDirectory(CanonicalPath);
        var source = Path.Combine(CanonicalPath, "test-owned.json");
        var json = ContentPackageJson.ToJson(new() { Id = "test-owned", Definitions = new() { Equipment = [new("json-bell", "Original JSON")] } });
        File.WriteAllText(source, json);
        var canonical = Canonical(); var workspace = Workspace(canonical);
        var original = canonical.Bundle.Units[0];
        var saved = workspace.Save(new() { Units = [original with { DisplayName = "Replaced C#" }], Equipment = [new("json-bell", "Replaced JSON")] }, workspace.Snapshot().Token);
        var promotion = new WorkspacePromotionService(workspace, canonical);
        var preview = promotion.Preview(new(saved.Token, [new(ContentFamily.Unit, original.Id), new(ContentFamily.Equipment, "json-bell")]));
        Assert.Equal(2, preview.Replacements.Length);
        promotion.Publish(new(preview, true));
        Assert.Equal(json, File.ReadAllText(source));
        var restarted = Canonical();
        Assert.Equal("Replaced C#", Assert.Single(restarted.Bundle.Units.Where(u => u.Id == original.Id)).DisplayName);
        Assert.Equal("Replaced JSON", Assert.Single(restarted.Bundle.Equipment.Where(u => u.Id == "json-bell")).Name);
        Assert.Empty(workspace.Snapshot().Definitions.Units);
        _ = ContentPackageCatalog.Compose(restarted.Packages.Select(p => new ContentPackageDocument(p)));
    }

    [Fact]
    public void FailedPublicationRetainsWorkspaceAndCleanupFailureCanBeSafelyRetried()
    {
        var canonical = Canonical(); var workspace = Workspace(canonical);
        var saved = workspace.Save(Kit(), workspace.Snapshot().Token);
        var promotion = new WorkspacePromotionService(workspace, canonical);
        var preview = promotion.Preview(new(saved.Token, [Gift, Bell]));
        canonical.PublicationReady = () => throw new IOException("publication interrupted");
        Assert.Throws<IOException>(() => promotion.Publish(new(preview, true)));
        Assert.Equal(saved.Token, workspace.Snapshot().Token);
        Assert.DoesNotContain(Canonical().Bundle.Equipment, e => e.Id == Bell.TypeId);
        canonical.PublicationReady = null;
        workspace.SaveReady = () => throw new IOException("cleanup interrupted");
        Assert.Throws<IOException>(() => promotion.Publish(new(preview, true)));
        Assert.Equal(saved.Token, workspace.Snapshot().Token);
        Assert.Contains(Canonical().Bundle.Equipment, e => e.Id == Bell.TypeId);
        workspace.SaveReady = null;
        Assert.Equal(409, Assert.Throws<LibraryException>(() => promotion.Publish(new(preview, true))).StatusCode);
        var retry = promotion.Preview(new(saved.Token, [Gift, Bell]));
        promotion.Publish(new(retry, true));
        Assert.Single(canonical.Bundle.Equipment.Where(e => e.Id == Bell.TypeId));
        Assert.Equal("unrelated-workspace", Assert.Single(workspace.Snapshot().Definitions.Equipment).Id);
    }

    [Fact]
    public void ChangedWorkspaceOrForgedPreviewCannotPublish()
    {
        var canonical = Canonical(); var workspace = Workspace(canonical);
        var saved = workspace.Save(Kit(), workspace.Snapshot().Token);
        var promotion = new WorkspacePromotionService(workspace, canonical);
        var preview = promotion.Preview(new(saved.Token, [Bell]));
        Assert.Throws<ArgumentException>(() => promotion.Publish(new(preview with { Definitions = new() }, true)));
        workspace.Save(Kit(), saved.Token);
        Assert.Equal(409, Assert.Throws<LibraryException>(() => promotion.Publish(new(preview, true))).StatusCode);
        Assert.DoesNotContain(canonical.Bundle.Equipment, e => e.Id == Bell.TypeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsolidatedMechanicsExecuteFromScopedCatalogAndSurviveCanonicalReplacement(bool scenarioOverride)
    {
        var canonical = Canonical(); var workspace = Workspace(canonical);
        var worker = UnitRoster.Worker() with { Mov = 7 };
        var cleric = UnitRoster.Cleric() with { ResourceStorage = new([new(ResourceTypes.Diamond, 1)]) };
        var healing = cleric.LocalAbilities.Single(a => a.Key == "heal");
        cleric = cleric with { LocalAbilities = cleric.LocalAbilities.Replace(healing,
            healing with { Mechanic = new AdjacentUnitHealMechanic(RelationshipSelector.All, 3) }) };
        var wand = new EquipmentDefinition("test-workspace-wand", "Test Workspace Wand")
        {
            LocalAbilities = [new("call", "Call Worker", AbilityTiming.Action,
                new AdjacentUnitSummonMechanic(worker.Id, Posture.Upright, 3))
                { Usage = new(2), Costs = [new SpendResource(ResourceTypeIds.Diamond, 1), new LayDownSelf()] }]
        };
        var saved = workspace.Save(new()
        {
            Units = [cleric, worker], Equipment = [wand, new("keep-workspace", "Keep Workspace")]
        }, workspace.Snapshot().Token);
        var reloaded = Workspace(canonical);
        Assert.Equal(saved.Token, reloaded.Snapshot().Token);
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 4, Height = 4 }, UnitTypeIds = [cleric.Id],
            UnitTypes = scenarioOverride ? [worker with { Mov = 9 }] : [],
            Units = [new(cleric.Id, "blue", new(1, 1), UnitId: "actor")
                { Inventory = [new("wand-instance", wand.Id)] }],
            Agency = [new(cleric.Id, "blue", ControllerKind.Human)],
            StoredResources = [new("blue", cleric.Id, ResourceTypes.Diamond, 1)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(definition, reloaded.Resolve().Definitions);
        Assert.Equal(scenarioOverride ? 9 : 7, state.Types.Single(t => t.Id == worker.Id).Mov);
        Assert.Equal(new AdjacentUnitHealMechanic(RelationshipSelector.All, 3), state.Types.Single(t => t.Id == cleric.Id).LocalAbilities.Single(a => a.Key == "heal").Mechanic);
        var snapshot = System.Text.Json.JsonSerializer.Serialize(state);
        var promotion = new WorkspacePromotionService(workspace, canonical);
        var preview = promotion.Preview(new(saved.Token,
            [new(ContentFamily.Unit, cleric.Id), new(ContentFamily.Unit, worker.Id), new(ContentFamily.Equipment, wand.Id)]));
        Assert.True(preview.CanPublish);
        promotion.Publish(new(preview, true));
        Assert.Equal("keep-workspace", Assert.Single(workspace.Snapshot().Definitions.Equipment).Id);
        var restarted = Canonical();
        Assert.Equal(7, restarted.Bundle.Units.Single(t => t.Id == worker.Id).Mov);
        Assert.Equal(2, Assert.Single(restarted.Bundle.Equipment.Single(e => e.Id == wand.Id).LocalAbilities).Costs.Length);
        Assert.Equal(snapshot, System.Text.Json.JsonSerializer.Serialize(state));
        state = GameEngine.StartRound(state, new FirstDice(), false).State;
        state = GameEngine.Advance(state, new Pick("stay"), new FirstDice(), false).State;
        var choice = state.Pending!.Candidates.First(c => c.Ability?.SourceKind == AbilitySourceKind.Equipment);
        var result = GameEngine.Advance(state, new Pick(choice.Key), new FirstDice(), false);
        Assert.Equal(0, result.State.StoredQuantity("blue", cleric.Id, ResourceTypeIds.Diamond));
        Assert.Single(result.Events.Where(e => e.Kind == "ResourceSpent"));
        var created = Assert.Single(result.Events.Where(e => e.Kind == "UnitCreated"));
        Assert.Equal(worker.Id, created.TypeId);
        Assert.Equal(scenarioOverride ? 9 : 7, result.State.Types.Single(t => t.Id == worker.Id).Mov);
        Assert.False(result.State.IsUpright("actor"));
    }

    private sealed class FirstDice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Pick(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }

    [Fact]
    public void ExistingExplicitCSharpTransferSupportsSubsequentPublicationAndWorkspaceReplacement()
    {
        Directory.CreateDirectory(CanonicalPath);
        var original = ContentBundle.Canonical().Units[0];
        var identity = new ContentIdentity(ContentFamily.Unit, original.Id);
        File.WriteAllText(Path.Combine(CanonicalPath, "transfer.json"), ContentPackageJson.ToJson(new()
        {
            Id = "transfer-owner", ReplacesCSharp = [identity], Dependencies = [ContentPackageCatalog.CSharpPackageId],
            Definitions = new() { Units = [original with { DisplayName = "Transferred" }] }
        }));
        var canonical = Canonical();
        Assert.DoesNotContain(canonical.Packages.Single(p => p.Id == ContentPackageCatalog.CSharpPackageId).Definitions.Units, u => u.Id == original.Id);
        _ = ContentPackageCatalog.Compose(canonical.Packages.Select(p => new ContentPackageDocument(p)));
        canonical.Transaction(view => (true, (ContentPackage?)new() { Id = "later-publication", Definitions = new() { Equipment = [new("later-bell", "Later Bell")] } }));
        var workspace = Workspace(canonical);
        var saved = workspace.Save(new() { Units = [original with { DisplayName = "Workspace replacement" }] }, workspace.Snapshot().Token);
        var promotion = new WorkspacePromotionService(workspace, canonical);
        promotion.Publish(new(promotion.Preview(new(saved.Token, [identity])), true));
        var restarted = Canonical();
        Assert.Equal("Workspace replacement", restarted.Bundle.Units.Single(u => u.Id == original.Id).DisplayName);
        Assert.Contains(restarted.Bundle.Equipment, e => e.Id == "later-bell");
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
