using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Authoring/provider format only. Package metadata never enters gameplay definitions or saves.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContentPackage
{
    [JsonRequired] public int FormatVersion { get; init; } = 1;
    [JsonRequired] public required string Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public ImmutableArray<CanonicalPublicationEntry>? Publications { get; init; }
    public ImmutableArray<string> Dependencies { get; init; } = [];
    // Explicit transfer of canonical ownership from the selected C# provider.
    public ImmutableArray<ContentIdentity> ReplacesCSharp { get; init; } = [];
    [JsonRequired] public ContentBundle Definitions { get; init; } = new();
}

public sealed record ContentPackageDocument(ContentPackage Package, string? Document = null);

public static class ContentPackageJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    // Provider/storage envelopes use the same typed definitions and serialization contracts.
    public static JsonSerializerOptions CreateOptions() => new(Options);

    public static ContentPackage FromJson(string json, string? document = null)
    {
        try
        {
            // Source locations are supplied by the provider, never accepted as authored scope.
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("A package object is required.");
            RejectDuplicateFields(parsed.RootElement, "$");
            if (parsed.RootElement.TryGetProperty("definitions", out var definitions) && definitions.ValueKind == JsonValueKind.Object && definitions.TryGetProperty("source", out _))
                throw new JsonException("Package provenance belongs to the provider, not definitions.source.");
            if (definitions.ValueKind == JsonValueKind.Object)
                foreach (var field in definitions.EnumerateObject())
                {
                    if (field.Value.ValueKind != JsonValueKind.Array)
                        throw new JsonException($"definitions.{field.Name} must be an array.");
                    if (field.Value.EnumerateArray().Any(row => row.ValueKind == JsonValueKind.Null))
                        throw new JsonException($"definitions.{field.Name} cannot contain null definitions.");
                }
            var package = JsonSerializer.Deserialize<ContentPackage>(json, Options) ?? throw new JsonException("A content package is required.");
            ValidateHeader(package);
            return package;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            error.Data["ProjectDelve.ContentSource"] = new ContentSource(document);
            throw;
        }
    }

    public static string ToJson(ContentPackage package)
    {
        ValidateHeader(package);
        return JsonSerializer.Serialize(package, Options).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    public static ContentSource? SourceOf(Exception error) => error.Data["ProjectDelve.ContentSource"] as ContentSource;

    private static void RejectDuplicateFields(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate package field '{path}.{property.Name}'.");
                RejectDuplicateFields(property.Value, path + "." + property.Name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in value.EnumerateArray()) RejectDuplicateFields(element, $"{path}[{index++}]");
        }
    }

    internal static void ValidateHeader(ContentPackage package)
    {
        if (package.FormatVersion != 1)
            throw ScenarioValidationContext.At(new ArgumentException($"Unsupported content package version {package.FormatVersion}; expected 1."), "formatVersion");
        if (!TerrainContentKey.Valid(package.Id))
            throw ScenarioValidationContext.At(new ArgumentException("A package requires a stable nonblank ID (letters, digits, '-', '_', ':' or '.')."), "id");
        if (package.Dependencies.IsDefault || package.Dependencies.Any(id => !TerrainContentKey.Valid(id) || id == package.Id) ||
            package.Dependencies.Distinct(StringComparer.Ordinal).Count() != package.Dependencies.Length)
            throw ScenarioValidationContext.At(new ArgumentException("Package dependencies require distinct, valid external package IDs."), "dependencies");
        if (package.Definitions is null) throw ScenarioValidationContext.At(new ArgumentException("Package definitions are required."), "definitions");
        if (package.ReplacesCSharp.IsDefault || package.ReplacesCSharp.Any(id => id is null || !Enum.IsDefined(id.Family) || string.IsNullOrWhiteSpace(id.TypeId)) ||
            package.ReplacesCSharp.Distinct().Count() != package.ReplacesCSharp.Length)
            throw ScenarioValidationContext.At(new ArgumentException("C# replacement claims require distinct valid content identities."), "replacesCSharp");
    }
}

public static class ContentPackageCatalog
{
    public const string CSharpPackageId = "canonical-csharp";

    // Validate all providers together: references may be forward, transitive or cyclic.
    public static EffectiveContent Compose(IEnumerable<ContentPackageDocument> documents, ContentBundle? csharp = null)
    {
        var packages = documents.OrderBy(p => p.Package.Id, StringComparer.Ordinal).ToArray();
        HashSet<string> ids = new(StringComparer.Ordinal);
        if (csharp is not null) ids.Add(CSharpPackageId);
        foreach (var document in packages)
        {
            try { ContentPackageJson.ValidateHeader(document.Package); }
            catch (ArgumentException error) { error.Data["ProjectDelve.ContentSource"] = new ContentSource(document.Document, document.Package.Id); throw; }
            if (!ids.Add(document.Package.Id))
                throw HeaderError(document, "id", $"Duplicate package ID '{document.Package.Id}'.");
        }
        foreach (var document in packages)
            foreach (var dependency in document.Package.Dependencies)
                if (!ids.Contains(dependency))
                    throw HeaderError(document, "dependencies", $"Package '{document.Package.Id}' requires absent package '{dependency}'.");

        var replacements = new HashSet<ContentIdentity>();
        foreach (var document in packages)
        foreach (var identity in document.Package.ReplacesCSharp)
        {
            if (csharp is null || !ScenarioContentReferences.Identities(csharp).Contains(identity) ||
                !ScenarioContentReferences.Identities(document.Package.Definitions).Contains(identity))
                throw HeaderError(document, "replacesCSharp", "A replacement must exist in both the selected C# provider and the replacing package.");
            if (!replacements.Add(identity))
                throw HeaderError(document, "replacesCSharp", $"Multiple packages claim C# identity '{identity.Family}:{identity.TypeId}'.");
        }
        var bundles = packages.Select(p => p.Package.Definitions with { Source = new(p.Document, p.Package.Id, "definitions"), Publications = p.Package.Publications ?? [] }).ToList();
        if (csharp is not null) bundles.Insert(0, ScenarioContentReferences.Filter(csharp, id => !replacements.Contains(id)) with { Source = new(Package: CSharpPackageId) });
        var effective = ContentComposition.Compose(bundles);
        foreach (var document in packages)
        if (document.Package.Publications is {} publications)
        {
            if (publications.IsDefault || publications.Any(p => p?.Identity is null || string.IsNullOrWhiteSpace(p.SourceKind) || p.SourceDigest?.Length != 64) ||
                publications.Select(p => p.Identity).Distinct().Count() != publications.Length ||
                publications.Any(p => !ScenarioContentReferences.Identities(document.Package.Definitions).Contains(p.Identity) ||
                    p.PayloadDigest != CanonicalPublications.PayloadDigest(document.Package.Definitions, p.Identity)))
                throw HeaderError(document, "publications", "Invalid canonical publication identity or payload digest.");
        }
        var owners = bundles.SelectMany(b => b.Definitions()).ToDictionary(d => d.Identity, d => d.Source!);
        foreach (var bundle in bundles)
            for (var index = 0; index < bundle.Resources.Length; index++)
                owners[new(ContentFamily.Resource, bundle.Resources[index].Id)] = new(bundle.Source?.Document, bundle.Source?.Package,
                    $"{(string.IsNullOrEmpty(bundle.Source?.Path) ? "" : bundle.Source.Path + ".")}resources[{index}]");
        List<ContentDiagnostic> errors = [];
        foreach (var document in packages)
        {
            var bundle = document.Package.Definitions with { Source = new(document.Document, document.Package.Id, "definitions") };
            foreach (var definition in bundle.Definitions().Append(new(new WorldDeckDefinition("default-world-deck", bundle.DefaultWorldDeck),
                new(document.Document, document.Package.Id, "definitions.defaultWorldDeck"))))
                foreach (var dependency in ContentDefinitions.Dependencies(definition.Value))
                {
                    if (dependency.Kind == ContentDependencyKind.Embedded || !owners.TryGetValue(dependency.Target, out var target) || target.Package == document.Package.Id) continue;
                    if (!document.Package.Dependencies.Contains(target.Package!, StringComparer.Ordinal))
                        errors.Add(new(definition.Identity, dependency.Path, "undeclaredPackageDependency",
                            $"Declare dependency on package '{target.Package}' for this reference.", definition.Source, dependency.Target, target));
                }
        }
        if (errors.Count > 0) throw new ContentCompositionException([.. errors]);
        return effective;
    }

    private static ArgumentException HeaderError(ContentPackageDocument document, string path, string message)
    {
        var error = ScenarioValidationContext.At(new ArgumentException(message), path);
        error.Data["ProjectDelve.ContentSource"] = new ContentSource(document.Document, document.Package.Id);
        return error;
    }

    public static IReadOnlyList<ContentPackageDocument> ReadDirectory(string directory) => Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
        .OrderBy(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), StringComparer.Ordinal)
        .Select(path => new ContentPackageDocument(ContentPackageJson.FromJson(File.ReadAllText(path), path), path)).ToArray();

    public static EffectiveContent LoadCanonical(string directory) => Compose(ReadDirectory(directory), ContentBundle.Canonical());
}

public static class ContentPackageExport
{
    // Default export is portable. Loading exported canonical identities alongside their originals must still fail.
    public static ContentPackage Selected(string packageId, EffectiveContent catalog, IEnumerable<ContentIdentity> selected,
        bool includeDependencies = true, IEnumerable<string>? externalPackages = null)
    {
        var roots = selected.ToArray();
        var ids = roots.Concat(includeDependencies ? catalog.Dependencies.Closure(roots) : []).ToHashSet();
        var source = catalog.Definitions;
        foreach (var root in roots)
            if (!source.Definitions().Any(d => d.Identity == root) && !(root.Family == ContentFamily.Resource && source.Resources.Any(r => r.Id == root.TypeId)) &&
                !(root.Family == ContentFamily.WorldDeck && root.TypeId == "default-world-deck"))
                throw new ArgumentException($"Selected {root.Family} '{root.TypeId}' is absent from the catalog.");
        ImmutableArray<T> Rows<T>(ImmutableArray<T> rows) => [.. rows.Where(row => ids.Contains(ContentDefinitions.Identity(row!)))];
        var body = new ContentBundle
        {
            Units = Rows(source.Units), Equipment = Rows(source.Equipment), Items = Rows(source.Items), LootCards = Rows(source.LootCards),
            LootPresets = Rows(source.LootPresets), WorldCards = Rows(source.WorldCards), Terrain = Rows(source.Terrain),
            FixedComponents = Rows(source.FixedComponents), Resources = Rows(source.Resources),
            DefaultWorldDeck = ids.Contains(new(ContentFamily.WorldDeck, "default-world-deck")) ? source.DefaultWorldDeck : []
        };
        var result = new ContentPackage { Id = packageId, Definitions = body, Dependencies = [.. externalPackages ?? []] };
        ContentPackageJson.ValidateHeader(result);
        return result;
    }
}
