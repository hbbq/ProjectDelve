using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal sealed record BeaconPlacementStatistics(int Target, int Placed, int Attempts, long Work, int ChangedTerrain);

/// <summary>Bounded standalone Machine placement; no Site or Building identity is created.</summary>
internal static class BeaconGenerator
{
    internal static BeaconPlacementStatistics Apply(WorldGenerationContext context, WorldGenerationStep step,
        PlanningBoard board, CellBounds area, string path)
    {
        var target = Math.Clamp((board.Width * board.Height + 3599) / 3600, 1, 4);
        var budget = new PlanningBudget(target * 96, target * 96 * 25);
        var random = WorldStepSeed.Stream(WorldStepSeed.Derive(context.World.Seed, step.Id, step.Kind, "beacons"), "placement");
        var placed = 0; var changed = 0;
        var entrances = context.Entrances.SelectMany(e => new[] { e.InsideIndex, e.OutsideIndex }).ToHashSet();
        // A complete open 5 x 5 neighborhood supplies a local bypass for both ordinary and
        // 2 x 2 figures after its center is occupied. This deliberately rejects narrow passages,
        // Doors, protected routes and Site entrances instead of attempting repairs.
        for (var request = 0; request < target; request++)
        for (var attempt = 0; attempt < 96 && budget.TryAttempt(); attempt++)
        {
            if (!budget.Spend(25)) break;
            var x = random.Next(board.Width); var y = random.Next(board.Height);
            if (!SafePad(x, y)) continue;
            var index = y * board.Width + x;
            var id = path + "/beacon-" + request;
            var draft = board.BeginDraft(area, id, budget);
            draft.SetTerrain(index, MachineTypes.Beacon.RequiredTerrainTypeId!);
            draft.PlaceMachine(new(id, new(x, y), MachineTypes.Beacon));
            var patch = draft.BuildPatch();
            if (!board.TryCommit(patch, out _)) continue;
            context.Construction.Add(new(id, path, "Beacon", id, new(x, y, 1, 1), patch.TerrainChanges, patch.EdgeChanges));
            changed += patch.TerrainChanges.Count; placed++; break;
        }
        return new(target, placed, budget.Attempts, budget.Work, changed);

        bool SafePad(int x, int y)
        {
            if (x < 2 || y < 2 || x + 2 >= board.Width || y + 2 >= board.Height) return false;
            for (var yy = y - 2; yy <= y + 2; yy++)
            for (var xx = x - 2; xx <= x + 2; xx++)
            {
                var i = yy * board.Width + xx;
                if (!board.Passable(i) || board.IsReserved(i) || board.IsRouteCorridor(i) || entrances.Contains(i)) return false;
                if (xx < x + 2 && board.EdgeAt(new(i, EdgeDirection.Right)) != EdgeKind.None ||
                    yy < y + 2 && board.EdgeAt(new(i, EdgeDirection.Down)) != EdgeKind.None) return false;
            }
            return true;
        }
    }
}
