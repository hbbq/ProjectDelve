using System.Globalization;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

// Temporary authoring setup only; the generated world and its recipe remain untouched.
public static class WorldQuickPlay
{
    public sealed record Preparation(ScenarioDefinition Definition, PopulationAccessibilityAudit Accessibility);
    private static readonly string[] HeroIds = [UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Wizard, UnitTypeIds.Cleric, UnitTypeIds.Beastmaster];

    public static ScenarioDefinition Prepare(ScenarioDefinition original, ContentBundle canonical, IReadOnlyList<string>? heroTypeIds = null)
        => PrepareCore(original, canonical, null, heroTypeIds).Definition;

    public static Preparation PrepareWithAccessibility(ScenarioDefinition original, ContentBundle canonical)
        => PrepareCore(original, canonical, null);

    // Only the HTTP handler passes its just-parsed, unmodified materialization.
    // The prepared version still crosses final authoritative validation below.
    internal static ScenarioDefinition PrepareValidated(ScenarioMaterialization source, ContentBundle canonical)
        => PrepareCore(source.Definition, canonical, source.State.DetachedCopy()).Definition;

    internal static Preparation PrepareValidatedWithAccessibility(ScenarioMaterialization source, ContentBundle canonical, IReadOnlyList<string>? heroTypeIds = null)
        => PrepareCore(source.Definition, canonical, source.State.DetachedCopy(), heroTypeIds);

    private static Preparation PrepareCore(ScenarioDefinition original, ContentBundle canonical, GameState? materialized, IReadOnlyList<string>? requested = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(canonical);
        var json = ContentPackageJson.CreateOptions();
        T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, json), json)!;
        var definition = Detach(original);
        var heroIds = requested?.ToArray() ?? HeroIds;
        if (heroIds.Length == 0 || heroIds.Distinct(StringComparer.Ordinal).Count() != heroIds.Length || heroIds.Any(id => !HeroIds.Contains(id)))
            throw new ArgumentException("Quick Play requires a nonempty selection of distinct canonical Heroes.", nameof(requested));
        var heroes = heroIds.Select(id => canonical.Units.SingleOrDefault(t => t.Id == id)
            ?? throw new ArgumentException($"Quick Play requires canonical Hero '{id}'.", nameof(canonical))).ToArray();
        var existing = materialized ?? GameEngine.CreateGame(definition, canonical);
        if (definition.Setup is { } setup)
        {
            var validation = PlayReadyScenarioSetup.Validate(definition, existing);
            if (!validation.Valid)
                throw new ArgumentException("PlayReady setup is invalid. Revalidate or repair it in Designer: " +
                    string.Join("; ", validation.Diagnostics.Select(d => d.Path + ": " + d.Message +
                        (d.LocationId is null ? "" : $" Location '{d.LocationId}', group '{d.GroupId}'."))), nameof(original));
            if (heroes.Length > setup.Deployment.Capacity)
                throw new ArgumentException($"PlayReady deployment capacity is {setup.Deployment.Capacity}; the requested party has {heroes.Length} Heroes.", nameof(original));
        }
        foreach (var hero in heroes)
        {
            var local = definition.UnitTypes?.SingleOrDefault(t => t.Id == hero.Id);
            var resolved = existing.Types.SingleOrDefault(t => t.Id == hero.Id);
            if (new[] { local, resolved }.OfType<UnitType>().Any(t =>
                    JsonSerializer.Serialize(t, json) != JsonSerializer.Serialize(hero, json)))
                throw new ArgumentException($"Quick Play cannot replace the existing definition of '{hero.Id}' with its canonical Hero definition.", nameof(original));
        }

        var reserved = existing.Units.Select(u => u.Id).Concat(existing.Physical.Board.Machines.Select(m => m.Id))
            .Concat(definition.Units.Select(u => u.GroupId).OfType<string>())
            .Concat(definition.Agency.Select(a => a.GroupId).OfType<string>())
            .Concat((definition.Orders ?? []).Select(o => o.GroupId).OfType<string>()).ToHashSet(StringComparer.Ordinal);
        string Reserve(string stem)
        {
            var id = stem;
            for (var suffix = 2; !reserved.Add(id); suffix++) id = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            return id;
        }
        var groupId = Reserve("quick-play-heroes");
        var temporaryLoot = definition.Loot ?? new LootSettings();
        definition = definition with
        {
            UnitTypeIds = definition.UnitTypeIds.Concat(heroIds).Distinct(StringComparer.Ordinal).ToList(),
            UnitTypes = (definition.UnitTypes ?? []).Concat(Detach(heroes).Where(h => !(definition.UnitTypes ?? []).Any(t => t.Id == h.Id))).ToList(),
            Agency = [.. definition.Agency, .. heroIds.Select(id => new AgencyAssignment(id, HeroesVsMonsters.Heroes, ControllerKind.Human, groupId))],
            Loot = temporaryLoot,
            FogOfWarSides = (definition.FogOfWarSides ?? []).Append(HeroesVsMonsters.Heroes).Distinct(StringComparer.Ordinal).ToList(),
            Objectives = definition.Units.Select(u => u.SideId).Concat(definition.Agency.Select(a => a.SideId))
                .Append(HeroesVsMonsters.Heroes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(side => new SideObjective(side, ScenarioObjective.NoObjective)).ToList()
        };

        // Resolve final terrain once, including scenario-local assemblies, and reserve complete
        // existing footprints. A compact open cluster is policy, not a new movement rule.
        // Reuse the detached, validated source for geometry/placement preflight.
        // Temporary deck/agency/FoW configuration is validated once with the
        // complete party below, rather than rebuilding the dense world midway.
        var state = existing;
        foreach (var hero in heroes) if (state.Types.All(t => t.Id != hero.Id)) state.Types.Add(hero);
        var board = state.Physical.Board;
        var occupied = state.Physical.Figures.SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id))
            .Concat(board.Machines.Select(m => m.Position)).ToHashSet();
        var tiles = board.Terrain.ToDictionary(t => t.Position);
        var playable = original.Setup is null ? PlayableWorldComponent.Select(board) :
            PlayReadyScenarioSetup.ReachableFromDeployment(original, existing).ToHashSet();
        HashSet<Cell> free = [];
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            if (playable.Contains(y * board.Width + x) && !occupied.Contains(cell) && board.TerrainDefinition(tiles.GetValueOrDefault(cell)).Passable) free.Add(cell);
        }
        var blocked = board.Edges.Where(e => !e.Kind.Passable()).Select(e => Key(e.A, e.B)).ToHashSet();
        List<Cell>? cluster = null;
        if (original.Setup is { } declared)
            cluster = declared.Deployment.PlacementOrder.Take(heroIds.Length).ToList();
        else foreach (var anchor in free.OrderBy(c => Math.Min(Math.Min(c.X, board.Width - 1 - c.X), Math.Min(c.Y, board.Height - 1 - c.Y)))
                     .ThenBy(c => c.Y).ThenBy(c => c.X))
        {
            List<Cell> found = [anchor];
            Queue<(Cell Cell, int Depth)> queue = new([(anchor, 0)]);
            HashSet<Cell> seen = [anchor];
            while (queue.TryDequeue(out var current) && found.Count < heroIds.Length)
            {
                if (current.Depth == heroIds.Length - 1) continue;
                foreach (var next in Neighbors(current.Cell))
                {
                    if (!free.Contains(next) || blocked.Contains(Key(current.Cell, next)) || !seen.Add(next)) continue;
                    found.Add(next);
                    queue.Enqueue((next, current.Depth + 1));
                    if (found.Count == heroIds.Length) break;
                }
            }
            if (found.Count == heroIds.Length) { cluster = found; break; }
        }
        if (cluster is null)
            throw new ArgumentException("Quick Play needs five free passable Cells connected by open Edges in a compact starting cluster in the principal doors-openable world component. No compatible Hero start exists there; try another world window.", nameof(original));

        List<string> partyIds = [];
        for (var i = 0; i < heroIds.Length; i++)
        {
            var id = Reserve("quick-play-" + heroIds[i]);
            partyIds.Add(id);
            state.PlaceUnit(heroIds[i], id, HeroesVsMonsters.Heroes, cluster[i], groupId: groupId);
            definition.Units.Add(new(heroIds[i], HeroesVsMonsters.Heroes, cluster[i], UnitId: id, GroupId: groupId));
        }
        var prepared = GameEngine.CreateGame(definition, canonical);
        return new(definition, WorldAccessibilityAudit.Units(prepared, partyIds, playable));
    }

    private static (Cell, Cell) Key(Cell a, Cell b) => a.Y < b.Y || a.Y == b.Y && a.X < b.X ? (a, b) : (b, a);
    private static IEnumerable<Cell> Neighbors(Cell c) => [new(c.X, c.Y - 1), new(c.X - 1, c.Y), new(c.X + 1, c.Y), new(c.X, c.Y + 1)];
}
