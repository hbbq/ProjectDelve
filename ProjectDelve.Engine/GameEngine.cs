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
        if (previous.Pending is null) throw new InvalidOperationException("No decision is pending.");
        var state = previous.Copy();
        // Pending data is also exposed to clients and survives serialization. Rebuild
        // legality from authoritative state, and never share canonical paths with a provider.
        var request = CreateDecision(state);
        if (!TryAutomaticChoice(request, out var choice))
        {
            choice = decisions.Choose(request with
            {
                Candidates = request.Candidates.Select(c => c with
                {
                    Path = c.Path is null ? null : [.. c.Path]
                }).ToList()
            }, new GameplayQueries(state));
        }
        if (choice is null && !request.AllowsNone ||
            choice is not null && !request.Candidates.Any(c => c.Key == choice))
            throw new ArgumentException("Decision is not among the supplied legal candidates.", nameof(decisions));

        state.Pending = null;
        var events = new List<RulesEvent>();
        ApplyDecision(state, request, choice, random, events);
        RunUntilDecision(state, random, events);
        return new(state, events, state.Pending);
    }

    private static bool TryAutomaticChoice(DecisionRequest request, out string? choice)
    {
        choice = null;
        if (request.Candidates.Count == 0 && request.AllowsNone) return true;
        if (request.Candidates.Count == 1 && !request.AllowsNone)
        {
            choice = request.Candidates[0].Key;
            return true;
        }
        return false;
    }

    private static void ApplyDecision(GameState state, DecisionRequest request, string? choice,
        IRandomProvider random, List<RulesEvent> events)
    {
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
                // Token selection must not expose the authoritative bag to a provider.
                var drawn = random.DrawToken(Array.AsReadOnly(state.Bag.ToArray()));
                if (!state.Bag.Remove(drawn))
                    throw new ArgumentException("Random provider drew a token outside the bag.", nameof(random));
                state.ActiveTypeId = drawn;
                state.Phase = Phase.BonusAction;
                state.CompletedUnitIds.Clear();
                events.Add(new RulesEvent("TokenDrawn", TypeId: drawn));
            }

            var eligible = EligibleUnits(state);
            if (eligible.Count == 0)
            {
                state.CompletedUnitIds.Clear();
                state.CurrentUnitId = null;
                if (state.Phase == Phase.Act) state.ActiveTypeId = null;
                else state.Phase++;
                continue;
            }
            var request = CreateDecision(state);
            if (!TryAutomaticChoice(request, out var choice))
            {
                state.Pending = request;
                return;
            }
            ApplyDecision(state, request, choice, random, events);
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
        return MovementRules.FindPaths(state, unit.Id, start, allowance)
            .Where(pair => pair.Key != start)
            .OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X)
            .Select(pair => new Candidate($"{pair.Key.X},{pair.Key.Y}", pair.Key, [.. pair.Value])).ToList();
    }

    private static bool Inside(Board board, Cell cell) =>
        cell.X >= 0 && cell.X < board.Width && cell.Y >= 0 && cell.Y < board.Height;

    private static List<Candidate> AttackCandidates(GameState state, Unit attacker)
    {
        var from = state.Physical.Figures.Single(f => f.Id == attacker.Id).Position;
        return state.Units
            .Where(target => AttackRules.EvaluateFrom(state, attacker.Id, from, target.Id)
                == NormalAttackEvaluation.Possible)
            .Select(target => new Candidate(target.Id)).ToList();
    }

    private static List<Unit> EligibleUnits(GameState state) =>
        state.Units.Where(u => u.TypeId == state.ActiveTypeId && u.CurrentHp > 0 &&
                !state.CompletedUnitIds.Contains(u.Id))
            .OrderBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.Y)
            .ThenBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.X)
            .ToList();

    private static DecisionRequest CreateDecision(GameState state)
    {
        var eligible = EligibleUnits(state);
        if (state.CurrentUnitId is null)
            return new DecisionRequest(DecisionKind.SelectUnit, state.ActiveTypeId!, null,
                eligible.Select(u => new Candidate(u.Id)).ToList(), false);

        var unit = eligible.Single(u => u.Id == state.CurrentUnitId);
        return state.Phase switch
        {
            Phase.Move => new DecisionRequest(DecisionKind.Move, state.ActiveTypeId!, unit.Id,
                MovementCandidates(state, unit), true),
            Phase.Act => new DecisionRequest(DecisionKind.Attack, state.ActiveTypeId!, unit.Id,
                AttackCandidates(state, unit), true),
            _ => throw new InvalidOperationException("No decision is available in this phase.")
        };
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
