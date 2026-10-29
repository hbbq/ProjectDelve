using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Permanent exploration is authoritative; observation and hypothetical queries never refresh it.
public static class FogOfWar
{
    // Scenario order supplies a stable default, independent of activation/controller changes.
    public static string? ViewingSide(GameState state) => state.FogOfWarSides.FirstOrDefault();
    public static bool IsEnabled(GameState state, string? side) => side is not null && state.FogOfWarSides.Contains(side);
    public static bool IsExplored(GameState state, string? side, Cell cell) =>
        !IsEnabled(state, side) || state.ExploredCells.TryGetValue(side!, out var cells) && cells.Contains(cell);
    public static bool IsUnitObservable(GameState state, string? side, string id) =>
        !IsEnabled(state, side) || state.Physical.Figures.Any(f => f.Id == id) &&
            FootprintGeometry.OccupiedCells(state, id).Any(c => IsExplored(state, side, c));
    public static bool IsEdgeObservable(GameState state, string? side, Edge edge) =>
        IsExplored(state, side, edge.A) || IsExplored(state, side, edge.B);

    public static IReadOnlyList<Cell> UnknownCells(GameState state, string? side)
    {
        if (!IsEnabled(state, side)) return [];
        return Enumerable.Range(0, state.Physical.Board.Height).SelectMany(y =>
            Enumerable.Range(0, state.Physical.Board.Width).Select(x => new Cell(x, y)))
            .Where(c => !IsExplored(state, side, c)).ToArray();
    }

    // Detached presentation data, never an executable replacement for authoritative state.
    public static GameState Observe(GameState state, string? side)
    {
        var copy = state.Copy();
        if (!IsEnabled(state, side)) return copy;
        var visible = state.Units.Where(u => IsUnitObservable(state, side, u.Id)).Select(u => u.Id).ToHashSet();
        var groups = state.Units.Where(u => visible.Contains(u.Id)).Select(u => new ActivationToken(u.TypeId, u.SideId)).ToHashSet();
        copy.ObservationValues = visible.ToDictionary(id => id, id =>
            (state.EffectiveMovOf(id), state.EffectiveRngOf(id), state.EffectiveAtkOf(id), state.EffectiveDefOf(id)));
        var board = copy.Physical.Board;
        copy.Physical = new(board with {
            Terrain = board.Terrain.Where(t => IsExplored(state, side, t.Position)).ToList(),
            Edges = board.Edges.Where(e => IsEdgeObservable(state, side, e)).ToList(),
            Machines = board.Machines.Where(m => IsExplored(state, side, m.Position)).ToList()
        }, copy.Physical.Figures.Where(f => visible.Contains(f.Id)).ToList());
        copy.Units.RemoveAll(u => !visible.Contains(u.Id));
        copy.Types.RemoveAll(t => !copy.Units.Any(u => u.TypeId == t.Id));
        copy.Resources.RemoveAll(r => !IsExplored(state, side, r.Position));
        copy.CellMarkers.RemoveAll(m => !IsExplored(state, side, m.Position));
        copy.Bag.Clear(); // Generic presentation; authoritative bag is untouched.
        copy.Controllers.RemoveAll(c => c.Token.SideId != side && !groups.Contains(c.Token));
        copy.Orders.RemoveAll(o => o.Token.SideId != side || o.Target is { Kind: OrderTargetKind.Unit, UnitId: { } id } && !visible.Contains(id) ||
            o.Target?.Door is { } d && !IsEdgeObservable(state, side, new(d.A, d.B, EdgeKind.None)));
        copy.StoredResources.RemoveAll(r => r.SideId != side && !groups.Contains(new(r.UnitTypeId, r.SideId)));
        copy.Objectives.RemoveAll(o => o.SideId != side);
        copy.ObjectiveStartUnitIds.Clear(); copy.DefeatedUnitIds.Clear();
        copy.Waypoints.RemoveAll(w => Enumerable.Range(w.Area.Y, w.Area.H).Any(y =>
            Enumerable.Range(w.Area.X, w.Area.W).Any(x => !IsExplored(state, side, new(x, y)))));
        copy.Routes.Clear();
        copy.ActiveToken = state.ActiveToken is { } token && groups.Contains(token) ? token : null;
        copy.CurrentUnitId = state.CurrentUnitId is { } current && visible.Contains(current) ? current : null;
        copy.CompletedUnitIds.RemoveAll(id => !visible.Contains(id));
        copy.AttackInProgress = ObserveAttack(state, side, state.AttackInProgress);
        if (copy.DoorInProgress is { } door && (!visible.Contains(door.UnitId) || !IsEdgeObservable(state, side, door.Door))) copy.DoorInProgress = null;
        copy.PendingExplosions.Clear();
        copy.Pending = ObserveDecision(state, side, state.Pending);
        if (copy.CurrentUnitId is null) copy.ModifiersThisTurn.Clear();
        copy.FogOfWarSides = [side!];
        copy.ExploredCells = new(StringComparer.Ordinal) { [side!] = new(state.ExploredCells.GetValueOrDefault(side!) ?? []) };
        return copy;
    }

    public static DecisionRequest? ObserveDecision(GameState state, string? side, DecisionRequest? request)
    {
        if (request is null || !IsEnabled(state, side)) return request;
        if (request.Token?.SideId != side) return null;
        return request with { Candidates = request.Candidates.Select(c => Preview(state, side!, c)).ToList(),
            Roll = ObserveDicePool(state, side, request.Roll) };
    }

    private static AttackContinuation? ObserveAttack(GameState state, string? side, AttackContinuation? attack)
    {
        if (attack is null) return null;
        var source = IsUnitObservable(state, side, attack.AttackerId);
        var targets = attack.Targets.Where(t => IsUnitObservable(state, side, t.TargetId)).ToImmutableArray();
        if (!source && targets.IsEmpty) return null;
        return attack with { AttackerId = source ? attack.AttackerId : "", Targets = targets,
            Results = attack.Results.IsDefault ? [] : attack.Results.Where(t => IsUnitObservable(state, side, t.TargetId)).ToImmutableArray(),
            SelectedCell = attack.SelectedCell is { } cell && IsExplored(state, side, cell) ? cell : null,
            AttackRoll = attack.AttackRoll is { } roll ? roll with { Pool = ObserveDicePool(state, side, roll.Pool)! } : null,
            DefeatedHostileCells = [], TargetIndex = attack.Targets.Take(attack.TargetIndex)
                .Count(t => IsUnitObservable(state, side, t.TargetId)) };
    }

    private static DicePool? ObserveDicePool(GameState state, string? side, DicePool? pool) => pool is null ? null : pool with
    {
        SourceUnitId = IsUnitObservable(state, side, pool.SourceUnitId) ? pool.SourceUnitId : "",
        OwnerUnitId = IsUnitObservable(state, side, pool.OwnerUnitId) ? pool.OwnerUnitId : "",
        TargetId = pool.TargetId is { } id && IsUnitObservable(state, side, id) ? id : null,
        TargetIds = pool.TargetIds.Where(id => IsUnitObservable(state, side, id)).ToImmutableArray(),
        SelectedCell = pool.SelectedCell is { } cell && IsExplored(state, side, cell) ? cell : null
    };

    public static RulesEvent? ObserveEvent(GameState state, string? side, RulesEvent e)
    {
        if (!IsEnabled(state, side)) return e;
        bool Known(string? id) => id is not null && (IsUnitObservable(state, side, id) ||
            e.DefeatContext is { } defeat && defeat.Unit.Id == id && defeat.OccupiedCells.Any(c => IsExplored(state, side, c)));
        var unit = Known(e.UnitId); var target = Known(e.TargetId);
        var cell = e.Cell is { } c && IsExplored(state, side, c);
        var door = e.Door is not null && IsEdgeObservable(state, side, e.Door);
        bool VisiblePosition(Cell position) => e.UnitId is { } id && state.Units.Any(u => u.Id == id)
            ? FootprintGeometry.OccupiedCells(state, id, position).Any(p => IsExplored(state, side, p))
            : IsExplored(state, side, position);
        var path = e.Path?.Where(VisiblePosition).ToList();
        List<List<Cell>>? segments = e.ObservedMovement?.Select(s => s.ToList()).ToList();
        if (segments is null && e.Kind is "MovementCompleted" or "UnitRepositioned" && e.Path is { } movement)
        {
            segments = [];
            for (var i = 0; i < movement.Count; i++)
            {
                if (!VisiblePosition(movement[i])) continue;
                var start = i;
                while (i + 1 < movement.Count && VisiblePosition(movement[i + 1])) i++;
                var end = i;
                bool Step(int a, int b) => Math.Abs(movement[a].X - movement[b].X) + Math.Abs(movement[a].Y - movement[b].Y) == 1;
                if (start > 0 && Step(start - 1, start)) start--;
                if (end + 1 < movement.Count && Step(end, end + 1)) end++;
                segments.Add(movement.GetRange(start, end - start + 1));
            }
        }
        var fragmentedMovement = e.Kind is "MovementCompleted" or "UnitRepositioned" && e.Path is { } original &&
            original.Zip(original.Skip(1), (a, b) => !VisiblePosition(a) && VisiblePosition(b)).Count(entered => entered) +
            (original.Count > 0 && VisiblePosition(original[0]) ? 1 : 0) > 1;
        var attack = e.AttackContext is null ? null : ObserveAttack(state, side, e.AttackContext);
        var result = e.Attack is null ? null : e.Attack with { Targets = e.Attack.Targets.Where(t => Known(t.TargetId)).ToImmutableArray() };
        var observable = e.Kind switch
        {
            "GameCompleted" or "RoundStarted" or "RoundCompleted" or "ActivationBagPopulated" or
            "WorldDeckShuffled" or "WorldDeckReshuffled" or "WorldDrawSkipped" or "WorldCardDrawn" or
            "WorldContinuousChanged" or "WorldCardDiscarded" => true,
            "TokenDrawn" => e.Token?.SideId == side || state.Units.Any(u => u.TypeId == e.Token?.TypeId &&
                u.SideId == e.Token?.SideId && Known(u.Id)),
            "MovementCompleted" or "UnitRepositioned" or "MovementObserved" => unit || path?.Count > 0 || segments?.Count > 0,
            "PlacesSwapped" => unit || target,
            "DoorOpened" or "DoorClosed" or "DoorOpeningAttemptResolved" => door,
            "AttackStarted" => attack is not null,
            "AttackResolved" => unit || target || result is { Targets.IsEmpty: false },
            "AttackTargetResolved" or "WorldDamageResolved" or "ExplosionDamageResolved" or "CleaveResolved" or "HealResolved" => target,
            "DiceRolled" => unit,
            "ResourceCreated" or "LavaCoalRolled" or "FruitTreeRolled" or "CellMarkerPlaced" => cell,
            _ => unit // Existing local abilities, resources, posture, creation and lifecycle events.
        };
        if (!observable) return null;
        return e with { Kind = fragmentedMovement ? "MovementObserved" : e.Kind,
            ObservedMovement = segments,
            UnitId = unit || path?.Count > 0 || segments?.Count > 0 ? e.UnitId : null, TargetId = target ? e.TargetId : null,
            SourceUnitId = Known(e.SourceUnitId) ? e.SourceUnitId : null,
            TypeId = unit || e.Kind == "TokenDrawn" ? e.TypeId : null,
            SideId = unit || e.Kind == "TokenDrawn" ? e.SideId : null,
            Token = e.Kind == "TokenDrawn" || unit ? e.Token : null,
            Cell = cell ? e.Cell : null, Door = door ? e.Door : null, Path = fragmentedMovement ? null : path,
            AttackContext = attack, Attack = result,
            DefeatContext = unit ? e.DefeatContext : null,
            Dice = e.Dice is { } dice ? dice with { Pool = ObserveDicePool(state, side, dice.Pool)! } : null,
            MachineId = e.MachineId is { } machine && state.Physical.Board.Machines.Any(m => m.Id == machine &&
                IsExplored(state, side, m.Position)) ? machine : null,
            AbilityName = unit || e.WorldCard is not null ? e.AbilityName : null };
    }
    internal static bool KnownFootprint(GameState state, string side, Footprint footprint, Cell anchor) =>
        FootprintGeometry.OccupiedCells(footprint, anchor).All(c => IsExplored(state, side, c));

    public static void Refresh(GameState state)
    {
        foreach (var side in state.FogOfWarSides)
        {
            if (!state.ExploredCells.TryGetValue(side, out var explored)) state.ExploredCells[side] = explored = [];
            foreach (var unit in state.Units.Where(u => u.SideId == side && u.CurrentHp > 0 &&
                state.Physical.Figures.Any(f => f.Id == u.Id)))
                ExploreFrom(state, unit, explored);
        }
    }

    private static void ExploreFrom(GameState state, Unit unit, HashSet<Cell> explored)
    {
        var board = state.Physical.Board;
        var range = Math.Max(5, state.EffectiveRngOf(unit.Id) + 3);
        var phase = state.Types.Single(t => t.Id == unit.TypeId).Phase is not null;
        foreach (var from in FootprintGeometry.OccupiedCells(state, unit.Id))
        for (var y = Math.Max(0, from.Y - range); y <= Math.Min(board.Height - 1, from.Y + range); y++)
        for (var x = Math.Max(0, from.X - range); x <= Math.Min(board.Width - 1, from.X + range); x++)
        {
            var to = new Cell(x, y);
            if (Math.Abs(x - from.X) + Math.Abs(y - from.Y) <= range &&
                AttackRules.HasGeometricLineOfSight(board, from, to, ignoredTerrainCell: to,
                    phaseVision: phase, discoverDestination: true)) explored.Add(to);
        }
    }

    internal static void Sweep(GameState state, string unitId, IReadOnlyList<Cell> path)
    {
        if (state.FogOfWarSides.Count == 0) return;
        // A leap's canonical path contains only its endpoints. No interpolation.
        foreach (var position in path)
        {
            var transit = state.Copy();
            var index = transit.Physical.Figures.FindIndex(f => f.Id == unitId);
            transit.Physical.Figures[index] = transit.Physical.Figures[index] with { Position = position };
            Refresh(transit);
            foreach (var side in state.FogOfWarSides)
            {
                if (!state.ExploredCells.TryGetValue(side, out var cells)) state.ExploredCells[side] = cells = [];
                cells.UnionWith(transit.ExploredCells[side]);
            }
        }
    }

    // Explicit selections are restricted; automatic recipient sets stay authoritative.
    internal static bool Allows(GameState state, string side, Candidate candidate) =>
        (candidate.TargetId is null || IsUnitObservable(state, side, candidate.TargetId)) &&
        (candidate.MachineId is null || IsExplored(state, side, state.Physical.Board.Machines.Single(m => m.Id == candidate.MachineId).Position)) &&
        (candidate.Door is null || IsEdgeObservable(state, side, candidate.Door)) &&
        (candidate.Destination is null || IsExplored(state, side, candidate.Destination));

    internal static Candidate Preview(GameState state, string side, Candidate candidate)
    {
        var targets = candidate.TargetIds.Where(id => IsUnitObservable(state, side, id)).ToImmutableArray();
        return candidate with { TargetIds = targets,
            Path = candidate.Path is null ? null : [.. candidate.Path],
            Relevant = candidate.Action switch
            {
                UnitAction.Fireball => targets.Any(id => state.Units.Single(u => u.Id == id).SideId != side),
                UnitAction.HolyWave => !targets.IsEmpty,
                _ => candidate.Relevant
            } };
    }

    internal static void DiscoverAttack(GameState state, AttackContinuation attack)
    {
        if (state.FogOfWarSides.Count == 0) return;
        var attacker = state.Units.Single(u => u.Id == attack.AttackerId);
        var range = attack.ActionId == "shoot-shell"
            ? state.Types.Single(t => t.Id == attacker.TypeId).ShootShell!.Rng
            : attack.ActionId == "claw-attack" ? 1 : state.EffectiveRngOf(attacker.Id);
        // Fireball has an established direct source-to-selected-Cell path. Its explosion
        // has no trajectory. Other direct Attacks use each established target line.
        var endpoints = attack.SelectedCell is { } cell
            ? new[] { (Cell: cell, TargetId: FootprintGeometry.UnitAtCell(state, cell)) }
            : attack.Targets.SelectMany(t => FootprintGeometry.OccupiedCells(state, t.TargetId)
                .Select(c => (Cell: c, TargetId: (string?)t.TargetId))).ToArray();
        var before = state.FogOfWarSides.ToDictionary(s => s, s =>
            endpoints.Where(e => attacker.SideId == s ||
                (e.TargetId is { } id ? IsUnitObservable(state, s, id) : IsExplored(state, s, e.Cell))).ToArray());
        foreach (var side in state.FogOfWarSides)
        foreach (var endpointsForTarget in before[side].GroupBy(e => e.TargetId))
        {
            var line = (from source in FootprintGeometry.OccupiedCells(state, attacker.Id)
                        from endpoint in endpointsForTarget
                        where (attack.ActionId == "fireball"
                            ? Math.Abs(source.X - endpoint.Cell.X) + Math.Abs(source.Y - endpoint.Cell.Y) <= range
                            : AttackRules.InRange(source, endpoint.Cell, range)) &&
                            AttackRules.HasUnitLineOfSight(state, attacker.Id, source, endpoint.Cell, endpoint.TargetId)
                        select (From: source, To: endpoint.Cell)).FirstOrDefault();
            if (line.From is null) continue;
            var explored = state.ExploredCells[side];
            var board = state.Physical.Board;
            for (var y = Math.Min(line.From.Y, line.To.Y); y <= Math.Max(line.From.Y, line.To.Y); y++)
            for (var x = Math.Min(line.From.X, line.To.X); x <= Math.Max(line.From.X, line.To.X); x++)
            {
                var crossed = new Cell(x, y);
                if (AttackRules.CrossesInterior(line.From, line.To, crossed)) explored.Add(crossed);
            }
        }
    }
}
