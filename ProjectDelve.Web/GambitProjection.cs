using ProjectDelve.Engine;
using System.Text.Json.Serialization;

namespace ProjectDelve.Web;

// Purpose-built allowlist for model consumption. Engine model growth cannot silently expand it.
public sealed record GambitStats(int Mov, int Rng, int Atk, int Def);
public sealed record GambitAbilityUse(string EntryId, int Remaining, int Maximum);
public sealed record GambitUnit(string Id, string TypeId, string SideId, int CurrentHp, int MaximumHp,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Cell? Position,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Cell>? OccupiedCells,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CellSpan,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Posture? Posture,
    GambitStats EffectiveStats, IReadOnlyList<GambitAbilityUse> LimitedUses);
public sealed record GambitAbility(string Id, string Name, string Category, string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaximumUses);
public sealed record GambitType(string Id, string Name, Footprint Footprint, GambitStats Stats, int Hp,
    IReadOnlyList<GambitAbility> Abilities);
public sealed record GambitBagEntry(string TypeId, string SideId, int Count);
public sealed record GambitWorldEffects(int CardsPerRound, int Cycling, int DrawPileCount, int DiscardPileCount,
    IReadOnlyList<BrowserWorldCard> ActiveContinuous,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrowserWorldCard? ResolvingCard);
public sealed record GambitGlossary(string MOV, string RNG, string ATK, string DEF, string HP,
    string Posture, string LOS, string Actions, string Board);
public sealed record GambitActivation(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ActivationToken? ActiveToken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CurrentUnitId,
    IReadOnlyList<string> CompletedUnitIds, bool MoveDone, int ActionsUsed, int ActionsRemaining,
    IReadOnlyDictionary<string,int> BonusActionUseCounts, IReadOnlyList<ModifierThisTurn> Modifiers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MoveAfterAttackAllowance,
    bool CleavePending,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitAttackContext? Attack,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DoorContinuation? Door,
    IReadOnlyList<GambitDefeatContext> PendingExplosions);
public sealed record GambitObservation(Board Board, IReadOnlyList<GambitUnit> Units, IReadOnlyList<GambitType> Types,
    IReadOnlyList<GambitBagEntry> Bag, GambitActivation Activation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitWorldEffects? WorldEffects,
    IReadOnlyList<SideObjective> Objectives,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameCompletion? Completion,
    int Round, bool RoundComplete, GambitGlossary Glossary);

public sealed record GambitCandidate(string Key, ActivationChoiceKind Kind, bool Relevant,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AbilityId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Cell? Destination,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Cell>? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UnitAction? Action,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? TargetIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Edge? Door,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DoorSuccessCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UnitFreeAction? FreeAction);
public sealed record GambitRoll(DiceFamily Family, int Count, string OwnerUnitId, string SourceUnitId,
    string SourceActionId, string Purpose,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? TargetIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Cell? SelectedCell,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Edge? Door,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SuccessCount);
public sealed record GambitDecision(DecisionKind Kind, string TypeId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnitId,
    IReadOnlyList<GambitCandidate> Candidates, bool AllowsNone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsMoveAfterAttack,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ActivationToken? Token,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitRoll? Roll);

public sealed record GambitAttackTargetResult(string TargetId, int DefenceDice, int Blocks, int Damage);
public sealed record GambitAttackResult(int AttackDice, int Hits, IReadOnlyList<GambitAttackTargetResult> Targets);
public sealed record GambitDice(GambitRoll Pool, IReadOnlyList<string> Faces, int Successes);
public sealed record GambitAttackContext(string AttackerId, string ActionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AbilityName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Cell? SelectedCell,
    IReadOnlyList<AttackTargetDice> Targets, int AttackDice, AttackStage Stage, int Hits, int TargetIndex,
    IReadOnlyList<GambitAttackTargetResult> Results,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitDice? AttackRoll);
public sealed record GambitDefeatContext(string UnitId, string TypeId, string SideId, int CurrentHp,
    Cell Position, IReadOnlyList<Cell> OccupiedCells, Posture Posture);
public sealed record GambitEvent(string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TypeId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceUnitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SideId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AbilityName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Cell>? Path = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Edge? Door = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Cell? Cell = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Posture? Posture = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ActivationToken? Token = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ActivationChoiceKind? Category = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Hits = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Blocks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Damage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Healing = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DieRoll = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SuccessCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Succeeded = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsMoveAfterAttack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Round = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameCompletion? Completion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitAttackResult? Attack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitAttackContext? AttackContext = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitDefeatContext? DefeatContext = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GambitDice? Dice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorldCard? WorldCard = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorldCard? CycledWorldCard = null);

internal static class GambitProjection
{
    internal static readonly GambitGlossary Glossary = new(
        "Movement allowance in Cells for one Move.", "Maximum attack/ability Range in Cells.",
        "Attack dice rolled; Hits are reduced by the target's Blocks.", "Defence dice rolled to produce Blocks.",
        "Current/maximum health; a Unit at 0 HP is defeated.",
        "Upright Units act normally; Lying Units have restricted options defined by legal candidates.",
        "Line of Sight; Trees, Walls and Closed Doors block it.",
        "A Unit normally has one Move and one Action per activation; legal candidates are authoritative.",
        "Unlisted terrain is StoneFloor; absent Edges are open (None). Walls and Closed Doors block movement; Open Doors do not.");

    internal static GambitDecision Decision(DecisionRequest request) => new(request.Kind, request.TypeId, request.UnitId,
        request.Candidates.Select(Candidate).ToArray(), request.AllowsNone, request.IsMoveAfterAttack, request.Token,
        request.Roll is null ? null : Roll(request.Roll));

    private static GambitCandidate Candidate(Candidate candidate) => new(candidate.Key, candidate.Kind, candidate.Relevant,
        ContentDescriptions.EntryId(candidate), candidate.Destination, candidate.Path,
        candidate.Action, candidate.TargetId, candidate.TargetIds.IsDefaultOrEmpty ? null : candidate.TargetIds,
        candidate.Door, candidate.TryOpenDoor?.SuccessCount, candidate.FreeAction);

    private static GambitRoll Roll(DicePool pool) => new(pool.Family, pool.Count, pool.OwnerUnitId, pool.SourceUnitId,
        pool.SourceActionId, pool.Purpose, pool.TargetId, pool.TargetIds.IsDefaultOrEmpty ? null : pool.TargetIds,
        pool.SelectedCell, pool.Door, pool.SuccessCount);

    internal static GambitObservation Observation(GameState state)
    {
        var geometries = BrowserProjection.Figures(state);
        var units = state.Units.Select(unit =>
        {
            var type = state.Types.Single(t => t.Id == unit.TypeId);
            geometries.TryGetValue(unit.Id, out var geometry);
            var posture = state.Physical.Figures.FirstOrDefault(f => f.Id == unit.Id)?.Posture;
            var uses = type.CardEntries(state.Types).Select(entry => (entry, uses: type.UsesFor(unit, entry.Id)))
                .Where(pair => pair.uses is not null)
                .Select(pair => new GambitAbilityUse(pair.entry.Id, pair.uses!.RemainingUses, pair.uses.MaxUses)).ToArray();
            return new GambitUnit(unit.Id, unit.TypeId, unit.SideId, unit.CurrentHp, type.Hp,
                geometry?.Anchor, type.Footprint == Footprint.TwoByTwo ? geometry?.OccupiedCells : null,
                type.Footprint == Footprint.TwoByTwo ? geometry?.CellSpan : null, posture,
                new(state.EffectiveMovOf(unit.Id), state.EffectiveRngOf(unit.Id), state.EffectiveAtkOf(unit.Id), state.EffectiveDefOf(unit.Id)), uses);
        }).ToArray();
        var types = state.Types.Select(type => new GambitType(type.Id, type.DisplayName ?? type.Id, type.Footprint,
            new(type.Mov, type.Rng, type.Atk, type.Def), type.Hp,
            type.CardEntries(state.Types).Select(entry => new GambitAbility(entry.Id, entry.Name, entry.Category,
                entry.Description, entry.MaxUses)).ToArray())).ToArray();
        var bag = state.Bag.GroupBy(token => token).OrderBy(group => group.Key.SideId).ThenBy(group => group.Key.TypeId)
            .Select(group => new GambitBagEntry(group.Key.TypeId, group.Key.SideId, group.Count())).ToArray();
        var activation = new GambitActivation(state.ActiveToken, state.CurrentUnitId, state.CompletedUnitIds.ToArray(), state.MoveDone,
            state.ActionsUsedThisActivation, state.ActionsRemaining,
            new SortedDictionary<string, int>(state.BonusActionUseCountsThisActivation), state.ModifiersThisTurn.ToArray(),
            state.MoveAfterAttackAllowance, state.CleavePending, Context(state.AttackInProgress), state.DoorInProgress,
            state.PendingExplosions.Select(Defeat).ToArray());
        var world = BrowserProjection.WorldEffects(state);
        return new(state.Physical.Board, units, types, bag, activation,
            world is null ? null : new(world.CardsPerRound, world.Cycling, world.DrawPileCount, world.DiscardPileCount,
                world.ActiveContinuous, world.ResolvingCard), state.Objectives.ToArray(), state.Completion,
            state.Round, state.RoundComplete, Glossary);
    }

    internal static GambitEvent Event(RulesEvent value)
    {
        var attackResolution = value.Kind is "AttackResolved" or "AttackTargetResolved";
        var damageResolution = value.Kind is "WorldDamageResolved" or "ExplosionDamageResolved" or "CleaveResolved";
        return new(value.Kind, value.UnitId, value.TargetId, value.TypeId, value.SourceUnitId, value.SideId,
            value.ActionId, value.AbilityName, value.Path, value.Door, value.Cell, value.Posture, value.Token, value.Category,
            attackResolution ? value.Hits : null, attackResolution ? value.Blocks : null,
            attackResolution || damageResolution ? value.Damage : null,
            value.Kind == "HealResolved" ? value.Healing : null, value.DieRoll, value.SuccessCount, value.Succeeded,
            value.IsMoveAfterAttack ? true : null, value.Round, value.Completion,
            value.Attack is null ? null : new(value.Attack.AttackDice, value.Attack.Hits,
                value.Attack.Targets.Select(t => new GambitAttackTargetResult(t.TargetId, t.DefenceDice, t.Blocks, t.Damage)).ToArray()),
            Context(value.AttackContext), value.DefeatContext is null ? null : Defeat(value.DefeatContext),
            value.Dice is null ? null : new(Roll(value.Dice.Pool), value.Dice.Faces, value.Dice.Successes),
            value.WorldCard, value.CycledWorldCard);
    }

    private static GambitAttackContext? Context(AttackContinuation? value) => value is null ? null : new(value.AttackerId,
        value.ActionId, value.AbilityName, value.SelectedCell, value.Targets, value.AttackDice, value.Stage, value.Hits,
        value.TargetIndex, value.Results.IsDefaultOrEmpty ? [] : value.Results.Select(t =>
            new GambitAttackTargetResult(t.TargetId, t.DefenceDice, t.Blocks, t.Damage)).ToArray(),
        value.AttackRoll is null ? null : new(Roll(value.AttackRoll.Pool), value.AttackRoll.Faces, value.AttackRoll.Successes));
    private static GambitDefeatContext Defeat(DefeatContext value) => new(value.Unit.Id, value.Unit.TypeId,
        value.Unit.SideId, value.Unit.CurrentHp, value.Figure.Position, value.OccupiedCells, value.Figure.Posture);
}