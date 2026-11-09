using System.Globalization;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

// Temporary authoring setup only; the generated world and its recipe remain untouched.
public static class WorldQuickPlay
{
    private static readonly string[] HeroIds = [UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Wizard, UnitTypeIds.Cleric, UnitTypeIds.Beastmaster];

    public static ScenarioDefinition Prepare(ScenarioDefinition original, ContentBundle canonical)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(canonical);
        var json = ContentPackageJson.CreateOptions();
        T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, json), json)!;
        var definition = Detach(original);
        var heroes = HeroIds.Select(id => canonical.Units.SingleOrDefault(t => t.Id == id)
            ?? throw new ArgumentException($"Quick Play requires canonical Hero '{id}'.", nameof(canonical))).ToArray();
        var existing = GameEngine.CreateGame(definition, canonical);
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
        definition = definition with
        {
            UnitTypeIds = definition.UnitTypeIds.Concat(HeroIds).Distinct(StringComparer.Ordinal).ToList(),
            UnitTypes = (definition.UnitTypes ?? []).Concat(Detach(heroes).Where(h => !(definition.UnitTypes ?? []).Any(t => t.Id == h.Id))).ToList(),
            Agency = [.. definition.Agency, .. HeroIds.Select(id => new AgencyAssignment(id, HeroesVsMonsters.Heroes, ControllerKind.Human, groupId))],
            Loot = new() { Preset = LootPresets.AllPresetId },
            FogOfWarSides = (definition.FogOfWarSides ?? []).Append(HeroesVsMonsters.Heroes).Distinct(StringComparer.Ordinal).ToList(),
            Objectives = definition.Units.Select(u => u.SideId).Concat(definition.Agency.Select(a => a.SideId))
                .Append(HeroesVsMonsters.Heroes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(side => new SideObjective(side, ScenarioObjective.NoObjective)).ToList()
        };

        // Resolve final terrain once, including scenario-local assemblies, and reserve complete
        // existing footprints. A compact open cluster is policy, not a new movement rule.
        var state = GameEngine.CreateGame(definition, canonical);
        var board = state.Physical.Board;
        var occupied = state.Physical.Figures.SelectMany(f => FootprintGeometry.OccupiedCells(state, f.Id))
            .Concat(board.Machines.Select(m => m.Position)).ToHashSet();
        var tiles = board.Terrain.ToDictionary(t => t.Position);
        HashSet<Cell> free = [];
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            if (!occupied.Contains(cell) && board.TerrainDefinition(tiles.GetValueOrDefault(cell)).Passable) free.Add(cell);
        }
        var blocked = board.Edges.Where(e => !e.Kind.Passable()).Select(e => Key(e.A, e.B)).ToHashSet();
        List<Cell>? cluster = null;
        foreach (var anchor in free.OrderBy(c => Math.Min(Math.Min(c.X, board.Width - 1 - c.X), Math.Min(c.Y, board.Height - 1 - c.Y)))
                     .ThenBy(c => c.Y).ThenBy(c => c.X))
        {
            List<Cell> found = [anchor];
            Queue<(Cell Cell, int Depth)> queue = new([(anchor, 0)]);
            HashSet<Cell> seen = [anchor];
            while (queue.TryDequeue(out var current) && found.Count < HeroIds.Length)
            {
                if (current.Depth == HeroIds.Length - 1) continue;
                foreach (var next in Neighbors(current.Cell))
                {
                    if (!free.Contains(next) || blocked.Contains(Key(current.Cell, next)) || !seen.Add(next)) continue;
                    found.Add(next);
                    queue.Enqueue((next, current.Depth + 1));
                    if (found.Count == HeroIds.Length) break;
                }
            }
            if (found.Count == HeroIds.Length) { cluster = found; break; }
        }
        if (cluster is null)
            throw new ArgumentException("Quick Play needs five free passable Cells connected by open Edges in a compact starting cluster for the Heroes. Try another world window.", nameof(original));

        for (var i = 0; i < HeroIds.Length; i++)
        {
            var id = Reserve("quick-play-" + HeroIds[i]);
            state.PlaceUnit(HeroIds[i], id, HeroesVsMonsters.Heroes, cluster[i], groupId: groupId);
            definition.Units.Add(new(HeroIds[i], HeroesVsMonsters.Heroes, cluster[i], UnitId: id, GroupId: groupId));
        }
        _ = GameEngine.CreateGame(definition, canonical);
        return definition;
    }

    private static (Cell, Cell) Key(Cell a, Cell b) => a.Y < b.Y || a.Y == b.Y && a.X < b.X ? (a, b) : (b, a);
    private static IEnumerable<Cell> Neighbors(Cell c) => [new(c.X, c.Y - 1), new(c.X - 1, c.Y), new(c.X + 1, c.Y), new(c.X, c.Y + 1)];
}
