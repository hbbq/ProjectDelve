using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Browser appearance only. Derive once at setup, never from a playback state.
public static class BoardAppearance
{
    public static string DeriveSeed(string scenarioId, Board board)
    {
        var terrain = board.Terrain.ToDictionary(t => t.Position);
        var cells = Enumerable.Range(0, board.Height).SelectMany(y => Enumerable.Range(0, board.Width).Select(x =>
        {
            terrain.TryGetValue(new(x, y), out var tile);
            var definition = board.TerrainDefinitionAt(new(x, y));
            return new object?[] { x, y, definition.LegacyKind?.ToString() ?? definition.Id, tile?.Direction?.ToString() };
        })).ToArray();
        var edges = board.Edges.Where(e => e.Kind != EdgeKind.None).Select(e =>
        {
            var aFirst = e.A.Y < e.B.Y || e.A.Y == e.B.Y && e.A.X <= e.B.X;
            var a = aFirst ? e.A : e.B; var b = aFirst ? e.B : e.A;
            return new { A = a, B = b, Kind = e.Kind.ToString() };
        }).OrderBy(e => e.A.Y).ThenBy(e => e.A.X).ThenBy(e => e.B.Y).ThenBy(e => e.B.X)
          .Select(e => new object[] { e.A.X, e.A.Y, e.B.X, e.B.Y, e.Kind }).ToArray();
        var canonical = JsonSerializer.Serialize(new object[] { 1, scenarioId, board.Width, board.Height, cells, edges });
        return "board-v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
