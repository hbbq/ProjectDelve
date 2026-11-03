using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Provider changes affect authoring and new-game creation; running games retain snapshots.
public sealed class CanonicalContent
{
    public const string WorkspacePackageId = "canonical-workspace";
    private sealed record Replacement(ContentIdentity Identity, string OwnerPackageId);
    private sealed record WorkspacePublication(ContentPackage Package, ImmutableArray<Replacement> Replacements);
    public sealed record View(IReadOnlyList<ContentPackage> Packages, EffectiveContent Catalog, string Revision);
    private readonly string directory;
    private View current;
    private readonly object gate = new();
    internal Action? PublicationReady { get; set; }
    public View Snapshot => Volatile.Read(ref current);
    public IReadOnlyList<ContentPackage> Packages => Snapshot.Packages;
    public EffectiveContent Catalog => Snapshot.Catalog;
    public ContentBundle Bundle => Catalog.Definitions;
    public CanonicalContent(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        CheckNoLinks(this.directory);
        current = Load();
    }
    private View Load()
    {
        void CheckTree(string path)
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                CheckNoLinks(child);
                if (Directory.Exists(child)) CheckTree(child);
            }
        }
        Directory.CreateDirectory(directory);
        CheckTree(directory);
        var documents = ContentPackageCatalog.ReadDirectory(directory).ToList();
        foreach (var document in documents) CheckNoLinks(document.Document!);
        var csharp = ContentBundle.Canonical();
        var publication = ReadPublication();
        if (publication is not null)
        {
            var identities = ContentWorkspace.Identities(publication.Package.Definitions).ToHashSet();
            if (publication.Package.Id != WorkspacePackageId || publication.Replacements.IsDefault ||
                publication.Replacements.Select(r => r.Identity).Distinct().Count() != publication.Replacements.Length ||
                publication.Replacements.Any(r => !identities.Contains(r.Identity)))
                throw new InvalidDataException("Invalid Workspace publication ownership manifest.");
            foreach (var replacement in publication.Replacements)
            {
                var owner = replacement.OwnerPackageId == ContentPackageCatalog.CSharpPackageId ? csharp :
                    documents.SingleOrDefault(d => d.Package.Id == replacement.OwnerPackageId)?.Package.Definitions;
                if (owner is null || !ContentWorkspace.Identities(owner).Contains(replacement.Identity))
                    throw new InvalidDataException("Workspace publication replacement owner is missing.");
            }
            var replaced = publication.Replacements.Select(r => r.Identity).ToHashSet();
            csharp = ScenarioContentReferences.Filter(csharp, id => !replaced.Contains(id));
            documents = documents.Select(d => d with { Package = d.Package with
            {
                Definitions = ScenarioContentReferences.Filter(d.Package.Definitions, id => !publication.Replacements.Any(r => r.Identity == id && r.OwnerPackageId == d.Package.Id)),
                ReplacesCSharp = [.. d.Package.ReplacesCSharp.Where(id => !replaced.Contains(id))],
                Publications = d.Package.Publications is {} entries ? [.. entries.Where(entry => !replaced.Contains(entry.Identity))] : null,
                Dependencies = [.. d.Package.Dependencies.Append(WorkspacePackageId).Distinct(StringComparer.Ordinal)]
            }}).ToList();
            documents.Add(new(publication.Package, PublicationPath));
        }
        var catalog = ContentPackageCatalog.Compose(documents, csharp);
        // A View exposes resolved provider ownership. Transfer claims are validated above;
        // downstream consumers must not apply them again to the already filtered provider.
        var transferred = documents.SelectMany(d => d.Package.ReplacesCSharp).ToHashSet();
        csharp = ScenarioContentReferences.Filter(csharp, id => !transferred.Contains(id));
        ContentPackage[] packages = [new() { Id = ContentPackageCatalog.CSharpPackageId, Definitions = csharp },
            .. documents.Select(d => d.Package with { ReplacesCSharp = [] })];
        var digest = Hash(string.Concat(packages.OrderBy(p => p.Id, StringComparer.Ordinal).Select(ContentPackageJson.ToJson)));
        return new(packages, catalog, digest);
    }
    private string PublicationPath => Path.Combine(directory, "workspace-publication.bundle");
    private WorkspacePublication? ReadPublication() => File.Exists(PublicationPath)
        ? JsonSerializer.Deserialize<WorkspacePublication>(File.ReadAllText(PublicationPath), ContentPackageJson.CreateOptions())
            ?? throw new InvalidDataException("Workspace publication is empty.") : null;

    internal T PublishWorkspace<T>(string expectedRevision, ContentBundle selected, Func<View, T> validate)
    {
        lock (gate)
        {
            CheckNoLinks(directory);
            var lockPath = Path.Combine(directory, ".publication.lock"); CheckNoLinks(lockPath);
            using var handle = FileLock(lockPath);
            var before = Load();
            if (before.Revision != expectedRevision) throw new LibraryException(409, "Canonical content changed. Preview promotion again.");
            var result = validate(before);
            var previous = ReadPublication();
            var selectedIds = ContentWorkspace.Identities(selected).ToHashSet();
            var previousBody = previous?.Package.Definitions ?? new ContentBundle();
            // Explicit ownership allows replacement without adding duplicate provider definitions.
            var merged = ContentWorkspace.Overlay(previousBody, selected);
            var replacements = (previous?.Replacements ?? []).ToList();
            foreach (var id in selectedIds)
            {
                if (replacements.Any(r => r.Identity == id)) continue;
                var owner = before.Packages.SingleOrDefault(p => p.Id != WorkspacePackageId && ContentWorkspace.Identities(p.Definitions).Contains(id));
                if (owner is not null) replacements.Add(new(id, owner.Id));
            }
            var package = new ContentPackage { Id = WorkspacePackageId, Definitions = merged,
                Dependencies = [.. before.Packages.Where(p => p.Id != WorkspacePackageId).Select(p => p.Id)] };
            var candidate = new WorkspacePublication(package, [.. replacements]);
            var replaced = replacements.Select(r => r.Identity).ToHashSet();
            var packages = before.Packages.Where(p => p.Id != WorkspacePackageId).Select(p => p with
            {
                Definitions = ScenarioContentReferences.Filter(p.Definitions, id => !replaced.Contains(id)),
                ReplacesCSharp = [],
                Publications = p.Publications is {} entries ? [.. entries.Where(entry => !replaced.Contains(entry.Identity))] : null,
                Dependencies = [.. p.Dependencies.Append(WorkspacePackageId).Distinct(StringComparer.Ordinal)]
            }).Append(package);
            _ = ComposePackages(packages);
            PublicationReady?.Invoke();
            AtomicWrite(PublicationPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, ContentPackageJson.CreateOptions()) + "\n"));
            current = Load();
            return result;
        }
    }
    internal T Transaction<T>(Func<View, (T Result, ContentPackage? Publish)> operation)
    {
        lock (gate)
        {
            CheckNoLinks(directory);
            var lockPath = Path.Combine(directory, ".publication.lock"); CheckNoLinks(lockPath);
            using var handle = FileLock(lockPath);
            var before = Load();
            var (result, package) = operation(before);
            if (package is not null)
            {
                // A whole publication is one new package/file. Existing files are never replaced.
                _ = ComposePackages(before.Packages.Append(package));
                var path = Path.Combine(directory, "publication-" + Guid.NewGuid().ToString("N") + ".json");
                var temporary = path + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(Encoding.UTF8.GetBytes(ContentPackageJson.ToJson(package)));
                        stream.Flush(true);
                    }
                    PublicationReady?.Invoke();
                    File.Move(temporary, path); // atomic visibility: reader sees none or all definitions
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                current = Load();
            }
            else current = before;
            return result;
        }
    }
    private static EffectiveContent ComposePackages(IEnumerable<ContentPackage> supplied)
    {
        var packages = supplied.ToArray();
        var csharp = packages.Single(p => p.Id == ContentPackageCatalog.CSharpPackageId).Definitions;
        return ContentPackageCatalog.Compose(packages.Where(p => p.Id != ContentPackageCatalog.CSharpPackageId)
            .Select(p => new ContentPackageDocument(p, p.Id)), csharp);
    }
    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static FileStream FileLock(string path)
    {
        for (var attempt = 0; ; attempt++)
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
    }
    internal static void AtomicWrite(string path, byte[] bytes)
    {
        CheckNoLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void CheckNoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Canonical content must not contain symbolic links or junctions.");
    }
}
