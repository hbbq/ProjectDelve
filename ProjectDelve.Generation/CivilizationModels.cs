using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public enum GeneratedSiteKind { Campsite, Settlement, MountainDungeon }
public enum MountainDungeonProfile { TunnelNetwork, ChamberComplex, DeepPassage }
public sealed record MountainDungeonInfo(MountainDungeonProfile Profile, IReadOnlyList<RoomInfo> Rooms,
    GeneratedArea Corridors, GeneratedArea ProtectedRock, GeneratedArea RequiredAccess, GeneratedArea Lava,
    IReadOnlyList<GeneratedArea> TreasureAreas, int RockMassArea, int RockMassAnchor, int RoomCapacity);
public enum SettlementFamily { MarketVillage, RoadsideHamlet, Farmstead, InnCompound }
public enum GeneratedObjectKind { Site, Building, Room, Space }
public enum SiteTag { Camp, Residential, Constructed, ArtificialIsland }
public enum PlacementFailureCode { InvalidRequest, InvalidContent, NoFootprint, ReservationConflict, PreparationLimit, EntranceConstraint, DisconnectedSpace, BudgetExhausted, ValidationFailed }
public sealed record PlacementFailure(string RequestId, string Generator, PlacementFailureCode Code, string Reason, int Attempts);
public abstract record PlacementResult<T> where T : class;
public sealed record Planned<T>(T Plan) : PlacementResult<T> where T : class;
public sealed record Rejected<T>(PlacementFailure Failure) : PlacementResult<T> where T : class;
public sealed record ScenarioRoleBinding(string RoleId, GeneratedObjectKind ObjectKind, string ObjectId);
public sealed record CellSpan(int Y, int X, int Length);
public sealed record GeneratedArea(string Id, CellBounds Bounds, IReadOnlyList<CellSpan> Spans, int Area)
{
    public IEnumerable<int> Indices(int width) => Spans.SelectMany(s => Enumerable.Range(s.Y * width + s.X, s.Length));
    public bool Contains(int index, int width) => Spans.Any(s => index / width == s.Y && index % width >= s.X && index % width < s.X + s.Length);
    public static GeneratedArea From(string id, IEnumerable<int> cells, int width)
    {
        var sorted = cells.Distinct().Order().ToArray();
        if (sorted.Length == 0) return new(id, new(0, 0, 0, 0), [], 0);
        List<CellSpan> spans = [];
        foreach (var row in sorted.GroupBy(i => i / width))
        {
            var start = -1; var previous = -2;
            foreach (var i in row)
            {
                var x = i % width;
                if (x != previous + 1) { if (start >= 0) spans.Add(new(row.Key, start, previous - start + 1)); start = x; }
                previous = x;
            }
            spans.Add(new(row.Key, start, previous - start + 1));
        }
        var left = sorted.Min(i => i % width); var top = sorted[0] / width;
        return new(id, new(left, top, sorted.Max(i => i % width) - left + 1, sorted[^1] / width - top + 1), spans, sorted.Length);
    }
}
public sealed record SitePortal(string Id, string OwnerId, int InsideIndex, int OutsideIndex, EdgeKey Edge, EdgeKind Kind, int Width = 1);
public sealed record SpaceAccess(int UsableCells, int ConnectedCells, bool InternallyConnected, bool RequiresExternalConnection, int TwoByTwoPlacements);
public sealed record RoomInfo(string Id, string Purpose, GeneratedArea Interior, GeneratedArea Usable, IReadOnlyList<string> PortalIds, int TwoByTwoPlacements);
public sealed record BuildingInfo(string Id, string Purpose, GeneratedArea Footprint, IReadOnlyList<RoomInfo> Rooms,
    IReadOnlyList<SitePortal> Portals, SpaceAccess Access, IReadOnlyList<ScenarioRoleBinding> Roles);
public sealed record SettlementLotInfo(string Id, CellBounds Bounds, string Purpose, string BuildingId);
public sealed record SettlementPublicSpace(string Id, string Role, GeneratedArea Area);
public sealed record SettlementLayoutInfo(SettlementFamily Family, IReadOnlyList<SettlementLotInfo> Lots,
    IReadOnlyList<SettlementPublicSpace> PublicSpaces, GeneratedArea Access, IReadOnlyList<string> FallbackReasons);
public sealed record PreparationSummary(int TreesRemoved, int RockExcavated, int WaterFilled, int ChangedTerrain, int ChangedEdges, int Cost)
{
    public static PreparationSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);
}
public sealed record SiteInfo(string Id, GeneratedSiteKind Kind, GeneratedArea Footprint, GeneratedArea Exterior,
    IReadOnlyList<BuildingInfo> Buildings, IReadOnlyList<SitePortal> Entrances, SpaceAccess Access,
    IReadOnlyList<SiteTag> Tags, IReadOnlyList<ScenarioRoleBinding> Roles, IReadOnlyList<string> SourceFormationIds,
    string? SourceOpportunityId, PreparationSummary Preparation)
{
    public IReadOnlyList<SiteAccessPort> AccessPorts { get; init; } = [];
    public IReadOnlyList<SiteConnectionRequest> ConnectionRequests { get; init; } = [];
    public SitePopulationIntent? PopulationIntent { get; init; }
    public SettlementLayoutInfo? SettlementLayout { get; init; }
    public MountainDungeonInfo? MountainDungeon { get; init; }
}
public sealed record ConstructionOperation(string Id, string? ParentId, string Name, string ObjectId, CellBounds Bounds,
    IReadOnlyList<TerrainChange> TerrainChanges, IReadOnlyList<EdgeChange> EdgeChanges);
public sealed record SitePlan(BoardPatch Patch, SiteInfo Site, IReadOnlyList<ConstructionOperation> Operations);
public sealed record BuildingPlan(BoardPatch Patch, BuildingInfo Building, IReadOnlyList<ConstructionOperation> Operations);
public sealed record RoomLayoutPlan(BoardPatch Patch, IReadOnlyList<RoomInfo> Rooms, IReadOnlyList<SitePortal> Portals,
    IReadOnlyList<ConstructionOperation> Operations);

public sealed record PreparationPolicy(bool ClearTrees = true, bool ExcavateRock = true, bool FillWater = true,
    bool ReplaceExistingEdges = false, int MaxChangedCells = 1200, int MaxCost = 8000)
{
    public PreparationPolicy Validate()
    {
        if (MaxChangedCells < 0 || MaxCost < 0) throw new ArgumentException("Preparation allowances cannot be negative.");
        return this;
    }
}

/// <summary>Authoring-only nested call scope. Failed drafts never refund this shared work budget.</summary>
public sealed class GenerationScope(ulong rootSeed, string path, BoardDraft draft)
{
    public ulong RootSeed { get; } = rootSeed;
    public string Path { get; } = ValidPath(path);
    internal BoardDraft Draft { get; } = draft ?? throw new ArgumentNullException(nameof(draft));
    public IPlanningBoardView Board => Draft;
    public ContentBundle Content => Draft.Content;
    public PlanningBudget Budget => Draft.Budget;
    public string ObjectId(string key) => Path + "/" + ValidKey(key);
    public GenerationScope Child(string key, BoardDraft child) => new(RootSeed, ObjectId(key), child);
    public Pcg32 Random(string purpose, int attempt = 0)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"CIV1:{RootSeed:X16}:{Path}:{ValidKey(purpose)}:{attempt}")));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)));
    }
    public static string ValidKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 64 &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? key : throw new ArgumentException("Generator keys must be stable ASCII identifiers.");
    private static string ValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 400) throw new ArgumentException("A bounded generator path is required.");
        foreach (var part in path.Split('/')) ValidKey(part);
        return path;
    }
}

internal static class SiteGeometry
{
    internal sealed record PreparationProblem(PlacementFailureCode Code, string Reason);
    internal static IEnumerable<int> Cells(CellBounds b, int width) => Enumerable.Range(b.Y, b.Height).SelectMany(y => Enumerable.Range(y * width + b.X, b.Width));
    internal static bool Fits(CellBounds inner, CellBounds outer) => inner.Width > 0 && inner.Height > 0 && inner.X >= outer.X && inner.Y >= outer.Y &&
        (long)inner.X + inner.Width <= (long)outer.X + outer.Width && (long)inner.Y + inner.Height <= (long)outer.Y + outer.Height;
    internal static CellBounds Pad(CellBounds inner, CellBounds outer, int padding)
    {
        var left = Math.Max(outer.X, inner.X - padding); var top = Math.Max(outer.Y, inner.Y - padding);
        return new(left, top, Math.Min(outer.X + outer.Width, inner.X + inner.Width + padding) - left,
            Math.Min(outer.Y + outer.Height, inner.Y + inner.Height + padding) - top);
    }
    internal static bool Crossable(BoardDraft d, int a, int b) => d.Passable(a) && d.Passable(b) &&
        (d.EdgeAt(EdgeKey.Between(a, b, d.Width)).Passable() || d.EdgeAt(EdgeKey.Between(a, b, d.Width)) == EdgeKind.ClosedDoor);
    internal static HashSet<int>? Flood(BoardDraft d, int start, IEnumerable<int>? restricted = null)
    {
        HashSet<int>? allowed = restricted?.ToHashSet(); HashSet<int> visited = []; Queue<int> queue = new();
        if (!d.Passable(start) || allowed is not null && !allowed.Contains(start)) return visited;
        visited.Add(start); queue.Enqueue(start);
        while (queue.TryDequeue(out var i))
        {
            if (!d.Budget.Spend(1)) return null;
            foreach (var j in d.Neighbors(i)) if ((allowed is null || allowed.Contains(j)) && Crossable(d, i, j) && visited.Add(j)) queue.Enqueue(j);
        }
        return visited;
    }
    internal static int TwoByTwo(BoardDraft d, IEnumerable<int> cells)
    {
        var set = cells.ToHashSet(); var count = 0;
        foreach (var i in set)
        {
            if (i % d.Width == d.Width - 1 || i / d.Width == d.Height - 1) continue;
            int[] quad = [i, i + 1, i + d.Width, i + d.Width + 1];
            if (quad.All(j => set.Contains(j) && d.Passable(j)) &&
                new[] { (quad[0],quad[1]),(quad[0],quad[2]),(quad[1],quad[3]),(quad[2],quad[3]) }.All(e => d.EdgeAt(EdgeKey.Between(e.Item1,e.Item2,d.Width)).Passable())) count++;
        }
        return count;
    }
    internal static ConstructionOperation Operation(BoardDraft d, BoardPatch before, string id, string? parent, string name, string objectId, CellBounds bounds)
    {
        var after = d.BuildPatch();
        return new(id, parent, name, objectId, bounds,
            before.TerrainChanges.Select(c => c.Index).Concat(after.TerrainChanges.Select(c => c.Index)).Distinct().Order()
                .Select(i => new TerrainChange(i, before.Result.TerrainAt(i), after.Result.TerrainAt(i))).Where(c => c.Before != c.After).ToArray(),
            before.EdgeChanges.Select(c => c.Key).Concat(after.EdgeChanges.Select(c => c.Key)).Distinct().OrderBy(k => k.Index).ThenBy(k => k.Direction)
                .Select(k => new EdgeChange(k, before.Result.EdgeAt(k), after.Result.EdgeAt(k))).Where(c => c.Before != c.After).ToArray());
    }
    internal static PreparationSummary Summary(BoardPatch patch)
    {
        var trees = patch.TerrainChanges.Count(c => c.Before is "tree" or "fruit-tree" && c.After != c.Before);
        var rock = patch.TerrainChanges.Count(c => c.Before == "solid-rock" && c.After != c.Before);
        var water = patch.TerrainChanges.Count(c => c.Before == "water" && c.After != c.Before);
        return new(trees, rock, water, patch.TerrainChanges.Count, patch.EdgeChanges.Count,
            patch.TerrainChanges.Sum(c => c.Before is "tree" or "fruit-tree" ? 2 : c.Before == "solid-rock" ? 4 : c.Before == "water" ? 6 : 1));
    }
    internal static PreparationProblem? Prepare(BoardDraft d, IEnumerable<int> cells, string floor, PreparationPolicy policy)
    {
        policy.Validate(); var distinct = cells.Distinct().ToArray();
        if (!d.Budget.Spend(distinct.Length)) return new(PlacementFailureCode.BudgetExhausted, "Shared planning budget exhausted.");
        var changes = distinct.Where(i => d.TerrainAt(i) != floor).ToArray();
        if (distinct.Any(i => !d.CanWrite(i))) return new(PlacementFailureCode.ReservationConflict, "Reserved or protected construction area.");
        if (changes.Length > policy.MaxChangedCells || changes.Sum(i => d.TerrainAt(i) is "tree" or "fruit-tree" ? 2 : d.TerrainAt(i) == "solid-rock" ? 4 : d.TerrainAt(i) == "water" ? 6 : 1) > policy.MaxCost) return new(PlacementFailureCode.PreparationLimit, "Preparation allowance exceeded.");
        foreach (var i in changes)
        {
            if (!d.CanWrite(i)) return new(PlacementFailureCode.ReservationConflict, "Reserved or protected construction area.");
            var id = d.TerrainAt(i);
            if (id is "tree" or "fruit-tree" && !policy.ClearTrees || id == "solid-rock" && !policy.ExcavateRock || id == "water" && !policy.FillWater) return new(PlacementFailureCode.PreparationLimit, "Requested terrain preparation is disabled.");
            if (!d.Passable(i) && id is not ("tree" or "fruit-tree" or "solid-rock" or "water")) return new(PlacementFailureCode.ReservationConflict, "Existing fixed geometry is protected.");
        }
        var cellSet = distinct.ToHashSet();
        foreach (var i in distinct) foreach (var j in d.Neighbors(i).Where(cellSet.Contains))
        {
            if (j <= i) continue; var key = EdgeKey.Between(i, j, d.Width);
            if (d.EdgeAt(key) != EdgeKind.None)
            {
                if (!policy.ReplaceExistingEdges) return new(PlacementFailureCode.ReservationConflict, "Existing Edge replacement is not authorized.");
                d.ReplaceEdge(key, EdgeKind.None);
            }
        }
        foreach (var i in changes) d.ReplaceTerrain(i, floor);
        return null;
    }
}
