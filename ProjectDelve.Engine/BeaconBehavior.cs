namespace ProjectDelve.Engine;

internal sealed partial class GameplayQueries
{
    // A decision-local preference. It never searches beyond supplied legal Moves,
    // never retains a goal, and never reads unknown geometry for scoring.
    public string? PreferredBeaconChoice(DecisionRequest request)
    {
        if (request.UnitId is not { } id || request.IsMoveAfterAttack || world.ActionsRemaining == 0 ||
            !world.IsUpright(id) || EffectiveOrderFor(id).Kind != OrderKind.Normal) return null;
        var actor = world.Units.Single(u => u.Id == id);
        if (!FogOfWar.IsEnabled(world, actor.SideId) || !world.AbilitiesOf(id).Any(a =>
                a.Definition.Mechanic is Operate && a.Definition.Timing == AbilityTiming.Action && world.CanUseAbility(id, a))) return null;
        var board = AutonomousBoard(id);
        var machines = board.Machines.Where(m => m.Type.Operation.Reveal is not null && m.HasUsesFor(actor.SideId) &&
            (m.OwnerSide is null || m.OwnerSide == actor.SideId)).ToArray();
        if (machines.Length == 0) return null;
        var areas = new Dictionary<string, IReadOnlySet<Cell>>(StringComparer.Ordinal);
        IReadOnlySet<Cell> Area(Machine machine)
        {
            if (!areas.TryGetValue(machine.Id, out var area))
                areas[machine.Id] = area = FogOfWar.VisionArea(board, [machine.Position], machine.Type.Operation.Reveal!.Distance,
                    excludedMachineId: machine.Id).Where(c => !FogOfWar.IsExplored(world, actor.SideId, c)).ToHashSet();
            return area;
        }
        var immediate = request.Candidates.Where(c => c.Action == UnitAction.Operate && c.MachineId is not null)
            .Select(c => (Candidate: c, Machine: machines.FirstOrDefault(m => m.Id == c.MachineId)))
            .Where(p => p.Machine is not null && Area(p.Machine).Count > 0)
            // Immediate operations share zero movement cost and the actor's
            // current operating position, so machine identity is the first tie.
            .OrderBy(p => p.Machine!.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Candidate.Key, StringComparer.Ordinal).FirstOrDefault();
        if (immediate.Candidate is not null) return immediate.Candidate.Key;
        if (request.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay) is { } stay &&
            machines.Any(m => OccupiedCellsOf(id).Any(c => MachineRules.CanOperateFrom(world, actor, m, c, board, checkReveal: false)) &&
                Area(m).Count > 0)) return stay.Key;
        if (world.MoveDone) return null;
        var opportunities = new List<(Candidate Move, string Machine)>();
        foreach (var move in request.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move))
        {
            var cells = FootprintGeometry.OccupiedCells(world, id, move.Destination!);
            // Known arrivals that endanger the actor or introduce unresolved loot
            // consequences do not establish a guaranteed Move + Operate opportunity.
            if (cells.Any(c => board.TerrainDefinitionAt(c).LegacyKind == TerrainKind.Lava ||
                    world.CellMarkers.Any(m => m.Position == c) || world.LootMarkers.Any(m => m.Position == c))) continue;
            foreach (var machine in machines.Where(m => cells.Any(c => MachineRules.CanOperateFrom(world, actor, m, c, board, checkReveal: false))))
            {
                var area = Area(machine);
                if (area.Count == 0) continue;
                // Swept discovery can change usefulness. The next ordinary decision
                // rechecks it; hypothetical discoveries must not expose hidden geometry.
                opportunities.Add((move, machine.Id));
            }
        }
        return opportunities.OrderBy(p => MovementRules.Cost(p.Move.Path!))
            .ThenBy(p => p.Move.Destination!.Y).ThenBy(p => p.Move.Destination!.X)
            .ThenBy(p => p.Machine, StringComparer.Ordinal).ThenBy(p => p.Move.Key, StringComparer.Ordinal)
            .Select(p => p.Move.Key).FirstOrDefault();
    }
}
