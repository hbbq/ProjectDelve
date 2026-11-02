using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Scenario/provider metadata, never a property of a gameplay definition or runtime identity.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PinnedContentOrigin(ContentIdentity Identity, string LibraryId, string PackageId,
    long Revision, string Digest, bool Canonical = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioContentReference(int FormatVersion, string LibraryId, string PackageId, long Revision,
    string RevisionDigest, ImmutableArray<ContentIdentity> Selected, ImmutableArray<ContentIdentity> Closure,
    ImmutableArray<PinnedContentOrigin> Origins, ContentBundle Snapshot, string SnapshotDigest);

public static class ScenarioContentReferences
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    public static string Digest(ScenarioContentReference reference) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reference with { SnapshotDigest = "" }, Json))));
    public static ImmutableArray<ContentIdentity> Identities(ContentBundle bundle) => [.. bundle.Definitions().Select(d => d.Identity)
        .Concat(bundle.Resources.Select(r => new ContentIdentity(ContentFamily.Resource, r.Id)))
        .OrderBy(id => id.Family).ThenBy(id => id.TypeId, StringComparer.Ordinal)];

    public static EffectiveContent Validate(ScenarioContentReference reference, string path = "contentReferences")
    {
        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        void Invalid(string field, string message) => throw Error(reference, path + "." + field, "invalidReference", message);
        if (reference is null) throw new ArgumentException("A pinned content reference is required.");
        if (reference.FormatVersion != 1) Invalid("formatVersion", "Unsupported pinned content format; expected 1.");
        if (string.IsNullOrWhiteSpace(reference.LibraryId) || string.IsNullOrWhiteSpace(reference.PackageId) || reference.Revision < 1 ||
            !Hash(reference.RevisionDigest)) Invalid("revision", "A library/package identity, positive revision and revision digest are required.");
        if (reference.Snapshot is null || reference.Selected.IsDefaultOrEmpty || reference.Closure.IsDefault || reference.Origins.IsDefault ||
            reference.Selected.Any(id => id is null || !Enum.IsDefined(id.Family) || string.IsNullOrWhiteSpace(id.TypeId)) ||
            reference.Origins.Any(o => o?.Identity is null || string.IsNullOrWhiteSpace(o.PackageId) || string.IsNullOrWhiteSpace(o.LibraryId) ||
                !Hash(o.Digest) || (o.Canonical ? o.Revision != 0 : o.Revision < 1)))
            Invalid("snapshot", "A typed snapshot, selections, dependency closure and source identities are required.");
        try
        {
            if (reference.Snapshot.Resources.IsDefault || reference.Snapshot.DefaultWorldDeck.IsDefault) throw new ArgumentException("Snapshot collections must be arrays.");
            foreach (var definition in reference.Snapshot.Definitions()) _ = definition.Identity;
        }
        catch (ArgumentException error) { Invalid("snapshot", error.Message); }
        if (reference.SnapshotDigest != Digest(reference)) Invalid("snapshotDigest", "The embedded content digest does not match its payload.");
        var source = new ContentSource($"library:{reference.PackageId}@{reference.Revision}", reference.PackageId, path + ".snapshot");
        var content = ContentComposition.Compose([reference.Snapshot with { Source = source }]);
        var identities = Identities(reference.Snapshot);
        if (identities.Distinct().Count() != identities.Length || reference.Origins.Length != identities.Length || reference.Origins.Select(o => o.Identity).Distinct().Count() != identities.Length ||
            !identities.ToHashSet().SetEquals(reference.Origins.Select(o => o.Identity))) Invalid("origins", "Each embedded definition requires exactly one provenance identity.");
        foreach (var selected in reference.Selected)
        {
            var owner = reference.Origins.SingleOrDefault(o => o.Identity == selected);
            if (owner is null || owner.Canonical || owner.PackageId != reference.PackageId || owner.Revision != reference.Revision || owner.Digest != reference.RevisionDigest)
                Invalid("selected", "Selections must belong to the pinned package revision.");
        }
        var closure = reference.Selected.Concat(content.Dependencies.Closure(reference.Selected)).Distinct().OrderBy(id => id.Family).ThenBy(id => id.TypeId, StringComparer.Ordinal).ToArray();
        if (reference.Selected.Distinct().Count() != reference.Selected.Length || !closure.SequenceEqual(reference.Closure) ||
            identities.Any(id => !closure.Contains(id))) Invalid("closure", "The embedded definitions must be exactly the selected typed dependency closure.");
        return content;
    }

    public static EffectiveContent Compose(ContentBundle canonical, IReadOnlyList<ScenarioContentReference>? references, ContentBundle? local = null)
    {
        if (references is null || references.Count == 0) return ContentComposition.Compose([canonical], local);
        Dictionary<ContentIdentity, (object Value, PinnedContentOrigin Origin, ContentSource Source)> selected = [];
        Dictionary<string, (long Revision, string Digest)> packages = new(StringComparer.Ordinal);
        void Package(string id, long revision, string digest, ScenarioContentReference reference, string path)
        {
            if (packages.TryGetValue(id, out var old) && old != (revision, digest))
                throw Error(reference, path, "incompatibleRevision", $"Package '{id}' selects incompatible immutable revisions ({old.Revision}, {revision}).");
            packages[id] = (revision, digest);
        }
        for (var index = 0; index < references.Count; index++)
        {
            var reference = references[index]; var path = $"contentReferences[{index}]";
            _ = Validate(reference, path);
            Package(reference.PackageId, reference.Revision, reference.RevisionDigest, reference, path);
            foreach (var origin in reference.Origins.Where(o => !o.Canonical)) Package(origin.PackageId, origin.Revision, origin.Digest, reference, path);
            foreach (var definition in reference.Snapshot.Definitions().Concat(reference.Snapshot.Resources.Select(r => new ContentDefinition(r))))
            {
                var origin = reference.Origins.Single(o => o.Identity == definition.Identity);
                var source = new ContentSource($"library:{origin.PackageId}@{origin.Revision}", origin.PackageId, path + ".snapshot");
                if (selected.TryGetValue(definition.Identity, out var old))
                {
                    if ((old.Origin != origin && !(old.Origin.Canonical && origin.Canonical)) || !Equal(old.Value, definition.Value))
                        throw new ContentCompositionException([new(definition.Identity, "", "conflictingDefinition", "Pinned sources contain conflicting definitions.", source, definition.Identity, old.Source)]);
                }
                else selected.Add(definition.Identity, (definition.Value, origin, source));
            }
        }
        // Captured canonical dependencies are an explicit frozen provider selection.
        // Library-owned identities still collide with current canonical identities normally.
        var capturedCanonical = selected.Where(pair => pair.Value.Origin.Canonical).Select(pair => pair.Key).ToHashSet();
        foreach (var reference in references)
        foreach (var identity in Identities(reference.Snapshot))
            if (CanonicalPublications.Matches(canonical, reference.Snapshot, identity)) capturedCanonical.Add(identity);
        var sources = new List<ContentBundle> { Filter(canonical, id => !capturedCanonical.Contains(id)) };
        foreach (var reference in references)
        {
            var rows = selected.Where(pair => reference.Origins.Any(o => o.Identity == pair.Key)).Select(pair => pair.Key).ToHashSet();
            var body = Filter(reference.Snapshot, rows.Contains);
            // Shared dependency identities enter the catalog once, from the same validated source.
            foreach (var identity in rows) selected.Remove(identity);
            sources.Add(body with { Source = new($"library:{reference.PackageId}@{reference.Revision}", reference.PackageId, $"contentReferences[{sources.Count - 1}].snapshot") });
        }
        return ContentComposition.Compose(sources, local);
    }

    public static EffectiveContent ScenarioCatalog(ContentBundle canonical, ScenarioDefinition scenario)
    {
        var provider = Compose(canonical, scenario.ContentReferences).Definitions;
        var authored = LootCompatibility.ResolveLocalIdCollisions(scenario, CanonicalPublications.ForLocal(provider, scenario).LootCards);
        return ContentComposition.Compose([provider], ContentBundle.Local(authored));
    }

    public static ContentBundle Filter(ContentBundle body, Func<ContentIdentity, bool> include)
    {
        ImmutableArray<T> Rows<T>(ImmutableArray<T> rows) => [.. rows.Where(row => include(ContentDefinitions.Identity(row!)))];
        return body with { Units = Rows(body.Units), Equipment = Rows(body.Equipment), Items = Rows(body.Items), LootCards = Rows(body.LootCards),
            LootPresets = Rows(body.LootPresets), WorldCards = Rows(body.WorldCards), Terrain = Rows(body.Terrain), Assemblies = Rows(body.Assemblies),
            FixedComponents = Rows(body.FixedComponents), Resources = Rows(body.Resources),
            DefaultWorldDeck = include(new(ContentFamily.WorldDeck, "default-world-deck")) ? body.DefaultWorldDeck : [] };
    }

    private static bool Hash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool Equal(object a, object b) => JsonSerializer.Serialize(a, a.GetType(), Json) == JsonSerializer.Serialize(b, b.GetType(), Json);
    private static ContentCompositionException Error(ScenarioContentReference reference, string path, string code, string message) => new(
        [new(reference is not null && !reference.Selected.IsDefaultOrEmpty ? reference.Selected[0] : new(ContentFamily.Equipment, ""), path, code,
            $"Library '{reference?.PackageId}' revision {reference?.Revision}: {message}", new(Package: reference?.PackageId, Path: path))]);
}
