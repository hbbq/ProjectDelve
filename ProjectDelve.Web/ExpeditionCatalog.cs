using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

// Only catalog metadata is exposed to browsers. Opening a game still verifies its checkpoint digest.
public sealed class ExpeditionCatalog
{
    private const string DirectoryPrefix = "expedition-";
    private readonly string root;
    private readonly Func<ContentBundle> latest;
    private readonly ILogger<ExpeditionCatalog>? logger;
    private readonly Func<ulong, ExpeditionRegionPlan, ContentBundle, ScenarioDefinition>? generate;

    internal ExpeditionCatalog(string root, Func<ContentBundle> latest, ILogger<ExpeditionCatalog>? logger = null,
        Func<ulong, ExpeditionRegionPlan, ContentBundle, ScenarioDefinition>? generate = null)
    {
        this.root = Path.GetFullPath(root);
        this.latest = latest;
        this.logger = logger;
        this.generate = generate;
    }

    internal ExpeditionRunner Create(string? name, string? seed, string? recipe)
    {
        var displayName = ParseName(name);
        var worldSeed = string.IsNullOrWhiteSpace(seed)
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8))
            : ParseSeed(seed);
        var settings = ParseRecipe(recipe);
        var id = Guid.NewGuid().ToString("N");
        if (generate is null)
            return ExpeditionRunner.Create(root, id, worldSeed, latest, settings, displayName: displayName);

        var baseline = ExpeditionWorld.Configure(worldSeed, settings);
        return ExpeditionRunner.Create(new ExpeditionStore(root, id), worldSeed, latest, generate, baseline.ToString(),
            ExpeditionWorldProfile.CreateV1(baseline.NaturalTerrain), displayName);
    }

    internal static void ValidateCreate(string? name, string? seed, string? recipe)
    {
        _ = ParseName(name);
        if (!string.IsNullOrWhiteSpace(seed)) _ = ParseSeed(seed);
        _ = ParseRecipe(recipe);
    }

    internal ExpeditionRunner Open(string? id)
    {
        if (id is null || !ExpeditionStore.IsValidId(id))
            throw new ExpeditionInputException("Select a saved Expedition from the list.");
        var store = new ExpeditionStore(root, id);
        if (!store.HasHead)
            throw new ExpeditionInputException("That Expedition is no longer available. Refresh the saved Expeditions list.");
        return generate is null ? ExpeditionRunner.Open(root, id, latest) : ExpeditionRunner.Open(store, latest, generate);
    }

    internal ExpeditionCatalogView List()
    {
        CanonicalContent.CheckNoLinks(root);
        if (!Directory.Exists(root)) return new([], 0);
        var summaries = new List<ExpeditionSummary>();
        var unavailable = 0;
        foreach (var path in Directory.EnumerateDirectories(root, DirectoryPrefix + "*", new EnumerationOptions
                 { AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var id = Path.GetFileName(path)[DirectoryPrefix.Length..];
            if (!ExpeditionStore.IsValidId(id)) continue;
            var store = new ExpeditionStore(root, id);
            if (!store.HasHead) continue; // An uncommitted initial checkpoint is not a saved Expedition.
            try
            {
                var head = store.ReadHead();
                summaries.Add(new(head.Id, head.DisplayName ?? head.Id, FormatSeed(head.WorldSeed), head.CurrentRegion,
                    head.Transitions.Length + (head.CurrentRegionCompleted ? 1 : 0), head.SavedAtUtc.GetValueOrDefault()));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                unavailable++;
                ExpeditionDiagnostics.LogFailure(logger, LogLevel.Warning, error, "Could not list saved Expedition {ExpeditionId}.", id);
            }
        }
        return new(summaries.OrderByDescending(entry => entry.SavedAt).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray(), unavailable);
    }

    internal static bool IsValidName(string name) => name is { Length: > 0 and <= 80 } &&
        !string.IsNullOrWhiteSpace(name) && !name.Any(character => char.IsControl(character) ||
            char.GetUnicodeCategory(character) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    internal static string FormatSeed(ulong seed) => $"0x{seed:X16}";

    private static string ParseName(string? name)
    {
        var trimmed = name?.Trim();
        if (trimmed is null || !IsValidName(trimmed))
            throw new ExpeditionInputException("Enter an Expedition name of 1 to 80 characters on one line.");
        return trimmed;
    }

    private static ulong ParseSeed(string seed)
    {
        var value = seed.AsSpan().Trim();
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex) value = value[2..];
        if (value.Length <= (hex ? 16 : 20) && ulong.TryParse(value,
                hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var result))
            return result;
        throw new ExpeditionInputException("Use a whole-number seed from 0 to 18446744073709551615, or 0x followed by up to 16 hexadecimal digits.");
    }

    private static WorldGenerationRecipe? ParseRecipe(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var code = input.Trim();
        var preset = WorldGenerationPresets.All.SingleOrDefault(candidate => candidate.Id == code);
        if (preset is not null)
            return new(new(Width: ExpeditionWorld.Stride, Height: ExpeditionWorld.Stride), preset.Settings,
                Enrichment: preset.Enrichment, Civilization: preset.Civilization, Routes: preset.Routes, Population: preset.Population);
        try { return WorldGenerationRecipe.Parse(code); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        {
            throw new ExpeditionInputException("Select a WLD1 preset or enter a valid WLD1 recipe code.");
        }
    }
}

internal sealed class ExpeditionInputException(string message) : ArgumentException(message);

internal sealed record ExpeditionSummary(string Id, string Name, string Seed, ExpeditionRegion Region, int CompletedRegions, DateTimeOffset SavedAt);
internal sealed record ExpeditionCatalogView(IReadOnlyList<ExpeditionSummary> Expeditions, int Unavailable);

internal static class ExpeditionDiagnostics
{
    internal static void LogFailure(ILogger? logger, LogLevel level, Exception error, string message, params object[] arguments)
    {
        try { logger?.Log(level, error, message, arguments); }
        catch (AggregateException providerFailure)
        {
            // A denied Windows Event Log, for example, must not prevent the
            // authoritative runner from publishing an error and accepting Retry.
            try { Console.Error.WriteLine($"Expedition diagnostic: {error}\nLogging provider failure: {providerFailure.Message}"); }
            catch (IOException) { }
        }
    }
}
