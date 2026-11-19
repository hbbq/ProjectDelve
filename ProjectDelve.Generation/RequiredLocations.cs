using System.Collections.Immutable;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

internal enum RequiredAccessClass { Ordinary, DeliberateSpecial }
internal enum RequiredAccessCoverage { Any, All }
internal sealed record RequiredAccessPreparation(int Position, ImmutableArray<int> Cells);
internal sealed record RequiredAccessGroup(string Id, RequiredAccessCoverage Coverage, ImmutableArray<int> Positions)
{
    public ImmutableArray<RequiredAccessPreparation> Preparation { get; init; } = [];
}
internal sealed record RequiredLocation(string Id, string ProducerId, string SourceId,
    RequiredAccessClass AccessClass, string? SpecialReason, ImmutableArray<RequiredAccessGroup> Groups)
{
    internal RequiredLocation(string id, string producerId, string sourceId, ImmutableArray<RequiredAccessGroup> groups)
        : this(id, producerId, sourceId, RequiredAccessClass.Ordinary, null, groups) { }
}

/// <summary>Immutable authoring declarations. Groups are conjunctive; Coverage applies within each group.</summary>
internal sealed class RequiredLocationRegistry
{
    internal ImmutableArray<RequiredLocation> Locations { get; }
    internal ImmutableArray<string> Errors { get; }
    private readonly int width, height;
    private RequiredLocationRegistry(IEnumerable<RequiredLocation> locations, int width, int height)
    {
        this.width = width; this.height = height;
        Locations = locations.Select(l => l with { Groups = l.Groups.IsDefault ? l.Groups : l.Groups
                .Select(g => g with { Positions = g.Positions.IsDefault ? g.Positions : g.Positions.Order().ToImmutableArray(),
                    Preparation = g.Preparation.IsDefault ? g.Preparation : g.Preparation.OrderBy(p => p.Position)
                        .Select(p => p with { Cells = p.Cells.IsDefault ? p.Cells : p.Cells.Order().ToImmutableArray() }).ToImmutableArray() })
                .OrderBy(g => g.Id, StringComparer.Ordinal).ToImmutableArray() })
            .OrderBy(l => l.Id, StringComparer.Ordinal).ThenBy(l => l.ProducerId, StringComparer.Ordinal)
            .ThenBy(l => l.SourceId, StringComparer.Ordinal).ToImmutableArray();
        var errors = new List<string>();
        if (width < 1 || height < 1) errors.Add("Required-location board dimensions must be positive.");
        foreach (var duplicate in Locations.GroupBy(l => l.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Duplicate required-location ID '{duplicate.Key}'.");
        foreach (var location in Locations)
        {
            if (string.IsNullOrWhiteSpace(location.Id) || string.IsNullOrWhiteSpace(location.ProducerId) || string.IsNullOrWhiteSpace(location.SourceId))
                errors.Add($"Location '{location.Id}' requires stable location, producer and source IDs.");
            if (!Enum.IsDefined(location.AccessClass)) errors.Add($"Location '{location.Id}' has invalid access class.");
            if (location.AccessClass == RequiredAccessClass.DeliberateSpecial && string.IsNullOrWhiteSpace(location.SpecialReason))
                errors.Add($"Location '{location.Id}' requires a deliberate-special reason.");
            if (location.AccessClass == RequiredAccessClass.Ordinary && location.SpecialReason is not null)
                errors.Add($"Ordinary location '{location.Id}' cannot carry a special-access reason.");
            if (location.Groups.IsDefaultOrEmpty) errors.Add($"Location '{location.Id}' requires nonempty access groups.");
            if (location.Groups.IsDefault) continue;
            foreach (var duplicate in location.Groups.GroupBy(g => g.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                errors.Add($"Location '{location.Id}' has duplicate group '{duplicate.Key}'.");
            foreach (var group in location.Groups.OrderBy(g => g.Id, StringComparer.Ordinal))
            {
                var prefix = $"Location '{location.Id}' group '{group.Id}'";
                if (string.IsNullOrWhiteSpace(group.Id) || !Enum.IsDefined(group.Coverage)) errors.Add(prefix + " has invalid identity or coverage.");
                if (group.Positions.IsDefaultOrEmpty) { errors.Add(prefix + " requires nonempty positions."); continue; }
                if (group.Positions.Any(i => i < 0 || i >= (long)width * height)) errors.Add(prefix + " has positions outside the Board.");
                if (group.Positions.Distinct().Count() != group.Positions.Length) errors.Add(prefix + " has duplicate positions.");
                if (group.Preparation.IsDefault) { errors.Add(prefix + " has uninitialized preparation metadata."); continue; }
                if (group.Preparation.Select(p => p.Position).Distinct().Count() != group.Preparation.Length)
                    errors.Add(prefix + " has duplicate preparation positions.");
                foreach (var preparation in group.Preparation)
                {
                    if (!group.Positions.Contains(preparation.Position)) errors.Add(prefix + " preparation does not match an access position.");
                    if (preparation.Cells.IsDefault) { errors.Add(prefix + " has uninitialized support Cells."); continue; }
                    if (preparation.Cells.Any(i => i < 0 || i >= (long)width * height)) errors.Add(prefix + " has support Cells outside the Board.");
                    if (preparation.Cells.Contains(preparation.Position)) errors.Add(prefix + " support must exclude the access position itself.");
                    if (preparation.Cells.Distinct().Count() != preparation.Cells.Length) errors.Add(prefix + " has duplicate support Cells.");
                }
            }
        }
        Errors = errors.Order(StringComparer.Ordinal).ToImmutableArray();
    }
    internal static RequiredLocationRegistry Create(IEnumerable<RequiredLocation> locations, int width, int height) => new(locations, width, height);
    internal RequiredLocationRegistry Merge(IEnumerable<RequiredLocation> proposed) => new(Locations.Concat(proposed), width, height);
}

internal static class RequiredLocations
{
    internal static ImmutableArray<RequiredLocation> ForSite(SiteInfo site, string producerId, Board board)
    {
        var geometry = AccessibilityAnalysis.Capture(board);
        return Site(site, producerId, board.Width, i => i >= 0 && i < board.Width * board.Height &&
            geometry.CanStop(ConstructionCapabilities.OrdinaryLand, new(i % board.Width, i / board.Width)));
    }
    internal static ImmutableArray<RequiredLocation> ForSite(SiteInfo site, string producerId, IPlanningBoardView board) =>
        Site(site, producerId, board.Width, i => i >= 0 && i < board.Width * board.Height && board.Passable(i));

    private static ImmutableArray<RequiredLocation> Site(SiteInfo site, string producer, int width, Func<int, bool> usable)
    {
        var result = new List<RequiredLocation>();
        void Add(string source, string role, IEnumerable<int> cells) => result.Add(Ordinary(producer, source, role,
            RequiredAccessCoverage.All, cells));
        foreach (var portal in site.Entrances.OrderBy(p => p.Id, StringComparer.Ordinal))
            Add(portal.Id, "entrance", [portal.InsideIndex, portal.OutsideIndex]);
        foreach (var building in site.Buildings.OrderBy(b => b.Id, StringComparer.Ordinal))
            foreach (var room in building.Rooms.OrderBy(r => r.Id, StringComparer.Ordinal))
                Add(room.Id, "room", room.Usable.Indices(width));
        if (site.MountainDungeon is { } dungeon)
        {
            Add(site.Id, "access-root", [dungeon.AccessRootIndex]);
            foreach (var room in dungeon.Rooms.OrderBy(r => r.Id, StringComparer.Ordinal)) Add(room.Id, "room", room.Usable.Indices(width));
            if (dungeon.Corridors.Area > 0) Add(dungeon.Corridors.Id, "corridors", dungeon.Corridors.Indices(width));
        }
        if (site.SettlementLayout is { } settlement)
        {
            foreach (var space in settlement.PublicSpaces.OrderBy(s => s.Id, StringComparer.Ordinal))
                Add(space.Id, "public-space", space.Area.Indices(width).Where(usable));
            Add(settlement.Access.Id, "access", settlement.Access.Indices(width).Where(usable));
        }
        if (site.Kind == GeneratedSiteKind.Campsite)
            Add(site.Id, "usable", site.Footprint.Indices(width).Concat(site.Exterior.Indices(width)).Where(usable));
        if (site.Kind is GeneratedSiteKind.Ruin or GeneratedSiteKind.Landmark)
            Add(site.Id, "approach", site.Exterior.Indices(width));
        if (site.Outpost is { } outpost)
        {
            Add(outpost.Approach.Id, "approach", outpost.Approach.Indices(width));
            Add(outpost.Courtyard.Id, "courtyard", outpost.Courtyard.Indices(width));
            foreach (var portal in site.Buildings.SelectMany(building => building.Portals))
                Add(portal.Id, "building-entrance", [portal.InsideIndex, portal.OutsideIndex]);
        }
        return result.OrderBy(l => l.Id, StringComparer.Ordinal).ToImmutableArray();
    }

    internal static ImmutableArray<RequiredLocation> ForInteractives(ScenarioDefinition definition, string producerId, Board board, Board? currentBoard = null) =>
        Interactives(producerId, board, definition.Machines ?? [], definition.Resources ?? [], definition.LootMarkers ?? [], definition.GroundObjects ?? [], currentBoard);
    internal static ImmutableArray<RequiredLocation> ForInteractives(GameState state, string producerId) =>
        Interactives(producerId, state.Physical.Board, state.Physical.Board.Machines, state.Resources, state.LootMarkers, state.GroundObjects);
    private static ImmutableArray<RequiredLocation> Interactives(string producer, Board board, IEnumerable<Machine> machines,
        IEnumerable<Resource> resources, IEnumerable<LootMarker> loot, IEnumerable<GroundObject> objects, Board? currentBoard = null)
    {
        int Index(Cell cell) => cell.X < 0 || cell.Y < 0 || cell.X >= board.Width || cell.Y >= board.Height
            ? -1 : cell.Y * board.Width + cell.X;
        RequiredLocation MachineLocation(Machine machine)
        {
            var positions = MachineGeometry.PotentialOperatingPositions(board, machine);
            var location = Ordinary(producer, machine.Id, "machine", RequiredAccessCoverage.Any, positions.Select(Index));
            if (currentBoard is null) return location;
            var support = positions.Select(position => new RequiredAccessPreparation(Index(position),
                MachineSupport(currentBoard, board, machine, position))).Where(p => !p.Cells.IsEmpty).ToImmutableArray();
            return location with { Groups = [location.Groups[0] with { Preparation = support }] };
        }
        return machines.Select(MachineLocation)
            // Resources have no physical Instance ID. This key is only a diagnostic authoring identity.
            .Concat(resources.Select(r => Ordinary(producer, $"resource/{r.Type.Id}/{r.Position.X}/{r.Position.Y}", "resource", RequiredAccessCoverage.All, [Index(r.Position)])))
            .Concat(loot.Select(l => Ordinary(producer, l.Id, "loot", RequiredAccessCoverage.All, [Index(l.Position)])))
            .Concat(objects.Select(o => Ordinary(producer, o.Object.Id, "ground-object", RequiredAccessCoverage.All, [Index(o.Position)])))
            .OrderBy(l => l.Id, StringComparer.Ordinal).ToImmutableArray();
    }

    // Adjacent operation can depend on the two diagonal flank Cells. Ask the Engine
    // about each small partial board; this authoring adapter does not reproduce LOS rules.
    private static ImmutableArray<int> MachineSupport(Board current, Board maximal, Machine machine, Cell position)
    {
        var diagonal = position.X != machine.Position.X && position.Y != machine.Position.Y;
        if (!diagonal) return [];
        var candidates = new[] { new Cell(machine.Position.X, position.Y), new Cell(position.X, machine.Position.Y) }
            .Where(c => current.TerrainDefinitionAt(c).Id != maximal.TerrainDefinitionAt(c).Id)
            .OrderBy(c => c.Y * current.Width + c.X).ToArray();
        var maximum = maximal.Terrain.ToDictionary(t => t.Position);
        var subsets = Enumerable.Range(0, 1 << candidates.Length).OrderBy(mask => System.Numerics.BitOperations.PopCount((uint)mask)).ThenBy(mask => mask);
        foreach (var mask in subsets)
        {
            var changed = candidates.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            var replace = changed.Append(position).ToHashSet();
            var partial = current with { Terrain = current.Terrain.Select(tile => replace.Contains(tile.Position) ? maximum[tile.Position] : tile).ToList() };
            if (MachineGeometry.PotentialOperatingPositions(partial, machine).Contains(position))
                return changed.Select(c => c.Y * current.Width + c.X).ToImmutableArray();
        }
        throw new ArgumentException("Maximal Machine access has no compatible local preparation witness.");
    }
    private static RequiredLocation Ordinary(string producer, string source, string role, RequiredAccessCoverage coverage, IEnumerable<int> positions) =>
        new($"{producer}/{role}/{source}", producer, source, RequiredAccessClass.Ordinary, null,
            [new(role, coverage, positions.Distinct().Order().ToImmutableArray())]);
}
