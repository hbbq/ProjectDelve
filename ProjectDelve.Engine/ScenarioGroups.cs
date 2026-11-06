namespace ProjectDelve.Engine;

/// <summary>Copies scenario controller defaults into exact runtime group identities at setup.</summary>
public static class ScenarioGroups
{
    public static IReadOnlyList<AgencyAssignment> Controllers(ScenarioDefinition definition)
    {
        var assignments = definition.Agency.ToList();
        var identities = definition.Units.Select(u => new ActivationToken(u.UnitTypeId, u.SideId, u.GroupId))
            .Concat((definition.Orders ?? []).Select(o => new ActivationToken(o.UnitTypeId, o.SideId, o.GroupId))).Distinct();
        foreach (var token in identities)
        {
            if (assignments.Any(a => a.UnitTypeId == token.TypeId && a.SideId == token.SideId && a.GroupId == token.GroupId)) continue;
            var fallback = definition.Agency.SingleOrDefault(a => a.UnitTypeId == token.TypeId && a.SideId == token.SideId && a.GroupId is null)
                ?? throw new ArgumentException("Each scenario group requires an exact or default controller assignment.", nameof(definition));
            assignments.Add(fallback with { GroupId = token.GroupId });
        }
        return assignments;
    }
}
