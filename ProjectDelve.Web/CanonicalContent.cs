using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// One explicit, immutable provider selection per host. Never consulted by a running game's rules.
public sealed class CanonicalContent
{
    public IReadOnlyList<ContentPackage> Packages { get; }
    public EffectiveContent Catalog { get; }
    public ContentBundle Bundle => Catalog.Definitions;

    public CanonicalContent(string directory)
    {
        var documents = ContentPackageCatalog.ReadDirectory(directory);
        var csharp = ContentBundle.Canonical();
        Catalog = ContentPackageCatalog.Compose(documents, csharp);
        Packages = [new() { Id = ContentPackageCatalog.CSharpPackageId, Definitions = csharp }, .. documents.Select(d => d.Package)];
    }
}
