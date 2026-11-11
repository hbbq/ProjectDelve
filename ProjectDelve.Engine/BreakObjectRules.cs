using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// A target identifies one physical layer; Edge endpoints are canonical board order.
public sealed record BreakTarget(Cell? Cell = null, Edge? Edge = null)
{
    public string Key => Cell is { } cell ? $"cell:{cell.X},{cell.Y}" :
        Edge is { } edge ? $"edge:{edge.A.X},{edge.A.Y}:{edge.B.X},{edge.B.Y}" : "invalid";
}
public sealed record BreakOutcome(BreakTarget Target, int SuccessThreshold, int? DieRoll, bool Succeeded,
    string? BeforeTerrainTypeId = null, string? AfterTerrainTypeId = null,
    EdgeKind? BeforeEdgeKind = null, EdgeKind? AfterEdgeKind = null)
{
    // Captured before the batch changes geometry. Observation removes this internal evidence.
    public ImmutableArray<string> BeforeVisibleSideIds { get; init; } = [];
}
public sealed record BreakContinuation(string UnitId, ResolvedAbility Ability, ImmutableArray<BreakTarget> Targets,
    bool IsArea, ImmutableArray<BreakOutcome> Outcomes, int TargetIndex = 0)
{
    public DefeatContext? DefeatContext { get; init; }
    public ImmutableArray<string> SourceVisibleSideIds { get; init; } = [];
}
public sealed record DefeatEffect(DefeatContext Context, ResolvedAbility Ability);

public static class BreakObjectRules
{
    public static Edge CanonicalEdge(Edge edge) => edge.A.Y < edge.B.Y || edge.A.Y == edge.B.Y && edge.A.X < edge.B.X
        ? edge : new(edge.B, edge.A, edge.Kind);

    public static EdgeKind? DestroyedEdgeKind(EdgeKind kind) => kind switch
    {
        EdgeKind.Wall or EdgeKind.WallWithWindow or EdgeKind.ClosedDoor or EdgeKind.OpenDoor => EdgeKind.None,
        _ => null
    };

    public static ImmutableArray<BreakTarget> Targets(GameState state, IReadOnlyList<Cell> sourceCells, bool area = false)
    {
        var board = state.Physical.Board;
        var occupied = sourceCells.ToHashSet();
        HashSet<Cell> ring = [];
        foreach (var source in sourceCells)
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var cell = new Cell(source.X + dx, source.Y + dy);
            if (!occupied.Contains(cell) && MovementRules.Inside(board, cell)) ring.Add(cell);
        }
        var cells = ring.OrderBy(c => c.Y).ThenBy(c => c.X)
            .Where(cell => sourceCells.Any(source => Math.Max(Math.Abs(source.X - cell.X), Math.Abs(source.Y - cell.Y)) == 1 &&
                AttackRules.HasGeometricLineOfSight(board, source, cell, ignoredTerrainCell: cell)))
            .Select(cell => new BreakTarget(Cell: cell)).Where(target => CanBreak(state, target));
        var affected = area ? occupied.Union(ring).ToHashSet() : occupied;
        var edges = board.Edges.Where(edge => area
                ? affected.Contains(edge.A) && affected.Contains(edge.B)
                : occupied.Contains(edge.A) || occupied.Contains(edge.B))
            .Select(CanonicalEdge).Distinct().OrderBy(e => e.A.Y).ThenBy(e => e.A.X).ThenBy(e => e.B.Y).ThenBy(e => e.B.X)
            .Select(edge => new BreakTarget(Edge: edge)).Where(target => CanBreak(state, target));
        var candidates = cells.Concat(edges).ToImmutableArray();
        if (!area) return candidates;
        // Freeze before rolling. Accept only a set safe for EVERY success/failure
        // subset, not just the all-success board. Other invariants are Cell-local;
        // Water/Wall compatibility must consider both original and accepted result
        // terrain. Edge removal only relaxes restrictions and is never relied on.
        // No speculative GameState or vision copy is needed.
        Dictionary<Cell, TerrainDefinition> proposed = [];
        List<BreakTarget> safe = [];
        foreach (var target in candidates)
        {
            if (!CanBreak(state, target, proposed)) continue;
            safe.Add(target);
            if (target.Cell is { } cell)
                proposed[cell] = TerrainCatalog.Find(board.TerrainTypes, board.TerrainDefinitionAt(cell).DestroyedTerrainTypeId!);
        }
        return [.. safe];
    }

    public static bool CanBreak(GameState state, BreakTarget target) => CanBreak(state, target, null);

    private static bool CanBreak(GameState state, BreakTarget target, IReadOnlyDictionary<Cell, TerrainDefinition>? proposed)
    {
        var board = state.Physical.Board;
        if (target.Cell is { } cell && target.Edge is null)
        {
            if (!MovementRules.Inside(board, cell) || board.TerrainDefinitionAt(cell).DestroyedTerrainTypeId is not { } resultId) return false;
            var result = board.TerrainTypes.SingleOrDefault(d => d.Id == resultId);
            if (result is null || result.LegacyKind == TerrainKind.Rail) return false;
            if (state.Physical.Figures.Any(f => FootprintGeometry.OccupiedCells(state, f.Id).Contains(cell) &&
                !MovementTraversal.OccupancyFor(state, f.Id).CanTraverse(result))) return false;
            if (state.Resources.Any(r => r.Position == cell) && (!result.Passable || !result.AllowsResourceMarkers)) return false;
            if (state.CellMarkers.Any(m => m.Position == cell) && !result.Passable) return false;
            if (board.Machines.Any(m => m.Position == cell && m.Type.RequiredTerrainTypeId is { } required && required != resultId)) return false;
            if (result.LegacyKind == TerrainKind.Water && board.Edges.Any(e =>
                e.Kind is EdgeKind.Wall or EdgeKind.WallWithWindow &&
                (e.A == cell && MayBeWater(e.B) || e.B == cell && MayBeWater(e.A)))) return false;
            return true;
        }
        if (target.Edge is { } edge && target.Cell is null)
            return MovementRules.Inside(board, edge.A) && MovementRules.Inside(board, edge.B) &&
                Math.Abs(edge.A.X - edge.B.X) + Math.Abs(edge.A.Y - edge.B.Y) == 1 &&
                board.EdgeBetween(edge.A, edge.B) == edge.Kind && DestroyedEdgeKind(edge.Kind) is not null;
        return false;

        bool MayBeWater(Cell position) => board.TerrainDefinitionAt(position).LegacyKind == TerrainKind.Water ||
            proposed?.GetValueOrDefault(position)?.LegacyKind == TerrainKind.Water;
    }

    internal static BreakOutcome Outcome(GameState state, BreakTarget target, int threshold, int? roll)
    {
        var before = target.Cell is { } cell ? state.Physical.Board.TerrainDefinitionAt(cell).Id : null;
        var success = threshold == 6 || roll <= threshold;
        return new(target, threshold, roll, success,
            before, target.Cell is { } c ? success ? state.Physical.Board.TerrainDefinitionAt(c).DestroyedTerrainTypeId : before : null,
            target.Edge?.Kind, target.Edge is null ? null : success ? EdgeKind.None : target.Edge.Kind)
        {
            BeforeVisibleSideIds = [.. state.FogOfWarSides.Where(side => target.Cell is { } position
                ? FogOfWar.IsExplored(state, side, position) : FogOfWar.IsEdgeObservable(state, side, target.Edge!))]
        };
    }

    // No refresh/publication here: all successful outcomes form one physical batch.
    internal static void Apply(GameState state, ImmutableArray<BreakOutcome> outcomes)
    {
        foreach (var outcome in outcomes.Where(o => o.Succeeded))
        {
            if (outcome.Target.Cell is { } cell)
            {
                state.Physical.Board.Terrain.RemoveAll(t => t.Position == cell);
                state.Physical.Board.Terrain.Add(new(cell) { TypeId = outcome.AfterTerrainTypeId });
            }
            else if (outcome.Target.Edge is { } edge)
                state.Physical.Board.Edges.RemoveAll(e => e.A == edge.A && e.B == edge.B || e.A == edge.B && e.B == edge.A);
        }
    }
}

public static partial class GameEngine
{
    private static void BeginBreak(GameState state, string unitId, ResolvedAbility ability,
        ImmutableArray<BreakTarget> targets, bool area, ResolutionEvents events, DefeatContext? defeatContext = null)
    {
        state.BreakInProgress = new(unitId, ability, targets, area, [])
        {
            DefeatContext = defeatContext,
            SourceVisibleSideIds = [.. state.FogOfWarSides.Where(side => defeatContext is { } defeat
                ? defeat.OccupiedCells.Any(c => FogOfWar.IsExplored(state, side, c)) : FogOfWar.IsUnitObservable(state, side, unitId))]
        };
        ProgressBreak(state, events);
        if (area)
            while (state.BreakInProgress is { } progress)
                SubmitRoll(state, BreakDicePool(progress), events.Random, events);
    }

    private static DicePool BreakDicePool(BreakContinuation progress) =>
        new(DiceFamily.D6, 1, progress.UnitId, progress.UnitId, progress.Ability.ActionId, "Break object", SuccessCount: BreakThreshold(progress.Ability))
        { AbilitySource = progress.Ability.SourceReference, BreakTarget = progress.Targets[progress.TargetIndex] };

    private static int BreakThreshold(ResolvedAbility ability) => ability.Definition.Mechanic switch
    {
        BreakObject selected => selected.SuccessThreshold,
        BreakAdjacentObjects area => area.SuccessThreshold,
        _ => throw new InvalidOperationException("A Break Ability is required.")
    };

    private static void ProgressBreak(GameState state, ResolutionEvents events, int? roll = null)
    {
        var progress = state.BreakInProgress!;
        var threshold = BreakThreshold(progress.Ability);
        if (roll is not null)
            progress = progress with { Outcomes = progress.Outcomes.Add(BreakObjectRules.Outcome(state, progress.Targets[progress.TargetIndex], threshold, roll)), TargetIndex = progress.TargetIndex + 1 };
        if (threshold == 6)
            while (progress.TargetIndex < progress.Targets.Length)
                progress = progress with { Outcomes = progress.Outcomes.Add(BreakObjectRules.Outcome(state, progress.Targets[progress.TargetIndex], threshold, null)), TargetIndex = progress.TargetIndex + 1 };
        state.BreakInProgress = progress;
        if (progress.TargetIndex < progress.Targets.Length) return;
        BreakObjectRules.Apply(state, progress.Outcomes);
        state.BreakInProgress = null;
        events.Add(new RulesEvent(progress.IsArea ? "AdjacentObjectsBreakResolved" : "ObjectBreakResolved", progress.UnitId,
            AbilityName: progress.Ability.Definition.Name)
        {
            SourceUnitId = progress.UnitId, ActionId = progress.Ability.ActionId,
            AbilitySource = progress.Ability.SourceReference, BreakOutcomes = progress.Outcomes,
            DefeatContext = progress.DefeatContext, BreakSourceVisibleSideIds = progress.SourceVisibleSideIds
        });
    }
}
