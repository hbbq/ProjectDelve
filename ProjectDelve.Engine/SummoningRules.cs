namespace ProjectDelve.Engine;

// The concrete summon Actions share placement and creation, but keep their own prerequisites.
public static class SummoningRules
{
    public static SummonAdjacent? Placement(AbilityMechanic mechanic) => mechanic switch
    {
        SummonAdjacent summon => summon,
        SummonWorker => new(UnitTypeIds.Worker, Posture.Upright),
        SummonTroll => new(UnitTypeIds.Troll, Posture.Upright),
        _ => null
    };

    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit) => state.AbilitiesOf(unit.Id)
        .Where(ability => Placement(ability.Definition.Mechanic) is not null && state.CanUseAbility(unit.Id, ability))
        .SelectMany(ability => Candidates(state, unit, ability));

    // A committed invocation keeps its selected source even after its final charge
    // or Item is spent. Recheck physical prerequisites using that bound payload.
    internal static IEnumerable<Candidate> Candidates(GameState state, Unit unit, ResolvedAbility ability)
    {
        var mechanic = ability.Definition.Mechanic;
        if (Placement(mechanic) is not { } summon) yield break;
        var action = mechanic switch
        {
            SummonWorker => UnitAction.SummonWorker, SummonTroll => UnitAction.SummonTroll,
            _ => UnitAction.SummonAdjacent
        };
        if (mechanic is SummonWorker worker && state.Units.Count(u => u.CurrentHp > 0 &&
                u.SideId == unit.SideId && u.TypeId == UnitTypeIds.Worker) >= worker.SideLimit) yield break;
        if (mechanic is SummonTroll troll && state.StoredQuantity(unit.SideId, unit.TypeId,
                ResourceTypes.Diamond.Id) < troll.DiamondCost) yield break;
        if (UnitContent.Find(summon.UnitTypeId, state.Types) is not { } summonedType ||
            !state.CanPlaceUnitType(summonedType, unit.SideId)) yield break;
        for (var y = 0; y < state.Physical.Board.Height; y++)
        for (var x = 0; x < state.Physical.Board.Width; x++)
        {
            var cell = new Cell(x, y);
            var footprint = FootprintGeometry.OccupiedCells(summonedType.Footprint, cell);
            if (SpatialRules.CanPlaceUnit(state, summonedType.Footprint, cell) &&
                footprint.All(c => TerrainCatalog.Accepts(summonedType, state.Physical.Board.TerrainDefinitionAt(c))) &&
                SpatialRules.AreFootprintsAdjacent(state.Physical.Board, FootprintGeometry.OccupiedCells(state, unit.Id), footprint))
                yield return new($"{ability.CandidatePrefix}:{x},{y}",
                    Destination: cell, Action: action) { Ability = ability };
        }
    }

    internal static RulesEvent SpendDiamond(GameState state, Unit source, SummonTroll summon, ResolvedAbility ability)
    {
        var index = state.StoredResources.FindIndex(r => r.SideId == source.SideId &&
            r.UnitTypeId == source.TypeId && r.Type.Id == ResourceTypes.Diamond.Id);
        var stored = state.StoredResources[index];
        state.StoredResources[index] = stored with { Quantity = stored.Quantity - summon.DiamondCost };
        return new("ResourceSpent", source.Id, TypeId: source.TypeId)
        {
            SourceUnitId = source.Id, SideId = source.SideId,
            ActionId = ability.ActionId,
            ResourceType = stored.Type, StoredQuantity = stored.Quantity - summon.DiamondCost
        };
    }
}
