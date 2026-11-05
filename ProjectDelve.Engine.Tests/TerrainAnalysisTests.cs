using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TerrainAnalysisTests
{
    private static NaturalTerrainSample[] Samples(params string[] rows) => rows.SelectMany(row => row.Select(c =>
        new NaturalTerrainSample(c switch { 'T' => "tree", 'R' => "solid-rock", 'W' => "water", 'D' => "dirt", _ => "grass" }, .2, .3, .4, .5))).ToArray();
    private static TerrainAnalysisSnapshot Analyze(params string[] rows) =>
        TerrainAnalyzer.Analyze(new(-10, 20, rows[0].Length, rows.Length), Samples(rows));

    [Fact]
    public void ControlledGapGroupsCoresWithoutRelabelingOrGrowingThem()
    {
        var result = Analyze("T.T", "WWW", "RDR");
        Assert.Equal(result.FormationLabels[0], result.FormationLabels[2]);
        Assert.NotEqual(result.ExactComponentLabels[0], result.ExactComponentLabels[2]);
        Assert.NotEqual(result.FormationLabels[0], result.FormationLabels[1]);
        var forest = result.Formations[result.FormationLabels[0]];
        Assert.Equal(2, forest.CoreArea); Assert.Equal(3, forest.EnvelopeArea);
        Assert.Equal(2d / 3, forest.Density);
        Assert.Equal(result.FormationLabels[6], result.FormationLabels[8]);
        Assert.Equal(3, result.Formations[result.FormationLabels[6]].EnvelopeArea);
    }

    [Theory]
    [InlineData("TWT")]
    [InlineData("TRT")]
    [InlineData("T..T")]
    public void GroupingDoesNotCrossWaterOtherFamiliesOrWideGaps(string row)
    {
        var result = Analyze(row);
        Assert.NotEqual(result.FormationLabels[0], result.FormationLabels[^1]);
    }

    [Fact]
    public void DiagonalsAreNeitherExactNorWalkableConnections()
    {
        var result = Analyze(".W", "W.");
        Assert.NotEqual(result.ExactComponentLabels[0], result.ExactComponentLabels[3]);
        Assert.NotEqual(result.WalkableLabels[0], result.WalkableLabels[3]);
        Assert.Equal(2, result.Connectivity.ComponentCount);
        Assert.All(new[] { 1, 2 }, i => Assert.Equal(-1, result.WalkableLabels[i]));
    }

    [Fact]
    public void DepthAndClearanceMeasureCardinalDistanceIncludingOutside()
    {
        var solid = Analyze("TTTTT", "TTTTT", "TTTTT", "TTTTT", "TTTTT");
        Assert.Equal(3, solid.Depth[12]); Assert.Equal(1, solid.Depth[0]);
        Assert.False(solid.Boundary[12]); Assert.True(solid.Boundary[0]);
        Assert.All(solid.Clearance, value => Assert.Equal(0, value));
        Assert.Empty(solid.Chokepoints); Assert.Empty(solid.Opportunities);
        Assert.Equal(0, solid.Connectivity.ComponentCount);
        var open = Analyze(".....", ".....", ".....", ".....", ".....");
        Assert.Equal(3, open.Clearance[12]); Assert.Equal(1, open.Clearance[0]);
        Assert.Empty(open.Chokepoints);
        Assert.Empty(open.NarrowPassages);
    }

    [Fact]
    public void ArticulationPointsIdentifyBridgeButNotRoomCorners()
    {
        var result = Analyze("..W..", ".....", "..W..");
        Assert.Contains(7, result.Chokepoints);
        Assert.Contains(6, result.Chokepoints); Assert.Contains(8, result.Chokepoints);
        Assert.DoesNotContain(0, result.Chokepoints);
    }

    [Fact]
    public void TypedProviderDefinesPassabilityAndLosIndependentlyOfLandscapeFamily()
    {
        var content = new ContentBundle { Terrain = [new("grass", "Blocked grass", false, true), new("water", "Walkable water", true, false)] };
        var result = TerrainAnalyzer.Analyze(new(Width: 3, Height: 1), Samples(".W."), content);
        Assert.Equal(new[] { -1, 0, -1 }, result.WalkableLabels);
        Assert.Equal(1, result.Connectivity.WalkableArea);
        Assert.Empty(result.Opportunities);
        Assert.Equal(1, result.Formations[result.FormationLabels[0]].BlocksLos);
        Assert.Throws<ArgumentException>(() => TerrainAnalyzer.Analyze(new(Width: 1, Height: 1), Samples("T"), content));
    }

    [Fact]
    public void WallsBlockAndDoorsRemainPotentialPassages()
    {
        var samples = Samples("..."); var window = new WorldWindow(Width: 3, Height: 1);
        var wall = TerrainAnalyzer.Analyze(window, samples, edges: [new(new(0, 0), EdgeDirection.Right, EdgeKind.Wall)]);
        Assert.Equal(2, wall.Connectivity.ComponentCount);
        var door = TerrainAnalyzer.Analyze(window, samples, edges: [new(new(0, 0), EdgeDirection.Right, EdgeKind.ClosedDoor)]);
        Assert.Equal(1, door.Connectivity.ComponentCount);
        var windowWall = TerrainAnalyzer.Analyze(window, samples, edges: [new(new(0, 0), EdgeDirection.Right, EdgeKind.WallWithWindow)]);
        Assert.Equal(2, windowWall.Connectivity.ComponentCount);
        Assert.Throws<ArgumentException>(() => TerrainAnalyzer.Analyze(window, samples,
            edges: [new(new(2, 0), EdgeDirection.Right, EdgeKind.Wall)]));
        Assert.Throws<ArgumentException>(() => TerrainAnalyzer.Analyze(window, samples,
            edges: [new(new(0, 0), (EdgeDirection)99, EdgeKind.Wall)]));
    }

    [Fact]
    public void ExistingClearingsAndIslandsProduceCompactOpportunities()
    {
        var clearing = Analyze("TTTTT", "T...T", "T.D.T", "T...T", "TTTTT");
        var opportunity = Assert.Single(clearing.Opportunities);
        Assert.Equal("clearing", opportunity.Kind); Assert.Equal(9, opportunity.UsableArea);
        Assert.False(opportunity.RequiresConnection);
        var island = Assert.Single(Analyze("WWW", "W.W", "WWW").Opportunities);
        Assert.Equal("island", island.Kind); Assert.True(island.RequiresConnection);
    }

    [Fact]
    public void TwoCellNeckBetweenRoomsIsNarrowWithoutAnArticulationPoint()
    {
        var result = Analyze("...WWW...", "...WWW...", ".........", ".........", "...WWW...", "...WWW...");
        Assert.Empty(result.Chokepoints);
        var neck = Assert.Single(result.NarrowPassages);
        Assert.Equal(2, neck.Width); Assert.Equal(6, neck.CellCount);
        Assert.Equal(new CellBounds(3, 2, 3, 2), neck.Bounds);
        Assert.Equal("horizontal", neck.Axis);
    }

    [Fact]
    public void NarrowPassagesRequireActualOpposingObstaclesAndRespectWalls()
    {
        Assert.Empty(Analyze(".....", ".....").NarrowPassages);
        var window = new WorldWindow(Width: 5, Height: 3);
        var result = TerrainAnalyzer.Analyze(window, Samples(".....", ".....", "....."), edges:
            [new(new(2, 0), EdgeDirection.Down, EdgeKind.Wall), new(new(2, 1), EdgeDirection.Down, EdgeKind.WallWithWindow)]);
        var passage = Assert.Single(result.NarrowPassages);
        Assert.Equal(1, passage.Width); Assert.Equal(7, passage.AnchorIndex);
    }

    [Fact]
    public void WallClearanceAndOpportunityBoundsRespectEachSide()
    {
        var samples = Samples(Enumerable.Repeat(".......", 7).ToArray());
        var edges = Enumerable.Range(0, 7).Select(y => new EdgeDefinition(new(3, y), EdgeDirection.Right, EdgeKind.Wall)).ToArray();
        var a = TerrainAnalyzer.Analyze(new(Width: 7, Height: 7), samples, edges: edges);
        Assert.Equal(1, a.Clearance[3 * 7 + 3]);
        Assert.Equal(1, a.Clearance[3 * 7 + 4]);
        Assert.Equal(new CellBounds(0, 0, 4, 7), a.Opportunities[0].Bounds);
        Assert.Equal(new CellBounds(4, 0, 3, 7), a.Opportunities[1].Bounds);
    }

    [Fact]
    public void LargestWindowLongSnakeUsesIterativeTraversal()
    {
        var rows = Enumerable.Range(0, 120).Select(y => y % 2 == 0 ? new string('.', 120) :
            new string('W', y / 2 % 2 == 0 ? 119 : 0) + "." + new string('W', y / 2 % 2 == 0 ? 0 : 119)).ToArray();
        var result = Analyze(rows);
        Assert.Equal(1, result.Connectivity.ComponentCount);
        Assert.Equal(7260, result.Connectivity.WalkableArea);
        Assert.Equal(7258, result.Chokepoints.Length);
    }

    [Fact]
    public void SampledLandscapeIsDeterministicDetachedAndWindowScoped()
    {
        var window = new WorldWindow(-17, -13, 40, 30);
        var context = new WorldGenerationContext(new(0x123456789ABCDEF0, new()), window);
        context.SampleNaturalTerrain(); var original = context.Samples.ToArray();
        var a = TerrainAnalyzer.Analyze(window, context.Samples, revision: 4);
        var b = TerrainAnalyzer.Analyze(window, context.Samples, revision: 4);
        Assert.Equal(a.FormationLabels, b.FormationLabels); Assert.Equal(a.Chokepoints, b.Chokepoints);
        Assert.Equal(a.Formations.Select(f => f.Id), b.Formations.Select(f => f.Id));
        Assert.Equal(window.Width * window.Height, a.Formations.Sum(f => f.CoreArea));
        Assert.Equal(original, context.Samples); Assert.Equal(4, a.Revision);
        a.FormationLabels[0] = -1;
        Assert.NotEqual(-1, b.FormationLabels[0]);
        Assert.Equal(original, context.Samples);
        var differentFields = original.ToArray(); differentFields[0] = differentFields[0] with { Soil = .123456789 };
        Assert.NotEqual(b.SourceFingerprint, TerrainAnalyzer.Analyze(window, differentFields).SourceFingerprint);
        Assert.NotEqual(b.Formations[0].Id, TerrainAnalyzer.Analyze(window, differentFields).Formations[0].Id);
        var shifted = TerrainAnalyzer.Analyze(window with { OffsetX = 0 }, original);
        Assert.NotEqual(b.Formations[0].Id, shifted.Formations[0].Id);
        Assert.Throws<ArgumentException>(() => TerrainAnalyzer.Analyze(window, []));
    }
}
