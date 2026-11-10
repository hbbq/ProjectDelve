using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace ProjectDelve.Engine;

internal sealed partial class GameplayQueries
{
    private sealed record AttackOriginContext(ImmutableArray<string> Targets, int Range, bool Exhaustive);
    private readonly ConcurrentDictionary<string, AttackOriginContext> attackOriginContexts = new(StringComparer.Ordinal);

    // This context belongs to the detached query snapshot, never to mutable game state.
    // Preserve world order: callers' short-circuit order must not depend on a hash set.
    private AttackOriginContext AttackOriginsFor(string unitId) => attackOriginContexts.GetOrAdd(unitId, id =>
    {
        var targets = world.Units.Where(target => IsAttackTargetAvailable(id, target.Id))
            .Select(target => target.Id).ToImmutableArray();
        var abilities = world.AbilitiesOf(id).ToArray();
        var range = Math.Max(0, world.EffectiveRngOf(id));
        foreach (var ability in abilities)
            range = ability.Definition.Mechanic switch
            {
                TargetUnitAttackMechanic attack => Math.Max(range, attack.Rng),
                ClawAttack => Math.Max(range, 1),
                _ => range
            };

        // Fireball has an indirect target set. Positional RNG modifiers can change
        // range after relocating, including when leaving a negative aura. Retain
        // exhaustive evaluation for either case rather than guessing an upper bound.
        static bool ChangesAdjacentRange(LocalAbilityDefinition ability) =>
            ability.Timing == AbilityTiming.Passive &&
            ability.Mechanic is AdjacentUnitsStatModifiersMechanic adjacent &&
            adjacent.Modifiers.Any(modifier => modifier.Stat == Stat.Rng);
        var exhaustive = abilities.Any(ability => ability.Definition.Mechanic is FireballMechanic) ||
            world.Types.Any(type => type.LocalAbilities.Any(ChangesAdjacentRange)) ||
            world.EquipmentTypes.Any(type => type.LocalAbilities.Any(ChangesAdjacentRange));
        return new(targets, range, exhaustive);
    });

    private ImmutableArray<string> KnownAttackTargets(string unitId) => AttackOriginsFor(unitId).Targets;

    // Keep this bound aligned with EvaluateAttackTargetFrom's supported mechanics:
    // adding an Attack there requires an explicit safe bound or exhaustive fallback here.
    // A conservative geometry filter only. Every retained origin still goes through
    // the authoritative Attack evaluator and the caller's original placement checks.
    internal IEnumerable<Cell> AttackOriginCandidates(string unitId, string? targetId = null, bool targeted = false)
    {
        var context = AttackOriginsFor(unitId);
        var targets = targeted
            ? context.Targets.Where(id => id == targetId)
            : context.Targets;
        var targetCells = targets.SelectMany(OccupiedCellsOf).ToArray();
        if (targetCells.Length == 0) return [];
        var board = world.Physical.Board;
        if (context.Exhaustive)
            return Enumerable.Range(0, board.Height).SelectMany(y =>
                Enumerable.Range(0, board.Width).Select(x => new Cell(x, y)));
        if (context.Range <= 0) return [];

        // A qualifying occupied-Cell pair implies anchor = target + displacement
        // - actor footprint offset. Union those anchors without inspecting terrain,
        // figures, LOS, ATK, remaining uses, or movement allowance. In particular,
        // current ATK 0 is unsafe to prune: Fury may activate at the future origin.
        var offsets = FootprintGeometry.OccupiedCells(FootprintGeometry.FootprintOf(world, unitId), new(0, 0));
        HashSet<Cell> origins = [];
        foreach (var target in targetCells)
        foreach (var offset in offsets)
        {
            var centerX = (long)target.X - offset.X;
            var centerY = (long)target.Y - offset.Y;
            var minY = Math.Max(0L, centerY - context.Range);
            var maxY = Math.Min(board.Height - 1L, centerY + context.Range);
            for (var y = minY; y <= maxY; y++)
            {
                var dy = Math.Abs(y - centerY);
                // RNG 1 is eight-neighbor Melee, including when another available
                // Attack supplies a larger bound. Distance zero is harmless excess.
                var horizontal = Math.Max(context.Range - dy, dy <= 1 ? 1 : 0);
                var minX = Math.Max(0L, centerX - horizontal);
                var maxX = Math.Min(board.Width - 1L, centerX + horizontal);
                for (var x = minX; x <= maxX; x++) origins.Add(new((int)x, (int)y));
            }
        }
        return origins.OrderBy(cell => cell.Y).ThenBy(cell => cell.X);
    }
}
