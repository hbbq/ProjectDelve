using ProjectDelve.Engine;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ProjectDelve.Generation;

public enum RouteNetworkMode { PrimaryRoutes, SiteConnections, ExplorationLinks }
public enum RouteProfileKind { ForestTrail, MinorPath, Road, RockPassage }
public sealed record RouteSettings(bool Enabled = true, ContentDensity ThroughRoutes = ContentDensity.Low, RouteProfileKind Profile = RouteProfileKind.MinorPath,
    bool ConnectSites = true, ContentDensity Alternatives = ContentDensity.Low, bool WaterCrossings = false, int Intensity = 2)
{
    public RouteSettings Validate()
    {
        if (!Enum.IsDefined(ThroughRoutes) || !Enum.IsDefined(Alternatives) || Intensity is < 1 or > 3 || !Enum.IsDefined(Profile))
            throw new ArgumentException("Routes require known route densities and intensity 1..3.");
        return this;
    }
}
public sealed record RouteNetworkStepParameters(RouteNetworkMode Mode, RouteSettings Settings) : WorldStepParameters;
public sealed record ConstructionBatch(string StepId, string CheckpointStepId, int FirstOperation, int EndOperation);

public static class RouteSeed
{
    public static Pcg32 Stream(ulong root, string path, string purpose, int attempt = 0)
    {
        GenerationScope.ValidKey(purpose);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"WLD1:Routes1:{root:X16}:{path}:{purpose}:{attempt}")));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)));
    }
}

public sealed partial class WorldGenerationContext
{
    public List<ConstructionBatch> ConstructionBatches { get; } = [];
    internal PlanningBoard EnsurePlanningBoard(ContentBundle content) => ConstructionBoard ??= new PlanningBoard(Materialize(), content);
}
