using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

/// <summary>Recorded authoring odds and amounts; these do not change Credits collection rules.</summary>
public sealed record ExpeditionCreditsSettings(
    int TreasureChance = 70, int DungeonChance = 35, int RuinChance = 40,
    int CampChance = 30, int SettlementChance = 15, int LandmarkChance = 10,
    int OpenGroundChance = 10, int SmallAmount = 3, int MediumAmount = 9,
    int LargeAmount = 25, int MediumChance = 18, int LargeChance = 2, int MaximumPiles = 24)
{
    public ExpeditionCreditsSettings Validate()
    {
        if (new[] { TreasureChance, DungeonChance, RuinChance, CampChance, SettlementChance,
            LandmarkChance, OpenGroundChance, MediumChance, LargeChance }.Any(p => p is < 0 or > 100) ||
            MediumChance + LargeChance > 100 || SmallAmount < 1 || MediumAmount < SmallAmount ||
            LargeAmount < MediumAmount || LargeAmount > 1000 || MaximumPiles is < 0 or > 64)
            throw new ArgumentException("Invalid Expedition Credits placement settings.");
        return this;
    }
}

public sealed record ExpeditionCreditsPlacement(string LocationId, string Kind, Cell Position, int Amount);

public sealed partial class WorldGenerationContext
{
    public List<ExpeditionCreditsPlacement> ExpeditionCreditsPlacements { get; } = [];
}

/// <summary>Independent final authoring stage: adds ordinary piles without modifying earlier generation.</summary>
internal static class ExpeditionCreditsGenerator
{
    internal sealed record Location(string Id, string Kind, int Chance, IEnumerable<int> Cells);

    internal static IEnumerable<Location> Locations(IEnumerable<SiteInfo> sites, int width,
        ExpeditionCreditsSettings settings)
    {
        foreach (var site in sites.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            var (kind, chance) = site.Kind switch
            {
                GeneratedSiteKind.MountainDungeon => ("dungeon", settings.DungeonChance),
                GeneratedSiteKind.Ruin => ("ruin", settings.RuinChance),
                GeneratedSiteKind.Campsite or GeneratedSiteKind.FortifiedOutpost => ("camp", settings.CampChance),
                GeneratedSiteKind.Landmark => ("landmark", settings.LandmarkChance),
                _ => ("settlement", settings.SettlementChance)
            };
            var treasure = site.MountainDungeon?.TreasureAreas ?? [];
            foreach (var area in treasure.OrderBy(a => a.Id, StringComparer.Ordinal))
                yield return new(site.Id + "/treasure/" + area.Id, "treasure", settings.TreasureChance, area.Indices(width));
            var rooms = (site.MountainDungeon?.Rooms ?? [])
                .Concat(site.Buildings.SelectMany(b => b.Rooms)).DistinctBy(r => r.Id)
                .OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
            foreach (var room in rooms)
                yield return new(site.Id + "/room/" + room.Id, kind, chance,
                    room.Usable.Indices(width).Except(treasure.SelectMany(a => a.Indices(width))));
            // Supply areas and courtyards give open camps and fortifications a concrete reward location.
            var exterior = site.PopulationIntent?.Areas.Where(a => a.Role == "supplies").Select(a => a.Area).ToArray() ?? [];
            var areaCells = exterior.Length > 0 ? exterior.SelectMany(a => a.Indices(width))
                : site.Outpost is { } outpost ? outpost.Courtyard.Indices(width) : site.Exterior.Indices(width);
            if (site.MountainDungeon is null && (rooms.Length == 0 || site.Outpost is not null || exterior.Length > 0))
                yield return new(site.Id + "/exterior", kind, chance, areaCells);
        }
    }

    internal static void Place(WorldGenerationContext context)
    {
        if (context.Expedition?.CreditsSettings is not { } settings) return;
        settings.Validate();
        var board = context.ConstructionBoard!;
        var domain = context.Completion!.InteractivePlacementDomain();
        var forbidden = context.PopulationProtectedAreas.SelectMany(a => a.Indices(board.Width))
            .Concat(context.Expedition.EndpointIndices).Concat(context.Deployment!.PlacementOrder).ToHashSet();
        var definition = context.Materialize();
        // Earlier stages have validated the resolved content. Local Types take precedence,
        // and Engine footprint geometry remains the single source for physical occupancy.
        var types = board.Content.Units.Concat(definition.UnitTypes ?? []).GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.Last());
        forbidden.UnionWith(definition.Units.SelectMany(u => FootprintGeometry.OccupiedCells(types[u.UnitTypeId].Footprint, u.Anchor))
            .Select(c => c.Y * board.Width + c.X));
        forbidden.UnionWith((definition.Machines ?? []).Select(m => m.Position.Y * board.Width + m.Position.X));
        var used = definition.CreditsPiles?.Select(p => p.Position.Y * board.Width + p.Position.X).ToHashSet() ?? [];
        var siteCells = context.Sites.SelectMany(s => s.Footprint.Indices(board.Width)).ToHashSet();
        var locations = Locations(context.Sites, board.Width, settings).ToList();
        locations.Add(new("open-ground", "open-ground", settings.OpenGroundChance,
            Enumerable.Range(0, board.Width * board.Height).Where(i => !siteCells.Contains(i))));
        // Independent ordering avoids consistently favoring a particular Site when the cap is reached.
        WorldStepSeed.Stream(context.Expedition.LocalSeed, "ExpeditionCredits1/order").Shuffle(locations);
        foreach (var location in locations)
        {
            if (context.ExpeditionCreditsPlacements.Count >= settings.MaximumPiles) break;
            var random = WorldStepSeed.Stream(context.Expedition.LocalSeed, "ExpeditionCredits1/" + location.Id);
            if (random.Next(100) >= location.Chance) continue;
            var candidates = location.Cells.Distinct().Where(i => domain.Contains(i) && board.Passable(i) &&
                !forbidden.Contains(i) && !used.Contains(i)).Order().ToArray();
            if (candidates.Length == 0) continue;
            var index = candidates[random.Next(candidates.Length)];
            var roll = random.Next(100);
            var amount = location.Kind == "open-ground" ? settings.SmallAmount : roll < settings.LargeChance ? settings.LargeAmount
                : roll < settings.LargeChance + settings.MediumChance ? settings.MediumAmount : settings.SmallAmount;
            used.Add(index);
            context.ExpeditionCreditsPlacements.Add(new(location.Id, location.Kind,
                new(index % board.Width, index / board.Width), amount));
        }
    }
}
