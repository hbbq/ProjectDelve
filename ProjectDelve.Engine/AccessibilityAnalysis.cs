using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public enum AccessibilityMode { ActualMove, StaticGeometry, RepeatedMoves, ConditionalDoors }
public enum AccessibilityStatus { Reachable, Conditional, Disconnected, Unknown }
public enum AccessibilityDoorStatus { OpenPassage, Deterministic, Probabilistic, Unsupported, Inaccessible }

/// <summary>Explicit frozen movement assumptions, not a promise of future activations or success.</summary>
public sealed record AccessibilityProfile(Footprint Footprint, ImmutableArray<string> Capabilities,
    int MoveAllowance, bool Phase = false, MovementPattern? Pattern = null,
    TerrainKind? RequiredTerrain = null, TerrainRestriction? Restriction = null);

public sealed record AccessibilityOptions(AccessibilityMode Mode = AccessibilityMode.RepeatedMoves,
    bool IgnoreFigures = true, bool AssumeUpright = false, int? MovementAllowance = null, int WorkBudget = 100000);

public sealed record AccessibilityDoor(Edge Door, AccessibilityDoorStatus Status);

public sealed record AccessibilityResult(ImmutableArray<Cell> Reachable, ImmutableArray<Cell> Conditional,
    ImmutableArray<AccessibilityDoor> Doors, bool Complete, int WorkUsed, long Revision)
{
    public ImmutableArray<string> Assumptions { get; init; } = [];
    public AccessibilityStatus StatusAt(Cell cell) => Reachable.Contains(cell) ? AccessibilityStatus.Reachable :
        Conditional.Contains(cell) ? AccessibilityStatus.Conditional : Complete ? AccessibilityStatus.Disconnected : AccessibilityStatus.Unknown;
}

// Captured index only: never attached to mutable Board/GameState or used by runtime caches.
internal sealed class AccessibilityGeometry
{
    private readonly Dictionary<Cell, TerrainDefinition> terrain;
    private readonly Dictionary<(Cell, Cell), EdgeKind> edges = [];
    private readonly HashSet<Cell> machines;
    private readonly Lazy<TerrainDefinition> floor;
    internal AccessibilityGeometry(Board board)
    {
        // Fully populated custom-only boards need not define the implicit Stone Floor.
        floor = new(() => board.TerrainDefinition(null));
        terrain = board.Terrain.ToDictionary(t => t.Position, board.TerrainDefinition);
        machines = board.Machines.Select(m => m.Position).ToHashSet();
        foreach (var edge in board.Edges)
        {
            edges.TryAdd((edge.A, edge.B), edge.Kind);
            edges.TryAdd((edge.B, edge.A), edge.Kind);
        }
    }
    internal TerrainDefinition TerrainAt(Cell cell) => terrain.GetValueOrDefault(cell) ?? floor.Value;
    internal bool HasMachine(Cell cell) => machines.Contains(cell);
    internal EdgeKind EdgeBetween(Cell a, Cell b) => edges.GetValueOrDefault((a, b), EdgeKind.None);
}

/// <summary>Authoring-only, full-information analysis of a captured revision. No state is mutated.
/// Repeated Moves hold the supplied profile fixed; combat, cooperation, acquired abilities and recovery are not planned.</summary>
public sealed class AccessibilityAnalysis
{
    private readonly GameState? snapshot;
    private readonly Board board;
    private readonly AccessibilityGeometry geometry;
    private readonly Dictionary<(AccessibilityProfile, Cell, AccessibilityOptions, string?), AccessibilityResult> results = [];
    private readonly List<(AccessibilityProfile Profile, AccessibilityOptions Options, string? DoorSignature,
        HashSet<Cell> Cells, AccessibilityResult Result)> regions = [];
    public long Revision { get; }
    public int Width => board.Width;
    public int Height => board.Height;
    public bool CanStop(AccessibilityProfile profile, Cell anchor) =>
        SpatialRules.Fits(board, profile.Footprint, anchor, new(Capabilities: profile.Capabilities), geometry);
    private AccessibilityAnalysis(Board source, GameState? state, long revision)
    {
        board = source with { Edges = [.. source.Edges], Terrain = [.. source.Terrain], Machines = [.. source.Machines] };
        snapshot = state;
        geometry = new(board);
        Revision = revision;
    }
    public static AccessibilityAnalysis Capture(Board board, long revision = 0) => new(board, null, revision);
    public static AccessibilityAnalysis Capture(GameState state, long revision = 0)
    {
        var copy = state.DetachedCopy();
        return new(copy.Physical.Board, copy, revision);
    }
    public AccessibilityProfile ProfileFor(string unitId)
    {
        var state = snapshot ?? throw new InvalidOperationException("Actor queries require a GameState capture.");
        var unit = state.Units.Single(u => u.Id == unitId);
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        return new(type.Footprint, state.EffectiveMovementCapabilitiesOf(unitId), state.EffectiveMovOf(unitId),
            state.HasPhase(unitId), type.MovementPattern, type.TerrainRestrictedMovement?.Terrain, type.TerrainRestriction);
    }
    public AccessibilityResult Analyze(string unitId, Cell from, AccessibilityOptions? options = null) =>
        AnalyzeCached(ProfileFor(unitId), from, options ?? new(), unitId);
    public AccessibilityResult Analyze(AccessibilityProfile profile, Cell from, AccessibilityOptions? options = null) =>
        AnalyzeCached(profile, from, options ?? new(), null);

    private AccessibilityResult AnalyzeCached(AccessibilityProfile profile, Cell from, AccessibilityOptions options, string? actor)
    {
        // Validate before either cache: equivalent completed regions may ignore the work budget.
        if (options.WorkBudget < 0 || options.MovementAllowance < 0 || profile.MoveAllowance < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        lock (results)
        {
            var key = (profile, from, options, actor);
            if (results.TryGetValue(key, out var result)) return result with { WorkUsed = 0 };
            var doorSignature = options.Mode == AccessibilityMode.ConditionalDoors ? DoorOperationSignature(actor) : null;
            var reusableRegion = (options.Mode == AccessibilityMode.RepeatedMoves ||
                options.Mode == AccessibilityMode.ConditionalDoors && doorSignature is not null) && options.IgnoreFigures &&
                !profile.Phase && profile.Pattern is null && profile.Restriction is null && profile.RequiredTerrain is null &&
                profile.MoveAllowance > 0 && options.MovementAllowance is not 0 &&
                (actor is null || options.AssumeUpright || snapshot!.IsUpright(actor));
            if (reusableRegion)
                foreach (var region in regions)
                    if (region.Options with { WorkBudget = 0 } == options with { WorkBudget = 0 } &&
                        region.DoorSignature == doorSignature && SameProfile(region.Profile, profile) && region.Cells.Contains(from))
                        return region.Result with { WorkUsed = 0 };
            result = AnalyzeCore(profile, from, options, actor);
            // Caller budgets bound cache size too; no partial or mutable runtime cache escapes this revision.
            if (results.Count + regions.Count < 256) results.Add(key, result);
            if (reusableRegion && result.Complete && results.Count + regions.Count < 256)
                regions.Add((profile, options, doorSignature, result.Reachable.ToHashSet(), result));
            return result;
        }
    }

    private string? DoorOperationSignature(string? actor)
    {
        if (actor is null) return "none";
        if (snapshot!.Units.Single(u => u.Id == actor).CurrentHp <= 0) return null;
        var abilities = snapshot.AbilitiesOf(actor).Where(a => a.Definition.Mechanic is OpenDoor or TryOpenDoor).ToArray();
        // Limited uses, physical consumables and Costs may depend on the actor/source.
        // Do not share their unsupported results, even alongside a supported operation.
        if (abilities.Any(a => a.SourceKind == AbilitySourceKind.Item ||
            a.Definition.Timing is not (AbilityTiming.Action or AbilityTiming.FreeAction) ||
            a.Definition.Usage.MaxUses is not null || !a.Definition.Costs.IsEmpty)) return null;
        return abilities.Length == 0 ? "none" : string.Join(",", abilities.Select(a => a.Definition.Mechanic switch
        {
            OpenDoor => "open",
            TryOpenDoor attempt => $"try:{attempt.SuccessCount}",
            _ => throw new InvalidOperationException()
        }).Distinct().Order(StringComparer.Ordinal));
    }

    private static bool SameProfile(AccessibilityProfile first, AccessibilityProfile second) =>
        first.Footprint == second.Footprint && first.Capabilities.SequenceEqual(second.Capabilities) &&
        first.MoveAllowance == second.MoveAllowance && first.Phase == second.Phase && first.Pattern == second.Pattern &&
        first.RequiredTerrain == second.RequiredTerrain && first.Restriction == second.Restriction;

    private sealed class BudgetExhausted : Exception;
    private AccessibilityResult AnalyzeCore(AccessibilityProfile profile, Cell from, AccessibilityOptions options, string? actor)
    {
        if (options.WorkBudget < 0 || options.MovementAllowance < 0 || profile.MoveAllowance < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.Mode == AccessibilityMode.StaticGeometry && !options.IgnoreFigures)
            throw new ArgumentException("Static geometry excludes figures. Use RepeatedMoves to retain figure occupancy.", nameof(options));
        if (profile.Capabilities.IsDefault) throw new ArgumentException("Explicit movement capabilities are required.", nameof(profile));
        var occupancy = new MovementTraversal(Capabilities: profile.Capabilities);
        if (!SpatialRules.Fits(board, profile.Footprint, from, occupancy, geometry))
            throw new ArgumentException("The start must be a legal complete stopping footprint.", nameof(from));
        var state = new GameState { Physical = new(board, []), Types = snapshot?.Types ?? [], Units = snapshot?.Units ?? [],
            CellMarkers = snapshot?.CellMarkers ?? [], FogOfWarSides = options.Mode == AccessibilityMode.ActualMove ? snapshot?.FogOfWarSides ?? [] : [],
            ExploredCells = snapshot?.ExploredCells ?? [] };
        var id = actor ?? "accessibility-actor";
        if (actor is null)
        {
            var type = new UnitType("accessibility-profile", profile.MoveAllowance, 0, 0, 0, 1) { Footprint = profile.Footprint };
            state.Types = [type]; state.Units = [type.CreateUnit(id, "accessibility")];
        }
        var ignoreFigures = options.Mode != AccessibilityMode.ActualMove && options.IgnoreFigures;
        state.Physical = new(board, ignoreFigures || actor is null
            ? [new(id, from)] : [.. snapshot!.Physical.Figures.Select(f => f.Id == id ? f with { Position = from } : f)]);
        if (!ignoreFigures && actor is not null) HypotheticalPosition.Validate(state, id, from, occupancy);
        // Authoring analysis is deliberately full-information; gameplay/Fog of War queries are unchanged.
        var traversal = new MovementTraversal(profile.Phase, RequiredTerrain: profile.RequiredTerrain,
            Restriction: profile.Restriction, Capabilities: profile.Capabilities,
            KnownCells: options.Mode == AccessibilityMode.ActualMove && actor is not null && FogOfWar.IsEnabled(state, state.Units.Single(u => u.Id == id).SideId)
                ? state.ExploredCells.GetValueOrDefault(state.Units.Single(u => u.Id == id).SideId) ?? [] : null);
        var allowance = options.Mode == AccessibilityMode.ActualMove && actor is not null
            ? snapshot!.MoveAfterAttackAllowance ?? profile.MoveAllowance : options.MovementAllowance ?? profile.MoveAllowance;
        var reachable = new HashSet<Cell> { from };
        var conditional = new HashSet<Cell>();
        var doors = new List<AccessibilityDoor>();
        var work = 0;
        var complete = true;
        void Spend() { if (work == options.WorkBudget) throw new BudgetExhausted(); work++; }
        var upright = actor is null || options.Mode != AccessibilityMode.ActualMove && options.AssumeUpright || snapshot!.IsUpright(id);
        var canMove = upright && (options.Mode != AccessibilityMode.ActualMove || actor is null ||
            snapshot!.CurrentUnitId == id && !snapshot.IsCompleted && !snapshot.CleavePending &&
            snapshot.BreakInProgress is null && snapshot.DoorInProgress is null && snapshot.AttackInProgress is null &&
            snapshot.Pending?.Kind != DecisionKind.RollDice && !snapshot.CompletedUnitIds.Contains(id) &&
            (!snapshot.MoveDone || snapshot.MoveAfterAttackAllowance is not null));
        if (options.Mode == AccessibilityMode.ActualMove && actor is not null && snapshot!.Physical.Figures.Single(f => f.Id == id).Position != from)
            throw new ArgumentException("An actual Move query must use the actor's captured position.", nameof(from));
        if (!canMove || allowance == 0 && options.Mode is not (AccessibilityMode.StaticGeometry or AccessibilityMode.ConditionalDoors)) return Finish();
        try
        {
            if (options.Mode == AccessibilityMode.ConditionalDoors)
            {
                var initial = AnalyzeCached(profile, from, options with { Mode = AccessibilityMode.RepeatedMoves }, actor);
                reachable.UnionWith(initial.Reachable); work += initial.WorkUsed;
                if (!initial.Complete) { complete = false; return Finish(); }
            }
            else Flood(board, geometry, reachable);
            if (options.Mode == AccessibilityMode.ConditionalDoors)
            {
                var abilities = actor is null ? [] : snapshot!.AbilitiesOf(id).Where(a => a.Definition.Mechanic is OpenDoor or TryOpenDoor).ToArray();
                var supported = abilities.Where(a => a.SourceKind != AbilitySourceKind.Item &&
                    a.Definition.Timing is AbilityTiming.Action or AbilityTiming.FreeAction &&
                    a.Definition.Usage.MaxUses is null && a.Definition.Costs.IsEmpty &&
                    a.Definition.Mechanic is not TryOpenDoor { SuccessCount: 0 } &&
                    snapshot!.Units.Single(u => u.Id == id).CurrentHp > 0).ToArray();
                var closedDoors = new List<Edge>();
                foreach (var edge in board.Edges)
                {
                    Spend();
                    if (edge.Kind == EdgeKind.OpenDoor) doors.Add(new(edge, AccessibilityDoorStatus.OpenPassage));
                    if (edge.Kind == EdgeKind.ClosedDoor) closedDoors.Add(edge);
                }
                var opened = new HashSet<Edge>();
                var all = reachable.ToHashSet();
                var changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var door in closedDoors)
                    {
                        Spend();
                        if (opened.Contains(door) || !Borders(all, door)) continue;
                        if (supported.Length == 0)
                        {
                            if (abilities.Any(a => a.Definition.Mechanic is not TryOpenDoor { SuccessCount: 0 })) complete = false;
                            continue;
                        }
                        opened.Add(door); changed = true;
                        doors.Add(new(door, supported.Any(a => a.Definition.Mechanic is OpenDoor or TryOpenDoor { SuccessCount: 6 }) ? AccessibilityDoorStatus.Deterministic : AccessibilityDoorStatus.Probabilistic));
                    }
                    if (!changed) break;
                    // Rebuilding an index is linear in the authored geometry, and counts against the same deterministic budget.
                    for (var i = 0; i < board.Edges.Count + board.Terrain.Count + board.Machines.Count; i++) Spend();
                    var openBoard = board with { Edges = board.Edges.Select(e => opened.Contains(e) ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() };
                    Flood(openBoard, new(openBoard), all);
                }
                conditional.UnionWith(all.Except(reachable));
                foreach (var door in closedDoors)
                {
                    Spend();
                    if (!opened.Contains(door)) doors.Add(new(door, Borders(all, door)
                        ? AccessibilityDoorStatus.Unsupported : AccessibilityDoorStatus.Inaccessible));
                }
            }
        }
        catch (BudgetExhausted) { complete = false; }
        return Finish();

        bool Borders(HashSet<Cell> anchors, Edge door)
        {
            // Invert footprint membership: at most eight anchor lookups per Door, independent of the region size.
            var span = profile.Footprint == Footprint.TwoByTwo ? 2 : 1;
            foreach (var cell in new[] { door.A, door.B })
            for (var dy = 0; dy < span; dy++)
            for (var dx = 0; dx < span; dx++)
                if (anchors.Contains(new(cell.X - dx, cell.Y - dy))) return true;
            return false;
        }

        AccessibilityResult Finish() => new([.. reachable.OrderBy(c => c.Y).ThenBy(c => c.X)],
            [.. conditional.OrderBy(c => c.Y).ThenBy(c => c.X)], [.. doors], complete, work, Revision)
        { Assumptions = options.Mode == AccessibilityMode.ActualMove
            ? actor is null ? ["Single bounded Move potential under the supplied geometry profile; no actual actor activation is claimed."]
                : ["Captured current Move, figures and explored geometry."] :
            ["Full-information geometry; supplied movement profile and allowance held fixed, including any captured temporary or spatial modifiers.",
             options.IgnoreFigures ? "Figures ignored." : "Captured figures remain fixed; current other-Side transit blocking retained.",
             options.AssumeUpright ? "Hypothetical Upright posture; no recovery Action or permission to Move on the next activation is claimed."
                 : "Captured posture retained.",
             "Repeated Moves assume additional Move opportunities; markers, posture recovery, combat, cooperation and acquired abilities are not planned.",
             "Conditional Door access assumes future Action opportunities and, for attempts, successful rolls; it is not guaranteed eventual success."] };
        void Flood(Board currentBoard, AccessibilityGeometry index, HashSet<Cell> reached)
        {
            if (allowance == 0 && options.Mode != AccessibilityMode.StaticGeometry) return;
            state.Physical = new(currentBoard, state.Physical.Figures);
            var queue = new Queue<Cell>(reached.OrderBy(c => c.Y).ThenBy(c => c.X));
            // Ordinary symmetric/directed local graphs need one expansion per anchor, not repeated all-board searches.
            var local = options.Mode == AccessibilityMode.StaticGeometry ||
                options.Mode != AccessibilityMode.ActualMove && !profile.Phase && profile.Pattern is null && ignoreFigures;
            while (queue.TryDequeue(out var current))
            {
                Spend();
                if (local)
                {
                    foreach (var next in MovementRules.Neighbors(current))
                        if (!reached.Contains(next) && MovementRules.CanStep(currentBoard, profile.Footprint, current, next,
                            options.Mode == AccessibilityMode.StaticGeometry ? traversal with { IgnoreTerrainAndEdges = false } : traversal, index))
                        { reached.Add(next); queue.Enqueue(next); }
                }
                else
                {
                    var advance = options.Mode == AccessibilityMode.ActualMove && actor is not null ? snapshot!.AdvanceOnKillCells : null;
                    var paths = MovementRules.FindPatternPaths(state, id, current, allowance,
                        options.Mode != AccessibilityMode.ActualMove, advance is null ? profile.Pattern : null, traversal, index, Spend);
                    foreach (var next in paths.Keys)
                    {
                        if (advance is not null && next != from && (!advance.Contains(next) ||
                            Math.Max(Math.Abs(next.X - from.X), Math.Abs(next.Y - from.Y)) != 1 ||
                            paths[next].Count - 1 != Math.Abs(next.X - from.X) + Math.Abs(next.Y - from.Y))) continue;
                        if (reached.Add(next) && options.Mode != AccessibilityMode.ActualMove) queue.Enqueue(next);
                    }
                    if (options.Mode == AccessibilityMode.ActualMove) break;
                }
            }
        }
    }
}
