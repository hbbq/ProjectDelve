using System.Text;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record ContentWorkspaceSnapshot(string Token, ContentBundle Definitions);
public sealed record SaveContentWorkspace(ContentBundle Definitions, string ExpectedToken);
public sealed record ClearContentWorkspace(string ExpectedToken);

/// <summary>The one mutable application-local authoring scope. No history or packages.</summary>
public sealed class ContentWorkspace(string directory, Func<ContentBundle> canonicalProvider)
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string root = Path.GetFullPath(directory);
    internal Action? SaveReady { get; set; }
    public ContentWorkspaceSnapshot Snapshot() => Access(snapshot => (snapshot, (ContentBundle?)null));
    public ContentWorkspaceSnapshot Save(ContentBundle definitions, string expectedToken) => Access(snapshot =>
    {
        RequireToken(snapshot, expectedToken);
        var cloned = Clone(definitions);
        _ = ContentComposition.ComposeScopes(canonicalProvider(), cloned);
        return (new ContentWorkspaceSnapshot(Guid.NewGuid().ToString("N"), cloned), (ContentBundle?)cloned);
    });
    public ContentWorkspaceSnapshot Clear(string expectedToken) => Save(new(), expectedToken);
    private sealed record Cached(string Token, ContentBundle Canonical, EffectiveContent Content);
    private Cached? cached;
    public EffectiveContent Resolve()
    {
        var snapshot = Snapshot();
        var canonical = canonicalProvider();
        var previous = Volatile.Read(ref cached);
        if (previous is not null && previous.Token == snapshot.Token && ReferenceEquals(previous.Canonical, canonical)) return previous.Content;
        var content = ContentComposition.ComposeScopes(canonical, snapshot.Definitions);
        Volatile.Write(ref cached, new(snapshot.Token, canonical, content));
        return content;
    }

    // Publication holds the workspace lock, then the canonical lock. Cleanup commits last.
    internal T WithSnapshot<T>(string token, Func<ContentWorkspaceSnapshot, (T Result, ContentBundle? Remaining)> action)
        => Access(snapshot => { RequireToken(snapshot, token); return action(snapshot); });

    private T Access<T>(Func<ContentWorkspaceSnapshot, (T Result, ContentBundle? Write)> action)
    {
        CanonicalContent.CheckNoLinks(root);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "workspace.json");
        CanonicalContent.CheckNoLinks(path);
        var lockPath = Path.Combine(root, ".workspace.lock");
        CanonicalContent.CheckNoLinks(lockPath);
        using var handle = CanonicalContent.FileLock(lockPath);
        var snapshot = File.Exists(path)
            ? JsonSerializer.Deserialize<ContentWorkspaceSnapshot>(ReadLimited(path), Json) ?? throw new InvalidDataException("Workspace document is empty.")
            : new("empty", new());
        if (string.IsNullOrWhiteSpace(snapshot.Token) || snapshot.Definitions is null) throw new InvalidDataException("Invalid Workspace document.");
        var (result, write) = action(snapshot);
        if (write is not null)
        {
            var replacement = result is ContentWorkspaceSnapshot supplied ? supplied : new(Guid.NewGuid().ToString("N"), write);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(replacement, Json) + "\n");
            if (bytes.Length > 8_000_000) throw new ArgumentException("Workspace exceeds the 8 MB limit.");
            SaveReady?.Invoke();
            CanonicalContent.AtomicWrite(path, bytes);
        }
        return result;
    }
    private static string ReadLimited(string path) => new FileInfo(path).Length > 8_000_000
        ? throw new InvalidDataException("Workspace exceeds the 8 MB limit.") : File.ReadAllText(path);
    private static void RequireToken(ContentWorkspaceSnapshot snapshot, string token)
    {
        if (token != snapshot.Token) throw new LibraryException(409, "Workspace changed. Reload it before saving or promoting.");
    }
    internal static ContentBundle Clone(ContentBundle definitions) => ContentPackageJson.FromJson(ContentPackageJson.ToJson(
        new ContentPackage { Id = "workspace", Definitions = definitions ?? throw new ArgumentException("Workspace definitions are required.") })).Definitions;
    internal static IEnumerable<ContentIdentity> Identities(ContentBundle definitions) => ScenarioContentReferences.Identities(definitions)
        .Concat(definitions.DefaultWorldDeck.IsEmpty ? [] : new[] { new ContentIdentity(ContentFamily.WorldDeck, "default-world-deck") });

    internal static ContentBundle Overlay(ContentBundle lower, ContentBundle upper)
    {
        var ids = Identities(upper).ToHashSet();
        var kept = ScenarioContentReferences.Filter(lower, id => !ids.Contains(id));
        return new()
        {
            Units = [.. kept.Units, .. upper.Units], Equipment = [.. kept.Equipment, .. upper.Equipment],
            Items = [.. kept.Items, .. upper.Items], LootCards = [.. kept.LootCards, .. upper.LootCards],
            LootPresets = [.. kept.LootPresets, .. upper.LootPresets], WorldCards = [.. kept.WorldCards, .. upper.WorldCards],
            Terrain = [.. kept.Terrain, .. upper.Terrain], Assemblies = [.. kept.Assemblies, .. upper.Assemblies],
            FixedComponents = [.. kept.FixedComponents, .. upper.FixedComponents], Resources = [.. kept.Resources, .. upper.Resources],
            DefaultWorldDeck = [.. kept.DefaultWorldDeck, .. upper.DefaultWorldDeck]
        };
    }
}
