namespace ProjectDelve.Engine;

public static class ObjectivePresentation
{
    public static string Name(SideObjective objective) => objective.Objective switch
    {
        ScenarioObjective.NoObjective => "No Objective",
        ScenarioObjective.EliminateEnemies => "Eliminate Enemies",
        ScenarioObjective.ReachArea => "Reach Area",
        ScenarioObjective.DefeatUnits => "Defeat Units",
        _ => throw new ArgumentException("Unsupported Objective.", nameof(objective))
    };

    public static string Meaning(SideObjective objective)
    {
        if (objective.Objective == ScenarioObjective.NoObjective)
            return "Never achieved. This Side participates normally but cannot win through its own Objective.";
        if (objective.Objective == ScenarioObjective.EliminateEnemies)
            return "No living hostile Units remain on the board.";
        var count = objective.Count == 0 ? "All" : $"At least {objective.Count}";
        var selections = string.Join("; ", objective.Selectors.Select(s => s.Kind switch
        {
            UnitSelectorKind.Unit => $"Unit {s.UnitId}",
            UnitSelectorKind.SideAtStart => $"Side {s.SideId} at start (fixed initial Units)",
            UnitSelectorKind.Group => $"{s.UnitTypeId} / {s.SideId} (dynamic group)",
            _ => "Unknown selector"
        }));
        var criterion = objective.Objective == ScenarioObjective.ReachArea
            ? $"must simultaneously have all Cells of all their figures inside ({objective.Area!.X}, {objective.Area.Y}), {objective.Area.W} x {objective.Area.H}, at a completed activation or fully resolved World Effect boundary"
            : "must actually be defeated; prevented defeat or mere removal does not count. Dynamic groups include defeated and later-created members";
        return $"{count} selected Units {criterion}. Selection: {selections}. Overlaps count once; an empty selection never succeeds.";
    }
}