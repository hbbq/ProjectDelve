using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record WorkspacePromotionRequest(string ExpectedToken, ImmutableArray<ContentIdentity> Selected);
public sealed record WorkspacePromotionDependency(ContentIdentity Identity, string Status, bool HasWorkspaceOverride);
public sealed record WorkspacePromotionPreview(WorkspacePromotionRequest Request, string CanonicalRevision,
    ImmutableArray<ContentIdentity> RequiredWorkspaceDependencies, ImmutableArray<ContentIdentity> MissingSelections,
    ImmutableArray<ContentIdentity> NewDefinitions, ImmutableArray<ContentIdentity> Replacements,
    ContentBundle Definitions, string PreviewToken)
{
    public bool CanPublish => MissingSelections.IsEmpty;
    public ImmutableArray<WorkspacePromotionDependency> Dependencies { get; init; } = [];
}
public sealed record ConfirmWorkspacePromotion(WorkspacePromotionPreview Preview, bool Confirm);
public sealed record WorkspacePromotionResult(string CanonicalRevision, ContentWorkspaceSnapshot Workspace,
    ImmutableArray<ContentIdentity> Published);

public sealed class WorkspacePromotionService(ContentWorkspace workspace, CanonicalContent canonical)
{
    public WorkspacePromotionPreview Preview(WorkspacePromotionRequest request) => workspace.WithSnapshot(request.ExpectedToken, snapshot =>
        (canonical.Transaction(view => (Build(request, snapshot, view), (ContentPackage?)null)), (ContentBundle?)null));

    public WorkspacePromotionResult Publish(ConfirmWorkspacePromotion command)
    {
        if (command?.Preview is null || !command.Confirm) throw new ArgumentException("Explicit promotion confirmation is required.");
        var expected = command.Preview;
        var published = workspace.WithSnapshot(expected.Request.ExpectedToken, snapshot =>
        {
            var identities = canonical.PublishWorkspace(expected.CanonicalRevision, expected.Definitions, view =>
            {
                var proposal = Build(expected.Request, snapshot, view);
                if (proposal.PreviewToken != expected.PreviewToken)
                    throw new LibraryException(409, "Promotion preview changed. Preview and confirm again.");
                if (!proposal.CanPublish) throw new ArgumentException("Explicitly select every required Workspace dependency before publishing.");
                // Never trust a client-submitted preview payload.
                if (ContentPackageJson.ToJson(new() { Id = "preview", Definitions = expected.Definitions }) !=
                    ContentPackageJson.ToJson(new() { Id = "preview", Definitions = proposal.Definitions }))
                    throw new ArgumentException("Promotion preview payload does not match Workspace content.");
                return proposal.Request.Selected;
            });
            var selected = identities.ToHashSet();
            return (identities, (ContentBundle?)ScenarioContentReferences.Filter(snapshot.Definitions, id => !selected.Contains(id)));
        });
        return new(canonical.Snapshot.Revision, workspace.Snapshot(), published);
    }

    private static WorkspacePromotionPreview Build(WorkspacePromotionRequest request, ContentWorkspaceSnapshot snapshot, CanonicalContent.View view)
    {
        if (request.Selected.IsDefaultOrEmpty || request.Selected.Any(id => id is null) || request.Selected.Distinct().Count() != request.Selected.Length)
            throw new ArgumentException("Select distinct Workspace definitions for promotion.");
        var owned = ContentWorkspace.Identities(snapshot.Definitions).ToHashSet();
        if (request.Selected.Any(id => !owned.Contains(id))) throw new ArgumentException("Every promotion selection must belong to Workspace.");
        var selected = request.Selected.ToHashSet();
        var content = ContentComposition.ComposeScopes(view.Catalog.Definitions, snapshot.Definitions);
        var canonicalIds = ContentWorkspace.Identities(view.Catalog.Definitions).ToHashSet();
        // Traverse the actual publication proposal: an unselected Workspace override
        // does not replace a dependency that Canonical already supplies.
        HashSet<ContentIdentity> dependencies = [], visited = [];
        void Visit(ContentIdentity identity)
        {
            if (!visited.Add(identity)) return;
            var graph = selected.Contains(identity) || !canonicalIds.Contains(identity) ? content.Dependencies : view.Catalog.Dependencies;
            void Follow(IEnumerable<ContentDependency> edges)
            {
                foreach (var edge in edges)
                {
                    if (edge.Kind == ContentDependencyKind.Embedded)
                    {
                        if (edge.EmbeddedDefinition is {} embedded) Follow(ContentDefinitions.Dependencies(embedded));
                        continue;
                    }
                    dependencies.Add(edge.Target);
                    Visit(edge.Target);
                }
            }
            Follow(graph.DirectDependencies(identity));
        }
        foreach (var identity in request.Selected) Visit(identity);
        var orderedDependencies = dependencies.OrderBy(id => id.Family).ThenBy(id => id.TypeId, StringComparer.Ordinal).ToImmutableArray();
        var required = orderedDependencies.Where(owned.Contains).Where(id => !selected.Contains(id) && !canonicalIds.Contains(id)).ToImmutableArray();
        var definitions = ScenarioContentReferences.Filter(snapshot.Definitions, selected.Contains);
        var additional = request.Selected.Where(id => !canonicalIds.Contains(id)).ToImmutableArray();
        var replacements = request.Selected.Where(canonicalIds.Contains).ToImmutableArray();
        // This validates all existing canonical consumers after the complete selected batch.
        if (required.IsEmpty) _ = ContentComposition.ComposeScopes(view.Catalog.Definitions, definitions);
        var proposal = new WorkspacePromotionPreview(request, view.Revision, required, required, additional, replacements, definitions, "")
        {
            Dependencies = [.. orderedDependencies.Select(id => new WorkspacePromotionDependency(id,
                selected.Contains(id) ? "selected" : canonicalIds.Contains(id) ? "canonical" : "workspace-required",
                owned.Contains(id) && canonicalIds.Contains(id)))]
        };
        return proposal with { PreviewToken = CanonicalContent.Hash(JsonSerializer.Serialize(proposal, ContentPackageJson.CreateOptions())) };
    }
}
