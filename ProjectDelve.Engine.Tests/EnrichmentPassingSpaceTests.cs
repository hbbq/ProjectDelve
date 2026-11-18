using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EnrichmentPassingSpaceTests
{
    [Theory]
    [InlineData('T')]
    [InlineData('R')]
    public void LongCorridorGetsLocalBypassAndKeepsNarrowSections(char terrainKind)
    {
        var context = EnrichmentFixtures.Create(Enumerable.Repeat(new string(terrainKind, 15), 7).ToArray());
        var terrain = new EnrichmentTerrain(context, ContentBundle.Canonical());
        var path = Enumerable.Range(3 * 15 + 1, 13).ToArray();
        var edits = path.ToHashSet();
        var bays = new EnrichmentPassingSpaces(terrain, edits, path.Length + 3, _ => true);
        bays.Along(path);

        Assert.Equal(1, bays.Added);
        var shoulder = edits.Except(path).Order().ToArray();
        Assert.Equal(3, shoulder.Length);
        Assert.Equal(Enumerable.Range(shoulder[0], 3), shoulder);
        Assert.Equal(shoulder[0] / 15, shoulder[^1] / 15);
        Assert.Single(terrain.Neighbors(path[0]).Where(edits.Contains));
        Assert.Single(terrain.Neighbors(path[^1]).Where(edits.Contains));
    }

    [Theory]
    [InlineData(7, 3, true, 0)]
    [InlineData(13, 0, true, 1)]
    [InlineData(13, 3, false, 1)]
    public void ShortChokesAndBaysWithoutBudgetOrPermissionRemainIntact(int length, int allowance, bool guardAllows, int skipped)
    {
        var context = EnrichmentFixtures.Create(Enumerable.Repeat(new string('R', 15), 7).ToArray());
        var terrain = new EnrichmentTerrain(context, ContentBundle.Canonical());
        var path = Enumerable.Range(3 * 15 + 1, length).ToArray();
        var edits = path.ToHashSet();
        var bays = new EnrichmentPassingSpaces(terrain, edits, length + allowance, _ => true, _ => guardAllows);
        bays.Along(path);

        Assert.Equal(0, bays.Added);
        Assert.Equal(skipped, bays.Skipped);
        Assert.Equal(path, edits.Order());
    }
}
