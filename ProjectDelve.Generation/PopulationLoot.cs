using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Setup-only supply planning. The Engine retains its ordinary finite draw pile.</summary>
public static class PopulationLoot
{
    internal static LootSettings FromPreset(string preset) => preset switch
    {
        "Empty" => LootSettings.Empty,
        "Classic" or "Fun" or "Experimental" or "FarOut" or "All" => new() { Preset = preset },
        _ => throw new ArgumentException($"Unknown world Loot preset '{preset}'.")
    };

    public static LootSettings Provision(LootSettings settings, int markers, int expectedDrops, ContentBundle content)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(content);
        if (markers < 0 || expectedDrops < 0) throw new ArgumentException("Loot supply counts cannot be negative.");
        var normalized = settings.Normalize(content.LootPresets);
        _ = normalized.ResolveComposition(content.LootCards, content.LootPresets);
        // Custom cards belong to the author, including the empty Custom opt-out.
        if (normalized.Composition is not null) return normalized;
        var rows = (normalized with { PresetMultiplier = null }).ResolveComposition(content.LootCards, content.LootPresets);
        var size = rows.Sum(r => r.Count);
        if (size == 0) return normalized;
        var limit = Math.Min(100, Math.Min(6000 / size, 1000 / rows.Max(r => r.Count)));
        var requiredCopies = ((long)markers + expectedDrops + size - 1) / size;
        if (requiredCopies > limit)
            throw new ArgumentException("Planned Loot consumption exceeds the supported finite preset supply; no markers were removed.");
        var required = (int)requiredCopies;
        var multiplier = Math.Max(normalized.PresetMultiplier ?? 1, Math.Max(1, required));
        return multiplier == (normalized.PresetMultiplier ?? 1) ? normalized : normalized with { PresetMultiplier = multiplier };
    }
}
