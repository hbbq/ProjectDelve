using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record ContentLibraryBinding(string PackageId, long Revision);
public sealed record SaveContentLibraryPackage(ContentPackage Package, long ExpectedRevision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<ContentLibraryBinding> Bindings = default);
public sealed record ContentLibraryRevision(string LibraryId, long Revision, ContentPackage Package,
    ImmutableArray<ContentLibraryBinding> Bindings, ImmutableArray<ContentPackage> CanonicalPackages, string Digest);
public sealed record ContentLibrarySummary(string PackageId, long CurrentRevision, string Digest,
    string? Title, string? Description, bool Archived, ImmutableArray<ContentLibraryBinding> Bindings);
public sealed record ContentLibraryCatalog(string LibraryId, ImmutableArray<ContentLibrarySummary> Packages);
public sealed record ContentLibraryArchive(int FormatVersion, ContentLibraryBinding Root,
    ImmutableArray<ContentLibraryRevision> Revisions);
public sealed record ContentLibraryInspection(ContentLibraryRevision Root, EffectiveContent Content,
    ImmutableArray<ContentLibraryRevision> Revisions);

public static class ContentLibraryJson
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    public static string ToJson(ContentLibraryArchive archive) => Serialize(archive);
    public static ContentLibraryArchive FromJson(string json) => JsonSerializer.Deserialize<ContentLibraryArchive>(json, Json)
        ?? throw new JsonException("A Content Library export is required.");
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
}

/// <summary>One application-local, scenario-independent store. Every operation reloads under
/// an exclusive file lock. A single atomic replacement commits revisions and heads together.</summary>
public sealed class ContentLibrary
{
    private static readonly JsonSerializerOptions Json = ContentPackageJson.CreateOptions();
    private readonly string root;
    private readonly ImmutableArray<ContentPackage> canonical;
    private readonly Func<IEnumerable<ContentPackage>>? canonicalProvider;
    private IEnumerable<ContentPackage> Canonical => canonicalProvider?.Invoke() ?? canonical;
    private readonly long maximumBytes;
    private sealed record Head(string PackageId, long Revision, bool Archived = false);
    private sealed class Document
    {
        [JsonRequired] public int FormatVersion { get; set; } = 1;
        [JsonRequired] public string LibraryId { get; set; } = Guid.NewGuid().ToString("N");
        [JsonRequired] public List<Head> Packages { get; set; } = [];
        [JsonRequired] public List<ContentLibraryRevision> Revisions { get; set; } = [];
    }

    public ContentLibrary(string directory, IEnumerable<ContentPackage>? canonicalPackages = null, long maximumBytes = 64_000_000,
        Func<IEnumerable<ContentPackage>>? canonicalProvider = null)
    {
        root = Path.GetFullPath(directory);
        canonical = [.. (canonicalPackages ?? [new ContentPackage { Id = ContentPackageCatalog.CSharpPackageId, Definitions = ContentBundle.Canonical() }])
            .Select(ClonePackage).OrderBy(p => p.Id, StringComparer.Ordinal)];
        _ = ContentPackageCatalog.Compose(canonical.Select(p => new ContentPackageDocument(p, $"canonical:{p.Id}")));
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.maximumBytes = maximumBytes;
        this.canonicalProvider = canonicalProvider;
    }

    public ContentLibraryCatalog Catalog(bool includeArchived = false) => Access<ContentLibraryCatalog>(document => new(document.LibraryId,
        [.. document.Packages.Where(h => includeArchived || !h.Archived).OrderBy(h => h.PackageId, StringComparer.Ordinal).Select(head =>
        {
            var revision = Find(document, new(head.PackageId, head.Revision));
            return new ContentLibrarySummary(head.PackageId, head.Revision, revision.Digest, revision.Package.Title,
                revision.Package.Description, head.Archived, revision.Bindings);
        })]));

    public ContentLibraryRevision Read(string id, long? revision = null) => Access(document =>
        CloneRevision(Find(document, new(id, revision ?? Current(document, id).Revision))));

    public ImmutableArray<ContentLibraryRevision> History(string id) => Access<ImmutableArray<ContentLibraryRevision>>(document =>
    {
        _ = Current(document, id);
        return [.. document.Revisions.Where(r => r.Package.Id == id).OrderBy(r => r.Revision).Select(CloneRevision)];
    });

    public ContentLibraryRevision Create(ContentPackage package, ImmutableArray<ContentLibraryBinding> bindings = default)
        => SaveBatch([new(package, 0, bindings)])[0];

    public ContentLibraryRevision Save(string id, SaveContentLibraryPackage request)
    {
        if (request?.Package is null || id != request.Package.Id || request.ExpectedRevision < 1) throw new ArgumentException("Saving requires the same package ID and a positive expectedRevision.");
        return SaveBatch([request])[0];
    }

    public ContentLibraryInspection Inspect(SaveContentLibraryPackage request) => Access(document =>
    {
        var workspace = new Document { LibraryId = document.LibraryId, Packages = [.. document.Packages], Revisions = [.. document.Revisions] };
        var revision = Prepare(workspace, [request])[0];
        return new ContentLibraryInspection(revision, Resolve(workspace, revision), [.. Closure(workspace, revision).Select(CloneRevision)]);
    });

    public ContentLibraryInspection Inspect(string id, long? revision = null) => Access(document =>
    {
        var selected = Find(document, new(id, revision ?? Current(document, id).Revision));
        return new ContentLibraryInspection(CloneRevision(selected), Resolve(document, selected), [.. Closure(document, selected).Select(CloneRevision)]);
    });

    // Hold the source store lock throughout publication, preventing stale-head races.
    internal T WithRevision<T>(string id, long revision, long expectedHead, Func<ContentLibraryInspection, T> action) => Access(document =>
    {
        var head = Current(document, id);
        if (head.Revision != expectedHead) throw Conflict(id, expectedHead, head.Revision, head.Archived);
        var selected = Find(document, new(id, revision));
        return action(new(CloneRevision(selected), Resolve(document, selected), [.. Closure(document, selected).Select(CloneRevision)]));
    });

    public ImmutableArray<ContentLibraryRevision> SaveBatch(ImmutableArray<SaveContentLibraryPackage> requests)
        => Access(document => Prepare(document, requests), write: true);

    private ImmutableArray<ContentLibraryRevision> Prepare(Document document, ImmutableArray<SaveContentLibraryPackage> requests)
    {
        if (requests.IsDefaultOrEmpty || requests.Length > 100 || requests.Any(r => r?.Package is null) ||
            requests.Select(r => r.Package.Id).Distinct(StringComparer.Ordinal).Count() != requests.Length)
            throw new ArgumentException("Supply 1–100 distinct packages in a save transaction.");
        List<ContentLibraryRevision> added = [];
        foreach (var request in requests.OrderBy(r => r.Package.Id, StringComparer.Ordinal))
        {
            var package = ClonePackage(request.Package);
            if (Canonical.Any(p => p.Id == package.Id)) throw new LibraryException(409, $"Package ID '{package.Id}' belongs to canonical content.");
            var head = document.Packages.SingleOrDefault(h => h.PackageId == package.Id);
            var published = Canonical.SelectMany(p => p.Publications ?? []).ToArray();
            foreach (var identity in ScenarioContentReferences.Identities(package.Definitions).Where(id => published.Any(p => p.Identity == id)))
            {
                if (head is null || !ScenarioContentReferences.Identities(Find(document, new(package.Id, head.Revision)).Package.Definitions).Contains(identity) ||
                    !published.Any(p => p.Identity == identity && p.PayloadDigest == CanonicalPublications.PayloadDigest(package.Definitions, identity)))
                    throw new ArgumentException($"{identity.Family} '{identity.TypeId}' has been published to Canonical and is read-only. Create a copy with a new Type ID.");
            }
            if (request.ExpectedRevision != (head?.Revision ?? 0) || head?.Archived == true)
                throw Conflict(package.Id, request.ExpectedRevision, head?.Revision ?? 0, head?.Archived ?? false);
            var bindings = NormalizeBindings(request.Bindings);
            var context = CanonicalContext(package, bindings);
            var next = checked((head?.Revision ?? 0) + 1);
            var revision = Seal(new(document.LibraryId, next, package, bindings, context, ""));
            ValidateRevision(revision);
            added.Add(revision);
            document.Revisions.Add(revision);
            document.Packages.RemoveAll(h => h.PackageId == package.Id);
            document.Packages.Add(new(package.Id, next));
        }
        // All candidate revisions exist before resolution, allowing legal cross-package cycles.
        foreach (var revision in added) _ = Resolve(document, revision);
        return [.. added.Select(CloneRevision)];
    }

    public void Archive(string id, long expectedRevision) => Access(document =>
    {
        var head = Current(document, id);
        if (expectedRevision != head.Revision) throw Conflict(id, expectedRevision, head.Revision, head.Archived);
        document.Packages[document.Packages.IndexOf(head)] = head with { Archived = true };
        return true;
    }, write: true);

    public ContentLibraryArchive Export(string id, long? revision = null) => Access<ContentLibraryArchive>(document =>
    {
        var selected = Find(document, new(id, revision ?? Current(document, id).Revision));
        var records = Closure(document, selected);
        return new(1, new(id, selected.Revision), [.. records.Select(CloneRevision)]);
    });

    public ContentPackage ExportPackage(string id, long? revision = null) => Access(document =>
    {
        var selected = Find(document, new(id, revision ?? Current(document, id).Revision));
        return ContentPackageExport.Selected(id, Resolve(document, selected), Identities(selected.Package.Definitions)) with
            { Title = selected.Package.Title, Description = selected.Package.Description };
    });

    // Plain JSON imports are creates. Archive imports preserve every revision's origin, number and digest.
    public ContentLibraryRevision Import(ContentPackage package, ImmutableArray<ContentLibraryBinding> bindings = default) => Create(package, bindings);
    public ContentLibraryRevision Import(ContentLibraryArchive archive) => Access(document =>
    {
        if (archive.FormatVersion != 1 || archive.Root is null || archive.Revisions.IsDefaultOrEmpty || archive.Revisions.Any(r => r is null))
            throw new ArgumentException("Unsupported or invalid Content Library export.");
        if (document.Packages.Any(h => h.PackageId == archive.Root.PackageId))
            throw new LibraryException(409, $"Package '{archive.Root.PackageId}' already exists; import does not replace packages.");
        foreach (var revision in archive.Revisions) ValidateRevision(revision);
        var records = archive.Revisions.Select(CloneRevision).ToArray();
        if (records.Select(r => new ContentLibraryBinding(r.Package.Id, r.Revision)).Distinct().Count() != records.Length)
            throw new ArgumentException("An export contains duplicate package revisions.");
        foreach (var revision in records)
        {
            ValidateRevision(revision);
            if (Canonical.Any(p => p.Id == revision.Package.Id)) throw new LibraryException(409, $"Package '{revision.Package.Id}' belongs to canonical content.");
            var existing = document.Revisions.SingleOrDefault(r => r.Package.Id == revision.Package.Id && r.Revision == revision.Revision);
            if (existing is not null && existing.Digest != revision.Digest)
                throw new LibraryException(409, $"Package '{revision.Package.Id}' revision {revision.Revision} conflicts with the installed revision.");
            if (existing is null && document.Packages.Any(h => h.PackageId == revision.Package.Id))
                throw new LibraryException(409, $"Package '{revision.Package.Id}' already exists with a different revision history.");
            if (existing is null) document.Revisions.Add(revision);
        }
        var selected = Find(document, archive.Root);
        var reachable = Closure(document, selected).Select(r => new ContentLibraryBinding(r.Package.Id, r.Revision)).ToHashSet();
        if (records.Any(r => !reachable.Contains(new(r.Package.Id, r.Revision)))) throw new ArgumentException("Export contains unrelated revisions.");
        _ = Resolve(document, selected);
        foreach (var group in records.GroupBy(r => r.Package.Id))
            if (!document.Packages.Any(h => h.PackageId == group.Key)) document.Packages.Add(new(group.Key, group.Max(r => r.Revision)));
        return CloneRevision(selected);
    }, write: true);

    private ImmutableArray<ContentPackage> CanonicalContext(ContentPackage package, ImmutableArray<ContentLibraryBinding> bindings)
    {
        var declared = package.Dependencies.ToHashSet(StringComparer.Ordinal);
        if (bindings.Any(b => !declared.Contains(b.PackageId) || Canonical.Any(p => p.Id == b.PackageId)))
            throw new ArgumentException("Bindings must identify declared library dependencies, not canonical packages.");
        Dictionary<string, ContentPackage> selected = new(StringComparer.Ordinal);
        void Include(string id)
        {
            if (selected.ContainsKey(id)) return;
            var provider = Canonical.SingleOrDefault(p => p.Id == id);
            if (provider is null) throw new ArgumentException($"dependencies: package '{id}' needs an exact library revision binding.");
            selected.Add(id, provider);
            foreach (var child in provider.Dependencies) Include(child);
        }
        foreach (var dependency in package.Dependencies)
            if (!bindings.Any(b => b.PackageId == dependency)) Include(dependency);
        return [.. selected.Values.OrderBy(p => p.Id, StringComparer.Ordinal)];
    }

    private static ImmutableArray<ContentLibraryBinding> NormalizeBindings(ImmutableArray<ContentLibraryBinding> bindings)
    {
        if (bindings.IsDefault) return [];
        if (bindings.Any(b => b is null || string.IsNullOrEmpty(b.PackageId) || b.Revision < 1) ||
            bindings.Select(b => b.PackageId).Distinct(StringComparer.Ordinal).Count() != bindings.Length)
            throw new ArgumentException("Bindings require distinct package IDs and positive immutable revisions.");
        return [.. bindings.OrderBy(b => b.PackageId, StringComparer.Ordinal)];
    }

    private static ImmutableArray<ContentLibraryRevision> Closure(Document document, ContentLibraryRevision rootRevision)
    {
        Dictionary<string, ContentLibraryRevision> selected = new(StringComparer.Ordinal);
        var pending = new Stack<ContentLibraryRevision>(); pending.Push(rootRevision);
        while (pending.TryPop(out var revision))
        {
            if (selected.TryGetValue(revision.Package.Id, out var previous))
            {
                if (previous.Revision != revision.Revision) throw new LibraryException(409,
                    $"Dependency closure selects incompatible revisions of '{revision.Package.Id}': {previous.Revision} and {revision.Revision}.");
                continue;
            }
            selected.Add(revision.Package.Id, revision);
            foreach (var binding in revision.Bindings) pending.Push(Find(document, binding));
        }
        return [.. selected.Values.OrderBy(r => r.Package.Id, StringComparer.Ordinal)];
    }

    private static EffectiveContent Resolve(Document document, ContentLibraryRevision rootRevision)
    {
        var revisions = Closure(document, rootRevision);
        Dictionary<string, ContentPackage> context = new(StringComparer.Ordinal);
        foreach (var revision in revisions)
        foreach (var provider in revision.CanonicalPackages)
        {
            if (context.TryGetValue(provider.Id, out var previous) && ContentPackageJson.ToJson(previous) != ContentPackageJson.ToJson(provider))
                throw new LibraryException(409, $"Dependency closure captures conflicting canonical definitions in package '{provider.Id}'.");
            context[provider.Id] = ClonePackage(provider);
        }
        return ContentPackageCatalog.Compose(revisions.Select(r => new ContentPackageDocument(r.Package, $"library:{r.Package.Id}@{r.Revision}"))
            .Concat(context.Values.Select(p => new ContentPackageDocument(p, $"canonical:{p.Id}"))));
    }

    private static IEnumerable<ContentIdentity> Identities(ContentBundle body)
    {
        var definitions = body.Units.Cast<object>().Concat(body.Equipment).Concat(body.Items).Concat(body.LootCards).Concat(body.LootPresets)
            .Concat(body.WorldCards).Concat(body.Terrain).Concat(body.Assemblies).Concat(body.FixedComponents).Concat(body.Resources);
        foreach (var definition in definitions) yield return ContentDefinitions.Identity(definition);
        if (!body.DefaultWorldDeck.IsEmpty) yield return new(ContentFamily.WorldDeck, "default-world-deck");
    }

    private static Head Current(Document document, string id) => document.Packages.SingleOrDefault(h => h.PackageId == id)
        ?? throw new LibraryException(404, $"Content package '{id}' was not found.");
    private static ContentLibraryRevision Find(Document document, ContentLibraryBinding binding) =>
        document.Revisions.SingleOrDefault(r => r.Package.Id == binding.PackageId && r.Revision == binding.Revision)
        ?? throw new LibraryException(404, $"Content package '{binding.PackageId}' revision {binding.Revision} was not found.");
    private static LibraryException Conflict(string id, long expected, long current, bool archived) => new(409,
        $"Package '{id}' expected revision {expected}; current revision is {current}{(archived ? " (archived)" : "")}. Reload before saving.");
    private static ContentPackage ClonePackage(ContentPackage package) => ContentPackageJson.FromJson(ContentPackageJson.ToJson(package));
    private static ContentLibraryRevision CloneRevision(ContentLibraryRevision revision) => revision with
    { Package = ClonePackage(revision.Package), CanonicalPackages = [.. revision.CanonicalPackages.Select(ClonePackage)] };
    private static ContentLibraryRevision Seal(ContentLibraryRevision revision) => revision with { Digest = Digest(revision) };
    private static string Digest(ContentLibraryRevision revision) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        ContentLibraryJson.Serialize(revision with { Digest = "" }))));

    private static void ValidateRevision(ContentLibraryRevision revision)
    {
        if (!Guid.TryParseExact(revision.LibraryId, "N", out _) || revision.Revision < 1 || revision.Revision == long.MaxValue ||
            revision.Package is null || revision.CanonicalPackages.IsDefault || revision.CanonicalPackages.Any(p => p is null) || revision.Bindings.IsDefault ||
            revision.Digest != Digest(revision)) throw new InvalidDataException("Invalid Content Library revision or digest.");
        _ = ClonePackage(revision.Package);
        if (!revision.Bindings.SequenceEqual(NormalizeBindings(revision.Bindings))) throw new InvalidDataException("Invalid dependency binding order.");
        if (revision.CanonicalPackages.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != revision.CanonicalPackages.Length ||
            !revision.CanonicalPackages.Select(p => p.Id).SequenceEqual(revision.CanonicalPackages.Select(p => p.Id).Order(StringComparer.Ordinal)) ||
            revision.Bindings.Any(b => !revision.Package.Dependencies.Contains(b.PackageId) || revision.CanonicalPackages.Any(p => p.Id == b.PackageId)))
            throw new InvalidDataException("Invalid revision dependency selection.");
        HashSet<string> visited = new(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (!visited.Add(id)) return;
            var provider = revision.CanonicalPackages.SingleOrDefault(p => p.Id == id)
                ?? throw new InvalidDataException("Unbound package dependency.");
            _ = ClonePackage(provider);
            foreach (var child in provider.Dependencies) Visit(child);
        }
        foreach (var id in revision.Package.Dependencies.Where(id => !revision.Bindings.Any(b => b.PackageId == id))) Visit(id);
        if (visited.Count != revision.CanonicalPackages.Length) throw new InvalidDataException("Unrelated canonical dependency context.");
    }

    private static void ValidateDocument(Document document)
    {
        if (document.FormatVersion != 1 || !Guid.TryParseExact(document.LibraryId, "N", out _) || document.Packages is null || document.Revisions is null ||
            document.Packages.Any(h => h is null || string.IsNullOrEmpty(h.PackageId) || h.Revision < 1) || document.Revisions.Any(r => r?.Package is null) ||
            document.Packages.Select(h => h.PackageId).Distinct(StringComparer.Ordinal).Count() != document.Packages.Count ||
            document.Revisions.Select(r => new ContentLibraryBinding(r.Package.Id, r.Revision)).Distinct().Count() != document.Revisions.Count)
            throw new InvalidDataException("Invalid Content Library document.");
        foreach (var revision in document.Revisions)
        {
            ValidateRevision(revision);
            foreach (var binding in revision.Bindings) _ = Find(document, binding);
            if (!document.Packages.Any(h => h.PackageId == revision.Package.Id)) throw new InvalidDataException("Revision has no package head.");
        }
        foreach (var head in document.Packages)
            if (head.Revision != document.Revisions.Where(r => r.Package.Id == head.PackageId).Select(r => r.Revision).DefaultIfEmpty().Max())
                throw new InvalidDataException("Invalid current revision reference.");
    }

    private T Access<T>(Func<Document, T> operation, bool write = false)
    {
        CheckNoLinks(root); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "library.json"); var lockPath = Path.Combine(root, ".library.lock");
        CheckNoLinks(path); CheckNoLinks(lockPath);
        using var handle = Lock(lockPath);
        var exists = File.Exists(path);
        Document document;
        try
        {
            if (exists && new FileInfo(path).Length > maximumBytes) throw new InvalidDataException("Library storage limit exceeded.");
            document = exists ? JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty library.") : new();
            ValidateDocument(document);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException or LibraryException)
        { throw new LibraryException(503, "The Content Library file is invalid. Restore a verified backup before editing; no data has been overwritten."); }
        var result = operation(document);
        if (write || !exists) Persist(path, document);
        return result;
    }

    private void Persist(string path, Document document)
    {
        document.Packages = [.. document.Packages.OrderBy(h => h.PackageId, StringComparer.Ordinal)];
        document.Revisions = [.. document.Revisions.OrderBy(r => r.Package.Id, StringComparer.Ordinal).ThenBy(r => r.Revision)];
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(ContentLibraryJson.Serialize(document));
                if (bytes.LongLength > maximumBytes) throw new ArgumentException("The Content Library storage limit has been reached; no revision was saved.");
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FileStream Lock(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
        }
    }
    private static void CheckNoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Content Library storage must not contain symbolic links or junctions.");
    }
}
