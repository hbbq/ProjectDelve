namespace ProjectDelve.Engine;

public static class GameEngine
{
    public static EngineResult StartRound(GameState previous, IRandomProvider random)
    {
        if (previous.Round != 0 && !previous.RoundComplete)
            throw new InvalidOperationException("The current round is still active.");
        ValidateScenario(previous);
        var state = previous.Copy();
        state.Round++;
        state.Bag = state.Types.Where(t => state.Units.Any(u => u.TypeId == t.Id && u.CurrentHp > 0))
            .Select(t => t.Id).ToList();
        state.ActiveTypeId = null;
        state.CurrentUnitId = null;
        state.Pending = null;
        state.CompletedUnitIds.Clear();
        state.RoundComplete = false;
        var events = new List<RulesEvent>();
        RunUntilDecision(state, random, events);
        return new(state, events, state.Pending);
    }

    public static EngineResult Advance(GameState previous, IDecisionProvider decisions, IRandomProvider random)
    {
        var request = previous.Pending ?? throw new InvalidOperationException("No decision is pending.");
        var choice = decisions.Choose(request);
        if (choice is null && !request.AllowsNone ||
            choice is not null && !request.Candidates.Any(c => c.Key == choice))
            throw new ArgumentException("Decision is not among the supplied legal candidates.", nameof(decisions));

        var state = previous.Copy();
        state.Pending = null;
        var events = new List<RulesEvent>();
        switch (request.Kind)
        {
            case DecisionKind.SelectUnit:
                state.CurrentUnitId = choice;
                if (state.Phase == Phase.BonusAction)
                    CompleteUnit(state);
                break;
            case DecisionKind.Move:
                var figure = state.Physical.Figures.Single(f => f.Id == request.UnitId);
                var path = choice is null ? new List<Cell> { figure.Position } :
                    request.Candidates.Single(c => c.Key == choice).Path!;
                if (choice is not null)
                    state.Physical.Figures[state.Physical.Figures.IndexOf(figure)] =
                        figure with { Position = path[^1] };
                events.Add(new RulesEvent("MovementCompleted", request.UnitId, Path: [.. path]));
                CompleteUnit(state);
                break;
            case DecisionKind.Attack:
                if (choice is not null)
                    ResolveAttack(state, request.UnitId!, choice, random, events);
                CompleteUnit(state);
                break;
        }
        RunUntilDecision(state, random, events);
        return new(state, events, state.Pending);
    }

    private static void RunUntilDecision(GameState state, IRandomProvider random, List<RulesEvent> events)
    {
        while (state.Pending is null && !state.RoundComplete)
        {
            if (state.ActiveTypeId is null)
            {
                if (state.Bag.Count == 0)
                {
                    state.RoundComplete = true;
                    events.Add(new RulesEvent("RoundCompleted"));
                    return;
                }
                var drawn = random.DrawToken(state.Bag);
                if (!state.Bag.Remove(drawn))
                    throw new ArgumentException("Random provider drew a token outside the bag.", nameof(random));
                state.ActiveTypeId = drawn;
                state.Phase = Phase.BonusAction;
                state.CompletedUnitIds.Clear();
                events.Add(new RulesEvent("TokenDrawn", TypeId: drawn));
            }

            var eligible = state.Units.Where(u => u.TypeId == state.ActiveTypeId && u.CurrentHp > 0 &&
                    !state.CompletedUnitIds.Contains(u.Id))
                .OrderBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.Y)
                .ThenBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.X)
                .ToList();
            if (eligible.Count == 0)
            {
                state.CompletedUnitIds.Clear();
                state.CurrentUnitId = null;
                if (state.Phase == Phase.Act) state.ActiveTypeId = null;
                else state.Phase++;
                continue;
            }
            if (state.CurrentUnitId is null)
            {
                state.Pending = new DecisionRequest(DecisionKind.SelectUnit, state.ActiveTypeId!, null,
                    eligible.Select(u => new Candidate(u.Id)).ToList(), false);
                return;
            }
            var unit = eligible.Single(u => u.Id == state.CurrentUnitId);
            if (state.Phase == Phase.Move)
            {
                var candidates = MovementCandidates(state, unit);
                if (candidates.Count > 0)
                {
                    state.Pending = new DecisionRequest(DecisionKind.Move, state.ActiveTypeId!, unit.Id, candidates, true);
                    return;
                }
                events.Add(new RulesEvent("MovementCompleted", unit.Id,
                    Path: [state.Physical.Figures.Single(f => f.Id == unit.Id).Position]));
            }
            else if (state.Phase == Phase.Act)
            {
                var candidates = AttackCandidates(state, unit);
                if (candidates.Count > 0)
                {
                    state.Pending = new DecisionRequest(DecisionKind.Attack, state.ActiveTypeId!, unit.Id, candidates, true);
                    return;
                }
            }
            CompleteUnit(state);
        }
    }

    private static void CompleteUnit(GameState state)
    {
        state.CompletedUnitIds.Add(state.CurrentUnitId!);
        state.CurrentUnitId = null;
    }

    private static List<Candidate> MovementCandidates(GameState state, Unit unit)
    {
        var start = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
        var allowance = state.Types.Single(t => t.Id == unit.TypeId).Mov;
        var paths = new Dictionary<Cell, List<Cell>> { [start] = [start] };
        var queue = new Queue<Cell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var from = queue.Dequeue();
            if (paths[from].Count - 1 == allowance) continue;
            foreach (var to in Neighbors(from)) // top, left, right, bottom
            {
                if (!Inside(state.Physical.Board, to) || paths.ContainsKey(to) || !Passable(state.Physical.Board, from, to))
                    continue;
                var occupant = state.Physical.Figures.FirstOrDefault(f => f.Position == to);
                if (occupant is not null && state.Units.Single(u => u.Id == occupant.Id).SideId != unit.SideId)
                    continue;
                paths[to] = [.. paths[from], to];
                queue.Enqueue(to);
            }
        }
        return paths.Where(pair => pair.Key != start &&
                state.Physical.Figures.All(f => f.Position != pair.Key))
            .OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X)
            .Select(pair => new Candidate($"{pair.Key.X},{pair.Key.Y}", pair.Key, pair.Value)).ToList();
    }

    private static IEnumerable<Cell> Neighbors(Cell cell)
    {
        yield return new(cell.X, cell.Y - 1);
        yield return new(cell.X - 1, cell.Y);
        yield return new(cell.X + 1, cell.Y);
        yield return new(cell.X, cell.Y + 1);
    }

    private static bool Inside(Board board, Cell cell) =>
        cell.X >= 0 && cell.X < board.Width && cell.Y >= 0 && cell.Y < board.Height;

    private static bool Passable(Board board, Cell a, Cell b) =>
        board.Edges.FirstOrDefault(e => e.A == a && e.B == b || e.A == b && e.B == a)?.Kind
            is null or EdgeKind.OpenDoor;

    private static List<Candidate> AttackCandidates(GameState state, Unit attacker)
    {
        var stats = state.Types.Single(t => t.Id == attacker.TypeId);
        if (stats.Rng == 0 || stats.Atk == 0) return [];
        var from = state.Physical.Figures.Single(f => f.Id == attacker.Id).Position;
        var result = new List<Candidate>();
        foreach (var target in state.Units.Where(u => u.CurrentHp > 0 && u.SideId != attacker.SideId))
        {
            var to = state.Physical.Figures.Single(f => f.Id == target.Id).Position;
            var dx = Math.Abs(to.X - from.X);
            var dy = Math.Abs(to.Y - from.Y);
            if (stats.Rng == 1 ? Math.Max(dx, dy) != 1 : dx + dy > stats.Rng) continue;
            // Edge LOS and intervening hostile LOS are explicitly undecided rules.
            // Omit only a target whose own LOS depends on one of those rules so
            // unrelated unresolved LOS does not remove otherwise legal choices.
            if (HasUnsupportedFeaturedEdgeLos(state.Physical.Board, from, to))
                continue;
            if (state.Units.Where(u => u.CurrentHp > 0 && u.SideId != attacker.SideId && u.Id != target.Id)
                .Any(u => CrossesInterior(from, to, state.Physical.Figures.Single(f => f.Id == u.Id).Position)))
                continue;
            result.Add(new Candidate(target.Id));
        }
        return result;
    }

    private static bool HasUnsupportedFeaturedEdgeLos(Board board, Cell from, Cell to)
    {
        if (board.Edges.Any(edge => CrossesEdgeInterior(from, to, edge)))
            return true;

        // At an exact corner there are two possible passages. The defined
        // corner rule makes LOS unambiguous when either passage has no featured
        // edge; otherwise the effects of the encountered features are unknown.
        var minX = Math.Min(from.X, to.X);
        var maxX = Math.Max(from.X, to.X);
        var minY = Math.Min(from.Y, to.Y);
        var maxY = Math.Max(from.Y, to.Y);
        for (var x = minX; x < maxX; x++)
        for (var y = minY; y < maxY; y++)
        {
            var cornerX = 2 * x + 1;
            var cornerY = 2 * y + 1;
            if ((cornerX - 2 * from.X) * (to.Y - from.Y) !=
                (cornerY - 2 * from.Y) * (to.X - from.X))
                continue;

            var stepX = Math.Sign(to.X - from.X);
            var stepY = Math.Sign(to.Y - from.Y);
            var before = new Cell(x + (stepX < 0 ? 1 : 0), y + (stepY < 0 ? 1 : 0));
            var after = new Cell(x + (stepX > 0 ? 1 : 0), y + (stepY > 0 ? 1 : 0));
            var horizontalFirst = new Cell(after.X, before.Y);
            var verticalFirst = new Cell(before.X, after.Y);
            if (!PassageHasFeaturedEdge(board, before, horizontalFirst, after) ||
                !PassageHasFeaturedEdge(board, before, verticalFirst, after))
                continue;
            return true;
        }
        return false;
    }

    private static bool PassageHasFeaturedEdge(Board board, Cell first, Cell middle, Cell last) =>
        HasFeaturedEdge(board, first, middle) || HasFeaturedEdge(board, middle, last);

    private static bool HasFeaturedEdge(Board board, Cell a, Cell b) =>
        board.Edges.Any(edge => edge.A == a && edge.B == b || edge.A == b && edge.B == a);

    private static bool CrossesEdgeInterior(Cell from, Cell to, Edge edge)
    {
        var fromX = 2 * from.X;
        var fromY = 2 * from.Y;
        var dx = 2 * (to.X - from.X);
        var dy = 2 * (to.Y - from.Y);
        if (edge.A.Y == edge.B.Y)
        {
            if (dx == 0) return false;
            var denominator = Math.Abs(dx);
            var numerator = Math.Sign(dx) * (edge.A.X + edge.B.X - fromX);
            var scaledY = fromY * denominator + dy * numerator;
            return numerator > 0 && numerator < denominator &&
                (2 * edge.A.Y - 1) * denominator < scaledY &&
                scaledY < (2 * edge.A.Y + 1) * denominator;
        }

        if (dy == 0) return false;
        var horizontalDenominator = Math.Abs(dy);
        var horizontalNumerator = Math.Sign(dy) * (edge.A.Y + edge.B.Y - fromY);
        var scaledX = fromX * horizontalDenominator + dx * horizontalNumerator;
        return horizontalNumerator > 0 && horizontalNumerator < horizontalDenominator &&
            (2 * edge.A.X - 1) * horizontalDenominator < scaledX &&
            scaledX < (2 * edge.A.X + 1) * horizontalDenominator;
    }

    // A center-to-center segment intersects a cell only when it enters its interior.
    private static bool CrossesInterior(Cell from, Cell to, Cell cell)
    {
        double low = 0, high = 1;
        foreach (var (start, delta, center) in new[] {
            ((double)from.X, (double)(to.X - from.X), (double)cell.X),
            ((double)from.Y, (double)(to.Y - from.Y), (double)cell.Y) })
        {
            if (delta == 0)
            {
                if (Math.Abs(start - center) >= 0.5) return false;
                continue;
            }
            var a = (center - 0.5 - start) / delta;
            var b = (center + 0.5 - start) / delta;
            low = Math.Max(low, Math.Min(a, b));
            high = Math.Min(high, Math.Max(a, b));
        }
        return high > low;
    }

    private static void ResolveAttack(GameState state, string attackerId, string targetId,
        IRandomProvider random, List<RulesEvent> events)
    {
        var attacker = state.Units.Single(u => u.Id == attackerId);
        var target = state.Units.Single(u => u.Id == targetId);
        var attackDice = state.Types.Single(t => t.Id == attacker.TypeId).Atk;
        var defenceDice = state.Types.Single(t => t.Id == target.TypeId).Def;
        var hits = 0;
        var blocks = 0;
        for (var i = 0; i < attackDice; i++)
        {
            var face = random.RollAttackDie();
            if (!Enum.IsDefined(face)) throw new ArgumentException("Invalid Attack Die face.", nameof(random));
            if (face == AttackFace.Hit) hits++;
        }
        for (var i = 0; i < defenceDice; i++)
        {
            var face = random.RollDefenceDie();
            if (!Enum.IsDefined(face)) throw new ArgumentException("Invalid Defence Die face.", nameof(random));
            if (face == DefenceFace.Block) blocks++;
        }
        var damage = Math.Max(0, hits - blocks);
        var index = state.Units.IndexOf(target);
        state.Units[index] = target with { CurrentHp = Math.Max(0, target.CurrentHp - damage) };
        events.Add(new RulesEvent("AttackResolved", attackerId, targetId, Hits: hits, Blocks: blocks, Damage: damage));
        if (state.Units[index].CurrentHp == 0)
        {
            state.Physical.Figures.RemoveAll(f => f.Id == targetId);
            events.Add(new RulesEvent("UnitDied", targetId));
        }
    }

    private static void ValidateScenario(GameState state)
    {
        var board = state.Physical.Board;
        if (board.Width < 1 || board.Height < 1 || state.Types.Select(t => t.Id).Distinct().Count() != state.Types.Count ||
            state.Units.Select(u => u.Id).Distinct().Count() != state.Units.Count ||
            state.Types.Any(t => t.Mov < 0 || t.Rng < 0 || t.Atk < 0 || t.Def < 0 || t.Hp < 1))
            throw new ArgumentException("Invalid board, Unit Type, or stat domain.");
        if (state.Units.Any(u => !state.Types.Any(t => t.Id == u.TypeId) ||
            u.CurrentHp < 0 || u.CurrentHp > state.Types.Single(t => t.Id == u.TypeId).Hp ||
            string.IsNullOrWhiteSpace(u.SideId)))
            throw new ArgumentException("Invalid Unit state.");
        var figures = state.Physical.Figures;
        if (figures.Any(f => !Inside(board, f.Position) || !state.Units.Any(u => u.Id == f.Id && u.CurrentHp > 0)) ||
            figures.Select(f => f.Id).Distinct().Count() != figures.Count ||
            figures.Select(f => f.Position).Distinct().Count() != figures.Count ||
            state.Units.Any(u => u.CurrentHp > 0 && !figures.Any(f => f.Id == u.Id)))
            throw new ArgumentException("Invalid figure placement.");
        if (board.Edges.Any(e => !Inside(board, e.A) || !Inside(board, e.B) ||
            Math.Abs(e.A.X - e.B.X) + Math.Abs(e.A.Y - e.B.Y) != 1) ||
            board.Edges.Select(e => e.A.Y < e.B.Y || e.A.Y == e.B.Y && e.A.X < e.B.X
                ? (e.A, e.B) : (e.B, e.A)).Distinct().Count() != board.Edges.Count)
            throw new ArgumentException("Invalid edge features.");
    }
}
