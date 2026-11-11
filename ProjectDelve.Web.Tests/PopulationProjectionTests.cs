using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class PopulationProjectionTests
{
    [Fact]
    public void GeneratedEncountersRemainHiddenInSideObservationAndBrowserPresentation()
    {
        var generated = WorldGenerator.Generate(new WorldGenerationRecipe(new(Width: 40, Height: 35), new(0, 0, 0), 123,
            Civilization: new(Campsites: ContentDensity.Medium, Settlements: ContentDensity.None), Population: new(PopulationDensity.High)));
        Assert.NotEmpty(generated.Definition.Units);
        var initial = GameEngine.CreateGame(generated.Definition);
        var occupied = initial.Units.SelectMany(u => FootprintGeometry.OccupiedCells(initial, u.Id)).ToHashSet();
        var position = Enumerable.Range(0, 40 * 35).Select(i => new Cell(i % 40, i / 40))
            .Where(c => !occupied.Contains(c) && initial.Physical.Board.TerrainDefinitionAt(c).Passable)
            .OrderByDescending(c => occupied.Min(u => Math.Abs(u.X - c.X) + Math.Abs(u.Y - c.Y))).First();
        var definition = generated.Definition with
        {
            UnitTypeIds = [.. generated.Definition.UnitTypeIds, UnitTypeIds.Barbarian],
            Units = [.. generated.Definition.Units, new(UnitTypeIds.Barbarian, HeroesVsMonsters.Heroes, position, UnitId: "test-hero")],
            Agency = [.. generated.Definition.Agency, new(UnitTypeIds.Barbarian, HeroesVsMonsters.Heroes, ControllerKind.Human)],
            Objectives = [new(HeroesVsMonsters.Heroes, ScenarioObjective.NoObjective), new(HeroesVsMonsters.Monsters, ScenarioObjective.NoObjective)],
            FogOfWarSides = [HeroesVsMonsters.Heroes]
        };
        var state = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
        var visible = FogOfWar.Observe(state, HeroesVsMonsters.Heroes).Units.Select(u => u.Id).ToHashSet();
        var hidden = state.Units.Where(u => !visible.Contains(u.Id)).ToArray();
        Assert.NotEmpty(hidden);
        var presentation = JsonSerializer.Serialize(BrowserProjection.Observe(new EngineResult(state, [], null), HeroesVsMonsters.Heroes));
        foreach (var unit in hidden) Assert.DoesNotContain(unit.Id, presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("encounters", presentation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"population\":", JsonSerializer.Serialize(state), StringComparison.OrdinalIgnoreCase);
        Assert.All(state.Units.Where(u => u.SideId == HeroesVsMonsters.Monsters), u => Assert.True(u.Dormant));
    }
}
