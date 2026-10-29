using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record LibraryScenario(string Id, string Name, string? CoverArt, long Revision, JsonElement Definition)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Source { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ChangeToken { get; init; }
}
public sealed record LibraryScenarioSummary(string Id, string Name, string? CoverArt, long Revision, string? Source = null, string? ChangeToken = null);
public sealed record LibraryCampaign(string Id, string Name, string? Description, string? CoverArt, IReadOnlyList<string> ScenarioIds, long Revision)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Source { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ChangeToken { get; init; }
}
public sealed record LibraryHiddenItem(string Id, string Name, long Revision, string ChangeToken);
public sealed record LibraryCatalog(IReadOnlyList<LibraryScenarioSummary> Scenarios, IReadOnlyList<LibraryCampaign> Campaigns,
    IReadOnlyList<LibraryHiddenItem> HiddenScenarios, IReadOnlyList<LibraryHiddenItem> HiddenCampaigns);
public sealed record SaveLibraryScenario(string Name, string? CoverArt, JsonElement Definition, long? Revision = null, string? ChangeToken = null);
public sealed record SaveLibraryCampaign(string Name, string? Description, string? CoverArt, List<string> ScenarioIds, long? Revision = null, string? ChangeToken = null);
public sealed record RestoreLibraryItem(long? Revision, string? ChangeToken);
public sealed class LibraryException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Read-only bundled records and atomic local overrides resolve by ID. Tombstones hide
/// bundled entries locally. No identity supplied by a client is ever used as a filesystem path.</summary>
public sealed class ScenarioLibrary(string directory, string? bundledDirectory = null, bool readOnly = false)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, RespectRequiredConstructorParameters = true };
    private static readonly Regex Identity = new("^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.CultureInvariant);
    private readonly string root = Path.GetFullPath(directory);
    private readonly string? bundlePath = ResolveBundlePath(directory, bundledDirectory);
    private static string? ResolveBundlePath(string local, string? bundled)
    {
        if (bundled is null) return null;
        var localPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(local));
        var bundledPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundled));
        if (string.Equals(localPath, bundledPath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Local and bundled library directories must be different.");
        return Path.Combine(bundledPath, "library.json");
    }
    private sealed record Tombstone(string Id, string Name, long Revision);
    private sealed class Document
    {
        [JsonRequired] public int Version { get; set; } = 2;
        [JsonRequired] public List<LibraryScenario> Scenarios { get; set; } = [];
        [JsonRequired] public List<LibraryCampaign> Campaigns { get; set; } = [];
        [JsonRequired] public List<Tombstone> DeletedScenarios { get; set; } = [];
        [JsonRequired] public List<Tombstone> DeletedCampaigns { get; set; } = [];
        // Epochs keep a pre-override draft stale after restoring the same bundled revision.
        [JsonRequired] public Dictionary<string, long> Generations { get; set; } = [];
    }
    private sealed class LegacyDocument
    {
        [JsonRequired] public int Version { get; set; }
        [JsonRequired] public List<string> RegisteredScenarios { get; set; } = [];
        [JsonRequired] public bool RegisteredTestCampaign { get; set; }
        [JsonRequired] public List<LibraryScenario> Scenarios { get; set; } = [];
        [JsonRequired] public List<LibraryCampaign> Campaigns { get; set; } = [];
    }
    private sealed class BundleDocument
    {
        [JsonRequired] public int Version { get; set; } = 1;
        [JsonRequired] public List<LibraryScenario> Scenarios { get; set; } = [];
        [JsonRequired] public List<LibraryCampaign> Campaigns { get; set; } = [];
    }
    private sealed record LibraryView(Document Local, BundleDocument Bundle)
    {
        public IEnumerable<LibraryScenario> Scenarios => Bundle.Scenarios.Where(s => !Local.Scenarios.Any(l => l.Id == s.Id))
            .Concat(Local.Scenarios).Where(s => !Local.DeletedScenarios.Any(d => d.Id == s.Id));
        public IEnumerable<LibraryCampaign> Campaigns => Bundle.Campaigns.Where(c => !Local.Campaigns.Any(l => l.Id == c.Id))
            .Concat(Local.Campaigns).Where(c => !Local.DeletedCampaigns.Any(d => d.Id == c.Id));
    }

    public LibraryCatalog Catalog() => Access(view => new LibraryCatalog(
        view.Scenarios.Select(s => Present(view, s)).Select(s => new LibraryScenarioSummary(s.Id, s.Name, s.CoverArt, s.Revision, s.Source, s.ChangeToken)).ToArray(),
        view.Campaigns.Select(c => Present(view, c)).ToArray(),
        view.Local.DeletedScenarios.Select(d => Hidden(view, d, "scenario")).ToArray(),
        view.Local.DeletedCampaigns.Select(d => Hidden(view, d, "campaign")).ToArray()));
    public LibraryScenario Scenario(string id) => Access(view => Present(view, FindScenario(view, id)));
    public LibraryCampaign Campaign(string id) => Access(view => Present(view, FindCampaign(view, id)));

    public LibraryScenario SaveScenario(string? id, SaveLibraryScenario request)
    {
        var name = RequiredText(request.Name, "Name", 120);
        var cover = Cover(request.CoverArt);
        if (request.Definition.ValueKind != JsonValueKind.Object) throw new ArgumentException("A scenario definition is required.");
        var raw = request.Definition.GetRawText();
        if (raw.Length > 2_000_000) throw new ArgumentException("Scenario definition is too large.");
        var definition = ScenarioDefinitionJson.FromJson(raw, DesignerApi.CheckHostLimits);
        using var canonical = JsonDocument.Parse(ScenarioDefinitionJson.ToJson(definition));
        return Access(view =>
        {
            var previous = id is null ? null : Present(view, FindScenario(view, id));
            if (previous is not null) CheckRevision(previous.Revision, previous.ChangeToken!, request.Revision, request.ChangeToken, previous.Source != "local");
            var saved = new LibraryScenario(id ?? Guid.NewGuid().ToString("N"), name, cover, checked((previous?.Revision ?? 0) + 1), canonical.RootElement.Clone());
            view.Local.Scenarios.RemoveAll(s => s.Id == saved.Id); view.Local.Scenarios.Add(saved);
            Bump(view.Local, "scenario", saved.Id);
            return Present(view, saved);
        }, write: true);
    }
    public LibraryCampaign SaveCampaign(string? id, SaveLibraryCampaign request)
    {
        var name = RequiredText(request.Name, "Name", 120);
        var description = OptionalText(request.Description, "Description", 2000);
        var cover = Cover(request.CoverArt);
        if (request.ScenarioIds is null || request.ScenarioIds.Count > 1000)
            throw new ArgumentException("A campaign requires an ordered list of at most 1000 scenario references.");
        foreach (var reference in request.ScenarioIds) CheckId(reference);
        return Access(view =>
        {
            var previous = id is null ? null : Present(view, FindCampaign(view, id));
            if (previous is not null) CheckRevision(previous.Revision, previous.ChangeToken!, request.Revision, request.ChangeToken, previous.Source != "local");
            var known = view.Scenarios.Select(s => s.Id).ToHashSet();
            foreach (var reference in request.ScenarioIds)
                if (!known.Contains(reference) && previous?.ScenarioIds.Contains(reference) != true)
                    throw new ArgumentException($"Scenario '{reference}' is no longer in the library. Refresh the library before adding it.");
            var saved = new LibraryCampaign(id ?? Guid.NewGuid().ToString("N"), name, description, cover, request.ScenarioIds.ToArray(), checked((previous?.Revision ?? 0) + 1));
            view.Local.Campaigns.RemoveAll(c => c.Id == saved.Id); view.Local.Campaigns.Add(saved);
            Bump(view.Local, "campaign", saved.Id);
            return Present(view, saved);
        }, write: true);
    }
    public void DeleteScenario(string id, long? revision, string? changeToken = null) => Access(view =>
    {
        var previous = Present(view, FindScenario(view, id));
        CheckRevision(previous.Revision, previous.ChangeToken!, revision, changeToken, previous.Source != "local");
        view.Local.Scenarios.RemoveAll(s => s.Id == id);
        if (view.Bundle.Scenarios.Any(s => s.Id == id)) view.Local.DeletedScenarios.Add(new(id, previous.Name, previous.Revision));
        Bump(view.Local, "scenario", id); return true;
    }, write: true);
    public void DeleteCampaign(string id, long? revision, string? changeToken = null) => Access(view =>
    {
        var previous = Present(view, FindCampaign(view, id));
        CheckRevision(previous.Revision, previous.ChangeToken!, revision, changeToken, previous.Source != "local");
        view.Local.Campaigns.RemoveAll(c => c.Id == id);
        if (view.Bundle.Campaigns.Any(c => c.Id == id)) view.Local.DeletedCampaigns.Add(new(id, previous.Name, previous.Revision));
        Bump(view.Local, "campaign", id); return true;
    }, write: true);
    public LibraryScenario RestoreScenario(string id, RestoreLibraryItem request) => Access(view =>
    {
        CheckId(id);
        var bundled = view.Bundle.Scenarios.FirstOrDefault(s => s.Id == id) ?? throw new LibraryException(404, "There is no bundled scenario to restore.");
        var hidden = view.Local.DeletedScenarios.FirstOrDefault(d => d.Id == id);
        if (hidden is not null)
        {
            var current = Hidden(view, hidden, "scenario");
            CheckRevision(current.Revision, current.ChangeToken, request.Revision, request.ChangeToken, true);
        }
        else
        {
            var current = Present(view, FindScenario(view, id));
            CheckRevision(current.Revision, current.ChangeToken!, request.Revision, request.ChangeToken, true);
        }
        view.Local.Scenarios.RemoveAll(s => s.Id == id); view.Local.DeletedScenarios.RemoveAll(d => d.Id == id);
        Bump(view.Local, "scenario", id); return Present(view, bundled);
    }, write: true);
    public LibraryCampaign RestoreCampaign(string id, RestoreLibraryItem request) => Access(view =>
    {
        CheckId(id);
        var bundled = view.Bundle.Campaigns.FirstOrDefault(c => c.Id == id) ?? throw new LibraryException(404, "There is no bundled campaign to restore.");
        var hidden = view.Local.DeletedCampaigns.FirstOrDefault(d => d.Id == id);
        if (hidden is not null)
        {
            var current = Hidden(view, hidden, "campaign");
            CheckRevision(current.Revision, current.ChangeToken, request.Revision, request.ChangeToken, true);
        }
        else
        {
            var current = Present(view, FindCampaign(view, id));
            CheckRevision(current.Revision, current.ChangeToken!, request.Revision, request.ChangeToken, true);
        }
        view.Local.Campaigns.RemoveAll(c => c.Id == id); view.Local.DeletedCampaigns.RemoveAll(d => d.Id == id);
        Bump(view.Local, "campaign", id); return Present(view, bundled);
    }, write: true);

    // Promotion writes the bundle first. Release only records identical to both the captured
    // local work and the published bundle; concurrent edits remain overrides.
    public int ReleasePromoted(IReadOnlyList<LibraryScenario> scenarios, IReadOnlyList<LibraryCampaign> campaigns) => Access(view =>
    {
        var count = 0;
        foreach (var captured in scenarios)
        {
            var local = view.Local.Scenarios.FirstOrDefault(s => s.Id == captured.Id);
            var bundled = view.Bundle.Scenarios.FirstOrDefault(s => s.Id == captured.Id);
            if (local is null || bundled is null || !Equivalent(Clean(local), Clean(captured)) || !Equivalent(Clean(local), Clean(bundled))) continue;
            view.Local.Scenarios.Remove(local); Bump(view.Local, "scenario", local.Id); count++;
        }
        foreach (var captured in campaigns)
        {
            var local = view.Local.Campaigns.FirstOrDefault(c => c.Id == captured.Id);
            var bundled = view.Bundle.Campaigns.FirstOrDefault(c => c.Id == captured.Id);
            if (local is null || bundled is null || !Equivalent(Clean(local), Clean(captured)) || !Equivalent(Clean(local), Clean(bundled))) continue;
            view.Local.Campaigns.Remove(local); Bump(view.Local, "campaign", local.Id); count++;
        }
        return count;
    }, write: true);

    private static LibraryScenario FindScenario(LibraryView view, string id)
    {
        CheckId(id); return view.Scenarios.FirstOrDefault(s => s.Id == id) ?? throw new LibraryException(404, "Scenario is no longer in the library.");
    }
    private static LibraryCampaign FindCampaign(LibraryView view, string id)
    {
        CheckId(id); return view.Campaigns.FirstOrDefault(c => c.Id == id) ?? throw new LibraryException(404, "Campaign is no longer in the library.");
    }
    private static LibraryScenario Clean(LibraryScenario value) => value with { Source = null, ChangeToken = null };
    private static LibraryCampaign Clean(LibraryCampaign value) => value with { Source = null, ChangeToken = null };
    private static LibraryScenario Present(LibraryView view, LibraryScenario value)
    {
        var bundled = view.Bundle.Scenarios.FirstOrDefault(s => s.Id == value.Id);
        var source = view.Local.Scenarios.Any(s => s.Id == value.Id) ? bundled is null ? "local" : "override" : "bundled";
        return value with { Source = source, ChangeToken = Token(view.Local, "scenario", value.Id, source, Clean(value), bundled is null ? null : Clean(bundled)) };
    }
    private static LibraryCampaign Present(LibraryView view, LibraryCampaign value)
    {
        var bundled = view.Bundle.Campaigns.FirstOrDefault(c => c.Id == value.Id);
        var source = view.Local.Campaigns.Any(c => c.Id == value.Id) ? bundled is null ? "local" : "override" : "bundled";
        return value with { Source = source, ChangeToken = Token(view.Local, "campaign", value.Id, source, Clean(value), bundled is null ? null : Clean(bundled)) };
    }
    private static LibraryHiddenItem Hidden(LibraryView view, Tombstone value, string kind)
    {
        object? bundled = kind == "scenario" ? view.Bundle.Scenarios.FirstOrDefault(s => s.Id == value.Id) : view.Bundle.Campaigns.FirstOrDefault(c => c.Id == value.Id);
        return new(value.Id, value.Name, value.Revision, Token(view.Local, kind, value.Id, "hidden", value, bundled));
    }
    private static string Token(Document local, string kind, string id, string source, object value, object? bundled)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { kind, id, source, generation = local.Generations.GetValueOrDefault(kind + ":" + id), value, bundled }, Json))));
    private static void Bump(Document local, string kind, string id)
        => local.Generations[kind + ":" + id] = checked(local.Generations.GetValueOrDefault(kind + ":" + id) + 1);
    private static void CheckId(string? id)
    {
        if (id is null || !Identity.IsMatch(id)) throw new ArgumentException("Invalid library identity.");
    }
    private static void CheckRevision(long current, string token, long? expected, string? expectedToken, bool requireToken)
    {
        if (current != expected || (requireToken || expectedToken is not null) && token != expectedToken)
            throw new LibraryException(409, "This item changed since it was opened. Open the latest version or save a new copy.");
    }
    private static string RequiredText(string? value, string field, int max) => OptionalText(value, field, max) ?? throw new ArgumentException($"{field} is required.");
    private static string? OptionalText(string? value, string field, int max)
    {
        value = value?.Trim(); if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > max || value.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')) throw new ArgumentException($"{field} must contain at most {max} characters and no control characters.");
        return value;
    }
    private static string? Cover(string? value)
    {
        value = OptionalText(value, "Cover art", 2048); if (value is null) return null;
        if (value.Any(char.IsWhiteSpace) || value.Contains('\\')) throw new ArgumentException("Cover art must be a root-relative image path or an HTTPS URL.");
        if (value.StartsWith('/') && !value.StartsWith("//")) return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)) return value;
        throw new ArgumentException("Cover art must be a root-relative image path or an HTTPS URL.");
    }

    private T Access<T>(Func<LibraryView, T> operation, bool write = false)
    {
        if (readOnly && write) throw new InvalidOperationException("This library was opened read-only.");
        CheckNoLinks(root);
        var path = Path.Combine(root, "library.json"); var lockPath = Path.Combine(root, ".library.lock");
        CheckNoLinks(path); CheckNoLinks(lockPath);
        if (!readOnly) Directory.CreateDirectory(root);
        using var handle = readOnly ? null : Lock(lockPath);
        var bundle = ReadBundle();
        Document document; string? legacyText = null;
        try
        {
            if (!File.Exists(path)) document = new();
            else
            {
                var text = ReadBounded(path); using var json = JsonDocument.Parse(text);
                if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number)) throw new InvalidDataException("Missing library version.");
                if (number == 1)
                {
                    document = Migrate(JsonSerializer.Deserialize<LegacyDocument>(text, Json) ?? throw new InvalidDataException("Library is empty."));
                    legacyText = text;
                }
                else document = JsonSerializer.Deserialize<Document>(text, Json) ?? throw new InvalidDataException("Library is empty.");
            }
            ValidateDocument(document);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException)
        {
            throw new LibraryException(503, "The local library file is invalid. Restore it from a backup before editing; no data has been overwritten.");
        }
        var result = operation(new(document, bundle));
        if (!readOnly && legacyText is not null) BackupLegacy(legacyText);
        if (!readOnly && (write || legacyText is not null)) Persist(path, document);
        return result;
    }
    private BundleDocument ReadBundle()
    {
        var baseline = Builtins(); if (bundlePath is null || !File.Exists(bundlePath)) return baseline;
        try
        {
            CheckNoLinks(bundlePath);
            var authored = JsonSerializer.Deserialize<BundleDocument>(ReadBounded(bundlePath), Json) ?? throw new InvalidDataException("Bundled library is empty.");
            if (authored.Version != 1) throw new InvalidDataException("Unsupported bundle version.");
            ValidateRecords(authored.Scenarios, authored.Campaigns);
            baseline.Scenarios.RemoveAll(s => authored.Scenarios.Any(a => a.Id == s.Id));
            baseline.Campaigns.RemoveAll(c => authored.Campaigns.Any(a => a.Id == c.Id));
            baseline.Scenarios.AddRange(authored.Scenarios.Select(Clean)); baseline.Campaigns.AddRange(authored.Campaigns.Select(Clean));
            return baseline;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException)
        {
            throw new LibraryException(503, "The bundled library is invalid. Repair the application content; local data has not been overwritten.");
        }
    }
    private static BundleDocument Builtins() => new()
    {
        Scenarios = PlaytestScenarios.Catalog.Select(source => new LibraryScenario(source.Id, source.Name, null, 1,
            JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(PlaytestScenarios.Definition(source.Id))))).ToList(),
        Campaigns = [new("test-scenarios", "Test Scenarios", "Explore the original Project Delve scenarios. Choose any adventure to begin.",
            null, PlaytestScenarios.Catalog.Select(s => s.Id).ToArray(), 1)]
    };
    private static Document Migrate(LegacyDocument legacy)
    {
        if (legacy.Version != 1 || legacy.RegisteredScenarios is null) throw new InvalidDataException("Invalid legacy library.");
        ValidateRecords(legacy.Scenarios, legacy.Campaigns);
        foreach (var id in legacy.RegisteredScenarios) CheckId(id);
        var baseline = Builtins(); var migrated = new Document();
        foreach (var scenario in legacy.Scenarios)
        {
            var original = baseline.Scenarios.FirstOrDefault(s => s.Id == scenario.Id);
            // Keep every saved revision or content difference as a deliberate local override.
            if (!legacy.RegisteredScenarios.Contains(scenario.Id) || original is null || scenario.Revision != 1 || !Equivalent(Clean(scenario), original)) migrated.Scenarios.Add(Clean(scenario));
        }
        foreach (var campaign in legacy.Campaigns)
        {
            var original = baseline.Campaigns.FirstOrDefault(c => c.Id == campaign.Id);
            if (!legacy.RegisteredTestCampaign || original is null || campaign.Revision != 1 || !Equivalent(Clean(campaign), original)) migrated.Campaigns.Add(Clean(campaign));
        }
        foreach (var id in legacy.RegisteredScenarios.Distinct())
            if (!legacy.Scenarios.Any(s => s.Id == id)) migrated.DeletedScenarios.Add(new(id, baseline.Scenarios.FirstOrDefault(s => s.Id == id)?.Name ?? id, 1));
        if (legacy.RegisteredTestCampaign && !legacy.Campaigns.Any(c => c.Id == "test-scenarios")) migrated.DeletedCampaigns.Add(new("test-scenarios", "Test Scenarios", 1));
        return migrated;
    }
    private static bool Equivalent<T>(T one, T two) => JsonNode.DeepEquals(JsonSerializer.SerializeToNode(one, Json), JsonSerializer.SerializeToNode(two, Json));
    private void BackupLegacy(string text)
    {
        var path = Path.Combine(root, "library.v1.backup.json"); CheckNoLinks(path);
        if (File.Exists(path))
        {
            if (File.ReadAllText(path) != text) throw new LibraryException(503, "A different V1 backup already exists. Preserve both versions before migrating this library.");
            return;
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes); stream.Flush(flushToDisk: true);
    }
    private static string ReadBounded(string path)
    {
        if (new FileInfo(path).Length > 64_000_000) throw new InvalidDataException("Library file is too large."); return File.ReadAllText(path);
    }
    private static FileStream Lock(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
        }
    }
    private static void ValidateDocument(Document document)
    {
        if (document.Version != 2 || document.DeletedScenarios is null || document.DeletedCampaigns is null || document.Generations is null) throw new InvalidDataException("Unsupported local library format.");
        ValidateRecords(document.Scenarios, document.Campaigns);
        foreach (var tombstones in new[] { document.DeletedScenarios, document.DeletedCampaigns })
        {
            if (tombstones.Any(t => t is null) || tombstones.Select(t => t.Id).Distinct().Count() != tombstones.Count) throw new InvalidDataException("Invalid hidden entries.");
            foreach (var tombstone in tombstones)
            {
                CheckId(tombstone.Id); RequiredText(tombstone.Name, "Name", 120);
                if (tombstone.Revision < 1) throw new InvalidDataException("Invalid hidden entry revision.");
            }
        }
        if (document.Scenarios.Any(s => document.DeletedScenarios.Any(t => t.Id == s.Id)) || document.Campaigns.Any(c => document.DeletedCampaigns.Any(t => t.Id == c.Id))) throw new InvalidDataException("An entry cannot be overridden and hidden together.");
        foreach (var pair in document.Generations)
        {
            var parts = pair.Key.Split(':');
            if (parts.Length != 2 || parts[0] is not ("scenario" or "campaign") || pair.Value < 0 || pair.Value == long.MaxValue) throw new InvalidDataException("Invalid change generation.");
            CheckId(parts[1]);
        }
    }
    private static void ValidateRecords(List<LibraryScenario> scenarios, List<LibraryCampaign> campaigns)
    {
        if (scenarios is null || campaigns is null || scenarios.Any(s => s is null) || campaigns.Any(c => c is null)) throw new InvalidDataException("Invalid library entries.");
        if (scenarios.Select(s => s.Id).Distinct().Count() != scenarios.Count || campaigns.Select(c => c.Id).Distinct().Count() != campaigns.Count) throw new InvalidDataException("Duplicate library identity.");
        foreach (var scenario in scenarios)
        {
            CheckId(scenario.Id); RequiredText(scenario.Name, "Name", 120); Cover(scenario.CoverArt);
            if (scenario.Revision < 1 || scenario.Revision == long.MaxValue || scenario.Definition.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid scenario.");
            _ = ScenarioDefinitionJson.FromJson(scenario.Definition.GetRawText(), DesignerApi.CheckHostLimits);
        }
        foreach (var campaign in campaigns)
        {
            CheckId(campaign.Id); RequiredText(campaign.Name, "Name", 120); Cover(campaign.CoverArt);
            OptionalText(campaign.Description, "Description", 2000);
            if (campaign.Revision < 1 || campaign.Revision == long.MaxValue || campaign.ScenarioIds is null || campaign.ScenarioIds.Count > 1000) throw new InvalidDataException("Invalid campaign.");
            foreach (var id in campaign.ScenarioIds) CheckId(id);
        }
    }
    private static void Persist(string path, Document document)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, Json);
                if (stream.Length > 64_000_000) throw new ArgumentException("The library has reached its 64 MB storage limit. Remove unused entries before adding more.");
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void CheckNoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Library storage must not contain symbolic links or junctions.");
    }
}
