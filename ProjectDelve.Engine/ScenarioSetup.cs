using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Initial geographic setup declarations only; these are not runtime game state.
public sealed record ScenarioSetup(int Version, ScenarioHeroDeployment Deployment,
    List<ScenarioRequiredLocation> RequiredLocations);

public sealed record ScenarioHeroDeployment(int Capacity, List<Cell> Cells, List<Cell> ManeuverCore,
    List<Cell> PlacementOrder, Cell DepartureFrom, Cell Departure, int MinimumArea, int MinimumCore,
    int ManeuverWidth = 2);

public enum ScenarioAccessClass { Ordinary, DeliberateSpecial }
public enum ScenarioAccessCoverage { Any, All }
public sealed record ScenarioRequiredLocation(string Id, string ProducerId, string SourceId,
    ScenarioAccessClass AccessClass, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SpecialReason,
    List<ScenarioAccessGroup> Groups);
public sealed record ScenarioAccessGroup(string Id, ScenarioAccessCoverage Coverage, List<Cell> Positions);
