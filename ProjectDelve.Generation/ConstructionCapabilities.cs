using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record ConstructionConversion(int Index, string Before, string After);
internal sealed record ConstructionInterface(string Id, int InsideIndex, int OutsideIndex, string ExactOwnerId);
internal sealed record ConstructionCellProtection(int Index, string SourceId, string Reason, string? OwnerId = null);
internal sealed record ConstructionEdgeProtection(EdgeKey Key, string SourceId, string Reason, string? OwnerId = null);
internal sealed record ConstructionProtections(IReadOnlyCollection<ConstructionCellProtection> Cells,
    IReadOnlyCollection<ConstructionEdgeProtection> Edges)
{
    /// <summary>Preserve authored shells and structures independently of reservation ownership or grants.
    /// This enumerates protection facts only; it makes no completeness claim about Site access ports.</summary>
    internal static ConstructionProtections FromSites(IEnumerable<SiteInfo> sites, int width)
    {
        var orderedSites = sites.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var cells = orderedSites.SelectMany(site =>
            (site.MountainDungeon is { } dungeon
                ? dungeon.ProtectedRock.Indices(width).Select(i => new ConstructionCellProtection(i,
                    dungeon.ProtectedRock.Id, "Immutable Mountain Dungeon rock shell", site.Id)) : Enumerable.Empty<ConstructionCellProtection>())
            .Concat(site.Buildings.OrderBy(b => b.Id, StringComparer.Ordinal).SelectMany(building =>
                building.Footprint.Indices(width).Select(i => new ConstructionCellProtection(i,
                    building.Id, "Immutable constructed Building", site.Id))))).ToImmutableArray();
        var edges = orderedSites.Where(site => site.Outpost is not null).SelectMany(site =>
            site.Outpost!.Perimeter.Select(edge => new ConstructionEdgeProtection(edge, site.Id,
                "Immutable fortified outpost perimeter", site.Id))).ToImmutableArray();
        // Cell protection also preserves all incident Edges. The snapshot resolves board bounds.
        return new(cells, edges);
    }
}

/// <summary>Opt-in, revision-bound construction facts, separate from ordinary movement permission.
/// Only explicit monotone declarations enlarge geometry. Existing protected land remains legal transit.</summary>
internal sealed class ConstructionCapabilities
{
    internal PlanningBoard Source { get; }
    internal string OwnerId { get; }
    internal ImmutableArray<RouteAccessGrant> Grants { get; }
    internal AccessibilityAnalysis MaximalGeometry { get; }
    internal AccessibilityAnalysis CurrentGeometry { get; }
    private readonly Board currentBoard;
    private readonly Board maximalBoard;
    internal Board CurrentBoard => CopyBoard(currentBoard);
    internal Board MaximalBoard => CopyBoard(maximalBoard);
    internal static readonly AccessibilityProfile OrdinaryLand = new(Footprint.OneByOne, [MovementKinds.Land], 1);
    public long Revision { get; }
    public int Width { get; }
    public int Height { get; }
    public bool Complete { get; }
    public string? Error { get; }
    public ImmutableArray<ConstructionConversion> Conversions { get; }
    public ImmutableArray<ConstructionInterface> Interfaces { get; }
    public ImmutableHashSet<int> WritableCells { get; }
    public ImmutableHashSet<EdgeKey> WritableEdges { get; }
    public ConstructionProtections Protections { get; }
    private readonly ImmutableDictionary<int, ConstructionCellProtection> protectedCells;
    private readonly ImmutableDictionary<EdgeKey, ConstructionEdgeProtection> protectedEdges;
    private readonly ImmutableArray<string> terrain;
    private readonly ImmutableDictionary<int, string> owners;
    private readonly ImmutableHashSet<int> machines;
    private readonly ImmutableDictionary<EdgeKey, EdgeKind> edges;

    private ConstructionCapabilities(PlanningBoard board, string ownerId,
        IEnumerable<ConstructionConversion> declarations, IEnumerable<RouteAccessGrant>? grants,
        IEnumerable<ConstructionInterface>? interfaces, bool conversionsComplete, bool interfacesComplete,
        ConstructionProtections? protections)
    {
        Source = board; OwnerId = ownerId; Revision = board.Revision; Width = board.Width; Height = board.Height;
        terrain = Enumerable.Range(0, Width * Height).Select(board.TerrainAt).ToImmutableArray();
        owners = board.State.Reservations.ToImmutableDictionary();
        machines = board.State.Machines.Select(m => m.Position.Y * Width + m.Position.X).ToImmutableHashSet();
        edges = board.State.Edges.ToImmutableDictionary();
        Protections = new(protections?.Cells.ToImmutableArray() ?? [], protections?.Edges.ToImmutableArray() ?? []);
        protectedCells = Protections.Cells.GroupBy(p => p.Index).ToImmutableDictionary(g => g.Key, g => g.OrderBy(p => p.SourceId, StringComparer.Ordinal).First());
        protectedEdges = Protections.Edges.GroupBy(p => p.Key).ToImmutableDictionary(g => g.Key, g => g.OrderBy(p => p.SourceId, StringComparer.Ordinal).First());
        // Never retain caller-owned collections, including nested grant collections.
        Grants = (grants ?? []).Select(g => new RouteAccessGrant(g.ExactOwnerId,
            g.TraverseCells.ToImmutableArray(), g.WritableCells.ToImmutableArray(), g.WritableEdges.ToImmutableArray())).ToImmutableArray();
        Interfaces = (interfaces ?? []).ToImmutableArray();
        var supplied = declarations.ToArray();
        Conversions = supplied.Distinct().OrderBy(c => c.Index).ThenBy(c => c.After, StringComparer.Ordinal).ToImmutableArray();
        Complete = conversionsComplete && interfacesComplete;
        var writable = ImmutableHashSet.CreateBuilder<int>();
        var writableEdges = ImmutableHashSet.CreateBuilder<EdgeKey>();
        var maximal = new Board(Width, Height, edges.Select(e => new Edge(CellAt(e.Key.Index),
            CellAt(e.Key.Index + (e.Key.Direction == EdgeDirection.Right ? 1 : Width)),
            e.Value == EdgeKind.ClosedDoor ? EdgeKind.OpenDoor : e.Value)).ToList(), board.Content.Terrain)
        {
            Terrain = terrain.Select((id, i) => new TerrainTile(CellAt(i)) { TypeId = id }).ToList(),
            Machines = [.. board.State.Machines]
        };
        currentBoard = maximal with { Edges = maximal.Edges.Select(e => e with
            { Kind = EdgeAt(EdgeKey.Between(e.A.Y * Width + e.A.X, e.B.Y * Width + e.B.X, Width)) }).ToList(),
            Terrain = [.. maximal.Terrain], Machines = [.. maximal.Machines] };
        // Potential ordinary Doors exist only in the analysis, never in an emitted patch.
        CurrentGeometry = AccessibilityAnalysis.Capture(currentBoard with
            { Edges = currentBoard.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() }, Revision);
        try
        {
            var draft = board.BeginDraft(new(0, 0, Width, Height), ownerId, new());
            draft.EnableRoutePlanning(Grants);
            foreach (var protection in Protections.Cells)
            { board.CheckIndex(protection.Index); ValidateProtection(protection.SourceId, protection.Reason); }
            foreach (var protection in Protections.Edges)
            { board.CheckEdge(protection.Key); ValidateProtection(protection.SourceId, protection.Reason); }
            foreach (var i in Enumerable.Range(0, Width * Height))
                if (!protectedCells.ContainsKey(i) && draft.CanWrite(i)) writable.Add(i);
            foreach (var i in Enumerable.Range(0, Width * Height))
            foreach (var j in board.Neighbors(i).Where(j => j > i))
            {
                var key = EdgeKey.Between(i, j, Width);
                // Structural Edges are immutable in this construction model, even with an Edge grant.
                if (board.EdgeAt(key) != EdgeKind.None || EdgeProtection(key) is not null) continue;
                try { draft.SetEdge(key, EdgeKind.None); writableEdges.Add(key); }
                catch (ArgumentException) { }
            }
            if (Conversions.GroupBy(c => c.Index).Any(g => g.Count() != 1))
                throw new ArgumentException("Incompatible conversion declarations target the same Cell.");
            foreach (var conversion in Conversions)
            {
                board.CheckIndex(conversion.Index);
                if (terrain[conversion.Index] != conversion.Before)
                    throw new ArgumentException("Conversion does not match captured terrain.");
                if (!LegalConversion(conversion)) throw new ArgumentException("Unsupported monotone conversion declaration.");
                if (CellProtection(conversion.Index) is { } protection)
                    throw new ArgumentException($"{protection.Reason} ({protection.SourceId}) forbids terrain conversion regardless of owner or grant.");
                var before = TerrainCatalog.Find(board.Content.Terrain, conversion.Before);
                var after = TerrainCatalog.Find(board.Content.Terrain, conversion.After);
                if (!after.MovementPermissions.Contains(MovementKinds.Land) ||
                    before.MovementPermissions.Any(kind => !after.MovementPermissions.Contains(kind)) ||
                    conversion.Before != "water" && !before.MovementPermissions.IsEmpty ||
                    conversion.Before == "water" && !before.MovementPermissions.Contains(MovementKinds.Water))
                    throw new ArgumentException("Conversion definitions do not preserve monotone ordinary terrain movement permissions.");
                if (!writable.Contains(conversion.Index)) throw new ArgumentException("Conversion lacks exact-owner write permission.");
                draft.SetTerrain(conversion.Index, conversion.After);
                maximal.Terrain[conversion.Index] = new(CellAt(conversion.Index)) { TypeId = conversion.After };
            }
            if (Interfaces.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != Interfaces.Length)
                throw new ArgumentException("Duplicate construction interface ID.");
            foreach (var port in Interfaces)
            {
                board.CheckIndex(port.InsideIndex); board.CheckIndex(port.OutsideIndex);
                var key = EdgeKey.Between(port.InsideIndex, port.OutsideIndex, Width);
                if (string.IsNullOrWhiteSpace(port.Id) || ReservationOwner(port.InsideIndex) != port.ExactOwnerId ||
                    port.ExactOwnerId.Contains('\0')) throw new ArgumentException("Interface must identify its exact reserved owner.");
                if (board.EdgeAt(key) is EdgeKind.Wall or EdgeKind.WallWithWindow)
                    throw new ArgumentException("Interface crosses an immutable blocking Edge.");
            }
            var maximalAnalysis = AccessibilityAnalysis.Capture(maximal, Revision);
            foreach (var conversion in Conversions)
            foreach (var neighbor in board.Neighbors(conversion.Index))
            {
                if (!maximalAnalysis.CanStep(OrdinaryLand, CellAt(conversion.Index), CellAt(neighbor))) continue;
                foreach (var pair in new[] { (Inside: neighbor, Outside: conversion.Index), (Inside: conversion.Index, Outside: neighbor) })
                {
                    var foreign = ReservationOwner(pair.Inside);
                    if (foreign is null || foreign == OwnerId || foreign == ReservationOwner(pair.Outside)) continue;
                    if (!Interfaces.Any(p => p.ExactOwnerId == foreign &&
                            p.InsideIndex == pair.Inside && p.OutsideIndex == pair.Outside) ||
                        !Grants.Any(g => g.ExactOwnerId == foreign && g.TraverseCells.Contains(pair.Inside)))
                        throw new ArgumentException("Conversion would create an undeclared foreign-owner boundary opening.");
                }
            }
            // Full combined patch preflight preserves the shared typed validation boundary and actual Edges.
            _ = GameEngine.CreateGame(draft.Export(), board.Provider);
        }
        catch (ArgumentException exception) { Error = exception.Message; }
        WritableCells = writable.ToImmutable(); WritableEdges = writableEdges.ToImmutable();
        maximalBoard = CopyBoard(maximal with { Edges = [.. currentBoard.Edges] });
        MaximalGeometry = AccessibilityAnalysis.Capture(maximal, Revision);
    }

    public static ConstructionCapabilities Capture(PlanningBoard board, string ownerId,
        IEnumerable<ConstructionConversion> conversions, IEnumerable<RouteAccessGrant>? grants = null,
        IEnumerable<ConstructionInterface>? interfaces = null, bool conversionsComplete = false, bool interfacesComplete = false,
        ConstructionProtections? protections = null) =>
        new(board, ownerId, conversions, grants, interfaces, conversionsComplete, interfacesComplete, protections);

    private static void ValidateProtection(string sourceId, string reason)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Immutable protection requires an explicit source and reason.");
    }

    private static bool LegalConversion(ConstructionConversion conversion) => conversion.Before switch
    {
        "tree" or "fruit-tree" or "solid-rock" => conversion.After is "grass" or "stone-floor",
        "water" => conversion.After == "stepping-stones",
        _ => false
    };
    public string TerrainAt(int index) => terrain[index];
    public string? ReservationOwner(int index) => owners.GetValueOrDefault(index);
    public bool HasMachine(int index) => machines.Contains(index);
    public EdgeKind EdgeAt(EdgeKey key) => edges.GetValueOrDefault(key, EdgeKind.None);
    internal ConstructionCellProtection? CellProtection(int index) => protectedCells.GetValueOrDefault(index);
    internal ConstructionEdgeProtection? EdgeProtection(EdgeKey key)
    {
        if (protectedEdges.TryGetValue(key, out var protection)) return protection;
        var cell = CellProtection(key.Index) ?? CellProtection(key.Index + (key.Direction == EdgeDirection.Right ? 1 : Width));
        return cell is null ? null : new(key, cell.SourceId, cell.Reason, cell.OwnerId);
    }
    internal Cell CellAt(int index) => new(index % Width, index / Width);
    internal bool IsCurrent(PlanningBoard board) => ReferenceEquals(Source, board) && Revision == board.Revision;
    internal ImmutableArray<ConstructionConversion> ActionsFor(IEnumerable<int> indices)
    {
        var selected = indices.ToHashSet();
        return Conversions.Where(c => selected.Contains(c.Index)).ToImmutableArray();
    }
    internal bool TryPrepareCells(IEnumerable<int> indices, PlanningBoard board, PlanningBudget budget,
        out BoardDraft? prepared, out string? error)
    {
        prepared = null;
        if (Error is not null) { error = Error; return false; }
        if (!IsCurrent(board)) { error = "Stale or unrelated construction snapshot."; return false; }
        try
        {
            var selected = indices.Distinct().ToArray();
            foreach (var index in selected)
            {
                board.CheckIndex(index);
                if (!MaximalGeometry.CanStop(OrdinaryLand, CellAt(index)))
                    throw new ArgumentException("Selected Cell has no compatible declared land preparation.");
            }
            var draft = board.BeginDraft(new(0, 0, Width, Height), OwnerId, budget);
            draft.EnableRoutePlanning(Grants);
            foreach (var action in ActionsFor(selected))
            {
                if (!budget.Spend(1)) { error = "Construction preparation work budget exhausted."; return false; }
                draft.SetTerrain(action.Index, action.After);
            }
            _ = GameEngine.CreateGame(draft.Export(), board.Provider);
            prepared = draft; error = null; return true;
        }
        catch (ArgumentException exception) { error = exception.Message; return false; }
    }
    private static Board CopyBoard(Board board) => board with
        { Terrain = [.. board.Terrain], Edges = [.. board.Edges], Machines = [.. board.Machines] };
}
