using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DungeonPopulationTests
{
    private static WorldGenerationContext Context(ulong seed)
    {
        var context = new WorldGenerationContext(new(seed, new(0, 0, 0)), new(Width: 34, Height: 28));
        context.SampleNaturalTerrain(); context.Loot = new() { Preset = "All" };
        var definition = context.Materialize() with { Board = new() { Width = 34, Height = 28,
            Cells = Enumerable.Range(4, 20).SelectMany(y => Enumerable.Range(4, 24).Select(x => new CellDefinition(new(x, y), TerrainKind.SolidRock))).ToList() } };
        var board = new PlanningBoard(definition, ContentBundle.Canonical()); context.ConstructionBoard = board;
        var draft = board.BeginDraft(new(0, 0, 34, 28), "test/dungeon", new(128, 1_000_000));
        var plan = Assert.IsType<Planned<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", draft.AllowedArea, new()), new(seed, "test", draft))).Plan;
        Assert.True(draft.Accept(plan.Patch, out var error), error); Assert.True(board.TryCommit(draft.BuildPatch(), out error), error);
        context.Sites.Add(plan.Site); context.RefreshConstructionAnalysis();
        DungeonSitePopulation.PublishIntents(context); DungeonSitePopulation.FinalizeGeometryAndLoot(context, board.Content);
        return context;
    }

    [Fact]
    public void OptionalTreasureSurvivesPopulationOffAndCurrentFormatRoundTrip()
    {
        var sawLoot = false; var sawQuiet = false;
        for (ulong seed = 0; seed < 12; seed++)
        {
            var context = Context(seed);
            Assert.Null(context.Population);
            var original = context.Materialize(); Assert.Empty(original.Units);
            sawLoot |= original.LootMarkers?.Count > 0; sawQuiet |= context.Sites[0].MountainDungeon!.TreasureAreas.Count == 0;
            var off = PopulationGenerator.Generate(context, new(PopulationDensity.Off));
            Assert.Empty(off.Encounters); Assert.Empty(off.LootMarkers);
            context.Population = off;
            Assert.Equal(original.LootMarkers ?? [], context.Materialize().LootMarkers ?? []);
            var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(original));
            Assert.Equal(ScenarioDefinitionJson.ToJson(original), ScenarioDefinitionJson.ToJson(restored));
            var game = GameEngine.CreateGame(restored);
            Assert.Equal(JsonSerializer.Serialize(game), JsonSerializer.Serialize(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(game))));
        }
        Assert.True(sawLoot); Assert.True(sawQuiet);
    }

    [Fact]
    public void RegionsStayIndependentAcrossWallsAndKeepRequiredAccessAndFullFootprintsClear()
    {
        var occupied = false; var empty = false; HashSet<string> kinds = [];
        for (ulong seed = 0; seed < 16; seed++)
        {
            var context = Context(seed); var site = context.Sites[0]; var width = context.Window.Width;
            var snapshot = PopulationGenerator.Generate(context, new(PopulationDensity.High, 5));
            var encounters = snapshot.Encounters.Where(e => e.Kind == "DungeonEncounter").ToArray();
            occupied |= encounters.Length > 0; empty |= encounters.Length == 0;
            var access = site.MountainDungeon!.RequiredAccess.Indices(width).ToHashSet();
            access.UnionWith(site.Entrances.SelectMany(e => new[] { e.InsideIndex, e.OutsideIndex }));
            var types = context.ConstructionBoard!.Content.Units.ToDictionary(t => t.Id);
            Assert.DoesNotContain(snapshot.Encounters.SelectMany(e => e.Units).SelectMany(u => FootprintGeometry.OccupiedCells(types[u.UnitTypeId].Footprint, u.Anchor)),
                c => access.Contains(c.Y * width + c.X));
            foreach (var encounter in encounters)
            {
                var region = site.PopulationIntent!.EncounterRegions.Single(r => r.Id == encounter.SourceId);
                var memberIds = encounter.Units.Select(u => u.UnitId).ToHashSet();
                var alert = Assert.Single(snapshot.AlertComponents, c => c.UnitIds.Any(memberIds.Contains));
                Assert.All(alert.UnitIds, id => Assert.Contains(id, memberIds));
                foreach (var unit in encounter.Units)
                {
                    kinds.Add(unit.UnitTypeId);
                    foreach (var cell in FootprintGeometry.OccupiedCells(types[unit.UnitTypeId].Footprint, unit.Anchor))
                    { Assert.DoesNotContain(cell.Y * width + cell.X, access); Assert.True(region.Area.Contains(cell.Y * width + cell.X, width)); }
                }
            }
            context.Population = snapshot; _ = GameEngine.CreateGame(context.Materialize());
        }
        Assert.True(occupied); Assert.True(empty); Assert.True(kinds.Count >= 3);
    }

    [Fact]
    public void FinalGeometryValidationRejectsLostWorldEntrance()
    {
        var context = Context(1); var board = context.ConstructionBoard!; var site = context.Sites[0];
        // A separate board deliberately bypasses construction reservations to model a bad later stage.
        var definition = board.Export(); var outside = site.Entrances[0].OutsideIndex;
        context.ConstructionBoard = new PlanningBoard(definition with { Board = definition.Board with {
            Cells = definition.Board.Cells.Where(c => c.Position.Y * board.Width + c.Position.X != outside)
                .Append(new(new(outside % board.Width, outside / board.Width), TerrainKind.SolidRock)).ToList() } }, board.Content);
        Assert.Contains("disconnected", Assert.Throws<ArgumentException>(() => DungeonSitePopulation.FinalizeGeometryAndLoot(context, board.Content)).Message);
    }
}
