using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioMigrationTests
{
    // Fixed expectations captured from the pre-migration maps, independent of definition helpers.
    public static TheoryData<string, int, int, string, string, string, string, string> Setups => new()
    {
        { "basic-combat",
            8,
            8,
            "barbarian-type,grunt-type",
            "3,2:Tree;3,3:Grass;4,5:StoneFloorWithTable",
            "",
            "barbarian-type:blue@1,4:Upright:5;grunt-type:red@4,4:Upright:1;grunt-type:red@4,3:Upright:1;grunt-type:red@6,6:Upright:1",
            "barbarian-type:blue:Human;grunt-type:red:Automated" },
        { "goblins",
            10,
            10,
            "barbarian-type,rogue-type,grunt-type,goblin-type",
            "4,2:Tree;4,3:Grass;2,6:StoneFloorWithTable;5,7:Water;6,7:Water",
            "",
            "barbarian-type:blue@2,4:Upright:5;rogue-type:blue@2,5:Upright:4;grunt-type:red@5,4:Upright:1;grunt-type:red@6,6:Upright:1;goblin-type:red@4,5:Upright:1;goblin-type:red@7,3:Upright:1",
            "barbarian-type:blue:Human;rogue-type:blue:Human;grunt-type:red:Automated;goblin-type:red:Automated" },
        { "archers",
            12,
            12,
            "barbarian-type,rogue-type,grunt-type,goblin-type,skeleton-archer-type",
            "5,3:Tree;5,4:Tree;7,8:StoneFloorWithTable;8,8:StoneFloorWithTable",
            "6,5>7,5:Wall;6,6>7,6:WallWithWindow;6,7>7,7:Wall",
            "barbarian-type:blue@2,5:Upright:5;rogue-type:blue@2,6:Upright:4;grunt-type:red@5,5:Upright:1;grunt-type:red@7,7:Upright:1;goblin-type:red@5,6:Upright:1;goblin-type:red@8,3:Upright:1;skeleton-archer-type:red@6,2:Upright:1;skeleton-archer-type:red@8,6:Upright:1",
            "barbarian-type:blue:Human;rogue-type:blue:Human;grunt-type:red:Automated;goblin-type:red:Automated;skeleton-archer-type:red:Automated" },
        { "wizard-doors",
            15,
            15,
            "barbarian-type,rogue-type,wizard-type,goblin-type,skeleton-archer-type,zombie-type,ghost-type",
            "5,5:Tree;7,9:StoneFloorWithTable;11,11:Tree",
            "7,1>8,1:Wall;7,2>8,2:Wall;7,3>8,3:ClosedDoor;7,4>8,4:Wall;7,5>8,5:Wall;12,1>13,1:Wall;12,2>13,2:Wall;12,3>13,3:Wall;12,4>13,4:Wall;12,5>13,5:Wall;8,0>8,1:Wall;9,0>9,1:Wall;10,0>10,1:Wall;11,0>11,1:Wall;12,0>12,1:Wall;8,5>8,6:Wall;9,5>9,6:Wall;10,5>10,6:ClosedDoor;11,5>11,6:Wall;12,5>12,6:Wall;1,9>1,10:Wall;2,9>2,10:Wall;3,9>3,10:OpenDoor;4,9>4,10:Wall;5,9>5,10:Wall",
            "barbarian-type:blue@3,6:Upright:5;rogue-type:blue@3,7:Upright:4;wizard-type:blue@2,6:Upright:4;goblin-type:red@6,6:Upright:1;goblin-type:red@7,8:Upright:1;skeleton-archer-type:red@7,4:Upright:1;skeleton-archer-type:red@10,8:Upright:1;zombie-type:red@8,3:Upright:1;zombie-type:red@10,5:Upright:1;ghost-type:red@8,5:Upright:1",
            "barbarian-type:blue:Human;rogue-type:blue:Human;wizard-type:blue:Human;goblin-type:red:Automated;skeleton-archer-type:red:Automated;zombie-type:red:Automated;ghost-type:red:Automated" },
        { "full-party-trolls",
            15,
            15,
            "barbarian-type,rogue-type,wizard-type,cleric-type,goblin-type,skeleton-archer-type,zombie-type,troll-type",
            "5,4:Tree;6,10:StoneFloorWithTable;3,12:Tree",
            "7,1>8,1:Wall;7,2>8,2:Wall;7,3>8,3:ClosedDoor;7,4>8,4:Wall;7,5>8,5:Wall;12,1>13,1:Wall;12,2>13,2:Wall;12,3>13,3:Wall;12,4>13,4:Wall;12,5>13,5:Wall;8,0>8,1:Wall;9,0>9,1:Wall;10,0>10,1:Wall;11,0>11,1:Wall;12,0>12,1:Wall;8,5>8,6:Wall;9,5>9,6:Wall;10,5>10,6:ClosedDoor;11,5>11,6:Wall;12,5>12,6:Wall;8,9>9,9:Wall;8,10>9,10:Wall;8,11>9,11:ClosedDoor;8,12>9,12:Wall;8,13>9,13:Wall;13,9>14,9:Wall;13,10>14,10:Wall;13,11>14,11:Wall;13,12>14,12:Wall;13,13>14,13:Wall;9,8>9,9:Wall;10,8>10,9:Wall;11,8>11,9:Wall;12,8>12,9:Wall;13,8>13,9:Wall;9,13>9,14:Wall;10,13>10,14:Wall;11,13>11,14:ClosedDoor;12,13>12,14:Wall;13,13>13,14:Wall;4,0>5,0:Wall;4,1>5,1:Wall;4,2>5,2:ClosedDoor;4,3>5,3:Wall;0,9>0,10:Wall;1,9>1,10:Wall;2,9>2,10:OpenDoor;3,9>3,10:Wall;4,9>4,10:Wall",
            "barbarian-type:blue@3,6:Upright:5;rogue-type:blue@3,7:Upright:4;wizard-type:blue@2,6:Upright:4;cleric-type:blue@2,7:Upright:4;goblin-type:red@6,6:Upright:1;goblin-type:red@7,7:Upright:1;skeleton-archer-type:red@6,3:Upright:1;skeleton-archer-type:red@8,8:Upright:1;zombie-type:red@8,3:Upright:1;zombie-type:red@10,5:Upright:1;troll-type:red@9,11:Upright:1;troll-type:red@11,9:Upright:1",
            "barbarian-type:blue:Human;rogue-type:blue:Human;wizard-type:blue:Human;cleric-type:blue:Human;goblin-type:red:Automated;skeleton-archer-type:red:Automated;zombie-type:red:Automated;troll-type:red:Automated" },
        { "shaman-hunt",
            15,
            15,
            "barbarian-type,rogue-type,wizard-type,cleric-type,shaman-type,grunt-type,red-dragon-type",
            "6,6:Tree;7,6:Tree;6,7:StoneFloorWithTable;10,10:Tree;3,11:Water;4,11:Water",
            "4,0>5,0:Wall;4,1>5,1:Wall;4,2>5,2:OpenDoor;4,3>5,3:Wall;4,4>5,4:Wall;9,8>10,8:Wall;9,9>10,9:Wall;9,10>10,10:Wall;9,11>10,11:OpenDoor;9,12>10,12:Wall;9,13>10,13:Wall;9,14>10,14:Wall;7,4>7,5:Wall;8,4>8,5:Wall;9,4>9,5:Wall;10,4>10,5:OpenDoor;11,4>11,5:Wall;12,4>12,5:Wall;13,4>13,5:Wall;0,10>0,11:Wall;1,10>1,11:Wall;2,10>2,11:OpenDoor;3,10>3,11:Wall;4,10>4,11:Wall;5,10>5,11:Wall;6,10>6,11:Wall",
            "barbarian-type:blue@2,6:Upright:5;rogue-type:blue@3,7:Upright:4;wizard-type:blue@2,7:Upright:4;cleric-type:blue@1,7:Upright:4;shaman-type:red@7,5:Upright:1;grunt-type:red@5,8:Upright:1;red-dragon-type:red@11,5:Upright:8",
            "barbarian-type:blue:Human;rogue-type:blue:Human;wizard-type:blue:Human;cleric-type:blue:Human;shaman-type:red:Automated;grunt-type:red:Automated;red-dragon-type:red:Automated" },
    };

    [Theory, MemberData(nameof(Setups))]
    public void CompleteCatalogSetupIsPreserved(string id, int width, int height, string types,
        string terrain, string edges, string units, string agency)
    {
        var definition = PlaytestScenarios.Definition(id);
        var state = GameEngine.CreateGame(definition);
        Assert.Equal((width, height), (state.Physical.Board.Width, state.Physical.Board.Height));
        Assert.Equal(types, string.Join(",", state.Types.Select(t => t.Id)));
        Assert.Equal(terrain, string.Join(";", state.Physical.Board.Terrain.OrderBy(t => t.Position.Y).ThenBy(t => t.Position.X)
            .Select(t => $"{t.Position.X},{t.Position.Y}:{t.Kind}")));
        Assert.Equal(edges, string.Join(";", state.Physical.Board.Edges.Select(e => $"{e.A.X},{e.A.Y}>{e.B.X},{e.B.Y}:{e.Kind}")));
        Assert.Equal(units, string.Join(";", state.Units.Select(u => {
            var figure = state.Physical.Figures.Single(f => f.Id == u.Id);
            return $"{u.TypeId}:{u.SideId}@{figure.Position.X},{figure.Position.Y}:{figure.Posture}:{u.CurrentHp}";
        })));
        Assert.Equal(agency, string.Join(";", state.Controllers.Select(a => $"{a.Token.TypeId}:{a.Token.SideId}:{a.Controller}")));
        Assert.Equal(0, state.Round);
        Assert.Empty(state.Bag);
        Assert.Equal(definition.UnitTypeIds, state.Types.Select(t => t.Id));
    }

    [Fact]
    public void CourtyardMapRetainsEveryOriginalEdgeAfterAuthoringCleanup()
    {
        // Original layout, independent of Room/line authoring and insensitive to edge-list order.
        var expected = new[] {
            "1,1>1,2:Wall", "2,1>2,2:Wall", "3,1>3,2:Wall",
            "1,5>1,6:Wall", "2,5>2,6:Wall", "3,5>3,6:Wall",
            "0,2>1,2:Wall", "0,3>1,3:Wall", "0,4>1,4:Wall", "0,5>1,5:Wall",
            "3,2>4,2:Wall", "3,3>4,3:Wall", "3,4>4,4:ClosedDoor", "3,5>4,5:Wall",
            "5,1>6,1:Wall", "5,2>6,2:Wall", "5,3>6,3:WallWithWindow", "5,4>6,4:Wall", "5,5>6,5:Wall",
            "9,0>10,0:Wall", "9,1>10,1:Wall", "9,2>10,2:Wall", "9,3>10,3:Wall", "9,4>10,4:Wall",
            "10,1>10,2:Wall", "11,1>11,2:ClosedDoor", "12,1>12,2:Wall", "13,1>13,2:Wall", "14,1>14,2:Wall",
            "10,2>10,3:Wall", "11,2>11,3:ClosedDoor", "12,2>12,3:Wall", "13,2>13,3:Wall", "14,2>14,3:Wall",
            "10,4>10,5:Wall", "11,4>11,5:ClosedDoor", "12,4>12,5:Wall", "13,4>13,5:Wall", "14,4>14,5:Wall",
            "0,11>0,12:Wall", "1,11>1,12:Wall", "2,11>2,12:Wall", "3,11>3,12:OpenDoor", "4,11>4,12:Wall", "5,11>5,12:Wall"
        };
        var definition = CourtyardFixture.Definition();
        Assert.Equal((15, 15), (definition.Board.Width, definition.Board.Height));
        Assert.Equal(expected.OrderBy(e => e, StringComparer.Ordinal), GameEngine.CreateGame(definition).Physical.Board.Edges
            .Select(e => $"{e.A.X},{e.A.Y}>{e.B.X},{e.B.Y}:{e.Kind}").OrderBy(e => e, StringComparer.Ordinal));
        Assert.Equal(definition.Board.Edges.Count, definition.Board.Edges.Select(e => (e.Position, e.Direction)).Distinct().Count());
    }

    [Fact]
    public void CourtyardPreservesWoundedWizardAndUnplacedGoblinTypeOrder()
    {
        var definition = CourtyardFixture.Definition();
        var state = GameEngine.CreateGame(definition);
        Assert.Equal(new[] { UnitTypeIds.Barbarian, UnitTypeIds.Rogue, UnitTypeIds.Cleric, UnitTypeIds.Wizard,
            UnitTypeIds.Grunt, UnitTypeIds.Zombie, UnitTypeIds.SkeletonArcher, UnitTypeIds.Goblin,
            UnitTypeIds.Shaman, UnitTypeIds.Troll }, state.Types.Select(t => t.Id));
        Assert.DoesNotContain(state.Units, u => u.TypeId == UnitTypeIds.Goblin);
        var wizard = Assert.Single(state.Units, u => u.TypeId == UnitTypeIds.Wizard);
        Assert.Equal(2, wizard.CurrentHp);
        Assert.Equal(new Cell(2, 8), state.Physical.Figures.Single(f => f.Id == wizard.Id).Position);
        Assert.Equal(new AbilityUses(2, 2), wizard.FireballUses);
        Assert.Equal(13, state.Units.Count);
    }
}
