using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>ExpeditionStations1: protected arrival access after region zero plus one rare standalone opportunity.</summary>
internal static class ExpeditionStationGenerator
{
    internal const string ArrivalId = ExpeditionRegionGeometry.ArrivalStationId;
    internal const string AdditionalId = ExpeditionRegionGeometry.AdditionalStationId;
    internal static void EstablishArrival(WorldGenerationContext context, PlanningBoard board)
    {
        var deployment = context.Deployment!;
        var excluded = deployment.Area.Indices(board.Width).Append(deployment.DepartureIndex)
            .Concat(context.Expedition!.EndpointIndices).ToHashSet();
        var core = deployment.ManeuverCore.Indices(board.Width).ToHashSet();
        var candidates = deployment.PlacementOrder.Where(core.Contains).SelectMany(actor =>
            Nearby(actor, board).Where(i => !excluded.Contains(i) && !board.IsReserved(i))
                .Select(i => (Actor: actor, Station: i)))
            .OrderBy(p => Math.Abs(p.Actor % board.Width - p.Station % board.Width) +
                Math.Abs(p.Actor / board.Width - p.Station / board.Width))
            .ThenBy(p => p.Station).ThenBy(p => p.Actor).ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException("Expedition arrival has no DelveMart position outside its deployment and endpoints.");
        var chosen = candidates[0];
        const string id = ArrivalId;
        var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), id, new(maxWork: 1000));
        // Early natural preparation only. The protected open pad keeps diagonal LOS open
        // and supplies a bypass around the Machine without consuming deployment Cells.
        var pad = Nearby(chosen.Station, board).Append(chosen.Station)
            .Where(i => !board.IsReserved(i) && !excluded.Contains(i)).ToArray();
        foreach (var i in pad) draft.SetTerrain(i, "grass");
        draft.Reserve(pad);
        draft.PlaceMachine(new(id, Cell(chosen.Station, board.Width), MachineTypes.EquipmentStation));
        var patch = draft.BuildPatch();
        if (!board.TryCommit(patch, out var error)) throw new InvalidOperationException(error);
        // The operator remains in the maneuver core, preserving the deployment contract.
        context.Deployment = deployment with { PlacementOrder = deployment.PlacementOrder
            .Where(i => i != chosen.Actor).Prepend(chosen.Actor).ToImmutableArray() };
        context.PopulationProtectedAreas.Add(GeneratedArea.From(id, pad, board.Width));
        context.Construction.Add(new(id, "expedition", MachineTypes.EquipmentStation.Name, id,
            new(chosen.Station % board.Width, chosen.Station / board.Width, 1, 1), patch.TerrainChanges, patch.EdgeChanges));
    }

    internal static bool RequestsAdditional(ulong localSeed) =>
        WorldStepSeed.Stream(localSeed, "ExpeditionStations1/opportunity").Next(12) == 0;

    internal static void PlaceAdditional(WorldGenerationContext context, WorldGenerationStep step, PlanningBoard board)
    {
        if (!RequestsAdditional(context.Expedition!.LocalSeed)) return;
        var random = WorldStepSeed.Stream(context.Expedition.LocalSeed, "ExpeditionStations1/placement");
        const string id = AdditionalId;
        var budget = new PlanningBudget(96, 96 * 25);
        var reachable = context.Completion!.InteractivePlacementDomain();
        var entrances = context.Entrances.SelectMany(e => new[] { e.InsideIndex, e.OutsideIndex }).ToHashSet();
        for (var attempt = 0; attempt < 96 && budget.TryAttempt() && budget.Spend(25); attempt++)
        {
            var x = random.Next(board.Width); var y = random.Next(board.Height);
            if (x < 2 || y < 2 || x + 2 >= board.Width || y + 2 >= board.Height) continue;
            if (!reachable.Contains(y * board.Width + x)) continue;
            var pad = (from yy in Enumerable.Range(y - 2, 5) from xx in Enumerable.Range(x - 2, 5)
                       select yy * board.Width + xx).ToArray();
            if (pad.Any(i => !board.Passable(i) || board.IsReserved(i) || board.IsRouteCorridor(i) || entrances.Contains(i) ||
                i % board.Width < x + 2 && board.EdgeAt(new(i, EdgeDirection.Right)) != EdgeKind.None ||
                i / board.Width < y + 2 && board.EdgeAt(new(i, EdgeDirection.Down)) != EdgeKind.None)) continue;
            var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), id, budget);
            draft.PlaceMachine(new(id, new(x, y), MachineTypes.EquipmentStation));
            var patch = draft.BuildPatch();
            if (!context.Completion!.TryPublish(patch, id + "/candidate-" + attempt, step.Id, out _)) continue;
            context.Construction.Add(new(id, "expedition", MachineTypes.EquipmentStation.Name, id,
                new(x, y, 1, 1), patch.TerrainChanges, patch.EdgeChanges));
            return;
        }
        context.Completion!.RecordOmission(id, step.Id, "No legal completion-safe open pad in 96 bounded attempts.");
    }

    private static Cell Cell(int i, int width) => new(i % width, i / width);
    private static IEnumerable<int> Nearby(int index, PlanningBoard board)
    {
        var x = index % board.Width; var y = index / board.Width;
        for (var yy = Math.Max(0, y - 1); yy <= Math.Min(board.Height - 1, y + 1); yy++)
        for (var xx = Math.Max(0, x - 1); xx <= Math.Min(board.Width - 1, x + 1); xx++)
            if (xx != x || yy != y) yield return yy * board.Width + xx;
    }
}
