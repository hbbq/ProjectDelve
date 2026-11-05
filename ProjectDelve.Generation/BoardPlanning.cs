using System.Collections.Immutable;
using System.Text.Json;
using System.Diagnostics;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public readonly record struct EdgeKey(int Index, EdgeDirection Direction)
{
    public static EdgeKey Between(int a, int b, int width)
    {
        if (width < 1 || a < 0 || b < 0) throw new ArgumentException("Invalid cell geometry.");
        var first = Math.Min(a, b); var last = Math.Max(a, b);
        if (last - first == 1 && first / width == last / width) return new(first, EdgeDirection.Right);
        if (last - first == width) return new(first, EdgeDirection.Down);
        throw new ArgumentException("Cells must be cardinal neighbors.");
    }
}

/// <summary>One monotonically consumed allowance, shared by every nested attempt.</summary>
public sealed class PlanningBudget(int maxAttempts = 128, long maxWork = 200000)
{
    public int MaxAttempts { get; } = maxAttempts >= 0 ? maxAttempts : throw new ArgumentOutOfRangeException(nameof(maxAttempts));
    public long MaxWork { get; } = maxWork >= 0 ? maxWork : throw new ArgumentOutOfRangeException(nameof(maxWork));
    public int Attempts { get; private set; }
    public long Work { get; private set; }
    public bool TryAttempt() { if (Attempts >= MaxAttempts) return false; Attempts++; return true; }
    public bool Spend(long work)
    {
        if (work < 0) throw new ArgumentOutOfRangeException(nameof(work));
        if (work > MaxWork - Work) return false;
        Work += work; return true;
    }
}

public sealed record TerrainChange(int Index, string Before, string After);
public sealed record EdgeChange(EdgeKey Key, EdgeKind Before, EdgeKind After);
public sealed record CellReservation(int Index, string OwnerId);

public interface IPlanningBoardView
{
    int Width { get; }
    int Height { get; }
    ContentBundle Content { get; }
    string TerrainAt(int index);
    EdgeKind EdgeAt(EdgeKey key);
    bool Passable(int index);
    bool IsReserved(int index, string? ownerId = null);
    IEnumerable<int> Neighbors(int index);
}

public sealed class BoardPatch
{
    internal object Target { get; }
    internal PlanningState Result { get; }
    public long ExpectedRevision { get; }
    public string OwnerId { get; }
    public IReadOnlyList<TerrainChange> TerrainChanges { get; }
    public IReadOnlyList<EdgeChange> EdgeChanges { get; }
    public IReadOnlyList<CellReservation> Reservations { get; }
    public ContentBundle LocalContent { get; }
    internal BoardPatch(object target, long revision, string owner, PlanningState before, PlanningState after,
        IEnumerable<int> terrain, IEnumerable<EdgeKey> edges, IEnumerable<int> reservations, ContentBundle local)
    {
        Target = target; ExpectedRevision = revision; OwnerId = owner; Result = after; LocalContent = local;
        TerrainChanges = Array.AsReadOnly(terrain.Distinct().Order().Where(i => before.TerrainAt(i) != after.TerrainAt(i))
            .Select(i => new TerrainChange(i, before.TerrainAt(i), after.TerrainAt(i))).ToArray());
        EdgeChanges = Array.AsReadOnly(edges.Distinct().OrderBy(k => k.Index).ThenBy(k => k.Direction)
            .Where(k => before.EdgeAt(k) != after.EdgeAt(k)).Select(k => new EdgeChange(k, before.EdgeAt(k), after.EdgeAt(k))).ToArray());
        Reservations = Array.AsReadOnly(reservations.Distinct().Order().Select(i => new CellReservation(i, after.Reservations[i])).ToArray());
    }
}

/// <summary>Indexed authoring state. Engine creation remains the final validation boundary.</summary>
public sealed class PlanningBoard : IPlanningBoardView
{
    internal PlanningState State;
    internal ScenarioDefinition Original { get; }
    internal ContentBundle Provider { get; }
    public int Width => Original.Board.Width;
    public int Height => Original.Board.Height;
    public long Revision { get; private set; }
    public ContentBundle Content => State.Content;
    public double ValidationMilliseconds { get; private set; }
    public int ValidationCount { get; private set; }
    public PlanningBoard(ScenarioDefinition scenario, ContentBundle content, IReadOnlySet<int>? protectedCells = null)
    {
        ArgumentNullException.ThrowIfNull(scenario); ArgumentNullException.ThrowIfNull(content);
        var validation = Stopwatch.StartNew();
        _ = GameEngine.CreateGame(scenario, content);
        ValidationMilliseconds += validation.Elapsed.TotalMilliseconds; ValidationCount++;
        // Detach mutable scenario lists from the caller and from subsequent exports.
        Original = Clone(scenario); Provider = content;
        var local = PlanningContent.Local(Original);
        var effective = ScenarioContentReferences.Compose(Provider, Original.ContentReferences, local).Definitions;
        var terrain = Enumerable.Repeat(TerrainCompatibility.ResolveTypeId(Original.Board.DefaultTerrain,
            Original.Board.DefaultTerrainTypeId), checked(Width * Height)).ToArray();
        foreach (var cell in Original.Board.Cells) terrain[cell.Position.Y * Width + cell.Position.X] =
            TerrainCompatibility.ResolveTypeId(cell.Terrain, cell.TerrainTypeId, cell.CoalGeneration);
        State = new(terrain, [], Original.Board.Edges.ToDictionary(e => new EdgeKey(e.Position.Y * Width + e.Position.X, e.Direction), e => e.Kind),
            [], local, effective);
        foreach (var index in protectedCells ?? new HashSet<int>()) { CheckIndex(index); State.Reservations[index] = "\0protected"; }
    }
    public string TerrainAt(int index) { CheckIndex(index); return State.TerrainAt(index); }
    public EdgeKind EdgeAt(EdgeKey key) { CheckEdge(key); return State.EdgeAt(key); }
    public bool Passable(int index) => TerrainCatalog.Find(Content.Terrain, TerrainAt(index)).Passable;
    public bool IsReserved(int index, string? ownerId = null)
    {
        CheckIndex(index); return State.Reservations.TryGetValue(index, out var owner) && owner != ownerId;
    }
    public IEnumerable<int> Neighbors(int index) { CheckIndex(index); return Adjacent(index, Width, Height); }
    public ScenarioDefinition Export() => Export(State);
    public BoardDraft BeginDraft(CellBounds allowedArea, string ownerId, PlanningBudget budget) => new(this, null, allowedArea, ownerId, budget);
    public bool TryCommit(BoardPatch patch, out string? error)
    {
        if (!ReferenceEquals(patch.Target, this) || patch.ExpectedRevision != Revision) { error = "Stale or unrelated board patch."; return false; }
        var validation = Stopwatch.StartNew();
        try { _ = GameEngine.CreateGame(Export(patch.Result), Provider); }
        catch (ArgumentException exception) { error = exception.Message; return false; }
        finally { ValidationMilliseconds += validation.Elapsed.TotalMilliseconds; ValidationCount++; }
        State = patch.Result; Revision++; error = null; return true;
    }
    internal void CheckIndex(int index) { if ((uint)index >= (uint)(Width * Height)) throw new ArgumentOutOfRangeException(nameof(index)); }
    internal int CheckEdge(EdgeKey key)
    {
        CheckIndex(key.Index);
        if (!Enum.IsDefined(key.Direction) || key.Direction == EdgeDirection.Right && key.Index % Width == Width - 1 ||
            key.Direction == EdgeDirection.Down && key.Index / Width == Height - 1) throw new ArgumentException("Edge must join two board cells.");
        return key.Index + (key.Direction == EdgeDirection.Right ? 1 : Width);
    }
    internal ScenarioDefinition Export(PlanningState state)
    {
        var cells = Original.Board.Cells.ToDictionary(c => c.Position.Y * Width + c.Position.X);
        foreach (var (index, id) in state.TerrainWrites)
            cells[index] = new(new(index % Width, index / Width), TerrainKind.StoneFloor) { TerrainTypeId = id };
        var local = state.Local;
        return Clone(Original with
        {
            Board = Original.Board with { Cells = cells.OrderBy(p => p.Key).Select(p => p.Value).ToList(),
                Edges = state.Edges.Where(p => p.Value != EdgeKind.None).OrderBy(p => p.Key.Index).ThenBy(p => p.Key.Direction)
                    .Select(p => new EdgeDefinition(new(p.Key.Index % Width, p.Key.Index / Width), p.Key.Direction, p.Value)).ToList() },
            UnitTypes = local.Units.ToList(), EquipmentTypes = local.Equipment.ToList(), ItemTypes = local.Items.ToList(),
            LootCardDefinitions = local.LootCards.ToList(), WorldCardDefinitions = local.WorldCards.ToList(),
            TerrainTypes = local.Terrain.ToList(), TerrainAssemblies = local.Assemblies.ToList(), FixedComponentTypes = local.FixedComponents.ToList()
        });
    }
    internal static ScenarioDefinition Clone(ScenarioDefinition scenario) => JsonSerializer.Deserialize<ScenarioDefinition>(JsonSerializer.Serialize(scenario))!;
    internal static IEnumerable<int> Adjacent(int i, int width, int height)
    {
        if (i % width > 0) yield return i - 1;
        if (i % width + 1 < width) yield return i + 1;
        if (i >= width) yield return i - width;
        if (i / width + 1 < height) yield return i + width;
    }
}

internal sealed record PlanningState(string[] Terrain, Dictionary<int, string> TerrainWrites,
    Dictionary<EdgeKey, EdgeKind> Edges, Dictionary<int, string> Reservations, ContentBundle Local, ContentBundle Content)
{
    internal string TerrainAt(int i) => TerrainWrites.GetValueOrDefault(i, Terrain[i]);
    internal EdgeKind EdgeAt(EdgeKey key) => Edges.GetValueOrDefault(key, EdgeKind.None);
    internal PlanningState Copy() => this with { TerrainWrites = new(TerrainWrites), Edges = new(Edges), Reservations = new(Reservations) };
}

public sealed class BoardDraft : IPlanningBoardView
{
    private readonly PlanningBoard board;
    private readonly BoardDraft? parent;
    private readonly PlanningState baseline;
    private PlanningState state;
    private readonly long expectedRevision;
    private readonly HashSet<int> terrainTouched = [];
    private readonly HashSet<EdgeKey> edgeTouched = [];
    private readonly HashSet<int> reservationsTouched = [];
    public CellBounds AllowedArea { get; }
    public string OwnerId { get; }
    public PlanningBudget Budget { get; }
    public long Revision { get; private set; }
    public int Width => board.Width;
    public int Height => board.Height;
    public ContentBundle Content => state.Content;
    internal BoardDraft(PlanningBoard board, BoardDraft? parent, CellBounds area, string owner, PlanningBudget budget)
    {
        this.board = board; this.parent = parent;
        ArgumentNullException.ThrowIfNull(area); ArgumentNullException.ThrowIfNull(budget);
        if (string.IsNullOrWhiteSpace(owner) || owner.Contains('\0')) throw new ArgumentException("Owner is required and cannot contain null characters.");
        var outer = parent?.AllowedArea ?? new CellBounds(0, 0, board.Width, board.Height);
        if (area.Width < 1 || area.Height < 1 || area.X < outer.X || area.Y < outer.Y ||
            (long)area.X + area.Width > (long)outer.X + outer.Width || (long)area.Y + area.Height > (long)outer.Y + outer.Height)
            throw new ArgumentException("Draft area must be within its parent.");
        AllowedArea = area; OwnerId = owner; Budget = budget;
        baseline = (parent?.state ?? board.State).Copy(); state = baseline.Copy();
        expectedRevision = parent?.Revision ?? board.Revision;
        if (parent is not null)
        {
            terrainTouched.UnionWith(parent.terrainTouched);
            edgeTouched.UnionWith(parent.edgeTouched);
        }
    }
    public string TerrainAt(int index) { board.CheckIndex(index); return state.TerrainAt(index); }
    public EdgeKind EdgeAt(EdgeKey key) { board.CheckEdge(key); return state.EdgeAt(key); }
    public bool Passable(int index) => TerrainCatalog.Find(Content.Terrain, TerrainAt(index)).Passable;
    public bool IsReserved(int index, string? ownerId = null)
    {
        board.CheckIndex(index); return state.Reservations.TryGetValue(index, out var owner) && owner != ownerId;
    }
    public bool CanWrite(int index)
    {
        try { Writable(index); return true; }
        catch (ArgumentException) { return false; }
    }
    public IEnumerable<int> Neighbors(int index) { board.CheckIndex(index); return PlanningBoard.Adjacent(index, Width, Height); }
    public ScenarioDefinition Export() => board.Export(state);
    public BoardDraft Fork(CellBounds area, string ownerId) => new(board, this, area, ownerId, Budget);
    private void Writable(int index)
    {
        board.CheckIndex(index); var x = index % Width; var y = index / Width;
        if (x < AllowedArea.X || x >= AllowedArea.X + AllowedArea.Width || y < AllowedArea.Y || y >= AllowedArea.Y + AllowedArea.Height)
            throw new ArgumentException("Cell is outside the draft area.");
        if (state.Reservations.TryGetValue(index, out var owner) && owner != OwnerId)
            throw new ArgumentException("Cell is reserved by another owner.");
    }
    public void SetTerrain(int index, string typeId) => WriteTerrain(index, typeId, false);
    public void ReplaceTerrain(int index, string typeId) => WriteTerrain(index, typeId, true);
    private void WriteTerrain(int index, string typeId, bool replace)
    {
        Writable(index); _ = TerrainCatalog.Find(Content.Terrain, typeId);
        if (!replace && terrainTouched.Contains(index) && TerrainAt(index) != typeId) throw new ArgumentException("Conflicting terrain write.");
        if (typeId == state.Terrain[index]) state.TerrainWrites.Remove(index);
        else state.TerrainWrites[index] = typeId;
        terrainTouched.Add(index); Revision++;
    }
    public void SetEdge(EdgeKey key, EdgeKind kind) => WriteEdge(key, kind, false);
    public void ReplaceEdge(EdgeKey key, EdgeKind kind) => WriteEdge(key, kind, true);
    private void WriteEdge(EdgeKey key, EdgeKind kind, bool replace)
    {
        var end = board.CheckEdge(key); Writable(key.Index); Writable(end);
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown Edge kind.");
        if (!replace && (edgeTouched.Contains(key) || EdgeAt(key) != EdgeKind.None) && EdgeAt(key) != kind)
            throw new ArgumentException("Conflicting Edge write; existing geometry needs explicit replacement.");
        state.Edges[key] = kind; edgeTouched.Add(key); Revision++;
    }
    public void Reserve(IEnumerable<int> indices)
    {
        var cells = indices.Distinct().ToArray(); foreach (var i in cells) Writable(i);
        foreach (var i in cells) { state.Reservations[i] = OwnerId; reservationsTouched.Add(i); } Revision++;
    }
    public void AddContent(ContentBundle local)
    {
        var merged = PlanningContent.Append(state.Local, local, state.Content);
        var effective = ScenarioContentReferences.Compose(board.Provider, board.Original.ContentReferences, merged).Definitions;
        state = state with { Local = merged, Content = effective }; Revision++;
    }
    public BoardPatch BuildPatch() => new(parent is null ? board : parent, expectedRevision, OwnerId, baseline, state.Copy(),
        terrainTouched, edgeTouched, reservationsTouched, state.Local);
    public bool CommitToParent(out string? error)
    {
        if (parent is null) { error = "Root draft has no parent."; return false; }
        return parent.Accept(BuildPatch(), out error);
    }
    public bool Accept(BoardPatch patch, out string? error)
    {
        if (!ReferenceEquals(patch.Target, this) || patch.ExpectedRevision != Revision) { error = "Stale or unrelated child patch."; return false; }
        // Child cells have already been checked against its narrower area and reservations.
        // Accepted children stay private until the entire enclosing patch commits.
        state = patch.Result.Copy();
        terrainTouched.UnionWith(patch.TerrainChanges.Select(c => c.Index));
        edgeTouched.UnionWith(patch.EdgeChanges.Select(c => c.Key));
        reservationsTouched.UnionWith(patch.Reservations.Select(c => c.Index));
        Revision++; error = null; return true;
    }
}

internal static class PlanningContent
{
    internal static ContentBundle Local(ScenarioDefinition s) => new()
    {
        Units = [.. s.UnitTypes ?? []], Equipment = [.. s.EquipmentTypes ?? []], Items = [.. s.ItemTypes ?? []],
        LootCards = [.. s.LootCardDefinitions ?? []], WorldCards = [.. s.WorldCardDefinitions ?? []],
        Terrain = [.. s.TerrainTypes ?? []], Assemblies = [.. s.TerrainAssemblies ?? []], FixedComponents = [.. s.FixedComponentTypes ?? []]
    };
    internal static ContentBundle Append(ContentBundle local, ContentBundle addition, ContentBundle effective)
    {
        if (!addition.Resources.IsEmpty || !addition.LootPresets.IsEmpty || !addition.DefaultWorldDeck.IsEmpty)
            throw new ArgumentException("Scenario-local content cannot declare Resources, Loot presets or default World composition.");
        ImmutableArray<T> Merge<T>(ImmutableArray<T> old, ImmutableArray<T> added, ImmutableArray<T> catalog, Func<T, string> id)
        {
            var result = old.ToBuilder(); var seen = catalog.ToDictionary(id, StringComparer.Ordinal);
            foreach (var value in added)
            {
                var key = id(value);
                if (seen.TryGetValue(key, out var prior))
                {
                    if (JsonSerializer.Serialize(prior) != JsonSerializer.Serialize(value)) throw new ArgumentException($"Content '{key}' conflicts with an effective definition.");
                    continue;
                }
                seen.Add(key, value); result.Add(value);
            }
            return result.ToImmutable();
        }
        // Assemblies share the Terrain identity namespace, including materialized effective assemblies.
        foreach (var a in addition.Assemblies)
            if (effective.Terrain.Any(t => t.Id == a.Id) && !local.Assemblies.Any(t => t.Id == a.Id && JsonSerializer.Serialize(t) == JsonSerializer.Serialize(a)))
                throw new ArgumentException($"Content '{a.Id}' conflicts with effective Terrain.");
        foreach (var t in addition.Terrain)
            if (local.Assemblies.Any(a => a.Id == t.Id)) throw new ArgumentException($"Content '{t.Id}' conflicts with an assembly.");
        return local with
        {
            Units = Merge(local.Units, addition.Units, effective.Units, t => t.Id),
            Equipment = Merge(local.Equipment, addition.Equipment, effective.Equipment, t => t.Id),
            Items = Merge(local.Items, addition.Items, effective.Items, t => t.Id),
            LootCards = Merge(local.LootCards, addition.LootCards, effective.LootCards, t => t.Id),
            WorldCards = Merge(local.WorldCards, addition.WorldCards, effective.WorldCards, t => t.Id),
            Terrain = Merge(local.Terrain, addition.Terrain, effective.Terrain, t => t.Id),
            Assemblies = Merge(local.Assemblies, addition.Assemblies, local.Assemblies, t => t.Id),
            FixedComponents = Merge(local.FixedComponents, addition.FixedComponents, effective.FixedComponents, t => t.Id)
        };
    }
}
