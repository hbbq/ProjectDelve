using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Provider changes affect authoring and new-game creation; running games retain snapshots.
public sealed class CanonicalContent
{
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
        CheckTree(directory);
        var documents = ContentPackageCatalog.ReadDirectory(directory);
        foreach (var document in documents) CheckNoLinks(document.Document!);
        var csharp = ContentBundle.Canonical();
        var catalog = ContentPackageCatalog.Compose(documents, csharp);
        ContentPackage[] packages = [new() { Id = ContentPackageCatalog.CSharpPackageId, Definitions = csharp }, .. documents.Select(d => d.Package)];
        var digest = Hash(string.Concat(packages.OrderBy(p => p.Id, StringComparer.Ordinal).Select(ContentPackageJson.ToJson)));
        return new(packages, catalog, digest);
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
                _ = ContentPackageCatalog.Compose(before.Packages.Append(package).Select(p => new ContentPackageDocument(p, p.Id)));
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
    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static FileStream FileLock(string path)
    {
        for (var attempt = 0; ; attempt++)
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
    }
    private static void CheckNoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Canonical content must not contain symbolic links or junctions.");
    }
}
