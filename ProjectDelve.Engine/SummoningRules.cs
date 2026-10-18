namespace ProjectDelve.Engine;

// The concrete summon Actions share placement and creation, but keep their own prerequisites.
public static class SummoningRules
{
    public static SummonAdjacent? Placement(UnitType type, UnitAction action) => action switch
    {
        UnitAction.SummonAdjacent => type.SummonAdjacent,
        UnitAction.SummonWorker when type.SummonWorker is not null => new(UnitTypeIds.Worker, Posture.Upright),
        UnitAction.SummonTroll when type.SummonTroll is not null => new(UnitTypeIds.Troll, Posture.Upright),
        _ => null
    };

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit)
    {
        var type = state.Types.Single(t => t.Id == unit.TypeId);
        foreach (var action in new[] { UnitAction.SummonAdjacent, UnitAction.SummonWorker, UnitAction.SummonTroll })
        {
            if (!type.Actions.HasFlag(action) || Placement(type, action) is not { } summon) continue;
            if (action == UnitAction.SummonWorker && state.Units.Count(u => u.CurrentHp > 0 &&
                    u.SideId == unit.SideId && u.TypeId == UnitTypeIds.Worker) >= type.SummonWorker!.SideLimit) continue;
            if (action == UnitAction.SummonTroll && state.StoredQuantity(unit.SideId, unit.TypeId,
                    ResourceTypes.Diamond.Id) < type.SummonTroll!.DiamondCost) continue;
            if (UnitContent.Find(summon.UnitTypeId, state.Types) is not { } summonedType ||
                !state.CanPlaceUnitType(summonedType)) continue;
            for (var y = 0; y < state.Physical.Board.Height; y++)
                for (var x = 0; x < state.Physical.Board.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (SpatialRules.CanPlaceUnit(state, summonedType.Footprint, cell) &&
                        SpatialRules.AreFootprintsAdjacent(state.Physical.Board, FootprintGeometry.OccupiedCells(state, unit.Id),
                            FootprintGeometry.OccupiedCells(summonedType.Footprint, cell)))
                        yield return new($"{ContentDescriptions.EntryId(new Candidate("", Action: action))}:{x},{y}",
                            Destination: cell, Action: action);
                }
        }
    }

    internal static RulesEvent SpendDiamond(GameState state, Unit source, SummonTroll summon)
    {
        var index = state.StoredResources.FindIndex(r => r.SideId == source.SideId &&
            r.UnitTypeId == source.TypeId && r.Type.Id == ResourceTypes.Diamond.Id);
        var stored = state.StoredResources[index];
        state.StoredResources[index] = stored with { Quantity = stored.Quantity - summon.DiamondCost };
        return new("ResourceSpent", source.Id, TypeId: source.TypeId)
        {
            SourceUnitId = source.Id, SideId = source.SideId, ActionId = "summon-troll",
            ResourceType = stored.Type, StoredQuantity = stored.Quantity - summon.DiamondCost
        };
    }
}
