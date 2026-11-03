namespace ProjectDelve.Engine;

// Invocation requirements are checked together, before any timing, source or
// payment changes. Resolution keeps the selected payload after that boundary.
public static class AbilityCostRules
{
    public static bool Supports(AbilityMechanic mechanic) => mechanic is
        HolyWaveMechanic or HealMechanic or FireballMechanic or StandAdjacentFriendlyMechanic or SelfModifiersMechanic or ExtraActionsMechanic or
        OpenDoor or TryOpenDoor or Telekinesis or FireBreath or ClawAttack or ShootShell or
        SummonAdjacent or SummonWorker or SummonTroll or PickupResource or GiveResource or Operate or
        PlaceBananaPeel or Boost or Swap or Displace or ReshapeRoom;

    public static bool CanAfford(GameState state, string unitId, ResolvedAbility ability)
    {
        var costs = ability.Definition.Costs;
        if (costs.IsDefault) return false;
        // Preserve existing no-cost mechanics' own legality checks.
        if (costs.IsEmpty) return true;
        if (!Supports(ability.Definition.Mechanic) || !CostCatalog.Validate(costs,
                new(MechanicCatalog.AbilitySourceContext(ability.SourceKind),
                    MechanicCatalog.AbilityTimingContext(ability.Definition.Timing), ability.Definition.Usage.MaxUses)).IsEmpty)
            return false;
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit is null) return false;
        Dictionary<string, long> required = new(StringComparer.Ordinal);
        void Require(string id, int amount) => required[id] = required.GetValueOrDefault(id) + amount;
        foreach (var cost in costs)
            switch (cost)
            {
                case LayDownSelf when !state.IsUpright(unitId): return false;
                case SpendResource spend: Require(spend.ResourceTypeId, spend.Amount); break;
            }
        // These canonical mechanics still pay their embedded costs themselves.
        // Reserve them here as well so declarations cannot spend the same marker.
        switch (ability.Definition.Mechanic)
        {
            case SummonTroll troll: Require(ResourceTypeIds.Diamond, troll.DiamondCost); break;
            case PlaceBananaPeel banana: Require(ResourceTypeIds.Banana, banana.BananaCost); break;
            case Boost boost: Require(ResourceTypeIds.Mushroom, boost.MushroomCost); break;
            case ShootShell shell: Require(ResourceTypeIds.Shell, shell.ShellCost); break;
        }
        return required.All(pair => state.StoredQuantity(unit.SideId, unit.TypeId, pair.Key) >= pair.Value);
    }
}

public static partial class GameEngine
{
    private static void PayAbilityCosts(GameState state, string unitId, ResolvedAbility ability, ResolutionEvents events)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        foreach (var cost in ability.Definition.Costs)
            switch (cost)
            {
                case LayDownSelf:
                    ChangePosture(state, unitId, Posture.Lying, events, unitId, ability.ActionId);
                    break;
                case SpendResource spend:
                    var index = state.StoredResources.FindIndex(r => r.SideId == unit.SideId &&
                        r.UnitTypeId == unit.TypeId && r.Type.Id == spend.ResourceTypeId);
                    var stored = state.StoredResources[index];
                    var quantity = stored.Quantity - spend.Amount;
                    state.StoredResources[index] = stored with { Quantity = quantity };
                    events.Add(new RulesEvent("ResourceSpent", unitId, TypeId: unit.TypeId)
                    {
                        SourceUnitId = unitId, SideId = unit.SideId, ActionId = ability.ActionId,
                        ResourceType = stored.Type, StoredQuantity = quantity
                    });
                    break;
            }
    }
}
