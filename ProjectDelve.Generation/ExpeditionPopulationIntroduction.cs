using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>ExpeditionPopulation1 operates on the resolved Fields1 bands and initial placements only.</summary>
internal sealed class ExpeditionPopulationIntroduction
{
    private const int RampLength = 4;
    private readonly int index;
    private ExpeditionPopulationIntroduction(int index) => this.index = index;

    internal static ExpeditionPopulationIntroduction? For(ExpeditionWorldProfile? profile, WorldWindow window)
    {
        if (profile?.PopulationVersion != ExpeditionWorldProfile.PopulationV1) return null;
        // Expedition windows start at exact multiples of the stride. Each crossing increases one
        // absolute coordinate by one, so the index needs no new checkpoint or gameplay state.
        var regionIndex = Math.Abs((long)window.OffsetX / ExpeditionRegionGeometry.RegionStride) +
            Math.Abs((long)window.OffsetY / ExpeditionRegionGeometry.RegionStride);
        return regionIndex < RampLength ? new((int)regionIndex) : null;
    }

    internal PopulationSettings Adjust(PopulationSettings normal)
    {
        if (normal.Density == PopulationDensity.Off) return normal;
        var cap = index switch { 0 => PopulationDensity.Low, 1 => PopulationDensity.Medium, _ => PopulationDensity.High };
        return normal with
        {
            Difficulty = 1 + ((normal.Difficulty - 1) * index + RampLength / 2) / RampLength,
            Density = (PopulationDensity)Math.Min((int)normal.Density, (int)cap)
        };
    }

    internal int? ArrivalBudget => index switch { 0 => 2, 1 => 3, _ => null };

    internal static int ArrivalCost(IEnumerable<UnitPlacement> units, IReadOnlyDictionary<string, UnitType> types,
        IReadOnlyList<Cell> formation)
    {
        var cost = 0;
        foreach (var unit in units)
        {
            var type = types[unit.UnitTypeId];
            var radius = Math.Max(8L, (long)type.Rng + 3);
            var footprint = FootprintGeometry.OccupiedCells(type.Footprint, unit.Anchor);
            if (footprint.Any(cell => formation.Any(hero => Math.Abs(cell.X - hero.X) + Math.Abs(cell.Y - hero.Y) <= radius)))
                cost += type.Rng > 1 ? 2 : 1;
        }
        return cost;
    }
}
