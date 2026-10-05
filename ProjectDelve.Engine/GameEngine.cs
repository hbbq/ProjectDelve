using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static class GameEngine
{
    public static EngineResult StartRound(GameState previous, IRandomProvider random, bool autoChooseSingleRelevantChoice = true)
    {
        if (previous.Round != 0 && !previous.RoundComplete)
            throw new InvalidOperationException("The current round is still active.");
        ValidateScenario(previous);
        var state = previous.Copy();
        // Scenario constructors may supply bare Units; initialize content once at game start.
        if (state.Round == 0)
            state.Units = state.Units.Select(u => u with
            {
                CleaveUses = u.CleaveUses ?? (state.Types.Single(t => t.Id == u.TypeId).Cleave is { } cleave
                    ? new(cleave.MaxUses, cleave.MaxUses) : null),
                HealUses = u.HealUses ?? (state.Types.Single(t => t.Id == u.TypeId).Heal is { } heal
                    ? new(heal.MaxUses, heal.MaxUses) : null),
                HolyWaveUses = u.HolyWaveUses ?? (state.Types.Single(t => t.Id == u.TypeId).HolyWave is { } wave
                    ? new(wave.MaxUses, wave.MaxUses) : null),
                BonusActionUses = state.Types.Single(t => t.Id == u.TypeId).BonusActions
                    .Aggregate(u.BonusActionUses, (uses, ability) => uses.ContainsKey(ability.Name)
                        ? uses : uses.Add(ability.Name, new(ability.MaxUses, ability.MaxUses)))
            }).ToList();
        state.Round++;
        state.Bag = state.Types.Where(t => state.Units.Any(u => u.TypeId == t.Id && u.CurrentHp > 0))
            .Select(t => t.Id).ToList();
        state.ActiveTypeId = null;
        state.CurrentUnitId = null;
        state.MoveAfterAttackAllowance = null;
        state.CleavePending = false;
        state.MoveDone = false;
        state.ActionDone = false;
        state.BonusActionsUsedThisActivation.Clear();
        state.ModifiersThisTurn.Clear();
        state.Pending = null;
        state.CompletedUnitIds.Clear();
        state.RoundComplete = false;
        var events = new ResolutionEvents(state);
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }

    public static EngineResult Advance(GameState previous, IDecisionProvider decisions, IRandomProvider random, bool autoChooseSingleRelevantChoice = true)
    {
        if (previous.Pending is null) throw new InvalidOperationException("No decision is pending.");
        var state = previous.Copy();
        var events = new ResolutionEvents(state);
        if (state.CurrentUnitId is not null &&
            !state.Units.Any(u => u.Id == state.CurrentUnitId && u.CurrentHp > 0))
        {
            // A dead Unit cannot resume either its activation or a mandatory follow-up.
            state.Pending = null;
            RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
            return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
        }
        // Pending data is also exposed to clients and survives serialization. Rebuild
        // legality from authoritative state, and never share canonical paths with a provider.
        var request = CreateDecision(state);
        var choice = SelectChoice(request, decisions, new GameplayQueries(state), autoChooseSingleRelevantChoice);

        state.Pending = null;
        ApplyDecision(state, request, choice, random, events);
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }

    internal static string? SelectChoice(DecisionRequest request, IDecisionProvider decisions,
        IGameplayQueries queries, bool autoChooseSingleRelevantChoice)
    {
        if (!TryAutomaticChoice(request, autoChooseSingleRelevantChoice, out var choice))
        {
            choice = decisions.Choose(request with
            {
                Candidates = request.Candidates.Select(c => c with
                {
                    Path = c.Path is null ? null : [.. c.Path]
                }).ToList()
            }, queries);
        }
        if (choice is null && !request.AllowsNone ||
            choice is not null && !request.Candidates.Any(c => c.Key == choice))
            throw new ArgumentException("Decision is not among the supplied legal candidates.", nameof(decisions));

        return choice;
    }

    internal static bool TryAutomaticChoice(DecisionRequest request, bool autoChooseSingleRelevantChoice,
        out string? choice)
    {
        choice = null;
        if (request.Candidates.Count == 0 && request.AllowsNone) return true;
        // Ordinary forced choices are independent of relevance and its preference.
        if (request.Candidates.Count == 1 && !request.AllowsNone)
        {
            choice = request.Candidates[0].Key;
            return true;
        }
        // The preference only adds auto-choice among multiple legal candidates.
        // Multiple legal choices with zero relevant candidates still need input.
        if (autoChooseSingleRelevantChoice && request.Candidates.Count > 1 && !request.AllowsNone)
        {
            var relevant = request.Candidates.Where(c => c.Relevant).ToList();
            if (relevant.Count == 1)
            {
                choice = relevant[0].Key;
                return true;
            }
        }
        return false;
    }

    private static void ApplyDecision(GameState state, DecisionRequest request, string? choice,
        IRandomProvider random, ResolutionEvents events)
    {
        switch (request.Kind)
        {
            case DecisionKind.Cleave:
                state.CleavePending = false;
                if (choice is not null)
                {
                    var cleaverIndex = state.Units.FindIndex(u => u.Id == request.UnitId);
                    var cleaveUses = state.Units[cleaverIndex].CleaveUses!;
                    state.Units[cleaverIndex] = state.Units[cleaverIndex] with
                    {
                        CleaveUses = new(cleaveUses.MaxUses, cleaveUses.RemainingUses - 1)
                    };
                    var targetId = request.Candidates.Single(c => c.Key == choice).TargetId!;
                    DealDamage(state, targetId, 1,
                        new RulesEvent("CleaveResolved", request.UnitId, targetId, Damage: 1, AbilityName: "Cleave"), events);
                }
                break;
            case DecisionKind.SelectUnit:
                state.CurrentUnitId = choice;
                state.MoveDone = false;
                state.ActionDone = false;
                state.BonusActionsUsedThisActivation.Clear();
                state.ModifiersThisTurn.Clear();
                break;
            case DecisionKind.Activation when request.Candidates.Single(c => c.Key == choice).Kind == ActivationChoiceKind.BonusAction:
                var ability = request.Candidates.Single(c => c.Key == choice).BonusAction!;
                var index = state.Units.FindIndex(u => u.Id == request.UnitId);
                var uses = state.Units[index].BonusActionUses[ability.Name];
                state.Units[index] = state.Units[index] with
                {
                    BonusActionUses = state.Units[index].BonusActionUses.SetItem(ability.Name,
                        new(uses.MaxUses, uses.RemainingUses - 1))
                };
                state.BonusActionsUsedThisActivation.Add(ability.Name);
                state.ModifiersThisTurn.AddRange(ability.Modifiers);
                events.Add(new RulesEvent("AbilityUsed", request.UnitId, AbilityName: ability.Name));
                break;
            case DecisionKind.Activation when request.Candidates.Single(c => c.Key == choice).Kind == ActivationChoiceKind.EndTurn:
                CompleteUnit(state);
                break;
            case DecisionKind.Activation when request.Candidates.Single(c => c.Key == choice).FreeAction == UnitFreeAction.OpenDoor:
                OpenDoor(state, request.UnitId!, request.Candidates.Single(c => c.Key == choice).Door!, events);
                break;
            case DecisionKind.Activation when request.Candidates.Single(c => c.Key == choice).Kind is ActivationChoiceKind.Move or ActivationChoiceKind.Stay:
            case DecisionKind.Move:
                var figure = state.Physical.Figures.Single(f => f.Id == request.UnitId);
                var path = choice is null ? new List<Cell> { figure.Position } :
                    request.Candidates.Single(c => c.Key == choice).Path!;
                if (choice is not null)
                    state.Physical.Figures[state.Physical.Figures.IndexOf(figure)] =
                        figure with { Position = path[^1] };
                if (request.IsMoveAfterAttack)
                {
                    state.MoveAfterAttackAllowance = null;
                }
                else state.MoveDone = true;
                events.Add(new RulesEvent("MovementCompleted", request.UnitId, Path: [.. path],
                    IsMoveAfterAttack: request.IsMoveAfterAttack));
                break;
            case DecisionKind.Activation:
                state.ActionDone = true;
                var attacked = false;
                if (choice is not null)
                {
                    var action = request.Candidates.Single(c => c.Key == choice);
                    if (action.TryOpenDoor is { } attempt)
                    {
                        var roll = random.RollD6();
                        if (roll is < 1 or > 6)
                            throw new ArgumentException("Invalid D6 face.", nameof(random));
                        // Designate faces 1 through SuccessCount as the success faces.
                        var succeeded = roll <= attempt.SuccessCount;
                        events.Add(new RulesEvent("DoorOpeningAttemptResolved", request.UnitId,
                            Door: action.Door, DieRoll: roll, SuccessCount: attempt.SuccessCount,
                            Succeeded: succeeded));
                        if (succeeded) OpenDoor(state, request.UnitId!, action.Door!, events);
                    }
                    else switch (action.Action)
                    {
                        case UnitAction.NormalAttack:
                            ResolveAttack(state, request.UnitId!, [action.TargetId!],
                                state.EffectiveAtkAgainst(request.UnitId!, action.TargetId!), random, events);
                            attacked = true;
                            break;
                        case UnitAction.HolyWave:
                            var waveIndex = state.Units.FindIndex(u => u.Id == request.UnitId);
                            var waveUses = state.Units[waveIndex].HolyWaveUses!;
                            state.Units[waveIndex] = state.Units[waveIndex] with
                            {
                                HolyWaveUses = new(waveUses.MaxUses, waveUses.RemainingUses - 1)
                            };
                            ResolveAttack(state, request.UnitId!, action.TargetIds, 2, random, events, "Holy Wave");
                            attacked = true;
                            break;
                        case UnitAction.Heal:
                            ResolveHeal(state, request.UnitId!, action.TargetId!, events);
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported action.");
                    }
                }
                if (attacked && state.Units.Single(u => u.Id == request.UnitId).CurrentHp > 0 && state.Types.Single(t => t.Id == request.TypeId).MoveAfterAttack is { } move)
                {
                    state.CurrentUnitId = request.UnitId;
                    state.MoveAfterAttackAllowance = move.MaxSteps;
                }
                break;
        }
    }

    private static void RunUntilDecision(GameState state, IRandomProvider random, ResolutionEvents events,
        bool autoChooseSingleRelevantChoice)
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
                state.CompletedUnitIds.Clear();
                events.Add(new RulesEvent("TokenDrawn", TypeId: drawn));
            }

            if (state.CurrentUnitId is not null &&
                !state.Units.Any(u => u.Id == state.CurrentUnitId && u.CurrentHp > 0))
                CompleteUnit(state);
            if (state.CurrentUnitId is null && EligibleUnits(state).Count == 0)
            {
                state.CompletedUnitIds.Clear();
                state.ActiveTypeId = null;
                continue;
            }
            var request = CreateDecision(state);
            if (!TryAutomaticChoice(request, autoChooseSingleRelevantChoice, out var choice))
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
        state.MoveAfterAttackAllowance = null;
        state.CleavePending = false;
        state.MoveDone = false;
        state.ActionDone = false;
        state.BonusActionsUsedThisActivation.Clear();
        state.ModifiersThisTurn.Clear();
    }

    private static void OpenDoor(GameState state, string unitId, Edge door, ResolutionEvents events)
    {
        var index = state.Physical.Board.Edges.FindIndex(e =>
            e.A == door.A && e.B == door.B || e.A == door.B && e.B == door.A);
        var opened = state.Physical.Board.Edges[index] with { Kind = EdgeKind.OpenDoor };
        state.Physical.Board.Edges[index] = opened;
        events.Add(new RulesEvent("DoorOpened", unitId, Door: opened));
    }

    private static List<Candidate> MovementCandidates(GameState state, Unit unit, int? maxSteps = null)
    {
        var start = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
        var allowance = maxSteps ?? state.EffectiveMovOf(unit.Id);
        return MovementRules.FindPaths(state, unit.Id, start, allowance)
            .Where(pair => pair.Key != start)
            .OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X)
            .Select(pair => new Candidate($"{pair.Key.X},{pair.Key.Y}", pair.Key, [.. pair.Value], Kind: ActivationChoiceKind.Move)).ToList();
    }

    private static bool Inside(Board board, Cell cell) =>
        cell.X >= 0 && cell.X < board.Width && cell.Y >= 0 && cell.Y < board.Height;

    private static List<Candidate> ActionCandidates(GameState state, Unit unit)
    {
        var from = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        var actions = type.Actions;
        var candidates = new List<Candidate>();
        if (actions.HasFlag(UnitAction.NormalAttack))
            candidates.AddRange(state.Units
                .Where(target => AttackRules.EvaluateFrom(state, unit.Id, from, target.Id)
                    == NormalAttackEvaluation.Possible)
                .Select(target => new Candidate($"attack:{target.Id}",
                    Action: UnitAction.NormalAttack, TargetId: target.Id)));
        if (actions.HasFlag(UnitAction.Heal) && type.Heal is not null &&
            unit.HealUses is { RemainingUses: > 0 })
            candidates.AddRange(state.Units.Where(target => target.Id != unit.Id &&
                    target.SideId == unit.SideId && target.CurrentHp > 0 &&
                    target.CurrentHp < state.Types.Single(t => t.Id == target.TypeId).Hp &&
                    SpatialRules.AreAdjacent(state.Physical.Board, from,
                        state.Physical.Figures.Single(f => f.Id == target.Id).Position))
                .Select(target => new Candidate($"heal:{target.Id}",
                    Action: UnitAction.Heal, TargetId: target.Id)));
        if (actions.HasFlag(UnitAction.HolyWave) && type.HolyWave is not null &&
            unit.HolyWaveUses is { RemainingUses: > 0 })
        {
            var targets = AdjacentHostiles(state, unit).Select(u => u.Id).ToImmutableArray();
            if (targets.Length > 0)
                candidates.Add(new Candidate("holy-wave", Action: UnitAction.HolyWave) { TargetIds = targets });
        }
        if (type.TryOpenDoor is not null)
        {
            foreach (var edge in AdjacentClosedDoors(state, unit))
            {
                var key = $"{edge.A.X},{edge.A.Y}:{edge.B.X},{edge.B.Y}";
                candidates.Add(new Candidate($"try-open-door:{key}", Door: edge, TryOpenDoor: type.TryOpenDoor));
            }
        }
        return candidates;
    }

    private static IEnumerable<Edge> AdjacentClosedDoors(GameState state, Unit unit)
    {
        var from = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
        return state.Physical.Board.Edges
            .Where(edge => edge.Kind == EdgeKind.ClosedDoor && (edge.A == from || edge.B == from))
            .Select(edge => edge.A.Y < edge.B.Y || edge.A.Y == edge.B.Y && edge.A.X < edge.B.X
                ? edge : edge with { A = edge.B, B = edge.A })
            .OrderBy(edge => edge.A.Y).ThenBy(edge => edge.A.X)
            .ThenBy(edge => edge.B.Y).ThenBy(edge => edge.B.X);
    }

    private static IEnumerable<Candidate> FreeActionCandidates(GameState state, Unit unit)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        if (!type.FreeActions.HasFlag(UnitFreeAction.OpenDoor)) return [];
        return AdjacentClosedDoors(state, unit).Select(edge => new Candidate(
            $"open-door:{edge.A.X},{edge.A.Y}:{edge.B.X},{edge.B.Y}", Door: edge,
            Kind: ActivationChoiceKind.FreeAction, FreeAction: UnitFreeAction.OpenDoor));
    }

    private static List<Unit> EligibleUnits(GameState state) =>
        state.Units.Where(u => u.TypeId == state.ActiveTypeId && u.CurrentHp > 0 &&
                !state.CompletedUnitIds.Contains(u.Id))
            .OrderBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.Y)
            .ThenBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.X)
            .ToList();

    private static IEnumerable<Candidate> BonusActionCandidates(GameState state, Unit unit)
    {
        foreach (var ability in state.Types.Single(t => t.Id == unit.TypeId).BonusActions)
            if (!state.BonusActionsUsedThisActivation.Contains(ability.Name) &&
                unit.BonusActionUses.TryGetValue(ability.Name, out var uses) && uses.RemainingUses > 0)
                yield return new Candidate($"bonus-action:{ability.Name}", Kind: ActivationChoiceKind.BonusAction,
                    Relevant: BonusActionRelevant(state, unit, ability), BonusAction: ability);
    }

    private static bool BonusActionRelevant(GameState state, Unit unit, BonusActionAbility ability)
    {
        var modified = state.Copy();
        // Compare gameplay results of the complete effect, regardless of its modifiers.
        modified.ModifiersThisTurn.AddRange(ability.Modifiers);
        var before = GameplayCandidates(state, unit).ToList();
        var after = GameplayCandidates(modified, unit).ToList();
        var destinations = before.Where(c => c.Kind == ActivationChoiceKind.Move)
            .Select(c => c.Destination).ToHashSet();
        if (after.Any(c => c.Kind == ActivationChoiceKind.Move && !destinations.Contains(c.Destination)))
            return true;

        var targets = before.Where(c => c.Action == UnitAction.NormalAttack)
            .Select(c => c.TargetId).ToHashSet();
        // A new target improves targeting; a shared target improves effectiveness
        // only when resolution would roll more attack dice.
        return after.Where(c => c.Action == UnitAction.NormalAttack).Any(c =>
            !targets.Contains(c.TargetId) ||
            modified.EffectiveAtkAgainst(unit.Id, c.TargetId!) > state.EffectiveAtkAgainst(unit.Id, c.TargetId!));
    }

    // Authoritative Move/Action opportunities at this point in the activation.
    // Both decision generation and hypothetical-effect relevance use this query.
    internal static IEnumerable<Candidate> GameplayCandidates(GameState state, Unit unit)
    {
        if (state.CleavePending) return [];
        if (state.MoveAfterAttackAllowance is { } allowance)
            return MovementCandidates(state, unit, allowance);
        if (!state.MoveDone) return MovementCandidates(state, unit);
        return state.ActionDone ? [] : ActionCandidates(state, unit);
    }

    private static DecisionRequest CreateDecision(GameState state)
    {
        if (state.CleavePending)
        {
            var cleaver = state.Units.Single(u => u.Id == state.CurrentUnitId);
            return new(DecisionKind.Cleave, cleaver.TypeId, cleaver.Id, CleaveCandidates(state, cleaver), true);
        }
        if (state.MoveAfterAttackAllowance is not null)
        {
            var mover = state.Units.Single(u => u.Id == state.CurrentUnitId);
            return new DecisionRequest(DecisionKind.Move, mover.TypeId, mover.Id,
                GameplayCandidates(state, mover).ToList(), true, IsMoveAfterAttack: true);
        }
        var eligible = EligibleUnits(state);
        if (state.CurrentUnitId is null)
            return new DecisionRequest(DecisionKind.SelectUnit, state.ActiveTypeId!, null,
                eligible.Select(u => new Candidate(u.Id, Kind: ActivationChoiceKind.SelectUnit)).ToList(), false);

        var unit = eligible.Single(u => u.Id == state.CurrentUnitId);
        var candidates = new List<Candidate>();
        candidates.AddRange(FreeActionCandidates(state, unit));
        candidates.AddRange(BonusActionCandidates(state, unit));
        candidates.AddRange(GameplayCandidates(state, unit));
        if (!state.MoveDone)
        {
            var position = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
            candidates.Add(new Candidate("stay", position, [position], Kind: ActivationChoiceKind.Stay));
        }
        else
        {
            candidates.Add(new Candidate("end-turn", Kind: ActivationChoiceKind.EndTurn));
        }
        return new DecisionRequest(DecisionKind.Activation, unit.TypeId, unit.Id, candidates, false);
    }

    // Rebuild all legal choices when a host changes relevance-based auto-choice.
    // Continue any newly automatic choices through the same resolution path.
    public static EngineResult RefreshChoices(GameState previous, IRandomProvider random,
        bool autoChooseSingleRelevantChoice = true)
    {
        var state = previous.Copy();
        state.Pending = null;
        var events = new ResolutionEvents(state);
        if (state.Round > 0) RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }

    private static void ResolveAttack(GameState state, string attackerId,
        ImmutableArray<string> targetIds, int attackDice,
        IRandomProvider random, ResolutionEvents events, string? abilityName = null)
    {
        var hits = 0;
        for (var i = 0; i < attackDice; i++)
        {
            var face = random.RollAttackDie();
            if (!Enum.IsDefined(face)) throw new ArgumentException("Invalid Attack Die face.", nameof(random));
            if (face == AttackFace.Hit) hits++;
        }
        var results = new List<AttackTargetResult>();
        // The authoritative candidate fixed membership before any roll. Death and
        // removal may change the world (including DEF), never the remaining targets.
        foreach (var targetId in targetIds)
        {
            var defenceDice = state.EffectiveDefOf(targetId);
            var blocks = 0;
            for (var i = 0; i < defenceDice; i++)
            {
                var face = random.RollDefenceDie();
                if (!Enum.IsDefined(face)) throw new ArgumentException("Invalid Defence Die face.", nameof(random));
                if (face == DefenceFace.Block) blocks++;
            }
            var damage = Math.Max(0, hits - blocks);
            results.Add(new(targetId, defenceDice, blocks, damage));
            DealDamage(state, targetId, damage,
                new RulesEvent(abilityName is null ? "AttackResolved" : "AttackTargetResolved",
                    attackerId, targetId, Hits: hits, Blocks: blocks, Damage: damage,
                    AbilityName: abilityName,
                    Attack: abilityName is null ? new(attackDice, hits, [.. results]) : null), events);
        }
        if (abilityName is not null)
            events.Add(new RulesEvent("AttackResolved", attackerId, Hits: hits,
                AbilityName: abilityName, Attack: new(attackDice, hits, [.. results])));
        var attacker = state.Units.Single(u => u.Id == attackerId);
        if (results.Any(r => r.Damage >= 2) && attacker.CurrentHp > 0 && CleaveCandidates(state, attacker).Count > 0)
            state.CleavePending = true;
    }

    private static void ResolveHeal(GameState state, string healerId, string targetId, ResolutionEvents events)
    {
        var healerIndex = state.Units.FindIndex(u => u.Id == healerId);
        var uses = state.Units[healerIndex].HealUses!;
        state.Units[healerIndex] = state.Units[healerIndex] with
        {
            HealUses = new(uses.MaxUses, uses.RemainingUses - 1)
        };
        var targetIndex = state.Units.FindIndex(u => u.Id == targetId);
        var target = state.Units[targetIndex];
        var healing = Math.Min(2, state.Types.Single(t => t.Id == target.TypeId).Hp - target.CurrentHp);
        state.Units[targetIndex] = target with { CurrentHp = target.CurrentHp + healing };
        events.Add(new RulesEvent("HealResolved", healerId, targetId, AbilityName: "Heal", Healing: healing));
    }

    private static List<Candidate> CleaveCandidates(GameState state, Unit unit)
    {
        if (state.Types.Single(t => t.Id == unit.TypeId).Cleave is null ||
            unit.CleaveUses is not { RemainingUses: > 0 }) return [];
        return AdjacentHostiles(state, unit)
            .Select(target => new Candidate($"cleave:{target.Id}", TargetId: target.Id,
                Kind: ActivationChoiceKind.Cleave)).ToList();
    }

    private static IEnumerable<Unit> AdjacentHostiles(GameState state, Unit unit)
    {
        var from = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
        return state.Units.Where(target => target.CurrentHp > 0 && target.SideId != unit.SideId &&
                SpatialRules.AreAdjacent(state.Physical.Board, from,
                    state.Physical.Figures.Single(f => f.Id == target.Id).Position));
    }

    private static void DealDamage(GameState state, string targetId, int damage,
        RulesEvent resolved, ResolutionEvents events)
    {
        var target = state.Units.Single(u => u.Id == targetId);
        var index = state.Units.IndexOf(target);
        state.Units[index] = target with { CurrentHp = Math.Max(0, target.CurrentHp - damage) };
        events.Add(resolved);
        if (state.Units[index].CurrentHp == 0)
        {
            state.Physical.Figures.RemoveAll(f => f.Id == targetId);
            events.Add(new RulesEvent("UnitDied", targetId));
        }
    }

    // Capture at emission time, before later operations mutate this run's working state.
    private sealed class ResolutionEvents(GameState state)
    {
        public List<RulesEvent> Events { get; } = [];
        public List<ResolutionStep> Steps { get; } = [];

        public void Add(RulesEvent resolved)
        {
            Steps.Add(new ResolutionStep(Events.Count, state.Copy()));
            Events.Add(resolved);
        }
    }

    private static void ValidateScenario(GameState state)
    {
        var board = state.Physical.Board;
        if (board.Width < 1 || board.Height < 1 || state.Types.Select(t => t.Id).Distinct().Count() != state.Types.Count ||
            state.Units.Select(u => u.Id).Distinct().Count() != state.Units.Count ||
            state.Types.Any(t => t.Mov < 0 || t.Rng < 0 || t.Atk < 0 || t.Def < 0 || t.Hp < 1 ||
                t.TryOpenDoor is { SuccessCount: < 0 or > 6 } || t.MoveAfterAttack is { MaxSteps: < 0 } ||
                t.Cleave is { MaxUses: < 1 } ||
                t.Heal is { MaxUses: < 1 } ||
                t.HolyWave is { MaxUses: < 1 } ||
                t.BonusActions.Any(ability => ability.MaxUses < 1 || string.IsNullOrWhiteSpace(ability.Name) ||
                    ability.Modifiers.Any(m => !Enum.IsDefined(m.Stat))) ||
                t.BonusActions.Select(a => a.Name).Distinct().Count() != t.BonusActions.Length))
            throw new ArgumentException("Invalid board, Unit Type, or stat domain.");
        if (state.Units.Any(u => !state.Types.Any(t => t.Id == u.TypeId) ||
            u.CurrentHp < 0 || u.CurrentHp > state.Types.Single(t => t.Id == u.TypeId).Hp ||
            string.IsNullOrWhiteSpace(u.SideId)))
            throw new ArgumentException("Invalid Unit state.");
        if (state.Units.Any(u => u.BonusActionUses.Any(entry =>
                state.Types.Single(t => t.Id == u.TypeId).BonusActions
                    .SingleOrDefault(a => a.Name == entry.Key)?.MaxUses != entry.Value.MaxUses)))
            throw new ArgumentException("Ability uses must match Unit Type content.");
        if (state.Units.Any(u => u.CleaveUses is { } uses &&
            state.Types.Single(t => t.Id == u.TypeId).Cleave?.MaxUses != uses.MaxUses))
            throw new ArgumentException("Cleave uses must match Unit Type content.");
        if (state.Units.Any(u => u.HealUses is { } uses &&
            state.Types.Single(t => t.Id == u.TypeId).Heal?.MaxUses != uses.MaxUses))
            throw new ArgumentException("Heal uses must match Unit Type content.");
        if (state.Units.Any(u => u.HolyWaveUses is { } uses &&
            state.Types.Single(t => t.Id == u.TypeId).HolyWave?.MaxUses != uses.MaxUses))
            throw new ArgumentException("Holy Wave uses must match Unit Type content.");
        var figures = state.Physical.Figures;
        if (board.Terrain.Any(tile => !Inside(board, tile.Position) || !Enum.IsDefined(tile.Kind)) ||
            board.Terrain.Select(tile => tile.Position).Distinct().Count() != board.Terrain.Count)
            throw new ArgumentException("Invalid terrain tiles.");
        if (figures.Any(f => !Inside(board, f.Position) || !state.Units.Any(u => u.Id == f.Id && u.CurrentHp > 0)) ||
            figures.Select(f => f.Id).Distinct().Count() != figures.Count ||
            figures.Select(f => f.Position).Distinct().Count() != figures.Count ||
            state.Units.Any(u => u.CurrentHp > 0 && !figures.Any(f => f.Id == u.Id)))
            throw new ArgumentException("Invalid figure placement.");
        if (board.Edges.Any(e => !Inside(board, e.A) || !Inside(board, e.B) ||
            Math.Abs(e.A.X - e.B.X) + Math.Abs(e.A.Y - e.B.Y) != 1 || !Enum.IsDefined(e.Kind)) ||
            board.Edges.Select(e => e.A.Y < e.B.Y || e.A.Y == e.B.Y && e.A.X < e.B.X
                ? (e.A, e.B) : (e.B, e.A)).Distinct().Count() != board.Edges.Count)
            throw new ArgumentException("Invalid edge features.");
    }
}
