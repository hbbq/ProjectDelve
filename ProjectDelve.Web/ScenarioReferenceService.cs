using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record PreviewScenarioReference(ScenarioDefinition Scenario, string PackageId, long Revision,
    ImmutableArray<ContentIdentity> Selected, string? ExpectedReferenceDigest = null);
public sealed record ScenarioReferenceChange(ContentIdentity Identity, string Change, JsonElement? Before, JsonElement? After);
public sealed record ScenarioReferencePreview(ScenarioContentReference Reference, IReadOnlyList<ScenarioContentReference> References,
    IReadOnlyList<ScenarioReferenceChange> Changes, ContentBrowserResponse Catalog);

public sealed class ScenarioReferenceService(ContentLibrary library, CanonicalContent canonical)
{
    public ScenarioReferencePreview Preview(PreviewScenarioReference request)
    {
        if (request?.Scenario is null || request.Revision < 1) throw new ArgumentException("A scenario and exact positive library revision are required.");
        var applicationId = library.Catalog(true).LibraryId;
        var existing = request.Scenario.ContentReferences?.SingleOrDefault(r => request.ExpectedReferenceDigest is not null
            ? r.SnapshotDigest == request.ExpectedReferenceDigest : r.PackageId == request.PackageId);
        if (request.ExpectedReferenceDigest is not null && (existing is null || existing.PackageId != request.PackageId))
            throw new LibraryException(409, "The scenario reference changed. Reopen its current preview before applying an update.");
        if (existing is not null && existing.LibraryId != applicationId)
            throw new LibraryException(404, "The original Content Library is unavailable. The embedded scenario remains portable and playable.");
        if (existing is not null && request.ExpectedReferenceDigest is null && existing.Revision != request.Revision)
            throw new LibraryException(409, "This package is already pinned at another revision. Use Update Reference explicitly.");
        var inspected = library.Inspect(request.PackageId, request.Revision);
        var selections = request.Selected.IsDefaultOrEmpty ? existing?.Selected ?? [] : request.Selected;
        if (existing is not null && request.ExpectedReferenceDigest is null) selections = [.. existing.Selected.Concat(selections).Distinct()];
        if (selections.IsDefaultOrEmpty || selections.Any(id => id is null || id.Family is not (ContentFamily.Equipment or ContentFamily.LootCard)) ||
            selections.Any(id => !ScenarioContentReferences.Identities(inspected.Root.Package.Definitions).Contains(id)))
            throw new ArgumentException("Select Equipment or Loot Cards owned by the specified library package revision.");
        selections = [.. selections.Distinct().OrderBy(id => id.Family).ThenBy(id => id.TypeId, StringComparer.Ordinal)];
        var snapshot = ContentPackageExport.Selected("pinned-snapshot", inspected.Content, selections).Definitions;
        var origins = ScenarioContentReferences.Identities(snapshot).Select(identity =>
        {
            var owner = inspected.Revisions.SingleOrDefault(r => ScenarioContentReferences.Identities(r.Package.Definitions).Contains(identity));
            if (owner is not null) return new PinnedContentOrigin(identity, owner.LibraryId, owner.Package.Id, owner.Revision, owner.Digest);
            var provider = inspected.Revisions.SelectMany(r => r.CanonicalPackages).First(p => ScenarioContentReferences.Identities(p.Definitions).Contains(identity));
            return new PinnedContentOrigin(identity, "canonical", provider.Id, 0,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ContentPackageJson.ToJson(provider)))), true);
        }).ToImmutableArray();
        var closure = selections.Concat(inspected.Content.Dependencies.Closure(selections)).Distinct().OrderBy(id => id.Family).ThenBy(id => id.TypeId, StringComparer.Ordinal).ToImmutableArray();
        var reference = new ScenarioContentReference(1, applicationId, request.PackageId, request.Revision, inspected.Root.Digest,
            selections, closure, origins, snapshot, "");
        reference = reference with { SnapshotDigest = ScenarioContentReferences.Digest(reference) };
        var references = (request.Scenario.ContentReferences ?? []).Where(r => r != existing).Append(reference).ToList();
        var scenario = request.Scenario with { ContentReferences = references };
        var effective = ScenarioContentReferences.ScenarioCatalog(canonical.Bundle, scenario).Definitions;
        var catalog = ContentBrowserCatalog.Definitions(effective.Equipment, effective.Items, effective.LootCards, effective.Units, canonical: canonical.Bundle);
        catalog = ContentBrowserCatalog.Pinned(catalog, references, scenario.UnitTypes?.Select(u => u.Id).ToHashSet());
        var oldIds = existing is null ? [] : ScenarioContentReferences.Identities(existing.Snapshot);
        var changes = oldIds.Union(ScenarioContentReferences.Identities(snapshot)).OrderBy(id => id.Family).ThenBy(id => id.TypeId, StringComparer.Ordinal)
            .Select(id =>
            {
                JsonElement? Payload(ContentBundle? body) => body is null || !ScenarioContentReferences.Identities(body).Contains(id) ? null
                    : JsonSerializer.SerializeToElement(ScenarioContentReferences.Filter(body, identity => identity == id), ContentPackageJson.CreateOptions());
                var before = Payload(existing?.Snapshot); var after = Payload(snapshot);
                return new ScenarioReferenceChange(id, before is null ? "Added" : after is null ? "Removed"
                    : before.Value.GetRawText() == after.Value.GetRawText() ? "Unchanged" : "Changed", before, after);
            }).ToArray();
        return new(reference, references, changes, catalog);
    }
}
