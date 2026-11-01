using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// One explicit, immutable provider selection per host. Never consulted by a running game's rules.
public sealed class CanonicalContent(string directory)
{
    public EffectiveContent Catalog { get; } = ContentPackageCatalog.LoadCanonical(directory);
    public ContentBundle Bundle => Catalog.Definitions;
}
