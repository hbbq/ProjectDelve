using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record LootManagementRequest(ContentEditorContext? Context = null);
public sealed record LootManagementCardLink(string TypeId, string Name);
public sealed record LootManagementReward(string Family, string TypeId, string Name, string Origin, ImmutableArray<LootManagementCardLink> Cards);
public sealed record LootManagementPreset(string TypeId, string Name, int Quantity, bool Dynamic);
public sealed record LootManagementCard(string TypeId, string Name,
    ImmutableArray<LootManagementPreset> Presets, ImmutableArray<ContentBrowserDiagnostic> Diagnostics,
    string? RewardFamily = null, string? RewardTypeId = null, string? RewardOrigin = null);
public sealed record LootManagementResponse(ImmutableArray<LootManagementReward> Rewards, ImmutableArray<LootManagementCard> Cards,
    ImmutableArray<ContentDiagnostic> Diagnostics);
public sealed record LootBatchRequest(string ExpectedToken, ImmutableArray<ContentIdentity> Selected,
    string Destination = "workspace", ContentEditorContext? Context = null);
public sealed record LootBatchPreview(LootBatchRequest Request, string CanonicalRevision, ContentBundle Definitions,
    ImmutableArray<ContentBrowserDiagnostic> Diagnostics, string PreviewToken)
{
    public bool CanCreate => Diagnostics.IsEmpty;
}
public sealed record ConfirmLootBatch(LootBatchPreview Preview, bool Confirm);
public sealed record LootBatchResult(ContentBundle Definitions, ContentWorkspaceSnapshot Workspace);

/// <summary>Authoring only; rewards and preset membership resolve from typed content.</summary>
public sealed class LootManagementService(ContentWorkspace workspace, CanonicalContent canonical)
{
    internal static ContentBundle Local(ContentEditorContext? context) => new()
    {
        Units = [.. context?.UnitTypes ?? []], Equipment = [.. context?.EquipmentTypes ?? []],
        Items = [.. context?.ItemTypes ?? []], LootCards = [.. context?.LootCardDefinitions ?? []]
    };
    public LootManagementResponse Inspect(ContentEditorContext? context) => workspace.WithSnapshot(workspace.Snapshot().Token, snapshot =>
        (canonical.Transaction(view => (Relationships(Base(view.Catalog.Definitions, snapshot.Definitions, context), new(), Local(context), snapshot.Definitions, view.Catalog.Definitions), (ContentPackage?)null)), (ContentBundle?)null));

    private static ContentBundle Base(ContentBundle canonical, ContentBundle workspace, ContentEditorContext? context)
        => ScenarioContentReferences.Compose(ContentComposition.ComposeScopes(canonical, workspace).Definitions, context?.ContentReferences).Definitions;

    internal static LootManagementResponse Relationships(ContentBundle canonical, ContentBundle workspace, ContentBundle local, ContentBundle? originWorkspace = null, ContentBundle? originCanonical = null)
    {
        ImmutableArray<ContentDiagnostic> errors = [];
        ContentBundle effective;
        try { effective = ContentComposition.ComposeScopes(canonical, workspace, local).Definitions; }
        catch (ContentCompositionException error)
        {
            errors = error.Diagnostics;
            effective = ContentWorkspace.Overlay(ContentWorkspace.Overlay(canonical, workspace), local);
        }
        var localIds = ContentWorkspace.Identities(local).ToHashSet();
        var workspaceIds = ContentWorkspace.Identities(originWorkspace ?? workspace).ToHashSet();
        var canonicalIds = ContentWorkspace.Identities(originCanonical ?? canonical).ToHashSet();
        string Origin(ContentFamily family, string id) => localIds.Contains(new(family, id)) ? "scenario-local" :
            workspaceIds.Contains(new(family, id)) ? canonicalIds.Contains(new(family, id)) ? "workspace-override" : "workspace" : "canonical";
        var rewards = effective.Equipment.Select(e => (Family: ContentFamily.Equipment, Id: e.Id, Name: e.Name))
            .Concat(effective.Items.Select(i => (Family: ContentFamily.Item, Id: i.Id, Name: i.Name)))
            .OrderBy(r => r.Family).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new LootManagementReward(r.Family == ContentFamily.Equipment ? "equipment" : "items", r.Id, r.Name, Origin(r.Family, r.Id),
                [.. effective.LootCards.Where(c => r.Family == ContentFamily.Equipment ? c.EquipmentTypeId == r.Id : c.ItemTypeId == r.Id)
                    .OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new LootManagementCardLink(c.Id, c.Name))])).ToImmutableArray();
        var cards = effective.LootCards.OrderBy(c => c.Id, StringComparer.Ordinal).Select(card =>
        {
            var family = card.EquipmentTypeId is not null ? "equipment" : card.ItemTypeId is not null ? "items" : null;
            var id = card.EquipmentTypeId ?? card.ItemTypeId;
            var reward = rewards.FirstOrDefault(r => r.Family == family && r.TypeId == id);
            var presets = effective.LootPresets.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p =>
            {
                var quantity = p.IsDynamic ? 1 : p.Composition.Sum(row =>
                    ContentDefinitions.Dependencies(p with { Composition = new[] { row } })
                        .Any(d => d.Target == new ContentIdentity(ContentFamily.LootCard, card.Id)) ? row.Count : 0);
                return new LootManagementPreset(p.Id, p.Name, quantity, p.IsDynamic);
            }).Where(p => p.Quantity > 0).ToImmutableArray();
            return new LootManagementCard(card.Id, card.Name, presets,
                [.. errors.Where(e => e.Owner == new ContentIdentity(ContentFamily.LootCard, card.Id))
                    .Select(e => new ContentBrowserDiagnostic(e.Message, e.Path, e.Code))], family, id, reward?.Origin);
        }).ToImmutableArray();
        return new(rewards, cards, errors);
    }

    public LootBatchPreview Preview(LootBatchRequest request) => workspace.WithSnapshot(request.ExpectedToken, snapshot =>
        (canonical.Transaction(view => (Build(request, snapshot, view), (ContentPackage?)null)), (ContentBundle?)null));

    public LootBatchResult Create(ConfirmLootBatch command)
    {
        if (command?.Preview is null || !command.Confirm) throw new ArgumentException("Explicit Loot batch confirmation is required.");
        var expected = command.Preview;
        ContentBundle? created = null;
        var saved = workspace.WithSnapshot(expected.Request.ExpectedToken, snapshot => canonical.Transaction(view =>
        {
            var proposal = Build(expected.Request, snapshot, view);
            if (proposal.PreviewToken != expected.PreviewToken || proposal.CanonicalRevision != expected.CanonicalRevision ||
                JsonSerializer.Serialize(proposal.Definitions, ContentPackageJson.CreateOptions()) != JsonSerializer.Serialize(expected.Definitions, ContentPackageJson.CreateOptions()))
                throw new LibraryException(409, "Loot batch preview changed. Preview and confirm again.");
            if (!proposal.CanCreate) throw new ArgumentException("Resolve Loot batch diagnostics before creating cards.");
            created = proposal.Definitions;
            var definitions = proposal.Request.Destination == "workspace" ? ContentWorkspace.Overlay(snapshot.Definitions, proposal.Definitions) : null;
            var resultSnapshot = definitions is null ? snapshot : new ContentWorkspaceSnapshot(Guid.NewGuid().ToString("N"), definitions);
            return ((resultSnapshot, definitions), (ContentPackage?)null);
        }));
        return new(created!, saved);
    }

    private static LootBatchPreview Build(LootBatchRequest request, ContentWorkspaceSnapshot snapshot, CanonicalContent.View view)
    {
        if (request.Destination is not ("workspace" or "scenario")) throw new ArgumentException("Choose Workspace or Scenario destination.");
        if (request.Selected.IsDefaultOrEmpty || request.Selected.Any(s => s is null) || request.Selected.Distinct().Count() != request.Selected.Length)
            throw new ArgumentException("Select distinct Equipment or Item rewards.");
        var local = Local(request.Context);
        var effectiveBase = Base(view.Catalog.Definitions, snapshot.Definitions, request.Context);
        // Batch creation requires the complete draft to pass authoritative validation.
        _ = ContentComposition.ComposeScopes(effectiveBase, scenarioLocal: local);
        var relationships = Relationships(effectiveBase, new(), local, snapshot.Definitions, view.Catalog.Definitions);
        List<ContentBrowserDiagnostic> diagnostics = [];
        List<LootCardDefinition> cards = [];
        foreach (var selected in request.Selected)
        {
            var family = selected.Family == ContentFamily.Equipment ? "equipment" : selected.Family == ContentFamily.Item ? "items" : "";
            var reward = relationships.Rewards.SingleOrDefault(r => r.Family == family && r.TypeId == selected.TypeId);
            if (reward is null) { diagnostics.Add(new("Select an existing Equipment or Item reward.", selected.TypeId, "missingReward")); continue; }
            if (!reward.Cards.IsEmpty) diagnostics.Add(new($"'{reward.Name}' already has a Loot Card.", selected.TypeId, "existingCard"));
            if (relationships.Cards.Any(c => c.TypeId == selected.TypeId) || cards.Any(c => c.Id == selected.TypeId))
                diagnostics.Add(new($"Loot Card ID '{selected.TypeId}' is already in use.", selected.TypeId, "identityCollision"));
            if (request.Destination == "workspace" && reward.Origin == "scenario-local")
                diagnostics.Add(new("Scenario-local rewards cannot be referenced from Workspace. Choose Scenario destination.", selected.TypeId, "scopeDependency"));
            cards.Add(new(selected.TypeId, reward.Name) { EquipmentTypeId = selected.Family == ContentFamily.Equipment ? selected.TypeId : null,
                ItemTypeId = selected.Family == ContentFamily.Item ? selected.TypeId : null });
        }
        var definitions = new ContentBundle { LootCards = [.. cards] };
        if (diagnostics.Count == 0)
        {
            if (request.Destination == "workspace")
                _ = ContentComposition.ComposeScopes(view.Catalog.Definitions, ContentWorkspace.Overlay(snapshot.Definitions, definitions));
            else _ = ContentComposition.ComposeScopes(effectiveBase, scenarioLocal: ContentWorkspace.Overlay(local, definitions));
        }
        var preview = new LootBatchPreview(request, view.Revision, definitions, [.. diagnostics], "");
        return preview with { PreviewToken = CanonicalContent.Hash(JsonSerializer.Serialize(preview, ContentPackageJson.CreateOptions())) };
    }
}
