using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class StrongholdGenerationCodeTests
{
    private const string Prefix = "DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QClassic";

    [Theory]
    [InlineData("Classic", 0UL)]
    [InlineData("Fun", 42UL)]
    [InlineData("Experimental", 0x0123456789abcdefUL)]
    [InlineData("FarOut", ulong.MaxValue)]
    public void RecipesPreserveEveryStrongholdParameterAndCanonicalSeed(string preset, ulong seed)
    {
        var expected = new GenerationCode(Width: 50, Height: 18, Profile: "OS", Openness: 3,
            Terrain: 0, Density: 1, Difficulty: 5, Treasure: 3, Objective: "ORA", FogOfWar: false,
            LootPreset: preset, Seed: seed);
        Assert.Equal(expected, GenerationCode.Parse(expected.ToString()));
        Assert.StartsWith("DNG2-OS-50x18-O3-T0-N1-L5-B3-ORA-F0-Q" + preset, expected.ToString());
        Assert.Equal(expected, expected.Complete());
        Assert.EndsWith($"-S{seed:X16}", expected.ToString());
    }

    [Fact]
    public void PrefixCompletesOnceAndAllFourHeroesCanonicalize()
    {
        var code = GenerationCode.Parse(Prefix);
        Assert.Equal("OS", code.Profile);
        Assert.Null(code.Seed);
        var complete = code.Complete();
        Assert.Equal(complete, complete.Complete());
        Assert.Equal(complete, GenerationCode.Parse(complete.ToString()));
        Assert.Equal(Prefix, complete.Prefix);
        Assert.Contains("-ORA-", GenerationCode.Parse(Prefix.Replace("OR2", "OR4")).ToString());
        Assert.EndsWith("-S0123456789ABCDEF", GenerationCode.Parse(Prefix + "-S0123456789abcdef").ToString());
    }

    [Theory]
    [InlineData("DNG1-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QClassic")]
    [InlineData("DNG3-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-030x24-O2-T2-N2-L3-B2-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-T2-O2-N2-L3-B2-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-O0-T2-N2-L3-B2-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-O2-T4-N2-L3-B2-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-O2-T2-N0-L3-B2-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L6-B2-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B4-OR2-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR0-F1-QClassic")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F2-QClassic")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QCustom")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F1")]
    [InlineData(Prefix + "-S123")]
    [InlineData(Prefix + "-S0123456789ABCDEG")]
    [InlineData(Prefix + "-extra")]
    public void MalformedOrUnsupportedRecipesAreRejected(string recipe) =>
        Assert.Throws<FormatException>(() => GenerationCode.Parse(recipe));

    [Theory]
    [InlineData(17, 24)] [InlineData(30, 17)] [InlineData(51, 24)] [InlineData(30, 51)]
    public void DimensionsAreValidatedBeforeGeneration(int width, int height) =>
        Assert.Throws<ArgumentException>(() => GenerationCode.Parse(Prefix.Replace("30x24", $"{width}x{height}")));

    [Fact]
    public void StructuredInputsCannotBypassRecipeLimits()
    {
        var code = new GenerationCode(Profile: "OS");
        foreach (var invalid in new[] { code with { Openness = 0 }, code with { Openness = 4 },
            code with { Terrain = -1 }, code with { Terrain = 4 }, code with { Treasure = -1 },
            code with { Treasure = 4 }, code with { Density = 0 }, code with { Difficulty = 6 },
            code with { LootPreset = "Custom" }, code with { Objective = "OR5" }, code with { Profile = "unknown" } })
            Assert.Throws<ArgumentException>(() => invalid.Validate());
    }
}
