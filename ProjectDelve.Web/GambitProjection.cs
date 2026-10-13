using ProjectDelve.Engine;
using System.Text;
using System.Text.Json.Serialization;

namespace ProjectDelve.Web;

// Purpose-built allowlist for model consumption. Engine model growth cannot silently expand it.
public enum GambitAgency { Direct, Allied, Hostile }
public sealed record GambitStats(int Mov, int Rng, int Atk, int Def);
public sealed record GambitAbilityUse(string EntryId, int Remaining, int Maximum);
public sealed record GambitUnit(string Id, string TypeId, string SideId, GambitAgency Agency, int CurrentHp, int MaximumHp,
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
    int Round, bool RoundComplete, GambitGlossary Glossary, string AsciiBoard);

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

    internal static GambitObservation Observation(GameState state) => Observation(state,
        state.Pending?.Token?.SideId ?? state.Objectives.FirstOrDefault()?.SideId ?? "",
        state.Controllers.ToDictionary(c => c.Token, c => c.Controller == ControllerKind.Automated ? "Gambit" : "Human"));

    internal static GambitObservation Observation(GameState state, string bindingSide,
        IReadOnlyDictionary<ActivationToken, string> controllerBindings)
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
            var token = new ActivationToken(unit.TypeId, unit.SideId);
            var agency = unit.SideId != bindingSide ? GambitAgency.Hostile
                : controllerBindings.GetValueOrDefault(token) == "Gambit" ? GambitAgency.Direct : GambitAgency.Allied;
            return new GambitUnit(unit.Id, unit.TypeId, unit.SideId, agency, unit.CurrentHp, type.Hp,
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
            state.Round, state.RoundComplete, Glossary, RenderAsciiBoard(state.Physical.Board, units));
    }

    private const string UnitSymbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private static string RenderAsciiBoard(Board board, IReadOnlyList<GambitUnit> units)
    {
        var positioned = units.Where(unit => unit.Position is not null)
            .OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray();
        var symbols = positioned.Select((unit, index) => (unit.Id, SymbolFor(index)))
            .ToDictionary(item => item.Id, item => item.Item2, StringComparer.Ordinal);
        var occupants = positioned.SelectMany(unit => (unit.OccupiedCells ?? [unit.Position!])
                .Select(cell => (Cell: cell, Symbol: symbols[unit.Id])))
            .ToDictionary(item => item.Cell, item => item.Symbol);
        var symbolWidth = positioned.Length == 0 ? 1 : symbols.Values.Max(symbol => symbol.Length);
        var cellWidth = Math.Max(1 + symbolWidth, Math.Max(1, (board.Width - 1).ToString().Length));
        var axisWidth = Math.Max(4, (board.Height - 1).ToString().Length + 3);

        var result = new StringBuilder();
        result.AppendLine("ASCII map (derived; structured Board/Units authoritative). x->right; y->down.");
        result.AppendLine("cell=terrain+unit: .=StoneFloor g=Grass t=Tree ~=Water #=StoneFloorWithTable; -=empty; repeated unit symbol=2x2 footprint.");
        result.AppendLine("edge between cells: V |/C/O/X/:; H -/C/O/X/:; Wall/ClosedDoor/OpenDoor/WallWithWindow/None(open).");

        var line = new StringBuilder("x>".PadRight(axisWidth));
        for (var x = 0; x < board.Width; x++)
        {
            if (x > 0) line.Append(' ');
            line.Append(Center(x.ToString(), cellWidth));
        }
        result.AppendLine(line.ToString().TrimEnd());

        for (var y = 0; y < board.Height; y++)
        {
            line.Clear();
            line.Append($"y{y}>".PadRight(axisWidth));
            for (var x = 0; x < board.Width; x++)
            {
                var cell = new Cell(x, y);
                var symbol = occupants.GetValueOrDefault(cell) ?? "-";
                var content = $"{Terrain(board.TerrainAt(cell))}{symbol.PadRight(symbolWidth)}";
                line.Append(content.PadRight(cellWidth));
                if (x < board.Width - 1)
                    line.Append(VerticalEdge(board.EdgeBetween(cell, new(x + 1, y))));
            }
            result.AppendLine(line.ToString().TrimEnd());
            if (y == board.Height - 1) continue;

            line.Clear();
            line.Append(' ', axisWidth);
            for (var x = 0; x < board.Width; x++)
            {
                if (x > 0) line.Append(' ');
                var marker = HorizontalEdge(board.EdgeBetween(new(x, y), new(x, y + 1)));
                line.Append(marker, cellWidth);
            }
            result.AppendLine(line.ToString().TrimEnd());
        }

        result.Append("units:");
        if (positioned.Length == 0) result.Append(" none");
        foreach (var unit in positioned)
            result.Append(' ').Append(symbols[unit.Id]).Append('=').Append(EscapeAscii(unit.Id)).Append(';');
        return result.ToString().TrimEnd(';', '\r', '\n');
    }

    private static string SymbolFor(int index)
    {
        var result = new StringBuilder();
        do
        {
            result.Insert(0, UnitSymbols[index % UnitSymbols.Length]);
            index = index / UnitSymbols.Length - 1;
        } while (index >= 0);
        return result.ToString();
    }

    private static string EscapeAscii(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            if (character is >= ' ' and <= '~' and not ('\\' or ';' or '=')) result.Append(character);
            else result.Append("\\u").Append(((int)character).ToString("X4"));
        return result.ToString();
    }

    private static string Center(string value, int width)
    {
        var left = (width - value.Length) / 2;
        return new string(' ', left) + value + new string(' ', width - left - value.Length);
    }

    private static char Terrain(TerrainKind kind) => kind switch
    {
        TerrainKind.StoneFloor => '.',
        TerrainKind.Grass => 'g',
        TerrainKind.Tree => 't',
        TerrainKind.Water => '~',
        TerrainKind.StoneFloorWithTable => '#',
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static char VerticalEdge(EdgeKind kind) => kind switch
    {
        EdgeKind.Wall => '|',
        EdgeKind.ClosedDoor => 'C',
        EdgeKind.OpenDoor => 'O',
        EdgeKind.WallWithWindow => 'X',
        EdgeKind.None => ':',
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static char HorizontalEdge(EdgeKind kind) => kind switch
    {
        EdgeKind.Wall => '-',
        EdgeKind.ClosedDoor => 'C',
        EdgeKind.OpenDoor => 'O',
        EdgeKind.WallWithWindow => 'X',
        EdgeKind.None => ':',
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

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