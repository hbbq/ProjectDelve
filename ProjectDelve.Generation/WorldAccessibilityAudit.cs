using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record AccessibilityRegion(int Id, Cell Root, int StoppingPositions);
public sealed record AccessibilityLocation(string Id, string Kind, Cell Position, bool InPrincipalRegion,
    AccessibilityStatus OpenLandAccess, int LandRegion, int WaterRegion);
public sealed record GeometryAccessibilityAudit(long Revision, Cell? PrincipalRoot, int PrincipalArea,
    IReadOnlyList<AccessibilityRegion> LandRegions, IReadOnlyList<AccessibilityRegion> WaterRegions,
    IReadOnlyList<AccessibilityLocation> Locations, bool Complete, int WorkUsed);
public sealed record UnitAccessibilityAudit(string UnitId, string? EncounterId, string? EncounterKind, Cell Start,
    AccessibilityProfile Profile, int StoppingPositions, int ConditionalStoppingPositions,
    AccessibilityStatus PrincipalAccess, bool InPrincipalRegion, bool RoomBoundLargeGuard,
    IReadOnlyList<AccessibilityDoor> Doors, bool Complete, int WorkUsed)
{
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public int DoorDetailsOmitted { get; init; }
}
public sealed record PopulationAccessibilityAudit(IReadOnlyList<UnitAccessibilityAudit> Units, bool Complete, int WorkUsed);

/// <summary>Advisory authoring facts. No RNG, acceptance policy, placement or scenario writes.</summary>
public static class WorldAccessibilityAudit
{
    public const int MaximumWork = 2_000_000;
    public const int MaximumLocations = 1024;
    public const int MaximumUnits = 256;
    public const int MaximumDoorsPerUnit = 128;

    internal static GeometryAccessibilityAudit Geometry(WorldGenerationContext context, ContentBundle content)
    {
        var state = GameEngine.CreateGame(context.Materialize(), content);
        var board = state.Physical.Board;
        var analysis = AccessibilityAnalysis.Capture(board, context.Revision);
        var principal = Principal(context, board);
        var rootIndex = PrincipalRoot(context, principal);
        Cell? root = rootIndex < 0 ? null : new(rootIndex % board.Width, rootIndex / board.Width);
        var remaining = MaximumWork;
        var land = Regions(new(Footprint.OneByOne, [MovementKinds.Land], 1));
        var water = Regions(new(Footprint.OneByOne, [MovementKinds.Water], 1));
        var rootRegion = rootIndex < 0 ? -1 : land.Labels[rootIndex];
        var points = context.Sites.OrderBy(s => s.Id, StringComparer.Ordinal).SelectMany(s =>
            s.AccessPorts.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => (p.Id, Kind: "SitePort", Index: p.AnchorIndex))
                .Concat(s.Entrances.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => (e.Id, Kind: "Entrance", Index: e.InsideIndex)))
                .Concat((s.MountainDungeon?.Rooms ?? []).OrderBy(r => r.Id, StringComparer.Ordinal)
                    .Select(r => (r.Id, Kind: "DungeonRoom", Index: r.Usable.Indices(board.Width).First())))).ToArray();
        var locations = points.Take(MaximumLocations).Select(p => new AccessibilityLocation(p.Id, p.Kind,
            new(p.Index % board.Width, p.Index / board.Width), principal.Contains(p.Index),
            rootRegion >= 0 && land.Labels[p.Index] == rootRegion ? AccessibilityStatus.Reachable :
                land.Complete ? AccessibilityStatus.Disconnected : AccessibilityStatus.Unknown,
            land.Labels[p.Index], water.Labels[p.Index])).ToArray();
        return new(context.Revision, root, principal.Count, land.Regions, water.Regions, locations,
            land.Complete && water.Complete && points.Length <= MaximumLocations, MaximumWork - remaining);

        (int[] Labels, IReadOnlyList<AccessibilityRegion> Regions, bool Complete) Regions(AccessibilityProfile profile)
        {
            var labels = Enumerable.Repeat(-1, board.Width * board.Height).ToArray();
            List<AccessibilityRegion> regions = [];
            var complete = true;
            for (var i = 0; i < labels.Length; i++)
            {
                if (remaining == 0) { complete = false; break; }
                remaining--;
                var cell = new Cell(i % board.Width, i / board.Width);
                if (labels[i] >= 0 || !analysis.CanStop(profile, cell)) continue;
                var result = analysis.Analyze(profile, cell, new(WorkBudget: remaining));
                remaining -= result.WorkUsed;
                var id = regions.Count;
                foreach (var position in result.Reachable) labels[position.Y * board.Width + position.X] = id;
                regions.Add(new(id, cell, result.Reachable.Length));
                if (!result.Complete) { complete = false; break; }
            }
            return (labels, regions.ToArray(), complete);
        }
    }

    internal static PopulationAccessibilityAudit Population(WorldGenerationContext context, GameState state)
    {
        var principal = Principal(context, state.Physical.Board);
        var encounters = (context.Population?.Encounters ?? []).SelectMany(e => e.Units
            .Where(u => u.UnitId is not null).Select(u => (Id: u.UnitId!, Encounter: e)))
            .ToDictionary(p => p.Id, p => p.Encounter, StringComparer.Ordinal);
        var roomSources = context.Sites.Where(s => s.MountainDungeon is not null)
            .SelectMany(s => new[] { s.Id }.Concat(s.PopulationIntent?.EncounterRegions.Select(r => r.Id) ?? []))
            .ToHashSet(StringComparer.Ordinal);
        return Units(state, state.Units.Select(u => u.Id), principal, context.Revision,
            id => encounters.GetValueOrDefault(id), e => e is not null && roomSources.Contains(e.SourceId));
    }

    /// <summary>Full-information hypothetical upright potential from the supplied actual starting positions.
    /// Figures are ignored for geographic potential; no cooperative Door operation is assumed.</summary>
    public static PopulationAccessibilityAudit Units(GameState state, IEnumerable<string> unitIds,
        IReadOnlySet<int>? principal = null, long revision = 0,
        Func<string, PopulationEncounter?>? encounterFor = null, Func<PopulationEncounter?, bool>? roomGuard = null)
    {
        var analysis = AccessibilityAnalysis.Capture(state, revision);
        principal ??= PlayableWorldComponent.Select(state.Physical.Board);
        var ids = unitIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var remaining = MaximumWork;
        List<UnitAccessibilityAudit> reports = [];
        foreach (var id in ids.Take(MaximumUnits))
        {
            var start = state.Physical.Figures.Single(f => f.Id == id).Position;
            var profile = analysis.ProfileFor(id);
            var result = analysis.Analyze(id, start, new(AccessibilityMode.ConditionalDoors,
                IgnoreFigures: true, AssumeUpright: true, WorkBudget: Math.Min(100_000, remaining)));
            remaining -= result.WorkUsed;
            var access = result.Reachable.Any(c => principal.Contains(c.Y * analysis.Width + c.X)) ? AccessibilityStatus.Reachable :
                result.Conditional.Any(c => principal.Contains(c.Y * analysis.Width + c.X)) ? AccessibilityStatus.Conditional :
                result.Complete ? AccessibilityStatus.Disconnected : AccessibilityStatus.Unknown;
            var encounter = encounterFor?.Invoke(id);
            reports.Add(new(id, encounter?.Id, encounter?.Kind, start, profile, result.Reachable.Length,
                result.Conditional.Length, access, principal.Contains(start.Y * analysis.Width + start.X),
                profile.Footprint == Footprint.TwoByTwo && roomGuard?.Invoke(encounter) == true,
                result.Doors.Take(MaximumDoorsPerUnit).ToArray(), result.Complete, result.WorkUsed)
            {
                Assumptions = result.Assumptions,
                DoorDetailsOmitted = Math.Max(0, result.Doors.Length - MaximumDoorsPerUnit)
            });
        }
        return new(reports.ToArray(), ids.Length <= MaximumUnits && reports.All(r => r.Complete), MaximumWork - remaining);
    }

    private static int PrincipalRoot(WorldGenerationContext context, HashSet<int> principal) =>
        context.Sites.Where(s => s.MountainDungeon is not null).OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => s.MountainDungeon!.AccessRootIndex).DefaultIfEmpty(principal.Count == 0 ? -1 : principal.Min()).First();

    private static HashSet<int> Principal(WorldGenerationContext context, Board board)
    {
        var dungeon = context.Sites.Where(s => s.MountainDungeon is not null).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        return dungeon is null || context.ConstructionBoard is null ? PlayableWorldComponent.Select(board) :
            PlayableWorldComponent.FromRoot(context.ConstructionBoard, dungeon.MountainDungeon!.AccessRootIndex);
    }
}
