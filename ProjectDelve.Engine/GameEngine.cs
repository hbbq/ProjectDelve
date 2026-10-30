using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    // Creates validated initial setup only; StartRound owns activation initialization and randomness.
    public static GameState CreateGame(ScenarioDefinition definition) => ScenarioCreation.Create(definition);

    // Observation runs synchronously at event production. It must not wait for a client.
    // Observer exceptions propagate; no result is committed, but consumed randomness is not undone.
    public static EngineResult StartRound(GameState previous, IRandomProvider random, bool autoChooseSingleRelevantChoice = true,
        Action<RulesEvent, ResolutionStep>? observe = null)
    {
        if (previous.IsCompleted) throw new InvalidOperationException("The game is complete.");
        if (previous.Round != 0 && !previous.RoundComplete)
            throw new InvalidOperationException("The current round is still active.");
        ValidateScenario(previous);
        var state = previous.Copy();
        FogOfWar.Refresh(state);
        EnsureObjectives(state);
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
                FireballUses = u.FireballUses ?? (state.Types.Single(t => t.Id == u.TypeId).Fireball is { } fireball
                    ? new(fireball.MaxUses, fireball.MaxUses) : null),
                BonusActionUses = state.Types.Single(t => t.Id == u.TypeId).BonusActions.Where(a => a.MaxUses.HasValue)
                    .Aggregate(u.BonusActionUses, (uses, ability) => uses.ContainsKey(ability.Name)
                        ? uses : uses.Add(ability.Name, new(ability.MaxUses!.Value, ability.MaxUses.Value)))
            }).ToList();
        state.Round++;
        state.Bag.Clear();
        state.ActiveToken = null;
        state.CurrentUnitId = null;
        state.MoveAfterAttackAllowance = null;
        state.AdvanceOnKillCells = null;
        state.CleavePending = false;
        state.AttackInProgress = null;
        state.DoorInProgress = null;
        state.PendingExplosions.Clear();
        state.ExtraActionsThisActivation = 0;
        state.MoveOpportunityModifierThisActivation = 0;
        state.MovesCompletedThisActivation = 0;
        state.WaypointAdvancedThisActivation = false;
        state.ActionDone = false;
        state.BonusActionUseCountsThisActivation.Clear();
        state.BonusActionsUsedThisActivation.Clear();
        state.ModifiersThisTurn.Clear();
        state.Pending = null;
        state.CompletedUnitIds.Clear();
        state.RoundComplete = false;
        var events = new ResolutionEvents(state, random, observe);
        if (state.Round == 1)
        {
            events.LootArrivals.AddRange(state.Physical.Figures.OrderBy(f => f.Position.Y).ThenBy(f => f.Position.X).Select(f => f.Id));
            ResolveLootArrivals(state, events);
        }
        var hasLava = state.Physical.Board.Terrain.Any(t => t.Kind == TerrainKind.FruitTree || t.Kind == TerrainKind.Lava && t.CoalGeneration != false);
        if (state.WorldEffects is not null || hasLava)
            events.Add(new RulesEvent("RoundStarted") { Round = state.Round });
        ResolveRoundStartWorldCards(state, random, events);
        ResolveLavaCoalGeneration(state, random, events);
        if (state.WorldEffects is { CardsPerRound: > 0 })
        {
            EvaluateObjectives(state, events);
            if (state.IsCompleted) return new(state, events.Events, null) { ResolutionSteps = events.Steps };
        }
        state.Bag = state.Types.SelectMany(t => state.Units
            .Where(u => u.TypeId == t.Id && u.CurrentHp > 0).Select(u => u.SideId)
            .Distinct().OrderBy(side => side, StringComparer.Ordinal)
            .Select(side => new ActivationToken(t.Id, side))).ToList();
        // Automatic Round Start operations expose a separate bag-population boundary.
        events.Add(new RulesEvent(state.WorldEffects is null && !hasLava ? "RoundStarted" : "ActivationBagPopulated") { Round = state.Round });
        RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }

    // The observer contract and exception behavior are the same as StartRound.
    public static EngineResult Advance(GameState previous, IDecisionProvider decisions, IRandomProvider random, bool autoChooseSingleRelevantChoice = true,
        Action<RulesEvent, ResolutionStep>? observe = null)
    {
        if (previous.IsCompleted) throw new InvalidOperationException("The game is complete.");
        if (previous.Pending is null) throw new InvalidOperationException("No decision is pending.");
        var state = previous.Copy();
        EnsureObjectives(state);
        var events = new ResolutionEvents(state, random, observe);
        if (state.AttackInProgress is null && state.DoorInProgress is null && state.CurrentUnitId is not null &&
            (!state.Units.Any(u => u.Id == state.CurrentUnitId && u.CurrentHp > 0) ||
             !state.IsUpright(state.CurrentUnitId)))
        {
            // A dead or Lying Unit cannot resume ordinary choices or follow-ups.
            state.Pending = null;
            RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
            return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
        }
        // Pending data is also exposed to clients and survives serialization. Rebuild
        // legality from authoritative state, and never share canonical paths with a provider.
        var request = CreateDecision(state, preview: false);
        var choice = SelectChoice(FogOfWar.ObserveDecision(state, request.Token?.SideId, request)!,
            decisions, new GameplayQueries(state), autoChooseSingleRelevantChoice);

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
        if (request.Kind == DecisionKind.RollDice) return false;
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
        if (choice is not null && (request.Candidates.Single(c => c.Key == choice).ObjectKind is not null || request.Candidates.Single(c => c.Key == choice).EquipmentTypeId is not null))
        {
            ResolveObjectChoice(state, request, request.Candidates.Single(c => c.Key == choice), events);
            return;
        }
        switch (request.Kind)
        {
            case DecisionKind.RollDice:
                SubmitRoll(state, request.Roll!, random, events);
                break;
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
                    var cleave = state.Types.Single(t => t.Id == request.TypeId).Cleave!;
                    DealDamage(state, targetId, cleave.Damage,
                        new RulesEvent("CleaveResolved", request.UnitId, targetId, Damage: cleave.Damage,
                            AbilityName: state.Types.Single(t => t.Id == request.TypeId).AbilityNames.Cleave ?? "Cleave")
                            { ActionId = "cleave", SourceUnitId = request.UnitId }, events);
                }
                break;
            case DecisionKind.SelectUnit:
                state.CurrentUnitId = choice;
                state.ExtraActionsThisActivation = 0;
        state.MoveOpportunityModifierThisActivation = 0;
                state.MovesCompletedThisActivation = 0;
                state.WaypointAdvancedThisActivation = false;
                state.ActionDone = false;
                state.BonusActionUseCountsThisActivation.Clear();
                state.BonusActionsUsedThisActivation.Clear();
                state.ModifiersThisTurn.Clear();
                events.Add(new RulesEvent("ActivationStarted", choice) { Token = state.ActiveToken });
                if (!state.IsUpright(choice!))
                {
                    ChangePosture(state, choice!, Posture.Upright, events);
                    CompleteUnit(state, random, events);
                }
                else if (state.Types.Single(t => t.Id == request.TypeId).RunRails is { } rails)
                {
                    ResolveRunRails(state, choice!, rails, events);
                    CompleteUnit(state, random, events);
                }
                break;
            case DecisionKind.Activation when request.Candidates.Single(c => c.Key == choice).Kind == ActivationChoiceKind.BonusAction:
                var bonusChoice = request.Candidates.Single(c => c.Key == choice);
                var ability = bonusChoice.BonusAction!;
                var index = state.Units.FindIndex(u => u.Id == request.UnitId);
                if (ability.MaxUses.HasValue)
                {
                    var uses = state.Units[index].BonusActionUses[ability.Name];
                    state.Units[index] = state.Units[index] with
                    {
                        BonusActionUses = state.Units[index].BonusActionUses.SetItem(ability.Name,
                            new(uses.MaxUses, uses.RemainingUses - 1))
                    };
                }
                state.BonusActionUseCountsThisActivation[ability.Name] = state.BonusActionUsesThisActivation(ability.Name) + 1;
                state.BonusActionsUsedThisActivation.Add(ability.Name);
                state.ModifiersThisTurn.AddRange(ability.Modifiers);
                EmitActionUsed(request, choice!, events);
                if (ability.PlaceBananaPeel is not null)
                    BananaPlacementRules.Resolve(state, request.UnitId!, bonusChoice, events.Add);
                else if (ability.Boost is { } boost)
                {
                    state.MoveOpportunityModifierThisActivation++;
                    RacingRules.Spend(state, request.UnitId!, ResourceTypes.Mushroom, boost.MushroomCost, "boost", events.Add);
                    events.Add(new RulesEvent("AbilityUsed", request.UnitId, AbilityName: ability.DisplayName ?? ability.Name) { ActionId = "boost" });
                }
                else if (ability.Swap is not null)
                {
                    ExternalMovementRules.SwapPlaces(state, request.UnitId!, bonusChoice.TargetId!);
                    events.Add(new RulesEvent("PlacesSwapped", request.UnitId, bonusChoice.TargetId,
                        AbilityName: ability.DisplayName ?? ability.Name)
                        { SourceUnitId = request.UnitId, ActionId = ActionIdentity(bonusChoice) });
                    ResolveArrival(state, request.UnitId!, events);
                    ResolveArrival(state, bonusChoice.TargetId!, events);
                }
                else if (ability.Displace is not null)
                {
                    var affectedIndex = state.Physical.Figures.FindIndex(f => f.Id == bonusChoice.TargetId);
                    var affected = state.Physical.Figures[affectedIndex];
                    state.Physical.Figures[affectedIndex] = affected with { Position = bonusChoice.Destination! };
                    events.Add(new RulesEvent("UnitRepositioned", affected.Id,
                        Path: affected.Position == bonusChoice.Destination ? [affected.Position] : [affected.Position, bonusChoice.Destination!],
                        AbilityName: ability.DisplayName ?? ability.Name)
                        { SourceUnitId = request.UnitId, ActionId = ActionIdentity(bonusChoice), Cell = bonusChoice.Destination });
                    if (affected.Position != bonusChoice.Destination) ResolveArrival(state, affected.Id, events);
                    else events.LootArrivals.Add(affected.Id);
                }
                else
                {
                    if (ability.ReshapeRoom is not null)
                        ReshapeRoomRules.Apply(state, request.UnitId!);
                    events.Add(new RulesEvent("AbilityUsed", request.UnitId, AbilityName: ability.DisplayName ?? ability.Name));
                }
                break;
            case DecisionKind.Activation when request.Candidates.Single(c => c.Key == choice).Kind == ActivationChoiceKind.EndTurn:
                CompleteUnit(state, random, events);
                break;
            case DecisionKind.Activation when request.Candidates.Single(c => c.Key == choice).FreeAction == UnitFreeAction.OpenDoor:
                EmitActionUsed(request, choice!, events);
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
                ResourceRules.TransportDuringOwnMove(state, request.UnitId!, figure.Position, path[^1]);
                if (request.IsMoveAfterAttack)
                {
                    state.MoveAfterAttackAllowance = null;
                    if (state.AdvanceOnKillCells is not null)
                    {
                        state.AdvanceOnKillCells = null;
                        state.ActionsUsedThisActivation = Math.Max(state.ActionsUsedThisActivation, state.EffectiveActionsPerActivation);
                    }
                }
                else state.MovesCompletedThisActivation++;
                FogOfWar.Sweep(state, request.UnitId!, path);
                events.Add(new RulesEvent("MovementCompleted", request.UnitId, Path: [.. path],
                    IsMoveAfterAttack: request.IsMoveAfterAttack));
                if (!request.IsMoveAfterAttack) RouteRules.CompleteMove(state, request.UnitId!, events.Add);
                if (path.Count > 1) ResolveArrival(state, request.UnitId!, events);
                else events.LootArrivals.Add(request.UnitId!);
                break;
            case DecisionKind.Activation:
                state.ActionsUsedThisActivation++;

                if (choice is not null)
                {
                    var action = request.Candidates.Single(c => c.Key == choice);
                    if (action.TryOpenDoor is { } attempt)
                    {
                        EmitActionUsed(request, choice!, events);
                        state.DoorInProgress = new(request.UnitId!, action.Door!, attempt.SuccessCount, "try-open-door");
                    }
                    else switch (action.Action)
                    {
                        case UnitAction.Operate:
                            EmitActionUsed(request, choice!, events);
                            events.Add(MachineRules.Resolve(state, request.UnitId!, action, random));
                            break;
                        case UnitAction.PickupResource:
                        case UnitAction.GiveResource:
                            EmitActionUsed(request, choice!, events);
                            events.Add(action.MachineId is not null
                                ? MachineRules.Resolve(state, request.UnitId!, action, random)
                                : ResourceRules.Transfer(state, request.UnitId!, action));
                            break;
                        case UnitAction.ShootShell:
                            EmitActionUsed(request, choice!, events);
                            var shell = state.Types.Single(t => t.Id == request.TypeId).ShootShell!;
                            RacingRules.Spend(state, request.UnitId!, ResourceTypes.Shell, shell.ShellCost, "shoot-shell", events.Add);
                            BeginAttack(state, request.UnitId!, new([action.TargetId!], shell.Atk, "Shoot Shell"), events, "shoot-shell", null);
                            break;
                        case UnitAction.NormalAttack:
                            EmitActionUsed(request, choice!, events);
                            BeginAttack(state, request.UnitId!,
                                new([action.TargetId!], AttackRules.AttackDice(state, request.UnitId!, action.TargetId, UnitAction.NormalAttack)), events, ActionIdentity(action), action.Destination);
                            break;
                        case UnitAction.ClawAttack:
                            var clawType = state.Types.Single(t => t.Id == request.TypeId);
                            EmitActionUsed(request, choice!, events);
                            BeginAttack(state, request.UnitId!,
                                new([action.TargetId!], AttackRules.AttackDice(state, request.UnitId!, action.TargetId, UnitAction.ClawAttack),
                                    clawType.AbilityNames.ClawAttack ?? "Claw Attack"), events, ActionIdentity(action), action.Destination);
                            break;
                        case UnitAction.FireBreath:
                            EmitActionUsed(request, choice!, events);
                            BeginAttack(state, request.UnitId!,
                                new(action.TargetIds, AttackRules.AttackDice(state, request.UnitId!, null, UnitAction.FireBreath),
                                    state.Types.Single(t => t.Id == request.TypeId).AbilityNames.FireBreath ?? "Fire Breath"), events, ActionIdentity(action), action.Destination);
                            break;
                        case UnitAction.HolyWave:
                            var waveIndex = state.Units.FindIndex(u => u.Id == request.UnitId);
                            var waveUses = state.Units[waveIndex].HolyWaveUses!;
                            state.Units[waveIndex] = state.Units[waveIndex] with
                            {
                                HolyWaveUses = new(waveUses.MaxUses, waveUses.RemainingUses - 1)
                            };
                            EmitActionUsed(request, choice!, events);
                            foreach (var targetId in action.TargetIds)
                                if (state.Physical.Figures.Any(f => f.Id == targetId))
                                    ChangePosture(state, targetId, Posture.Lying, events, request.UnitId, ActionIdentity(action));
                            ChangePosture(state, request.UnitId!, Posture.Lying, events, request.UnitId, ActionIdentity(action));
                            break;
                        case UnitAction.Fireball:
                            var fireballIndex = state.Units.FindIndex(u => u.Id == request.UnitId);
                            var fireballUses = state.Units[fireballIndex].FireballUses!;
                            state.Units[fireballIndex] = state.Units[fireballIndex] with
                            {
                                FireballUses = new(fireballUses.MaxUses, fireballUses.RemainingUses - 1)
                            };
                            EmitActionUsed(request, choice!, events);
                            BeginAttack(state, request.UnitId!,
                                new(action.TargetIds, AttackRules.AttackDice(state, request.UnitId!, null, UnitAction.Fireball),
                                    state.Types.Single(t => t.Id == request.TypeId).AbilityNames.Fireball ?? "Fireball"), events, ActionIdentity(action), action.Destination);
                            break;
                        case UnitAction.Telekinesis:
                            EmitActionUsed(request, choice!, events);
                            ChangePosture(state, action.TargetId!, Posture.Lying, events, request.UnitId, ActionIdentity(action));
                            break;
                        case UnitAction.SummonWorker:
                        case UnitAction.SummonTroll:
                        case UnitAction.SummonAdjacent:
                            var summoner = state.Types.Single(t => t.Id == request.TypeId);
                            var summon = SummoningRules.Placement(summoner, action.Action!.Value)!;
                            var summonedType = UnitContent.Find(summon.UnitTypeId, state.Types)
                                ?? throw new InvalidOperationException("Summoned Unit Type is not defined.");
                            var prefix = summonedType.Id.EndsWith("-type", StringComparison.Ordinal)
                                ? summonedType.Id[..^5] : summonedType.Id;
                            var number = 1;
                            while (state.Units.Any(u => u.Id == $"{prefix}-{number}")) number++;
                            var summonedId = $"{prefix}-{number}";
                            var source = state.Units.Single(u => u.Id == request.UnitId);
                            if (!SummoningRules.Candidates(state, source).Contains(action))
                                throw new InvalidOperationException("Summoning is unavailable.");
                            var group = new ActivationToken(summonedType.Id, source.SideId);
                            if (!state.Controllers.Any(c => c.Token == group))
                                state.Controllers.Add(new(group, state.ControllerFor(new ActivationToken(source.TypeId, source.SideId))));
                            EmitActionUsed(request, choice!, events);
                            var spending = action.Action == UnitAction.SummonTroll
                                ? SummoningRules.SpendDiamond(state, source, summoner.SummonTroll!) : null;
                            state.PlaceUnit(summonedType.Id, summonedId, source.SideId,
                                action.Destination!, summon.InitialPosture);
                            if (spending is not null) events.Add(spending);
                            events.Add(new RulesEvent("UnitCreated", summonedId, TypeId: summonedType.Id,
                                AbilityName: summoner.CardEntries(state.Types).Single(e => e.Id == ActionIdentity(action)).Name, Posture: summon.InitialPosture)
                                { SourceUnitId = source.Id, SideId = source.SideId, Cell = action.Destination, ActionId = ActionIdentity(action) });
                            ResolveArrival(state, summonedId, events);
                            break;
                        case UnitAction.Heal:
                            ResolveHeal(state, request, choice!, action.TargetId!, events);
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported action.");
                    }
                }
                break;
        }
    }

    private static void RunUntilDecision(GameState state, IRandomProvider random, ResolutionEvents events,
        bool autoChooseSingleRelevantChoice)
    {
        while (state.Pending is null && !state.RoundComplete && !state.IsCompleted)
        {
            if (state.AttackInProgress is not null)
            {
                ProgressAttack(state, events);
                if (state.AttackInProgress is not null) { state.Pending = CreateDecision(state); return; }
                continue;
            }
            ResolveExplosions(state, events);
            ResolveLootArrivals(state, events);
            if (state.DoorInProgress is not null) { state.Pending = CreateDecision(state); return; }
            if (state.ActiveToken is null)
            {
                if (state.Bag.Count == 0)
                {
                    state.RoundComplete = true;
                    events.Add(new RulesEvent("RoundCompleted") { Round = state.Round });
                    return;
                }
                // Token selection must not expose the authoritative bag to a provider.
                var drawn = random.DrawToken(Array.AsReadOnly(state.Bag.ToArray()));
                if (!state.Bag.Remove(drawn))
                    throw new ArgumentException("Random provider drew a token outside the bag.", nameof(random));
                state.ActiveToken = drawn;
                state.CompletedUnitIds.Clear();
                events.Add(new RulesEvent("TokenDrawn", TypeId: drawn.TypeId) { Token = drawn, SideId = drawn.SideId });
            }

            if (state.AttackInProgress is null && state.DoorInProgress is null && state.CurrentUnitId is not null &&
                (!state.Units.Any(u => u.Id == state.CurrentUnitId && u.CurrentHp > 0) ||
                 !state.IsUpright(state.CurrentUnitId)))
                CompleteUnit(state, random, events);
            if (state.IsCompleted) return;
            if (state.CurrentUnitId is null && EligibleUnits(state).Count == 0)
            {
                state.CompletedUnitIds.Clear();
                state.ActiveToken = null;
                continue;
            }
            var request = CreateDecision(state, preview: false);
            var preview = FogOfWar.ObserveDecision(state, request.Token?.SideId, request)!;
            if (!TryAutomaticChoice(preview, autoChooseSingleRelevantChoice, out var choice))
            {
                state.Pending = preview;
                return;
            }
            ApplyDecision(state, request, choice, random, events);
        }
    }

    private static void CompleteUnit(GameState state, IRandomProvider random, ResolutionEvents events)
    {
        var unitId = state.CurrentUnitId;
        RacingRules.AwardWaypoint(state, unitId!, random, events.Add);
        state.CompletedUnitIds.Add(state.CurrentUnitId!);
        state.CurrentUnitId = null;
        state.MoveAfterAttackAllowance = null;
        state.AdvanceOnKillCells = null;
        state.CleavePending = false;
        state.ExtraActionsThisActivation = 0;
        state.MoveOpportunityModifierThisActivation = 0;
        state.MovesCompletedThisActivation = 0;
        state.WaypointAdvancedThisActivation = false;
        state.ActionDone = false;
        state.BonusActionUseCountsThisActivation.Clear();
        state.BonusActionsUsedThisActivation.Clear();
        state.ModifiersThisTurn.Clear();
        events.Add(new RulesEvent("ActivationCompleted", unitId) { Token = state.ActiveToken });
        EvaluateObjectives(state, events);
    }

    private static void ChangePosture(GameState state, string unitId, Posture posture, ResolutionEvents events, string? sourceUnitId = null, string? actionId = null)
    {
        var index = state.Physical.Figures.FindIndex(f => f.Id == unitId);
        state.Physical.Figures[index] = state.Physical.Figures[index] with { Posture = posture };
        events.Add(new RulesEvent("PostureChanged", unitId, Posture: posture) { SourceUnitId = sourceUnitId, ActionId = actionId });
    }

    private static void OpenDoor(GameState state, string? unitId, Edge door, ResolutionEvents events, string actionId = "open-door")
    {
        var index = state.Physical.Board.Edges.FindIndex(e =>
            e.A == door.A && e.B == door.B || e.A == door.B && e.B == door.A);
        var opened = state.Physical.Board.Edges[index] with { Kind = EdgeKind.OpenDoor };
        state.Physical.Board.Edges[index] = opened;
        events.Add(new RulesEvent("DoorOpened", unitId, Door: opened) { SourceUnitId = unitId, ActionId = actionId });
    }

    private static List<Candidate> MovementCandidates(GameState state, Unit unit, int? maxSteps = null)
    {
        var start = state.Physical.Figures.Single(f => f.Id == unit.Id).Position;
        var allowance = maxSteps ?? state.EffectiveMovOf(unit.Id);
        return MovementRules.FindPaths(state, unit.Id, start, allowance,
                ignoreMovementPattern: state.AdvanceOnKillCells is not null)
            .Where(pair => pair.Key != start)
            .Where(pair => state.AdvanceOnKillCells is not { } cells ||
                cells.Contains(pair.Key) && Math.Max(Math.Abs(pair.Key.X - start.X), Math.Abs(pair.Key.Y - start.Y)) == 1 &&
                pair.Value.Count - 1 == Math.Abs(pair.Key.X - start.X) + Math.Abs(pair.Key.Y - start.Y))
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
        candidates.AddRange(ResourceRules.Candidates(state, unit));
        candidates.AddRange(MachineRules.Candidates(state, unit));
        candidates.AddRange(SummoningRules.Candidates(state, unit));
        if (actions.HasFlag(UnitAction.ShootShell) && type.ShootShell is { } shell &&
            state.StoredQuantity(unit.SideId, unit.TypeId, ResourceTypes.Shell.Id) >= shell.ShellCost)
            candidates.AddRange(state.Units.Where(target => AttackRules.EvaluateHostileTargetFrom(state, unit.Id, from, target.Id, shell.Rng)
                == UnitTargetEvaluation.Possible).Select(target => new Candidate($"shoot-shell:{target.Id}", Action: UnitAction.ShootShell, TargetId: target.Id)));
        if (actions.HasFlag(UnitAction.NormalAttack))
            candidates.AddRange(state.Units
                .Where(target => AttackRules.EvaluateFrom(state, unit.Id, from, target.Id)
                    == UnitTargetEvaluation.Possible)
                .Select(target => new Candidate($"attack:{target.Id}",
                    Action: UnitAction.NormalAttack, TargetId: target.Id)));
        if (actions.HasFlag(UnitAction.ClawAttack))
            candidates.AddRange(state.Units
                .Where(target => AttackRules.EvaluateApproachFrom(state, unit.Id, from, target.Id, UnitAction.ClawAttack)
                    == UnitTargetEvaluation.Possible)
                .Select(target => new Candidate($"claw-attack:{target.Id}", Action: UnitAction.ClawAttack, TargetId: target.Id)));
        if (actions.HasFlag(UnitAction.FireBreath))
        {
            var targets = state.Units.Where(target =>
                AttackRules.EvaluateApproachFrom(state, unit.Id, from, target.Id, UnitAction.FireBreath)
                    == UnitTargetEvaluation.Possible).Select(target => target.Id).ToImmutableArray();
            if (!targets.IsEmpty)
                candidates.Add(new Candidate("fire-breath", Action: UnitAction.FireBreath) { TargetIds = targets });
        }
        if (actions.HasFlag(UnitAction.Heal) && type.Heal is not null &&
            unit.HealUses is { RemainingUses: > 0 })
            candidates.AddRange(state.Units.Where(target => target.Id != unit.Id &&
                    target.SideId == unit.SideId && target.CurrentHp > 0 &&
                    target.CurrentHp < state.Types.Single(t => t.Id == target.TypeId).Hp &&
                    SpatialRules.AreAdjacent(state, unit.Id, target.Id))
                .Select(target => new Candidate($"heal:{target.Id}",
                    Action: UnitAction.Heal, TargetId: target.Id)));
        if (actions.HasFlag(UnitAction.HolyWave) && type.HolyWave is not null &&
            unit.HolyWaveUses is { RemainingUses: > 0 })
        {
            var targets = AdjacentHostiles(state, unit).Where(u => state.IsUpright(u.Id))
                .Select(u => u.Id).ToImmutableArray();
            candidates.Add(new Candidate("holy-wave", Action: UnitAction.HolyWave,
                Relevant: !targets.IsEmpty) { TargetIds = targets });
        }
        if (actions.HasFlag(UnitAction.Fireball) && type.Fireball is not null &&
            unit.FireballUses is { RemainingUses: > 0 })
            candidates.AddRange(FireballCandidates(state, unit));
        if (actions.HasFlag(UnitAction.Telekinesis))
            candidates.AddRange(state.Units
                .Where(target => state.IsUpright(target.Id) &&
                    AttackRules.EvaluateHostileTargetFrom(state, unit.Id, from, target.Id)
                        == UnitTargetEvaluation.Possible)
                .Select(target => new Candidate($"telekinesis:{target.Id}",
                    Action: UnitAction.Telekinesis, TargetId: target.Id)));
        if (type.TryOpenDoor is not null)
        {
            foreach (var edge in BorderingClosedDoors(state, unit))
            {
                var key = $"{edge.A.X},{edge.A.Y}:{edge.B.X},{edge.B.Y}";
                candidates.Add(new Candidate($"try-open-door:{key}", Door: edge, TryOpenDoor: type.TryOpenDoor));
            }
        }
        return candidates;
    }

    private static IEnumerable<Candidate> FireballCandidates(GameState state, Unit unit)
    {
        var board = state.Physical.Board;
        var sources = FootprintGeometry.OccupiedCells(state, unit.Id);
        var range = state.EffectiveRngOf(unit.Id);
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var center = new Cell(x, y);
            if (!sources.Any(from => Math.Abs(x - from.X) + Math.Abs(y - from.Y) <= range &&
                AttackRules.HasUnitLineOfSight(state, unit.Id, from, center))) continue;
            var targets = state.Units.Where(u => u.CurrentHp > 0)
                .Where(u =>
                {
                    return FootprintGeometry.OccupiedCells(state, u.Id).Any(position =>
                        Math.Max(Math.Abs(position.X - x), Math.Abs(position.Y - y)) <= 1 &&
                        AttackRules.HasGeometricLineOfSight(board, center, position));
                }).Select(u => u.Id).ToImmutableArray();
            yield return new Candidate($"fireball:{x},{y}", Destination: center,
                Action: UnitAction.Fireball,
                Relevant: targets.Any(id => state.Units.Single(u => u.Id == id).SideId != unit.SideId))
                { TargetIds = targets };
        }
    }

    private static IEnumerable<Edge> BorderingClosedDoors(GameState state, Unit unit)
    {
        var occupied = FootprintGeometry.OccupiedCells(state, unit.Id);
        return state.Physical.Board.Edges
            .Where(edge => edge.Kind == EdgeKind.ClosedDoor && (occupied.Contains(edge.A) || occupied.Contains(edge.B)))
            .Select(edge => edge.A.Y < edge.B.Y || edge.A.Y == edge.B.Y && edge.A.X < edge.B.X
                ? edge : edge with { A = edge.B, B = edge.A })
            .OrderBy(edge => edge.A.Y).ThenBy(edge => edge.A.X)
            .ThenBy(edge => edge.B.Y).ThenBy(edge => edge.B.X);
    }

    private static IEnumerable<Candidate> FreeActionCandidates(GameState state, Unit unit)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        if (!state.HasOpenDoor(unit.Id)) return [];
        return BorderingClosedDoors(state, unit).Select(edge => new Candidate(
            $"open-door:{edge.A.X},{edge.A.Y}:{edge.B.X},{edge.B.Y}", Door: edge,
            Kind: ActivationChoiceKind.FreeAction, FreeAction: UnitFreeAction.OpenDoor)
            { EquipmentGranted = !type.FreeActions.HasFlag(UnitFreeAction.OpenDoor) });
    }

    private static List<Unit> EligibleUnits(GameState state) =>
        state.Units.Where(u => u.TypeId == state.ActiveToken!.TypeId && u.SideId == state.ActiveToken.SideId && u.CurrentHp > 0 &&
                !state.CompletedUnitIds.Contains(u.Id))
            .OrderBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.Y)
            .ThenBy(u => state.Physical.Figures.Single(f => f.Id == u.Id).Position.X)
            .ToList();

    private static IEnumerable<Candidate> BonusActionCandidates(GameState state, Unit unit)
    {
        foreach (var ability in state.Types.Single(t => t.Id == unit.TypeId).BonusActions)
            if (state.BonusActionUsesThisActivation(ability.Name) < state.EffectiveBonusActionUsesPerActivation &&
                (ability.MaxUses is null || unit.BonusActionUses.TryGetValue(ability.Name, out var uses) && uses.RemainingUses > 0))
            {
                if (ability.Boost is { } boost)
                {
                    if (!state.ActionDone && state.StoredQuantity(unit.SideId, unit.TypeId, ResourceTypes.Mushroom.Id) >= boost.MushroomCost)
                        yield return new("bonus-action:boost", Kind: ActivationChoiceKind.BonusAction, BonusAction: ability);
                }
                else if (ability.PlaceBananaPeel is { } banana)
                {
                    foreach (var candidate in BananaPlacementRules.Candidates(state, unit, banana))
                        yield return candidate with { Kind = ActivationChoiceKind.BonusAction, BonusAction = ability };
                }
                else if (ability.Swap is not null || ability.Displace is not null)
                {
                    foreach (var target in AdjacentHostiles(state, unit))
                    {
                        if (ability.Swap is not null && ExternalMovementRules.CanSwap(state, unit.Id, target.Id))
                            yield return new Candidate($"bonus-action:{ability.Name}:{target.Id}",
                                TargetId: target.Id, Kind: ActivationChoiceKind.BonusAction, BonusAction: ability);
                        if (ability.Displace is { } displace)
                            foreach (var destination in ExternalMovementRules.Destinations(state, unit.Id, target.Id, displace.MaxMove))
                                yield return new Candidate($"bonus-action:{ability.Name}:{target.Id}:{destination.X},{destination.Y}",
                                    Destination: destination, TargetId: target.Id,
                                    Kind: ActivationChoiceKind.BonusAction, BonusAction: ability);
                    }
                }
                else yield return new Candidate($"bonus-action:{ability.Name}", Kind: ActivationChoiceKind.BonusAction,
                    Relevant: BonusActionRelevant(state, unit, ability), BonusAction: ability);
            }
    }

    private static bool BonusActionRelevant(GameState state, Unit unit, BonusActionAbility ability)
    {
        if (ability.ReshapeRoom is not null) return true;
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
        if (!state.IsUpright(unit.Id) || state.CleavePending) return [];
        if (state.MoveAfterAttackAllowance is { } allowance)
            return MovementCandidates(state, unit, allowance);
        if (!state.MoveDone) return MovementCandidates(state, unit);
        return state.ActionsRemaining == 0 ? [] : ActionCandidates(state, unit).Where(c => FogOfWar.Allows(state, unit.SideId, c));
    }

    private static DecisionRequest CreateDecision(GameState state, bool preview = true)
    {
        FogOfWar.Refresh(state);
        var request = CreateDecisionCore(state);
        var owner = request.UnitId is null ? null : state.Units.Single(u => u.Id == request.UnitId);
        var token = owner is null ? state.ActiveToken : new(owner.TypeId, owner.SideId);
        if (request.Kind != DecisionKind.RollDice && owner is not null && FogOfWar.IsEnabled(state, owner.SideId))
            request = request with { Candidates = request.Candidates.Where(c => FogOfWar.Allows(state, owner.SideId, c)).ToList() };
        request = request with { Token = token };
        return preview ? FogOfWar.ObserveDecision(state, token?.SideId, request)! : request;
    }

    private static DecisionRequest CreateDecisionCore(GameState state)
    {
        DicePool? pool = null;
        if (state.DoorInProgress is { } door)
            pool = new(DiceFamily.D6, 1, door.UnitId, door.UnitId, door.ActionId, "Door check", Door: door.Door, SuccessCount: door.SuccessCount);
        if (state.AttackInProgress is { } attack)
        {
            var target = attack.Stage == AttackStage.DefenceRoll ? attack.Targets[attack.TargetIndex] : null;
            pool = target is null
                ? new(DiceFamily.Attack, attack.AttackDice, attack.AttackerId, attack.AttackerId, attack.ActionId, "Attack")
                : new(DiceFamily.Defence, target.DefenceDice, target.TargetId, attack.AttackerId, attack.ActionId, "Defence", target.TargetId);
            pool = pool with { SelectedCell = attack.SelectedCell, TargetIds = attack.Targets.Select(t => t.TargetId).ToImmutableArray() };
        }
        if (pool is not null)
            return new DecisionRequest(DecisionKind.RollDice, state.Units.Single(u => u.Id == pool.OwnerUnitId).TypeId,
                pool.OwnerUnitId, [new("roll-dice", Kind: ActivationChoiceKind.RollDice)], false) { Roll = pool };

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
            return new DecisionRequest(DecisionKind.SelectUnit, state.ActiveToken!.TypeId, null,
                eligible.Select(u => new Candidate(u.Id, Kind: ActivationChoiceKind.SelectUnit)).ToList(), false);

        var unit = eligible.Single(u => u.Id == state.CurrentUnitId);
        var candidates = new List<Candidate>();
        candidates.AddRange(FreeActionCandidates(state, unit));
        candidates.AddRange(BonusActionCandidates(state, unit));
        candidates.AddRange(ObjectCandidates(state, unit));
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
        bool autoChooseSingleRelevantChoice = true, Action<RulesEvent, ResolutionStep>? observe = null)
    {
        var state = previous.Copy();
        EnsureObjectives(state);
        state.Pending = null;
        var events = new ResolutionEvents(state, random, observe);
        if (state.Round > 0) RunUntilDecision(state, random, events, autoChooseSingleRelevantChoice);
        return new(state, events.Events, state.Pending) { ResolutionSteps = events.Steps };
    }

    // Every Attack supplies its complete membership and authoritative ATK at Attack start.
    // One shared Attack roll is the general rule, irrespective of target selection.
    // Resolution snapshots DEF, then resolves per-target damage/defeat and follow-ups.
    private sealed record SharedRollAttack(ImmutableArray<string> TargetIds, int AttackDice, string? AbilityName = null);
    private static void EmitActionUsed(DecisionRequest request, string choice, ResolutionEvents events)
    {
        var used = request.Candidates.Single(c => c.Key == choice);
        events.Add(new RulesEvent("ActionUsed", request.UnitId, used.TargetId, Door: used.Door,
            AbilityName: used.BonusAction?.DisplayName ?? used.BonusAction?.Name)
            { MachineId = used.MachineId, ActionId = ActionIdentity(used), Category = used.Kind, Cell = used.Destination, SourceUnitId = request.UnitId });
    }

    private static string ActionIdentity(Candidate candidate) => ContentDescriptions.EntryId(candidate)
        ?? throw new InvalidOperationException("Action has no stable identity.");

    private static void BeginAttack(GameState state, string attackerId, SharedRollAttack attack,
        ResolutionEvents events, string actionId, Cell? selectedCell)
    {
        var targets = attack.TargetIds.Select(id => new AttackTargetDice(id, state.EffectiveDefOf(id))).ToImmutableArray();
        state.AttackInProgress = new(attackerId, actionId, attack.AbilityName, selectedCell, targets,
            attack.AttackDice, Results: []) { IsNormalMelee = actionId == "attack" && state.EffectiveRngOf(attackerId) == 1 };
        FogOfWar.DiscoverAttack(state, state.AttackInProgress);
        events.Add(new RulesEvent("AttackStarted", attackerId, AbilityName: attack.AbilityName)
            { ActionId = actionId, Cell = selectedCell, AttackContext = state.AttackInProgress, SourceUnitId = attackerId });
    }

    // Progress only deterministic portions; nonempty pools always return to the decision boundary.
    private static void ProgressAttack(GameState state, ResolutionEvents events)
    {
        var attack = state.AttackInProgress!;
        if (attack.Stage == AttackStage.AttackRoll)
        {
            if (attack.AttackDice > 0) return;
            state.AttackInProgress = attack = attack with { Stage = AttackStage.DefenceRoll };
        }
        while (attack.TargetIndex < attack.Targets.Length)
        {
            // Membership stays fixed, but a recipient that has left play is skipped.
            if (!state.Physical.Figures.Any(f => f.Id == attack.Targets[attack.TargetIndex].TargetId))
            {
                state.AttackInProgress = attack = attack with { TargetIndex = attack.TargetIndex + 1 };
                continue;
            }
            if (attack.Targets[attack.TargetIndex].DefenceDice > 0) return;
            ResolveAttackTarget(state, 0, events);
            attack = state.AttackInProgress!;
        }
        if (attack.AbilityName is not null)
            events.Add(new RulesEvent("AttackResolved", attack.AttackerId, Hits: attack.Hits,
                AbilityName: attack.AbilityName, Attack: new(attack.AttackDice, attack.Hits, attack.Results))
                { ActionId = attack.ActionId });
        state.AttackInProgress = null;
        ResolveExplosions(state, events);
        var attacker = state.Units.Single(u => u.Id == attack.AttackerId);
        var type = state.Types.Single(t => t.Id == attacker.TypeId);
        if (attacker.CurrentHp > 0 && state.IsUpright(attacker.Id))
        {
            if (type.Cleave is { } cleave && attack.Results.Any(r => r.Damage >= cleave.TriggerDamage) && CleaveCandidates(state, attacker).Count > 0)
                state.CleavePending = true;
            if (type.MoveAfterAttack is { } move) state.MoveAfterAttackAllowance = move.MaxSteps;
            if (type.AdvanceOnKill is not null && attack.IsNormalMelee && !attack.DefeatedHostileCells.IsEmpty)
            {
                state.AdvanceOnKillCells = attack.DefeatedHostileCells;
                state.MoveAfterAttackAllowance = 2;
            }
        }
    }

    private static void ResolveAttackTarget(GameState state, int blocks, ResolutionEvents events)
    {
        var attack = state.AttackInProgress!;
        var target = attack.Targets[attack.TargetIndex];
        var damage = Math.Max(0, attack.Hits - blocks);
        var results = attack.Results.Add(new(target.TargetId, target.DefenceDice, blocks, damage));
        var formerCells = FootprintGeometry.OccupiedCells(state, target.TargetId).ToImmutableArray();
        var hostile = state.AreHostile(state.Units.Single(u => u.Id == attack.AttackerId).SideId,
            state.Units.Single(u => u.Id == target.TargetId).SideId);
        state.AttackInProgress = attack with { Results = results, TargetIndex = attack.TargetIndex + 1 };
        DealDamage(state, target.TargetId, damage,
            new RulesEvent(attack.AbilityName is null ? "AttackResolved" : "AttackTargetResolved",
                attack.AttackerId, target.TargetId, Hits: attack.Hits, Blocks: blocks, Damage: damage,
                AbilityName: attack.AbilityName,
                Attack: attack.AbilityName is null ? new(attack.AttackDice, attack.Hits, results) : null)
                { ActionId = attack.ActionId, SourceUnitId = attack.AttackerId }, events);
        if (hostile && state.DefeatedUnitIds.Contains(target.TargetId))
            state.AttackInProgress = state.AttackInProgress! with
            { DefeatedHostileCells = state.AttackInProgress.DefeatedHostileCells.AddRange(formerCells) };
    }

    private static void SubmitRoll(GameState state, DicePool pool, IRandomProvider random, ResolutionEvents events)
    {
        var faces = ImmutableArray.CreateBuilder<string>(pool.Count);
        var successes = 0;
        for (var i = 0; i < pool.Count; i++)
        {
            switch (pool.Family)
            {
                case DiceFamily.Attack:
                    var hit = random.RollAttackDie();
                    if (!Enum.IsDefined(hit)) throw new ArgumentException("Invalid Attack Die face.", nameof(random));
                    faces.Add(hit.ToString());
                    if (hit == AttackFace.Hit) successes++;
                    break;
                case DiceFamily.Defence:
                    var block = random.RollDefenceDie();
                    if (!Enum.IsDefined(block)) throw new ArgumentException("Invalid Defence Die face.", nameof(random));
                    faces.Add(block.ToString());
                    if (block == DefenceFace.Block) successes++;
                    break;
                case DiceFamily.D6:
                    var d6 = random.RollD6();
                    if (d6 is < 1 or > 6) throw new ArgumentException("Invalid D6 face.", nameof(random));
                    faces.Add(d6.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (d6 <= pool.SuccessCount) successes++;
                    break;
            }
        }
        var result = new DiceResult(pool, faces.ToImmutable(), successes);
        if (pool.Family == DiceFamily.Attack)
            state.AttackInProgress = state.AttackInProgress! with { Hits = successes, Stage = AttackStage.DefenceRoll, AttackRoll = result };
        events.Add(new RulesEvent("DiceRolled", pool.OwnerUnitId, pool.TargetId, Door: pool.Door)
            { SourceUnitId = pool.SourceUnitId, ActionId = pool.SourceActionId, Dice = result });
        if (pool.Family == DiceFamily.Defence) ResolveAttackTarget(state, successes, events);
        if (pool.Family == DiceFamily.D6)
        {
            var door = state.DoorInProgress!;
            state.DoorInProgress = null;
            var roll = int.Parse(faces[0], System.Globalization.CultureInfo.InvariantCulture);
            events.Add(new RulesEvent("DoorOpeningAttemptResolved", door.UnitId, Door: door.Door,
                DieRoll: roll, SuccessCount: door.SuccessCount, Succeeded: successes > 0) { ActionId = door.ActionId });
            if (successes > 0) OpenDoor(state, door.UnitId, door.Door, events, door.ActionId);
        }
    }

    private static void ResolveHeal(GameState state, DecisionRequest request, string choice, string targetId, ResolutionEvents events)
    {
        var healerId = request.UnitId!;
        var healerIndex = state.Units.FindIndex(u => u.Id == healerId);
        var uses = state.Units[healerIndex].HealUses!;
        state.Units[healerIndex] = state.Units[healerIndex] with
        {
            HealUses = new(uses.MaxUses, uses.RemainingUses - 1)
        };
        EmitActionUsed(request, choice, events);
        var type = state.Types.Single(t => t.Id == state.Units[healerIndex].TypeId);
        RestoreHp(state, targetId, type.Heal!.Amount, healerId, type.AbilityNames.Heal ?? "Heal", "heal", events);
    }

    private static List<Candidate> CleaveCandidates(GameState state, Unit unit)
    {
        if (!state.IsUpright(unit.Id) || state.Types.Single(t => t.Id == unit.TypeId).Cleave is null ||
            unit.CleaveUses is not { RemainingUses: > 0 }) return [];
        return AdjacentHostiles(state, unit)
            .Select(target => new Candidate($"cleave:{target.Id}", TargetId: target.Id,
                Kind: ActivationChoiceKind.Cleave)).ToList();
    }

    private static IEnumerable<Unit> AdjacentHostiles(GameState state, Unit unit)
    {
        return state.Units.Where(target => target.CurrentHp > 0 && state.AreHostile(unit.SideId, target.SideId) &&
                SpatialRules.AreAdjacent(state, unit.Id, target.Id));
    }

    private static void DealDamage(GameState state, string targetId, int damage,
        RulesEvent resolved, ResolutionEvents events)
    {
        var target = state.Units.Single(u => u.Id == targetId);
        var index = state.Units.IndexOf(target);
        var hp = Math.Max(0, target.CurrentHp - damage);
        state.Units[index] = target with { CurrentHp = hp };
        // Defeat or its replacement completes before any outcome snapshot is exposed.
        var lifecycle = hp == 0 ? ResolveDefeat(state, targetId, resolved.UnitId, resolved.ActionId) : null;
        events.Add(resolved);
        if (lifecycle is not null) events.Add(lifecycle);
    }

    // A single lifecycle boundary for lethal Damage and any direct defeat instruction.
    // Undying replaces defeat itself, independently of its cause; it is not a continuous effect.
    internal static RulesEvent ResolveDefeat(GameState state, string unitId,
        string? sourceUnitId = null, string? actionId = null)
    {
        var index = state.Units.FindIndex(u => u.Id == unitId);
        var unit = state.Units[index];
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        var figure = state.Physical.Figures.Single(f => f.Id == unitId);
        if (type.Undying is not null && figure.Posture == Posture.Upright)
        {
            state.Units[index] = unit with { CurrentHp = 1 };
            state.Physical.Figures[state.Physical.Figures.IndexOf(figure)] = figure with { Posture = Posture.Lying };
            return new("PostureChanged", unitId, Posture: Posture.Lying)
                { SourceUnitId = sourceUnitId, ActionId = actionId };
        }
        var context = new DefeatContext(unit, type, figure,
            [.. FootprintGeometry.OccupiedCells(type.Footprint, figure.Position)]);
        // Retained records use zero HP to mark absence; removal and defeat are one outcome.
        state.Units[index] = unit with { CurrentHp = 0 };
        foreach (var item in unit.Inventory) state.GroundObjects.Add(new(item, figure.Position));
        state.Units[index] = state.Units[index] with { Inventory = [] };
        state.DefeatedUnitIds.Add(unitId);
        state.Physical.Figures.RemoveAll(f => f.Id == unitId);
        if (type.Explosion is not null) state.PendingExplosions.Add(context);
        return new("UnitDefeated", unitId)
            { SourceUnitId = sourceUnitId, ActionId = actionId, DefeatContext = context };
    }

    // Concrete automatic consequence: finish each recipient set before draining the next defeat.
    private static void ResolveExplosions(GameState state, ResolutionEvents events)
    {
        while (state.PendingExplosions.Count > 0)
        {
            var source = state.PendingExplosions[0];
            state.PendingExplosions.RemoveAt(0);
            var damage = source.Type.Explosion!.Damage;
            var recipients = state.Physical.Figures
                .Where(f => SpatialRules.AreFootprintsAdjacent(state.Physical.Board,
                    source.OccupiedCells, FootprintGeometry.OccupiedCells(state, f.Id)))
                .OrderBy(f => f.Position.Y).ThenBy(f => f.Position.X)
                .Select(f => f.Id).ToArray();
            foreach (var id in recipients)
            {
                if (!state.Physical.Figures.Any(f => f.Id == id)) continue;
                DealDamage(state, id, damage,
                    new RulesEvent("ExplosionDamageResolved", source.Unit.Id, id, Damage: damage,
                        AbilityName: source.Type.AbilityNames.Explosion ?? "Explosion")
                        { SourceUnitId = source.Unit.Id, ActionId = "explosion", Cell = source.Figure.Position }, events);
            }
        }
    }

    // Capture at emission time, before later operations mutate this run's working state.
    private sealed class ResolutionEvents(GameState state, IRandomProvider random, Action<RulesEvent, ResolutionStep>? observe)
    {
        public IRandomProvider Random { get; } = random;
        public List<string> LootArrivals { get; } = [];
        public List<RulesEvent> Events { get; } = [];
        public List<ResolutionStep> Steps { get; } = [];

        public void Add(RulesEvent resolved)
        {
            FogOfWar.Refresh(state);
            if (resolved.WorldCard is null && state.WorldDeck?.ResolvingCard is { } card)
                resolved = resolved with { WorldCard = card };
            Steps.Add(new ResolutionStep(Events.Count, state.Copy()));
            Events.Add(resolved);
            if (observe is not null)
            {
                // The consumer owns these copies; mutation must not affect resolution or results.
                var observedState = state.Copy();
                if (observedState.Pending is { } pending)
                    observedState.Pending = pending with
                    {
                        Candidates = pending.Candidates.Select(c => c with
                        {
                            Path = c.Path is null ? null : [.. c.Path]
                        }).ToList()
                    };
                observe(resolved with { Path = resolved.Path is null ? null : [.. resolved.Path] },
                    new ResolutionStep(Events.Count - 1, observedState));
            }
            if (resolved.Kind == "UnitDefeated" && resolved.DefeatContext is { } defeat)
                ResolveLootDrop(state, defeat, this);
        }
    }

    internal static void ValidateScenario(GameState state)
    {
        LootRules.Validate(state);
        ValidateWorldDeck(state);
        RouteRules.ValidateDefinitions(state);
        ResourceRules.ValidateStorage(state);
        MachineRules.Validate(state);
        foreach (var marker in state.CellMarkers) CellMarkerRules.Validate(marker, state.Physical.Board);
        if (state.CellMarkers.Select(m => m.Position).Distinct().Count() != state.CellMarkers.Count)
            throw new ArgumentException("A Cell may contain at most one Cell Marker.");
        var board = state.Physical.Board;
        foreach (var resource in state.Resources) ResourceRules.Validate(resource, board);
        if (state.Resources.Select(resource => resource.Position).Distinct().Count() != state.Resources.Count)
            throw new ArgumentException("A Cell may contain at most one Resource.");
        if (state.Orders.Any(order => order is null) ||
            state.Orders.Where(order => order is not null).GroupBy(order => order.Token).Any(group => group.Count() != 1) ||
            state.Orders.Any(order => order is not null && (order.Kind == OrderKind.Normal ||
                !OrderValidation.IsStructurallyValid(order, state) ||
                !state.Controllers.Any(controller => controller.Token == order.Token))))
            throw new ArgumentException("Orders must be sparse, unique, valid assignments for participating groups.");
        if (state.Controllers.GroupBy(c => c.Token).Any(g => g.Count() != 1) ||
            state.Controllers.Any(c => !Enum.IsDefined(c.Controller) || string.IsNullOrWhiteSpace(c.Token.SideId) || string.IsNullOrWhiteSpace(c.Token.TypeId)) ||
            state.Units.Any(u => u.CurrentHp > 0 && !state.Controllers.Any(c => c.Token == new ActivationToken(u.TypeId, u.SideId))))
            throw new ArgumentException("Every participating Unit Type + Side needs one explicit controller assignment.");
        if (board.Width < 1 || board.Height < 1 || state.Types.Select(t => t.Id).Distinct().Count() != state.Types.Count ||
            state.Units.Select(u => u.Id).Distinct().Count() != state.Units.Count ||
            state.Types.Any(t => !Enum.IsDefined(t.Footprint) || t.Mov < 0 || t.Rng < 0 || t.Atk < 0 || t.Def < 0 || t.Hp < 1 || (t.Hp > 1 && !t.Unique) ||
                (t.RunRails is { } rails && (rails.Steps < 1 || t.Footprint != Footprint.OneByOne)) ||
                t.TryOpenDoor is { SuccessCount: < 0 or > 6 } || t.MoveAfterAttack is { MaxSteps: < 0 } ||
                t.Cleave is { MaxUses: < 1 } or { TriggerDamage: < 1 } or { Damage: < 1 } ||
                t.Heal is { MaxUses: < 1 } or { Amount: < 1 } ||
                t.Fury is { AdjacentEnemyThreshold: < 1 } ||
                (t.Actions.HasFlag(UnitAction.SummonWorker) && t.SummonWorker is null) ||
                (t.Actions.HasFlag(UnitAction.SummonTroll) && t.SummonTroll is null) ||
                t.SummonWorker is { SideLimit: < 1 } || t.SummonTroll is { DiamondCost: < 1 } ||
                t.ShootShell is { ShellCost: < 1 } or { Rng: < 1 } or { Atk: < 1 } ||
                (t.Actions.HasFlag(UnitAction.ShootShell) && t.ShootShell is null) ||
                t.Actions.HasFlag(UnitAction.PlaceBananaPeel) ||
                (t.MovementPattern is { } pattern && !Enum.IsDefined(pattern)) ||
                (t.TerrainRestrictedMovement is { } restriction && (!Enum.IsDefined(restriction.Terrain) || !restriction.Terrain.Passable())) ||
                (t.Actions.HasFlag(UnitAction.SummonAdjacent) && t.SummonAdjacent is null) ||
                (t.Actions.HasFlag(UnitAction.ClawAttack) && t.ClawAttack is null) ||
                (t.SummonAdjacent is { } summon && (string.IsNullOrWhiteSpace(summon.UnitTypeId) ||
                    !Enum.IsDefined(summon.InitialPosture) || UnitContent.Find(summon.UnitTypeId, state.Types) is null)) ||
                t.HolyWave is { MaxUses: < 1 } ||
                t.Fireball is { MaxUses: < 1 } ||
                t.BonusActions.Any(ability => ability.MaxUses < 1 || string.IsNullOrWhiteSpace(ability.Name) ||
                    ability.Boost is { MushroomCost: < 1 } || ability.PlaceBananaPeel is { BananaCost: < 1 } ||
                    ability.Displace is { MaxMove: < 0 or > 1 } ||
                    (Convert.ToInt32(ability.Swap is not null) + Convert.ToInt32(ability.Displace is not null) +
                        Convert.ToInt32(ability.ReshapeRoom is not null) + Convert.ToInt32(ability.Boost is not null) +
                        Convert.ToInt32(ability.PlaceBananaPeel is not null)) > 1 ||
                    (ability.Swap is not null || ability.Displace is not null || ability.ReshapeRoom is not null ||
                        ability.Boost is not null || ability.PlaceBananaPeel is not null) && !ability.Modifiers.IsEmpty ||
                    ability.Modifiers.Any(m => !Enum.IsDefined(m.Stat))) ||
                t.BonusActions.Select(a => a.Name).Distinct().Count() != t.BonusActions.Length))
            throw new ArgumentException("Invalid board, Unit Type, or stat domain.");
        if (state.Units.Any(u => !state.Types.Any(t => t.Id == u.TypeId) ||
            u.CurrentHp < 0 || u.CurrentHp > state.Types.Single(t => t.Id == u.TypeId).Hp ||
            string.IsNullOrWhiteSpace(u.SideId)))
            throw new ArgumentException("Invalid Unit state.");
        if (state.Types.Any(t => t.Unique && state.Units.Where(u => u.TypeId == t.Id && u.CurrentHp > 0)
                .GroupBy(u => u.SideId).Any(g => g.Count() > 1)))
            throw new ArgumentException("Only one Unit of a Unique Unit Type per Side may be in play.");
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
        if (state.Units.Any(u => u.FireballUses is { } uses &&
            state.Types.Single(t => t.Id == u.TypeId).Fireball?.MaxUses != uses.MaxUses))
            throw new ArgumentException("Fireball uses must match Unit Type content.");
        var figures = state.Physical.Figures;
        if (board.Terrain.Any(tile => !Inside(board, tile.Position) || !Enum.IsDefined(tile.Kind) ||
                (tile.Kind == TerrainKind.Rail ? tile.Direction is null || !Enum.IsDefined(tile.Direction.Value) : tile.Direction is not null)) ||
            board.Terrain.Select(tile => tile.Position).Distinct().Count() != board.Terrain.Count)
            throw new ArgumentException("Invalid terrain tiles.");
        if (figures.Any(f => !Enum.IsDefined(f.Posture) || !state.Units.Any(u => u.Id == f.Id && u.CurrentHp > 0)) ||
            figures.Select(f => f.Id).Distinct().Count() != figures.Count ||
            state.Units.Any(u => u.CurrentHp > 0 && !figures.Any(f => f.Id == u.Id)))
            throw new ArgumentException("Invalid figure placement.");
        if (figures.Any(f => !SpatialRules.CanPlaceUnit(state, FootprintGeometry.FootprintOf(state, f.Id), f.Position, f.Id)))
            throw new ArgumentException("Invalid figure footprint placement.");
        if (board.Edges.Any(e => !EdgeReplacementRules.CanReplace(state, e)) ||
            board.Edges.Select(e => e.A.Y < e.B.Y || e.A.Y == e.B.Y && e.A.X < e.B.X
                ? (e.A, e.B) : (e.B, e.A)).Distinct().Count() != board.Edges.Count)
            throw new ArgumentException("Invalid edge features.");
    }
}
