namespace ProjectDelve.Generation;

public enum CampAlertTopology { Connected, Separated }

/// <summary>Authoring geometry for a population role; not a Unit placement or gameplay rule.</summary>
public sealed record SitePopulationArea(string Id, string Role, GeneratedArea Area, int AnchorIndex, string ClusterId);

/// <summary>Site planning preferences resolved against authoritative placement and Alert rules by population planning.</summary>
public sealed record SitePopulationIntent(string Layout, IReadOnlyList<SitePopulationArea> Areas,
    IReadOnlyList<int> PatrolPath, int MinimumUnits, int MaximumUnits, CampAlertTopology AlertTopology)
{
    public IReadOnlyList<SiteEncounterRegion> EncounterRegions { get; init; } = [];
}

/// <summary>Independently optional occupation, checked against all existing Alert relays.
/// Preferences select existing content; they never alter Unit or Group rules.</summary>
public sealed record SiteEncounterRegion(string Id, GeneratedArea Area, int MinimumUnits, int MaximumUnits,
    IReadOnlyList<string> PreferredUnitTypes, IReadOnlyList<int> PatrolPath)
{
    public CampAlertTopology AlertTopology { get; init; } = CampAlertTopology.Connected;
}
