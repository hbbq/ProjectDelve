using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LevelMovementTests
{
    private static UnitType Runner()
    {
        var claw = new LocalAbilityDefinition("claw", "Claw", AbilityTiming.Action, UnitAuthoring.ClawAttack());
        return new("runner", 3, 1, 2, 1, 1)
        {
            TerrainRestrictedMovement = new(TerrainKind.Grass),
            AttackDefinitionOrder = [UnitAction.NormalAttack, UnitAction.ClawAttack], LocalAbilities = [claw],
            AdditionalLevels =
            [
                new(3, 1, 2, 1, 1)
                {
                    TerrainRestrictedMovement = new(TerrainKind.Asphalt),
                    AttackDefinitionOrder = [UnitAction.ClawAttack, UnitAction.NormalAttack], LocalAbilities = [claw]
                },
                new(3, 1, 2, 1, 1) { TerrainRestriction = new([TerrainCategories.RacingGround]) }
            ]
        };
    }

    private static ScenarioDefinition Scene(UnitType type) => new()
    {
        Board = Scenario.Map(4, 2, [new(new(1, 0), TerrainKind.Asphalt), new(new(2, 0), TerrainKind.Asphalt)],
            defaultTerrain: TerrainKind.Grass),
        UnitTypes = [type], UnitTypeIds = [type.Id],
        Units = [new(type.Id, "blue", new(0, 0), UnitId: "grass"), new(type.Id, "blue", new(2, 0), UnitId: "track") { Level = 2 }],
        Agency = [new(type.Id, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
    };

    [Fact]
    public void PlacementAndMovementUseEachUnitsOwnTerrainRestriction()
    {
        var scene = Scene(Runner());
        var state = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scene)));
        Assert.DoesNotContain(new Cell(1, 0), MovementRules.FindPaths(state, "grass", new(0, 0)).Keys);
        Assert.Contains(new Cell(1, 0), MovementRules.FindPaths(state, "track", new(2, 0)).Keys);
        Assert.DoesNotContain(new Cell(2, 1), MovementRules.FindPaths(state, "track", new(2, 0)).Keys);
        scene.Units[1] = scene.Units[1] with { Anchor = new(2, 1) };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scene));
        // An existing figure may occupy a legal off-track Cell (as after displacement)
        // and enter its newly permitted terrain on its next ordinary Move.
        var upgraded = GameEngine.UpgradeUnit(state, "grass");
        Assert.Contains(new Cell(1, 0), MovementRules.FindPaths(upgraded, "grass", new(0, 0)).Keys);
        Assert.DoesNotContain(new Cell(0, 1), MovementRules.FindPaths(upgraded, "grass", new(0, 0)).Keys);
        var categorical = GameEngine.UpgradeUnit(upgraded, "grass");
        Assert.Null(categorical.UnitConfiguration("grass").TerrainRestrictedMovement);
        Assert.Contains(new Cell(1, 0), MovementRules.FindPaths(categorical, "grass", new(0, 0)).Keys);
    }

    [Fact]
    public void AttackRankingAndPackageDependenciesFollowLevelDeclarations()
    {
        var type = Runner();
        var state = GameEngine.CreateGame(Scene(type));
        var queries = new GameplayQueries(state);
        var claw = new Candidate("claw", Action: UnitAction.ClawAttack);
        Assert.Equal(1, queries.AttackDefinitionOrder("grass", claw));
        Assert.Equal(0, queries.AttackDefinitionOrder("track", claw));
        Assert.Empty(type.AtLevel(3).AttackDefinitionOrder);
        Assert.Contains(ContentDefinitions.Dependencies(type), d => d.Target == new ContentIdentity(ContentFamily.Terrain, "asphalt") &&
            d.Path == "additionalLevels[0].terrainRestrictedMovement.terrain");
        var invalid = type with { AdditionalLevels = [type.AdditionalLevels[0] with { TerrainRestriction = new([TerrainCategories.Land]) }] };
        Assert.Equal("additionalLevels[0].terrainRestriction", Assert.Single(ContentDefinitions.Validate(new(invalid))).Path);
        invalid = type with { AdditionalLevels = [type.AdditionalLevels[0] with { AttackDefinitionOrder = [UnitAction.Operate] }] };
        Assert.Equal("additionalLevels[0].attackDefinitionOrder", Assert.Single(ContentDefinitions.Validate(new(invalid))).Path);
    }
}
