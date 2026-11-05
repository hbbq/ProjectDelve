using ProjectDelve.Generation;

namespace ProjectDelve.Engine.Tests;

internal static class EnrichmentFixtures
{
    internal static WorldGenerationContext Create(string[] rows, ulong seed = 12345)
    {
        var c = new WorldGenerationContext(new(seed, new()), new(-17, -13, rows[0].Length, rows.Length));
        c.SampleNaturalTerrain();
        for (var y = 0; y < rows.Length; y++) for (var x = 0; x < rows[y].Length; x++)
        {
            var id = rows[y][x] switch { 'T' => "tree", 'R' => "solid-rock", 'W' => "water", 'D' => "dirt", _ => "grass" };
            var i = y * rows[0].Length + x;
            c.Samples[i] = c.OriginalSamples[i] = c.Samples[i] with { TerrainTypeId = id };
        }
        c.OriginalAnalysis = c.CurrentAnalysis = TerrainAnalyzer.Analyze(c.Window, c.OriginalSamples);
        return c;
    }
    internal static string[] Mass(char kind, int size = 20) => Enumerable.Range(0, size + 4).Select(y =>
        y < 2 || y >= size + 2 ? new string('.', size + 4) : ".." + new string(kind, size) + "..").ToArray();
    internal static WorldGenerationStep Step(WorldStepKind kind, string id = "test", int intensity = 2) => new(id, kind, new EnrichmentStepParameters(intensity), ["formations"]);
}
