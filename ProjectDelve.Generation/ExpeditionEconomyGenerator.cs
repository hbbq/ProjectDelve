using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Optional public services, published after existing Population and Loot.</summary>
internal static class ExpeditionEconomyGenerator
{
    internal static bool Requests(ulong seed, string typeId) =>
        WorldStepSeed.Stream(seed, $"ExpeditionEconomy1/{typeId}/opportunity").Next(12) == 0;

    internal static void Place(WorldGenerationContext context)
    {
        if (context.Expedition?.EconomyEnabled != true) return;
        Place(context, MachineTypes.DelveCare);
        Place(context, MachineTypes.MeteorOMat);
    }

    private static void Place(WorldGenerationContext context, MachineType type)
    {
        var seed = context.Expedition!.LocalSeed;
        if (!Requests(seed, type.Id)) return;
        var board = context.ConstructionBoard!;
        var random = WorldStepSeed.Stream(seed, $"ExpeditionEconomy1/{type.Id}/placement");
        var policyId = "expedition/" + type.Id;
        // Region-qualified physical identities keep vendor receipts and dispensed Items
        // distinct across archived boards, including after an earlier Item was consumed.
        var id = FormattableString.Invariant($"{policyId}/{context.Window.OffsetX},{context.Window.OffsetY}");
        if (type.Operation.PaidServices!.Services.Any(s => s.ItemTypeId is { } item &&
                !board.Content.Items.Any(t => t.Id == item)))
        {
            context.Completion!.RecordOmission(policyId, "expedition-economy", "The current content does not supply the vending Item Type.");
            return;
        }
        var budget = new PlanningBudget(96, 96 * 9);
        var definition = context.Materialize();
        var state = GameEngine.CreateGame(definition, board.Provider);
        var protectedCells = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id))
            .Concat(state.LootMarkers.Select(m => m.Position))
            .Concat(state.GroundObjects.Select(o => o.Position))
            .Concat(state.CreditsPiles.Select(p => p.Position))
            .Concat(state.Resources.Select(r => r.Position))
            .Select(c => c.Y * board.Width + c.X).ToHashSet();
        var reachable = context.Completion!.InteractivePlacementDomain();
        for (var attempt = 0; attempt < 96 && budget.TryAttempt() && budget.Spend(9); attempt++)
        {
            var x = random.Next(board.Width); var y = random.Next(board.Height);
            if (x < 1 || y < 1 || x + 1 >= board.Width || y + 1 >= board.Height) continue;
            // Eight empty neighbors provide ordinary operating positions and a bypass ring.
            var pad = (from yy in Enumerable.Range(y - 1, 3) from xx in Enumerable.Range(x - 1, 3)
                       select yy * board.Width + xx).ToArray();
            if (pad.Any(i => !reachable.Contains(i) || !board.Passable(i) || board.IsReserved(i) ||
                board.IsRouteCorridor(i) || protectedCells.Contains(i) ||
                i % board.Width < x + 1 && board.EdgeAt(new(i, EdgeDirection.Right)) != EdgeKind.None ||
                i / board.Width < y + 1 && board.EdgeAt(new(i, EdgeDirection.Down)) != EdgeKind.None)) continue;
            var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), id, budget);
            draft.PlaceMachine(new(id, new(x, y), type));
            var patch = draft.BuildPatch();
            var candidate = definition with { Machines = board.Export(patch.Result).Machines };
            var proposed = GameEngine.CreateGame(candidate, board.Provider);
            if (!context.Expedition.HasOccupiedAccess(proposed, context.Deployment!,
                    ExpeditionGeometryPlan.OutpostDoorCells(context.Sites)) ||
                !context.Completion.ActualFailures(proposed).IsEmpty) continue;
            if (!context.Completion.TryPublish(patch, policyId + "/candidate-" + attempt, "expedition-economy",
                    out _, interactiveSnapshot: candidate)) continue;
            context.UnitAccessibility = WorldAccessibilityAudit.Population(context, proposed);
            context.Construction.Add(new(id, "expedition", type.Name, id, new(x, y, 1, 1),
                patch.TerrainChanges, patch.EdgeChanges));
            return;
        }
        context.Completion.RecordOmission(policyId, "expedition-economy", "No legal occupied-access-safe open pad in 96 bounded attempts.");
    }
}
