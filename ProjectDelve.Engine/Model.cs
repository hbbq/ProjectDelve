using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public sealed record ActivationToken(string TypeId, string SideId);
public enum ControllerKind { Human, Automated }
public sealed record ControllerAssignment(ActivationToken Token, ControllerKind Controller);
public enum DiceFamily { Attack, Defence, D6 }
public sealed record DicePool(DiceFamily Family, int Count, string OwnerUnitId, string SourceUnitId,
    string SourceActionId, string Purpose, string? TargetId = null, Edge? Door = null, int? SuccessCount = null)
{
    public Cell? SelectedCell { get; init; }
    public ImmutableArray<string> TargetIds { get; init; } = [];
}
public sealed record DiceResult(DicePool Pool, ImmutableArray<string> Faces, int Successes);
public enum AttackStage { AttackRoll, DefenceRoll }
public sealed record AttackTargetDice(string TargetId, int DefenceDice);
// Immutable, narrowly scoped progress for one committed Attack, safe to share in detached copies.
public sealed record AttackContinuation(string AttackerId, string ActionId, string? AbilityName,
    Cell? SelectedCell, ImmutableArray<AttackTargetDice> Targets, int AttackDice,
    AttackStage Stage = AttackStage.AttackRoll, int Hits = 0, int TargetIndex = 0,
    ImmutableArray<AttackTargetResult> Results = default)
{
    public DiceResult? AttackRoll { get; init; }
}
public sealed record DoorContinuation(string UnitId, Edge Door, int SuccessCount, string ActionId);

public sealed record Cell(int X, int Y);
public enum Footprint { OneByOne, TwoByTwo }
public enum EdgeKind { Wall, ClosedDoor, OpenDoor, None, WallWithWindow }
public enum TerrainKind { Grass, Tree, Water, StoneFloor, StoneFloorWithTable }
public sealed record TerrainTile(Cell Position, TerrainKind Kind);
public sealed record Edge(Cell A, Cell B, EdgeKind Kind);
public enum Posture { Upright, Lying }
public sealed record Figure(string Id, Cell Position, Posture Posture = Posture.Upright);
[Flags]
public enum UnitAction { None = 0, NormalAttack = 1, Heal = 2, HolyWave = 4, Fireball = 8, Telekinesis = 16, SummonAdjacent = 32, FireBreath = 64, ClawAttack = 128 }
[Flags]
public enum UnitFreeAction { None = 0, OpenDoor = 1 }
[Flags]
public enum UnitBehavior { None = 0, ApproachThroughClosedDoors = 1, MaximizeAttackDistance = 2, BackAwayAfterAttack = 4, Flee = 8, UseSummon = 16, PreferFireBreathThenClaw = 32, SwapThenAttackThenDisplace = 64 }
public sealed record Swap;
// Current external movement supports zero or one ordinary step only.
public sealed record Displace(int MaxMove);
public sealed record SummonAdjacent(string UnitTypeId, Posture InitialPosture);
public sealed record Telekinesis;
// Presentation only: fixed mechanic/counter identities never depend on these names.
public sealed record AbilityPresentationNames
{
    public string? Heal { get; init; }
    public string? Cleave { get; init; }
    public string? HolyWave { get; init; }
    public string? Fireball { get; init; }
    public string? FireBreath { get; init; }
    public string? ClawAttack { get; init; }
    public string? Aura { get; init; }
    public string? Fury { get; init; }
    public string? Backstab { get; init; }
    public string? Undying { get; init; }
    public string? Explosion { get; init; }
    public string? Telekinesis { get; init; }
    public string? TryOpenDoor { get; init; }
    public string? Summon { get; init; }
    public string? MoveAfterAttack { get; init; }
}
public sealed record TryOpenDoor(int SuccessCount);
public sealed record MoveAfterAttack(int MaxSteps);
public sealed record Undying;
public sealed record Explosion
{
    public int Damage { get; init; } = 1;
}
public sealed record Phase;
public sealed record Cleave(int MaxUses = 2)
{
    public int TriggerDamage { get; init; } = 2;
    public int Damage { get; init; } = 1;
}
// The positional argument remains MaxUses; configure healing through the named Amount property.
public sealed record Heal(int MaxUses = 2)
{
    public int Amount { get; init; } = 2;
}
public sealed record HolyWave(int MaxUses = 2);
public sealed record Fireball(int MaxUses = 2);
public sealed record FireBreath;
public sealed record ClawAttack(int AtkBonus = 1);
public sealed record AdjacentFriendlyUnitsDefenceBonus(int Amount, string Name = "Aura");
public sealed record Fury
{
    public int AdjacentEnemyThreshold { get; init; } = 2;
    public int AtkBonus { get; init; } = 1;
    public string Name => "Fury";
    public string DisplayText => $"ATK {AtkBonus:+0;-0;0} while adjacent to {AdjacentEnemyThreshold} or more enemies";
}
public sealed record Backstab
{
    public int AtkBonus { get; init; } = 1;
    public string Name => "Backstab";
    public string DisplayText => $"{AtkBonus:+0;-0;0} ATK when attacking an enemy adjacent to another friendly Unit";
}
// Presentation metadata only; passive rules retain their concrete representations.
public sealed record PassiveDescription(string Name, string DisplayText);
// Current HP is never a modifier stat.
public enum Stat { Atk, Mov, Rng, Def }
public sealed record ModifierThisTurn(Stat Stat, int Amount);
// Immutable content and per-ability counters may be shared safely by state copies.
public sealed record BonusActionAbility(string Name, int? MaxUses, ImmutableArray<ModifierThisTurn> Modifiers)
{
    public Swap? Swap { get; init; }
    public Displace? Displace { get; init; }
    // Name remains the existing persisted key; presentation can vary independently.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }
}
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
    public bool Unique { get; init; }
    public Footprint Footprint { get; init; } = Footprint.OneByOne;
    public string? DisplayName { get; init; }
    public AbilityPresentationNames AbilityNames { get; init; } = new();
    public SummonAdjacent? SummonAdjacent { get; init; }
    public AdjacentFriendlyUnitsDefenceBonus? AdjacentFriendlyUnitsDefenceBonus { get; init; }
    public Fury? Fury { get; init; }
    public Backstab? Backstab { get; init; }
    public Undying? Undying { get; init; }
    public Explosion? Explosion { get; init; }
    public Phase? Phase { get; init; }
    public Cleave? Cleave { get; init; }
    public Heal? Heal { get; init; }
    public HolyWave? HolyWave { get; init; }
    public Fireball? Fireball { get; init; }
    public ClawAttack? ClawAttack { get; init; }
    public IReadOnlyList<PassiveDescription> Passives
    {
        get
        {
            List<PassiveDescription> passives = [];
            if (AdjacentFriendlyUnitsDefenceBonus is { } aura)
                passives.Add(new(AbilityNames.Aura ?? aura.Name, $"Adjacent friendly Units get DEF +{aura.Amount}"));
            if (Fury is { } fury) passives.Add(new(AbilityNames.Fury ?? fury.Name, fury.DisplayText));
            if (Backstab is { } backstab) passives.Add(new(AbilityNames.Backstab ?? backstab.Name, backstab.DisplayText));
            return passives;
        }
    }
    public ImmutableArray<BonusActionAbility> BonusActions { get; init; } = [];
    public static UnitType Hero(string id, int mov, int rng, int atk, int def, int hp) =>
        new(id, mov, rng, atk, def, hp);

    public Unit CreateUnit(string id, string sideId) => 
        new(id, Id, sideId, Hp)
        {
            CleaveUses = Cleave is { } cleave ? new(cleave.MaxUses, cleave.MaxUses) : null,
            HealUses = Heal is { } heal ? new(heal.MaxUses, heal.MaxUses) : null,
            HolyWaveUses = HolyWave is { } wave ? new(wave.MaxUses, wave.MaxUses) : null,
            FireballUses = Fireball is { } fireball ? new(fireball.MaxUses, fireball.MaxUses) : null,
            BonusActionUses = BonusActions.Where(a => a.MaxUses.HasValue)
                .ToImmutableDictionary(a => a.Name, a => new AbilityUses(a.MaxUses!.Value, a.MaxUses.Value))
        };

    public static UnitType Define(string id, string name, UnitAuthoring.BaseStats stats, params UnitAuthoring.Entry[] entries) =>
        UnitAuthoring.Define(id, name, stats, entries);

    public static UnitType Barbarian(string id = UnitTypeIds.Barbarian) => UnitRoster.Barbarian(id);
    public static UnitType Rogue(string id = UnitTypeIds.Rogue) => UnitRoster.Rogue(id);
    public static UnitType Cleric(string id = UnitTypeIds.Cleric) => UnitRoster.Cleric(id);
    public static UnitType Wizard(string id = UnitTypeIds.Wizard) => UnitRoster.Wizard(id);
    public static UnitType Grunt(string id = UnitTypeIds.Grunt) => UnitRoster.Grunt(id);
    public static UnitType Zombie(string id = UnitTypeIds.Zombie) => UnitRoster.Zombie(id);
    public static UnitType Ghost(string id = UnitTypeIds.Ghost) => UnitRoster.Ghost(id);
    public static UnitType SkeletonArcher(string id = UnitTypeIds.SkeletonArcher) => UnitRoster.SkeletonArcher(id);
    public static UnitType BombImp(string id = UnitTypeIds.BombImp) => UnitRoster.BombImp(id);
    public static UnitType DisplacerDemon(string id = UnitTypeIds.DisplacerDemon) => UnitRoster.DisplacerDemon(id);
    public static UnitType Troll(string id = UnitTypeIds.Troll) => UnitRoster.Troll(id);
    public static UnitType Goblin(string id = UnitTypeIds.Goblin) => UnitRoster.Goblin(id);
    public static UnitType Shaman(string id = UnitTypeIds.Shaman) => UnitRoster.Shaman(id);
    public static UnitType RedDragon(string id = UnitTypeIds.RedDragon) => UnitRoster.RedDragon(id);
}
public sealed record Unit(string Id, string TypeId, string SideId, int CurrentHp)
{
    public AbilityUses? CleaveUses { get; init; }
    public AbilityUses? HealUses { get; init; }
    public AbilityUses? HolyWaveUses { get; init; }
    public AbilityUses? FireballUses { get; init; }
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
public enum DecisionKind { SelectUnit, Activation, Move, Act, Cleave, RollDice }
public enum ActivationChoiceKind { Action, Move, Stay, EndTurn, SelectUnit, FreeAction, BonusAction, Cleave, RollDice }
// Every candidate is legal. Relevance guides decision stops and presentation only;
// choices default to relevant unless their rule component supplies a narrower policy.
public sealed record Candidate(string Key, Cell? Destination = null, List<Cell>? Path = null,
    UnitAction? Action = null, string? TargetId = null, Edge? Door = null, TryOpenDoor? TryOpenDoor = null,
    ActivationChoiceKind Kind = ActivationChoiceKind.Action, UnitFreeAction? FreeAction = null,
    bool Relevant = true, BonusActionAbility? BonusAction = null)
{
    public ImmutableArray<string> TargetIds { get; init; } = [];
}
public sealed record DecisionRequest(DecisionKind Kind, string TypeId, string? UnitId, List<Candidate> Candidates, bool AllowsNone,
    bool IsMoveAfterAttack = false)
{
    public ActivationToken? Token { get; init; }
    public DicePool? Roll { get; init; }
}
public sealed record AttackTargetResult(string TargetId, int DefenceDice, int Blocks, int Damage);
// One roll and separate per-Unit Damage; there is deliberately no total Damage.
public sealed record AttackResult(int AttackDice, int Hits, ImmutableArray<AttackTargetResult> Targets);
// Event-local source information remains available after the Figure has left play.
// Retained only for events and the currently resolving automatic sequence, never an in-play Unit.
public sealed record DefeatContext(Unit Unit, UnitType Type, Figure Figure, ImmutableArray<Cell> OccupiedCells);
public sealed record RulesEvent(string Kind, string? UnitId = null, string? TargetId = null,
    string? TypeId = null, List<Cell>? Path = null, int Hits = 0, int Blocks = 0, int Damage = 0,
    Edge? Door = null, int? DieRoll = null, int? SuccessCount = null, bool? Succeeded = null,
    bool IsMoveAfterAttack = false, string? AbilityName = null, int Healing = 0,
    AttackResult? Attack = null, Posture? Posture = null)
{
    public ActivationToken? Token { get; init; }
    public string? SideId { get; init; }
    public string? SourceUnitId { get; init; }
    public string? ActionId { get; init; }
    public ActivationChoiceKind? Category { get; init; }
    public Cell? Cell { get; init; }
    public AttackContinuation? AttackContext { get; init; }
    public DefeatContext? DefeatContext { get; init; }
    public DiceResult? Dice { get; init; }
    public int? Round { get; init; }
    public WorldCard? WorldCard { get; init; }
    public WorldCard? CycledWorldCard { get; init; }
}

// Old group-phase saves cannot be resumed as per-unit activations.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class GameState
{
    public required PhysicalState Physical { get; set; }
    public required List<UnitType> Types { get; set; }
    public required List<Unit> Units { get; set; }

    internal bool CanPlaceUnitType(UnitType type) =>
        !type.Unique || !Units.Any(u => u.TypeId == type.Id && u.CurrentHp > 0);

    // Creating detached Unit data does not enter play; placement is the runtime entry boundary.
    public void PlaceUnit(string typeId, string id, string sideId, Cell cell, Posture posture = Posture.Upright)
    {
        var type = UnitContent.Find(typeId, Types)
            ?? throw new ArgumentException("Unit Type is not defined.", nameof(typeId));
        if (type.Hp > 1 && !type.Unique)
            throw new ArgumentException("Maximum HP greater than 1 requires a Unique Unit Type.", nameof(typeId));
        if (!CanPlaceUnitType(type))
            throw new ArgumentException("A Unit of this Unique Unit Type is already in play.", nameof(typeId));
        if (Units.Any(u => u.Id == id) || string.IsNullOrWhiteSpace(sideId) ||
            !Enum.IsDefined(posture) || !Enum.IsDefined(type.Footprint) || !SpatialRules.CanPlaceUnit(this, type.Footprint, cell))
            throw new ArgumentException("Invalid Unit placement.");
        if (!Types.Any(t => t.Id == type.Id)) Types.Add(type);
        Units.Add(type.CreateUnit(id, sideId));
        Physical.Figures.Add(new(id, cell, posture));
    }
    public WorldEffectsSettings? WorldEffects { get; set; }
    public WorldDeck? WorldDeck { get; set; }
    public int Round { get; set; }
    public List<ActivationToken> Bag { get; set; } = [];
    public ActivationToken? ActiveToken { get; set; }
    public List<ControllerAssignment> Controllers { get; set; } = [];
    public ControllerKind ControllerFor(ActivationToken token) => Controllers.Single(c => c.Token == token).Controller;
    public ControllerKind ControllerFor(DecisionRequest request) => ControllerFor(request.Token
        ?? throw new InvalidOperationException("Decision has no controlling group."));
    public AttackContinuation? AttackInProgress { get; set; }
    public DoorContinuation? DoorInProgress { get; set; }
    // FIFO defeat consequences, retained across dice boundaries only until resolution completes.
    public List<DefeatContext> PendingExplosions { get; set; } = [];
    public bool MoveDone { get; set; }
    // Existing flag records whether an Action has been performed; counts govern opportunities.
    public bool ActionDone
    {
        get => ActionsUsedThisActivation > 0;
        set => ActionsUsedThisActivation = value ? Math.Max(1, ActionsUsedThisActivation) : 0;
    }
    public int ActionsUsedThisActivation { get; set; }
    public Dictionary<string, int> BonusActionUseCountsThisActivation { get; set; } = [];
    public int EffectiveActionsPerActivation => Math.Max(0, 1 +
        (WorldDeck?.Modifier(WorldEffect.Frenzy, WorldEffect.Fatigue) ?? 0));
    public int EffectiveBonusActionUsesPerActivation => Math.Max(0, 1 +
        (WorldDeck?.Modifier(WorldEffect.Surge, WorldEffect.Hesitation) ?? 0));
    public int ActionsRemaining => Math.Max(0, EffectiveActionsPerActivation - ActionsUsedThisActivation);
    public int BonusActionUsesThisActivation(string name) => Math.Max(
        BonusActionUseCountsThisActivation.GetValueOrDefault(name), BonusActionsUsedThisActivation.Contains(name) ? 1 : 0);
    // Ability names are the existing content identities, shared with persistent use counters.
    public HashSet<string> BonusActionsUsedThisActivation { get; set; } = [];
    public List<ModifierThisTurn> ModifiersThisTurn { get; set; } = [];
    // Derived authoritative values are also serialized for rule-independent clients.
    public Dictionary<string, int> EffectiveAtk => Units.ToDictionary(u => u.Id, u => EffectiveAtkOf(u.Id));
    public Dictionary<string, int> EffectiveMov => Units.ToDictionary(u => u.Id, u => EffectiveMovOf(u.Id));
    public Dictionary<string, int> EffectiveRng => Units.ToDictionary(u => u.Id, u => EffectiveRngOf(u.Id));
    public Dictionary<string, int> EffectiveDef => Units.ToDictionary(u => u.Id, u => EffectiveDefOf(u.Id));

    public bool IsUpright(string unitId) =>
        Physical.Figures.Any(f => f.Id == unitId && f.Posture == Posture.Upright);

    public int EffectiveAtkOf(string unitId)
    {
        var unit = Units.Single(u => u.Id == unitId);
        var type = Types.Single(t => t.Id == unit.TypeId);
        var attack = type.Atk + (WorldDeck?.Modifier(WorldEffect.Bloodlust, WorldEffect.Weakness) ?? 0) + (CurrentUnitId == unitId
            ? ModifiersThisTurn.Where(m => m.Stat == Stat.Atk).Sum(m => m.Amount) : 0);
        var figure = Physical.Figures.SingleOrDefault(f => f.Id == unitId);
        if (type.Fury is not { } fury || unit.CurrentHp <= 0 || figure is null || !IsUpright(unitId)) return Math.Max(0, attack);

        // Derive Fury solely from the evaluated world, including hypothetical copies.
        var adjacentEnemies = Units.Where(u => u.CurrentHp > 0 && u.SideId != unit.SideId)
            .Count(enemy => Physical.Figures.SingleOrDefault(f => f.Id == enemy.Id) is { } enemyFigure &&
                SpatialRules.AreAdjacent(this, unitId, enemy.Id));
        return Math.Max(0, attack + (adjacentEnemies >= fury.AdjacentEnemyThreshold ? fury.AtkBonus : 0));
    }
    // Target-specific Attack Dice, shared by legality, effectiveness and resolution.
    // General EffectiveAtk remains independent of the selected target.
    public int EffectiveAtkAgainst(string attackerId, string targetId)
    {
        var attack = EffectiveAtkOf(attackerId);
        var attacker = Units.Single(u => u.Id == attackerId);
        var target = Units.Single(u => u.Id == targetId);
        if (Types.Single(t => t.Id == attacker.TypeId).Backstab is not { } backstab ||
            attacker.CurrentHp <= 0 || !IsUpright(attackerId) || target.CurrentHp <= 0 || attacker.SideId == target.SideId)
            return Math.Max(0, attack);

        var targetFigure = Physical.Figures.SingleOrDefault(f => f.Id == targetId);
        if (targetFigure is null) return Math.Max(0, attack);
        var supported = Units.Any(u => u.Id != attackerId && u.CurrentHp > 0 && u.SideId == attacker.SideId &&
            Physical.Figures.SingleOrDefault(f => f.Id == u.Id) is { } friendlyFigure &&
            SpatialRules.AreAdjacent(this, targetId, u.Id));
        return Math.Max(0, attack + (supported ? backstab.AtkBonus : 0));
    }
    public int EffectiveMovOf(string unitId) =>
        Math.Max(0, Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Mov +
        (WorldDeck?.Modifier(WorldEffect.Haste, WorldEffect.Sluggishness) ?? 0) +
        (CurrentUnitId == unitId ? ModifiersThisTurn.Where(m => m.Stat == Stat.Mov).Sum(m => m.Amount) : 0));
    public int EffectiveRngOf(string unitId) =>
        Types.Single(t => t.Id == Units.Single(u => u.Id == unitId).TypeId).Rng +
        (CurrentUnitId == unitId ? ModifiersThisTurn.Where(m => m.Stat == Stat.Rng).Sum(m => m.Amount) : 0);
    public int EffectiveDefOf(string unitId)
    {
        var unit = Units.Single(u => u.Id == unitId);
        var type = Types.Single(t => t.Id == unit.TypeId);
        var defence = type.Def + (WorldDeck?.Modifier(WorldEffect.IronSkin, WorldEffect.Vulnerability) ?? 0) + (CurrentUnitId == unitId
            ? ModifiersThisTurn.Where(m => m.Stat == Stat.Def).Sum(m => m.Amount) : 0);
        var figure = Physical.Figures.SingleOrDefault(f => f.Id == unitId);
        if (unit.CurrentHp <= 0 || figure is null) return Math.Max(0, defence);

        // Derive passives from this state's content and physical situation on every query.
        // No derived state is shared with live, copied or hypothetical worlds.
        foreach (var source in Units.Where(u => u.Id != unitId && u.CurrentHp > 0 && u.SideId == unit.SideId))
        {
            var bonus = Types.Single(t => t.Id == source.TypeId).AdjacentFriendlyUnitsDefenceBonus;
            if (bonus is null) continue;
            var sourceFigure = Physical.Figures.SingleOrDefault(f => f.Id == source.Id);
            if (sourceFigure is { Posture: Posture.Upright } && SpatialRules.AreAdjacent(this, source.Id, unitId))
                defence += bonus.Amount;
        }
        return Math.Max(0, defence);
    }
    public List<string> CompletedUnitIds { get; set; } = [];
    public string? CurrentUnitId { get; set; }
    // Mandatory post-attack movement resolves before the activation may end.
    public int? MoveAfterAttackAllowance { get; set; }
    // Optional immediate resolution created by a qualifying Attack, never activation history.
    public bool CleavePending { get; set; }
    public DecisionRequest? Pending { get; set; }
    public bool RoundComplete { get; set; }

    internal GameState Copy() => new()
    {
        Physical = new PhysicalState(new Board(Physical.Board.Width, Physical.Board.Height,
            [.. Physical.Board.Edges]) { Terrain = [.. Physical.Board.Terrain] }, [.. Physical.Figures]),
        Types = [.. Types], Units = [.. Units], Round = Round, Bag = [.. Bag],
        WorldEffects = WorldEffects, WorldDeck = WorldDeck?.Copy(),
        ActionsUsedThisActivation = ActionsUsedThisActivation,
        BonusActionUseCountsThisActivation = new(BonusActionUseCountsThisActivation),
        ActiveToken = ActiveToken, Controllers = [.. Controllers],
        AttackInProgress = AttackInProgress, DoorInProgress = DoorInProgress, MoveDone = MoveDone, ActionDone = ActionDone,
        BonusActionsUsedThisActivation = [.. BonusActionsUsedThisActivation], CompletedUnitIds = [.. CompletedUnitIds],
        ModifiersThisTurn = [.. ModifiersThisTurn],
        CurrentUnitId = CurrentUnitId, MoveAfterAttackAllowance = MoveAfterAttackAllowance,
        CleavePending = CleavePending, PendingExplosions = [.. PendingExplosions],
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
    ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag);
    // Fisher-Yates shuffle selection, replaceable independently of combat dice and token draws.
    int WorldShuffleIndex(int exclusiveMax) => Random.Shared.Next(exclusiveMax);
    AttackFace RollAttackDie();
    DefenceFace RollDefenceDie();
    int RollD6();
}

public enum AttackFace { Hit, Miss }
public enum DefenceFace { Block, Miss }
