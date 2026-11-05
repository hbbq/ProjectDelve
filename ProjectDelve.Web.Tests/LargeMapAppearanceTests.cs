using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class LargeMapAppearanceTests
{
    [Fact]
    public void IndexedAppearancePreservesCapturedLargeBoardSeedAcrossTransportAndCopies()
    {
        var world = WorldGenerator.Generate(new WorldGenerationRecipe(new(-17, -13, 120, 100), new(), 0x123456789ABCDEF0));
        var board = GameEngine.CreateGame(world.Definition).Physical.Board;
        // Captured by the detached baseline before replacing the repeated linear lookup.
        const string expected = "board-v1:738661fafc682646472657805b2b7ed97bcb70b0247e9c5671c00fefdc12e244";
        Assert.Equal(expected, BoardAppearance.DeriveSeed("benchmark", board));
        var imported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(world.Definition));
        Assert.Equal(expected, BoardAppearance.DeriveSeed("benchmark", GameEngine.CreateGame(imported).Physical.Board));
        Assert.Equal(expected, BoardAppearance.DeriveSeed("benchmark", board with { Terrain = [.. board.Terrain.AsEnumerable().Reverse()] }));
        var first = board.Terrain[0];
        var changed = board with { Terrain = [.. board.Terrain] };
        changed.Terrain[0] = new(first.Position, TerrainKind.Gravel);
        Assert.NotEqual(expected, BoardAppearance.DeriveSeed("benchmark", changed));
        Assert.Equal(expected, BoardAppearance.DeriveSeed("benchmark", board));
    }
}
