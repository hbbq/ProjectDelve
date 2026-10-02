using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record Cell(int X, int Y);
public enum EdgeKind { Wall, ClosedDoor, OpenDoor, None, WallWithWindow }
public enum TerrainKind { Grass, Tree, Water, StoneFloor, StoneFloorWithTable }
public sealed record TerrainTile(Cell Position, TerrainKind Kind);
public sealed record Edge(Cell A, Cell B, EdgeKind Kind);
public enum Posture { Upright, Lying }
public sealed record Figure(string Id, Cell Position, Posture Posture = Posture.Upright);
[Flags]
public enum UnitAction { None = 0, NormalAttack = 1 }
[Flags]
public enum UnitFreeAction { None = 0, OpenDoor = 1 }
[Flags]
public enum UnitBehavior { None = 0, ApproachThroughClosedDoors = 1, MaximizeAttackDistance = 2, BackAwayAfterAttack = 4 }
public sealed record TryOpenDoor(int SuccessCount);
public sealed record MoveAfterAttack(int MaxSteps);
public sealed record AdjacentFriendlyUnitsDefenceBonus(int Amount, string Name = "Aura");
// Current HP is never a modifier stat.
public enum Stat { Atk, Mov, Rng, Def }
public sealed record ModifierThisTurn(Stat Stat, int Amount);
// Immutable content and per-ability counters may be shared safely by state copies.
public sealed record BonusActionAbility(string Name, int MaxUses, ImmutableArray<ModifierThisTurn> Modifiers);
public sealed record AbilityUses
{
    public int MaxUses { get; }
    public int RemainingUses { get; }

    public AbilityUses(int maxUses, int remainingUses)
    {
        if (maxUses < 0 || remainingUses < 0 || remainingUses > maxUses)
            throw new ArgumentOutOfRangeException(nameof(remainingUses));
        MaxUses = maxUses;
        RemainingUses = remainingUses;
    }
}
public sealed record UnitType(string Id, int Mov, int Rng, int Atk, int Def, int Hp,
    UnitAction Actions = UnitAction.NormalAttack, TryOpenDoor? TryOpenDoor = null,
    UnitBehavior Behaviors = UnitBehavior.None, MoveAfterAttack? MoveAfterAttack = null,
    UnitFreeAction FreeActions = UnitFreeAction.None)
{
    public AdjacentFriendlyUnitsDefenceBonus? AdjacentFriendlyUnitsDefenceBonus { get; init; }
    public ImmutableArray<BonusActionAbility> BonusActions { get; init; } = [];
    public static UnitType Hero(string id, int mov, int rng, int atk, int def, int hp) =>
        new(id, mov, rng, atk, def, hp);

    public Unit CreateUnit(string id, string sideId) => 
        new(id, Id, sideId, Hp)
        {
            BonusActionUses = BonusActions.ToImmutableDictionary(a => a.Name, a => new AbilityUses(a.MaxUses, a.MaxUses))
        };

    public static UnitType Barbarian(string id = "barbarian-type") =>
        new(id, 3, 1, 4, 3, 5, FreeActions: UnitFreeAction.OpenDoor)
        {
            BonusActions = [
                new("Rage", 2, [new(Stat.Atk, 2)])
            ]
        };

    public static UnitType Rogue(string id = "rogue-type") =>
        new(id, 4, 1, 3, 2, 4, FreeActions: UnitFreeAction.OpenDoor)
        {
            BonusActions = [
                new("Dash", 2, [new(Stat.Mov, 2)]),
                new("Throwing Knife", 2, [new(Stat.Rng, 2), new(Stat.Atk, -1)])
            ]
        };

    public static UnitType Cleric(string id = "cleric-type") =>
        new(id, 3, 1, 3, 3, 4, FreeActions: UnitFreeAction.OpenDoor)
        {
            AdjacentFriendlyUnitsDefenceBonus = new(1, "Aura")
        };

    public static UnitType Grunt(string id = "grunt-type") => new(id, 3, 1, 3, 3, 1);

    public static UnitType Zombie(string id = "zombie-type") =>
        new(id, 2, 1, 3, 3, 1, TryOpenDoor: new(2),
            Behaviors: UnitBehavior.ApproachThroughClosedDoors);

    public static UnitType SkeletonArcher(string id = "skeleton-archer-type") =>
        new(id, 3, 4, 3, 3, 1, Behaviors: UnitBehavior.MaximizeAttackDistance);

    public static UnitType Goblin(string id = "goblin-type") =>
        new(id, 4, 1, 2, 2, 1, Behaviors: UnitBehavior.BackAwayAfterAttack, MoveAfterAttack: new(1));
}
public sealed record Unit(string Id, string TypeId, string SideId, int CurrentHp)
{
    public ImmutableDictionary<string, AbilityUses> BonusActionUses { get; init; } = ImmutableDictionary<string, AbilityUses>.Empty;
}

public sealed record Board(int Width, int Height, List<Edge> Edges)
{
    // Unlisted cells contain stone floor; absent edges have kind None.
    public List<TerrainTile> Terrain { get; init; } = [];
    public TerrainKind TerrainAt(Cell cell) =>
        Terrain.FirstOrDefault(tile => tile.Position == cell)?.Kind ?? TerrainKind.StoneFloor;
    public EdgeKind EdgeBetween(Cell a, Cell b) =>
        Edges.FirstOrDefault(e => e.A == a && e.B == b || e.A == b && e.B == a)?.Kind ?? EdgeKind.None;
}

public static class BoardProperties
{
    public static bool Passable(this TerrainKind kind) => kind is TerrainKind.Grass or TerrainKind.StoneFloor;
    public static bool BlocksLos(this TerrainKind kind) => kind == TerrainKind.Tree;
    public static bool Passable(this EdgeKind kind) => kind is EdgeKind.None or EdgeKind.OpenDoor;
    public static bool BlocksLos(this EdgeKind kind) => kind is EdgeKind.Wall or EdgeKind.ClosedDoor;
}
public sealed record PhysicalState(Board Board, List<Figure> Figures);
// Normal Unit choices use Activation. Move is also used for the narrow post-attack
// continuation; Move/Act requests support the providers' existing ranking routines.
public enum DecisionKind { SelectUnit, Activation, Move, Act }
public enum ActivationChoiceKind { Action, Move, Stay, EndTurn, SelectUnit, FreeAction, BonusAction }
// Every candidate is legal. Relevance guides decision stops and presentation only;
// choices default to relevant unless their rule component supplies a narrower policy.
public sealed record Candidate(string Key, Cell? Destination = null, List<Cell>? Path = null,
    UnitAction? Action = null, string? TargetId = null, Edge? Door = null, TryOpenDoor? TryOpenDoor = null,
    ActivationChoiceKind Kind = ActivationChoiceKind.Action, UnitFreeAction? FreeAction = null,
    bool Relevant = true, BonusActionAbility? BonusAction = null);
public sealed record DecisionRequest(DecisionKind Kind, string TypeId, string? UnitId, List<Candidate> Candidates, bool AllowsNone,
    bool IsMoveAfterAttack = false);
public sealed record RulesEvent(string Kind, string? UnitId = null, string? TargetId = null,
    string? TypeId = null, List<Cell>? Path = null, int Hits = 0, int Blocks = 0, int Damage = 0,
    Edge? Door = null, int? DieRoll = null, int? SuccessCount = null, bool? Succeeded = null,
    bool IsMoveAfterAttack = false, string? AbilityName = null);

// Old group-phase saves cannot be resumed as per-unit activations.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class GameState
{
    public required PhysicalState Physical { get; set; }
    public required List<UnitType> Types { get; set; }
    public required List<Unit> Units { get; set; }
    public int Round { get; set; }
    public List<string> Bag { get; set; } = [];
    public string? ActiveTypeId { get; set; }
    public bool MoveDone { get; set; }
    public bool ActionDone { get; set; }
    public bool BonusActionUsed { get; set; }
    public List<ModifierThisTurn> ModifiersThisTurn { get; set; } = [];
    // Derived authoritative values are also serialized for rule-independent clients.
    public Dictionary<string, int> EffectiveAtk => Units.ToDictionary(u => u.Id, u => EffectiveAtkOf(u.Id));
    public Dictionary<string, int> EffectiveMov => Units.ToDictionary(u => u.Id, u => EffectiveMovOf(u.Id));
    public Dictionary<string, int> EffectiveRng => Units.ToDictionary(u => u.Id, u => EffectiveRngOf(u.Id));
    public Dictionary<string, int> EffectiveDef => Units.ToDictionary(u => u.Id, u => EffectiveDefOf(u.Id));

    public int EffectiveAtkOf(string unitId) =>
        Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Atk +
        (CurrentUnitId == unitId ? ModifiersThisTurn.Where(m => m.Stat == Stat.Atk).Sum(m => m.Amount) : 0);
    public int EffectiveMovOf(string unitId) =>
        Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Mov +
        (CurrentUnitId == unitId ? ModifiersThisTurn.Where(m => m.Stat == Stat.Mov).Sum(m => m.Amount) : 0);
    public int EffectiveRngOf(string unitId) =>
        Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Rng +
        (CurrentUnitId == unitId ? ModifiersThisTurn.Where(m => m.Stat == Stat.Rng).Sum(m => m.Amount) : 0);
    public int EffectiveDefOf(string unitId)
    {
        var unit = Units.Single(u => u.Id == unitId);
        var type = Types.Single(t => t.Id == unit.TypeId);
        var defence = type.Def + (CurrentUnitId == unitId
            ? ModifiersThisTurn.Where(m => m.Stat == Stat.Def).Sum(m => m.Amount) : 0);
        var figure = Physical.Figures.SingleOrDefault(f => f.Id == unitId);
        if (unit.CurrentHp <= 0 || figure is null) return defence;

        // Derive passives from this state's content and physical situation on every query.
        // No derived state is shared with live, copied or hypothetical worlds.
        foreach (var source in Units.Where(u => u.CurrentHp > 0 && u.SideId == unit.SideId))
        {
            var bonus = Types.Single(t => t.Id == source.TypeId).AdjacentFriendlyUnitsDefenceBonus;
            if (bonus is null) continue;
            var sourceFigure = Physical.Figures.SingleOrDefault(f => f.Id == source.Id);
            if (sourceFigure is not null && SpatialRules.AreAdjacent(Physical.Board, sourceFigure.Position, figure.Position))
                defence += bonus.Amount;
        }
        return defence;
    }
    public List<string> CompletedUnitIds { get; set; } = [];
    public string? CurrentUnitId { get; set; }
    // Mandatory post-attack movement resolves before the activation may end.
    public int? MoveAfterAttackAllowance { get; set; }
    public DecisionRequest? Pending { get; set; }
    public bool RoundComplete { get; set; }

    internal GameState Copy() => new()
    {
        Physical = new PhysicalState(new Board(Physical.Board.Width, Physical.Board.Height,
            [.. Physical.Board.Edges]) { Terrain = [.. Physical.Board.Terrain] }, [.. Physical.Figures]),
        Types = [.. Types], Units = [.. Units], Round = Round, Bag = [.. Bag],
        ActiveTypeId = ActiveTypeId, MoveDone = MoveDone, ActionDone = ActionDone,
        BonusActionUsed = BonusActionUsed, CompletedUnitIds = [.. CompletedUnitIds],
        ModifiersThisTurn = [.. ModifiersThisTurn],
        CurrentUnitId = CurrentUnitId, MoveAfterAttackAllowance = MoveAfterAttackAllowance,
        Pending = Pending, RoundComplete = RoundComplete
    };
}

// EventIndex associates a snapshot with Events without duplicating semantic events.
// StateAfter is a detached engine state, not a patch or a resumable decision point.
public sealed record ResolutionStep(int EventIndex, GameState StateAfter);
public sealed record EngineResult(GameState State, List<RulesEvent> Events, DecisionRequest? NextInput)
{
    public List<ResolutionStep> ResolutionSteps { get; init; } = [];
}

public interface IDecisionProvider
{
    string? Choose(DecisionRequest request, IGameplayQueries queries);
}

public interface IRandomProvider
{
    string DrawToken(IReadOnlyList<string> bag);
    AttackFace RollAttackDie();
    DefenceFace RollDefenceDie();
    int RollD6();
}

public enum AttackFace { Hit, Miss }
public enum DefenceFace { Block, Miss }
