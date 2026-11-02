using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record PromotionSource(string Kind, string? PackageId = null, long? Revision = null, ScenarioDefinition? Scenario = null);
public sealed record PromotionRequest(PromotionSource Source, string DestinationPackageId, ImmutableArray<ContentIdentity> Selected);
public sealed record PromotionPreview(PromotionRequest Request, string SourceDigest, long? SourceHead, string DestinationRevision,
    ImmutableArray<ContentIdentity> Closure, ImmutableArray<ContentIdentity> ReusedCanonical,
    ImmutableArray<ContentIdentity> Additional, ContentPackage Package, string PreviewDigest);
public sealed record PublishPromotion(PromotionPreview Preview, bool Confirm);
public sealed record PromotionResult(string PackageId, string CanonicalRevision, ImmutableArray<ContentIdentity> Published);

// All publication I/O lives here and in CanonicalContent. Engine remains storage independent.
public sealed class ContentPromotionService(ContentLibrary library, CanonicalContent canonical)
{
    public PromotionPreview Preview(PromotionRequest request) => Source(request, null, (content, owned, digest, head, origins) =>
        canonical.Transaction(view => (Build(request, content, owned, digest, head, origins, view), (ContentPackage?)null)));

    public PromotionResult Publish(PublishPromotion command)
    {
        if (!command.Confirm || command.Preview is null) throw new ArgumentException("Explicit publication confirmation is required.");
        var expected = command.Preview;
        var published = Source(expected.Request, expected.SourceHead, (content, owned, digest, head, origins) => canonical.Transaction(view =>
        {
            if (digest != expected.SourceDigest || view.Revision != expected.DestinationRevision)
                throw new LibraryException(409, "The source or canonical destination changed. Prepare and confirm a new promotion preview.");
            var preview = Build(expected.Request, content, owned, digest, head, origins, view);
            if (preview.PreviewDigest != expected.PreviewDigest)
                throw new LibraryException(409, "The promotion preview changed. Prepare and confirm a new preview.");
            return (preview.Package, (ContentPackage?)preview.Package);
        }));
        return new(published.Id, canonical.Snapshot.Revision, ScenarioContentReferences.Identities(published.Definitions));
    }

    private T Source<T>(PromotionRequest request, long? expectedHead,
        Func<EffectiveContent, ImmutableArray<ContentIdentity>, string, long?, ImmutableArray<PinnedContentOrigin>, T> action)
    {
        if (request?.Source is null || request.Selected.IsDefaultOrEmpty || request.Selected.Any(id => id is null) ||
            request.Selected.Distinct().Count() != request.Selected.Length)
            throw new ArgumentException("Select distinct typed definitions from an explicit promotion source.");
        if (request.Source.Kind == "library")
        {
            if (request.Source.PackageId is null || request.Source.Revision is not > 0) throw new ArgumentException("Choose an immutable library revision.");
            var current = expectedHead ?? library.Read(request.Source.PackageId).Revision;
            return library.WithRevision(request.Source.PackageId, request.Source.Revision.Value, current, inspection =>
            {
                var origins = inspection.Revisions.SelectMany(r => ScenarioContentReferences.Identities(r.Package.Definitions)
                    .Select(id => new PinnedContentOrigin(id, r.LibraryId, r.Package.Id, r.Revision, r.Digest))).ToImmutableArray();
                return action(inspection.Content, ScenarioContentReferences.Identities(inspection.Root.Package.Definitions), inspection.Root.Digest, current, origins);
            });
        }
        var scenario = request.Source.Scenario ?? throw new ArgumentException("A scenario snapshot is required.");
        var digest = CanonicalContent.Hash(ScenarioDefinitionJson.ToJson(scenario));
        var content = ScenarioContentReferences.ScenarioCatalog(canonical.Bundle, scenario);
        ImmutableArray<ContentIdentity> owned;
        ImmutableArray<PinnedContentOrigin> sourceOrigins = [.. (scenario.ContentReferences ?? []).SelectMany(r => r.Origins).Distinct()];
        if (request.Source.Kind == "scenario-pinned") owned = [.. (scenario.ContentReferences ?? []).SelectMany(r => ScenarioContentReferences.Identities(r.Snapshot)).Distinct()];
        else if (request.Source.Kind == "scenario-local") owned = ScenarioContentReferences.Identities(new ContentBundle
        {
            Units = [.. scenario.UnitTypes ?? []], Equipment = [.. scenario.EquipmentTypes ?? []], Items = [.. scenario.ItemTypes ?? []],
            LootCards = [.. scenario.LootCardDefinitions ?? []], WorldCards = [.. scenario.WorldCardDefinitions ?? []],
            Terrain = [.. scenario.TerrainTypes ?? []], Assemblies = [.. scenario.TerrainAssemblies ?? []], FixedComponents = [.. scenario.FixedComponentTypes ?? []]
        });
        else throw new ArgumentException("Promotion source must be library, scenario-local or scenario-pinned.");
        return action(content, owned, digest, null, sourceOrigins);
    }

    private static PromotionPreview Build(PromotionRequest request, EffectiveContent content, ImmutableArray<ContentIdentity> owned,
        string sourceDigest, long? head, ImmutableArray<PinnedContentOrigin> origins, CanonicalContent.View destination)
    {
        if (request.Selected.Any(id => !owned.Contains(id))) throw new ArgumentException("Selections must belong to the specified source scope.");
        if (destination.Packages.Any(p => p.Id == request.DestinationPackageId))
            throw new LibraryException(409, "The destination package already exists. Promotion creates a new canonical package; replacement is a separate operation.");
        var closure = request.Selected.Concat(content.Dependencies.Closure(request.Selected)).Distinct()
            .OrderBy(id => id.Family).ThenBy(id => id.TypeId, StringComparer.Ordinal).ToImmutableArray();
        var canonicalIds = ScenarioContentReferences.Identities(destination.Catalog.Definitions).ToHashSet();
        var reused = ImmutableArray.CreateBuilder<ContentIdentity>();
        foreach (var id in closure.Where(canonicalIds.Contains))
        {
            if (request.Selected.Contains(id) || CanonicalPublications.PayloadDigest(content.Definitions, id) != CanonicalPublications.PayloadDigest(destination.Catalog.Definitions, id))
                throw new ContentCompositionException([new(id, "selected", "conflictingDefinition", "This identity already belongs to canonical content. Promotion never replaces canonical definitions.", new(Package: request.Source.PackageId), id)]);
            reused.Add(id);
        }
        var publish = closure.Except(reused).ToImmutableArray();
        if (publish.IsEmpty) throw new ArgumentException("There are no new definitions to publish.");
        var dependencies = destination.Packages.Where(p => ScenarioContentReferences.Identities(p.Definitions).Any(reused.Contains))
            .Select(p => p.Id).Order(StringComparer.Ordinal).ToArray();
        var package = ContentPackageExport.Selected(request.DestinationPackageId, content, publish, false, dependencies);
        package = package with { Publications = [.. publish.Select(id => new CanonicalPublicationEntry(id,
            CanonicalPublications.PayloadDigest(package.Definitions, id), request.Source.Kind, sourceDigest, origins.FirstOrDefault(o => o.Identity == id)))] };
        _ = ContentPackageCatalog.Compose(destination.Packages.Append(package).Select(p => new ContentPackageDocument(p, "canonical:" + p.Id)));
        var preview = new PromotionPreview(request, sourceDigest, head, destination.Revision, closure, reused.ToImmutable(),
            [.. publish.Except(request.Selected)], package, "");
        return preview with { PreviewDigest = CanonicalContent.Hash(JsonSerializer.Serialize(preview, ContentPackageJson.CreateOptions())) };
    }
}
