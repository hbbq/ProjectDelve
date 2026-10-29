using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectDelve.Web;

public sealed record LibraryPromotionResult(string CampaignId, IReadOnlyList<string> ScenarioIds,
    IReadOnlyList<string> Covers, bool Applied, int ReleasedLocalRecords);

/// <summary>Explicit authoring command, never an HTTP endpoint. Preserve identities and cover URLs.</summary>
public static class LibraryPromotion
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Command(string[] args)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var apply = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--apply") { apply = true; continue; }
                if (args[i] is not ("--promote-campaign" or "--library-path" or "--bundle-path") || i + 1 >= args.Length)
                    throw new ArgumentException("Usage: --promote-campaign ID --library-path DIRECTORY --bundle-path DIRECTORY [--apply]");
                if (!values.TryAdd(args[i], args[++i])) throw new ArgumentException("Duplicate command argument.");
            }
            if (!values.TryGetValue("--promote-campaign", out var id) || !values.TryGetValue("--library-path", out var local) || !values.TryGetValue("--bundle-path", out var bundle))
                throw new ArgumentException("Campaign ID, local directory and bundled directory are required. Without --apply this command only previews changes.");
            Console.WriteLine(JsonSerializer.Serialize(Promote(local, bundle, id, apply), Json));
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or JsonException or LibraryException)
        { Console.Error.WriteLine(error.Message); return 1; }
    }

    public static LibraryPromotionResult Promote(string localDirectory, string bundledDirectory, string campaignId, bool apply = false)
    {
        var local = Path.GetFullPath(localDirectory);
        var bundle = Path.GetFullPath(bundledDirectory);
        if (string.Equals(local.TrimEnd(Path.DirectorySeparatorChar), bundle.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Local and bundled storage must be separate directories.");
        NoLinks(local); NoLinks(bundle);
        if (!Directory.Exists(local)) throw new ArgumentException("The local library directory does not exist.");
        // Preview performs no migration or filesystem writes. Apply serializes promotion commands.
        if (apply) Directory.CreateDirectory(bundle);
        var lockPath = Path.Combine(bundle, ".promotion.lock"); NoLinks(lockPath);
        using var promotionLock = apply ? new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None) : null;
        var library = new ScenarioLibrary(local, bundle, readOnly: true);
        var campaign = library.Campaign(campaignId);
        var scenarios = campaign.ScenarioIds.Distinct(StringComparer.Ordinal).Select(library.Scenario).ToArray();
        var coverStore = new LibraryCoverStore(local, bundle);
        var covers = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var cover in scenarios.Select(s => s.CoverArt).Append(campaign.CoverArt).Where(c => c is not null).Distinct())
        {
            const string prefix = "/api/library/covers/";
            if (!cover!.StartsWith(prefix, StringComparison.Ordinal) || !LibraryCoverStore.IsCoverName(cover[prefix.Length..]))
                throw new ArgumentException($"Import cover '{cover}' through Designer or Campaign Designer before promotion. Promotion requires stored images, not external or unmanaged URLs.");
            var name = cover[prefix.Length..];
            var image = coverStore.Read(name) ?? throw new ArgumentException($"Cover '{name}' is missing. Restore or replace it before promotion.");
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(image.Bytes)) + ".png", name, StringComparison.Ordinal))
                throw new InvalidDataException($"Cover '{name}' does not match its content hash.");
            covers.Add(name, image.Bytes);
        }
        var path = Path.Combine(bundle, "library.json"); NoLinks(path);
        var document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException("Bundled library is empty.") : new JsonObject { ["version"] = 1, ["scenarios"] = new JsonArray(), ["campaigns"] = new JsonArray() };
        // Library loading above validates the existing bundle before we construct its replacement.
        var scenarioRecords = document["scenarios"]!.AsArray();
        var campaignRecords = document["campaigns"]!.AsArray();
        foreach (var scenario in scenarios) Upsert(scenarioRecords, scenario.Id, JsonSerializer.SerializeToNode(new
        { scenario.Id, scenario.Name, scenario.CoverArt, scenario.Revision, scenario.Definition }, Json)!);
        Upsert(campaignRecords, campaign.Id, JsonSerializer.SerializeToNode(new
        { campaign.Id, campaign.Name, campaign.Description, campaign.CoverArt, campaign.ScenarioIds, campaign.Revision }, Json)!);
        var bundleBytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString(Json) + "\n");
        if (bundleBytes.Length > 64_000_000) throw new ArgumentException("The promoted bundled catalog would exceed the 64 MB library limit.");
        var released = 0;
        if (apply)
        {
            var coverDirectory = Path.Combine(bundle, "covers"); NoLinks(coverDirectory);
            if (covers.Count > 0) Directory.CreateDirectory(coverDirectory);
            foreach (var (name, bytes) in covers)
            {
                var target = Path.Combine(coverDirectory, name); NoLinks(target);
                if (File.Exists(target))
                {
                    if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException($"Bundled cover '{name}' has different bytes.");
                }
                else AtomicWrite(target, bytes);
            }
            AtomicWrite(path, bundleBytes);
            // Only release overrides still identical to these exact bundled records. Concurrent edits stay local.
            released = new ScenarioLibrary(local, bundle).ReleasePromoted(scenarios, [campaign]);
        }
        return new(campaign.Id, scenarios.Select(s => s.Id).ToArray(), covers.Keys.Order().ToArray(), apply, released);
    }

    private static void Upsert(JsonArray records, string id, JsonNode value)
    {
        var index = records.Select((record, i) => (record, i)).FirstOrDefault(p => p.record?["id"]?.GetValue<string>() == id).i;
        if (records.Any(record => record?["id"]?.GetValue<string>() == id)) records[index] = value;
        else records.Add(value);
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void NoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Promotion paths must not contain symbolic links or junctions.");
    }
}
