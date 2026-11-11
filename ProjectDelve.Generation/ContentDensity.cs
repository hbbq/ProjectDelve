namespace ProjectDelve.Generation;

/// <summary>Authoring abundance; independent of preparation and route intensity.</summary>
public enum ContentDensity { None, Low, Medium, High, VeryHigh }
public sealed record DensityAllocation(int Requested, int Attempted, int Accepted, string? Reason = null)
{
    public int Omitted => Requested - Accepted;
}

/// <summary>Density Scaling V1 resource policy. Counts are targets, never placement guarantees.</summary>
public static class DensityScaling
{
    public static int Campsites(WorldWindow window, ContentDensity density) => Sites(window, density, 720, 32);
    public static int Settlements(WorldWindow window, ContentDensity density) => Sites(window, density, 1440, 24);
    public static int ExplorationLinks(WorldWindow window, ContentDensity density) => Sites(window, density, 3600, 16);
    public static int ThroughRoutes(WorldWindow window, ContentDensity density)
    {
        Validate(density);
        return density == ContentDensity.None ? 0 : Math.Min(12, (int)Math.Ceiling(Math.Sqrt((long)window.Width * window.Height) * (int)density / 30));
    }
    private static int Sites(WorldWindow window, ContentDensity density, int cellsPerLow, int cap)
    {
        Validate(density);
        var effectiveArea = (long)Math.Sqrt((long)window.Width * window.Height * 720);
        return density == ContentDensity.None ? 0 : Math.Min(cap, (int)((effectiveArea * (int)density + cellsPerLow - 1) / cellsPerLow));
    }
    private static void Validate(ContentDensity density)
    {
        if (!Enum.IsDefined(density)) throw new ArgumentException("Unknown content density.");
    }
}
