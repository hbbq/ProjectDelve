using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ScenarioMigrationTests
{
    public static TheoryData<string> Fixtures => new() { "serialization", "resources", "dragon", "shaper" };

    [Theory, MemberData(nameof(Fixtures))]
    public void CreationPreservesTestOwnedSetup(string id)
    {
        var definition = WebScenarioFixtures.Named(id);
        var state = GameEngine.CreateGame(definition);
        var board = state.Physical.Board;
        Assert.Equal((definition.Board.Width, definition.Board.Height), (board.Width, board.Height));
        Assert.Equal(definition.UnitTypeIds, state.Types.Select(type => type.Id));
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
        {
            var cell = new Cell(x, y);
            Assert.Equal(definition.Board.Cells.SingleOrDefault(tile => tile.Position == cell)?.Terrain
                ?? definition.Board.DefaultTerrain, board.TerrainAt(cell));
        }
        Assert.Equal(definition.Board.Edges.Select(edge => new Edge(edge.Position,
            edge.Direction == EdgeDirection.Right ? new(edge.Position.X + 1, edge.Position.Y)
                : new(edge.Position.X, edge.Position.Y + 1), edge.Kind)), board.Edges);
        Assert.Equal(definition.Units.Count, state.Units.Count);
        foreach (var (placement, unit) in definition.Units.Zip(state.Units))
        {
            Assert.Equal(placement.UnitTypeId, unit.TypeId);
            Assert.Equal(placement.SideId, unit.SideId);
            if (placement.UnitId is not null) Assert.Equal(placement.UnitId, unit.Id);
            Assert.Equal(placement.InitialHp ?? state.Types.Single(type => type.Id == unit.TypeId).Hp, unit.CurrentHp);
            var figure = Assert.Single(state.Physical.Figures, figure => figure.Id == unit.Id);
            Assert.Equal(placement.Anchor, figure.Position);
            Assert.Equal(placement.Posture, figure.Posture);
        }
        Assert.Equal(definition.Agency.Select(agency => new ControllerAssignment(
            new(agency.UnitTypeId, agency.SideId), agency.Controller)), state.Controllers);
        Assert.Equal((definition.Orders ?? []).Select(order => new GroupOrder(
            new(order.UnitTypeId, order.SideId), order.Kind, order.Destination, order.Target)), state.Orders);
        Assert.Equal(definition.Resources ?? [], state.Resources);
        Assert.Equal(0, state.Round);
        Assert.Empty(state.Bag);
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
        Assert.Equal(new AbilityUses(2, 2), WebAbilityTestQueries.Uses(state, wizard.Id, "fireball"));
        Assert.Equal(13, state.Units.Count);
    }
}
