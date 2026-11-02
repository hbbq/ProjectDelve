using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectDelve.Engine;

// Provider provenance, outside gameplay definitions. Only an exact published payload
// can coexist with its retained authoring snapshot; this is not a general override.
public sealed record CanonicalPublicationEntry(ContentIdentity Identity, string PayloadDigest,
    string SourceKind, string SourceDigest, PinnedContentOrigin? LibraryOrigin = null);

public static class CanonicalPublications
{
    public static string PayloadDigest(ContentBundle bundle, ContentIdentity identity) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ScenarioContentReferences.Filter(bundle, id => id == identity), ContentPackageJson.CreateOptions()))));

    public static bool Matches(ContentBundle canonical, ContentBundle snapshot, ContentIdentity identity) =>
        canonical.Publications.Any(p => p.Identity == identity && p.PayloadDigest == PayloadDigest(snapshot, identity));

    public static ContentBundle ForLocal(ContentBundle canonical, ScenarioDefinition scenario)
        => ForLocal(canonical, ContentBundle.Local(scenario));

    public static ContentBundle ForLocal(ContentBundle canonical, ContentBundle local)
    {
        var retained = ScenarioContentReferences.Identities(local).Where(id => Matches(canonical, local, id)).ToHashSet();
        return ScenarioContentReferences.Filter(canonical, id => !retained.Contains(id));
    }
}
