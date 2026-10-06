using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ProjectDelve.Engine;
using Xunit;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Engine.Tests;

public sealed class ScenarioDefinitionTransportTests
{
    private static ScenarioDefinition Example() => Define(
        Map(6, 5, defaultTerrain: TerrainKind.Grass,
            cells: [Tile(4, 3, TerrainKind.Tree)],
            edges: [Edge(1, 2, EdgeDirection.Right, EdgeKind.ClosedDoor),
                Edge(3, 2, EdgeDirection.Down, EdgeKind.OpenDoor)]),
        [Group(UnitTypeIds.Grunt, "side / amber 17 — blå", ControllerKind.Human, At(0, 0)),
            Group(UnitTypeIds.Grunt, "violet-99", ControllerKind.Automated, At(2, 0)),
            Group(UnitTypeIds.Wizard, "side / amber 17 — blå", ControllerKind.Human, At(0, 2, Posture.Lying, initialHp: 2)),
            Group(UnitTypeIds.Goblin, "violet-99", ControllerKind.Automated)],
        unitTypeIds: [UnitTypeIds.Goblin, UnitTypeIds.Wizard, UnitTypeIds.Grunt]);

    [Fact]
    public void TransportPreservesOverridesOrderSidesAgencyAndCanonicalEdges()
    {
        var authored = Example();
        var encoded = ScenarioDefinitionTransport.Encode(authored);
        var loaded = ScenarioDefinitionTransport.Decode(encoded);
        Assert.Equal(authored.UnitTypeIds, loaded.UnitTypeIds);
        Assert.Equal(authored.Units, loaded.Units);
        Assert.Equal(authored.Agency, loaded.Agency);
        Assert.Equal(authored.Board.Cells, loaded.Board.Cells);
        Assert.Equal(authored.Board.Edges, loaded.Board.Edges);
        Assert.Equal(ScenarioDefinitionJson.ToJson(authored), ScenarioDefinitionJson.ToJson(loaded));
        var state = GameEngine.CreateGame(loaded);
        Assert.Equal(2, state.Units[2].CurrentHp);
        Assert.Equal(Posture.Lying, state.Physical.Figures[2].Posture);
        Assert.Equal(loaded.UnitTypeIds, state.Types.Select(t => t.Id));
        Assert.Equal(ControllerKind.Human, state.ControllerFor(new ActivationToken(UnitTypeIds.Grunt, "side / amber 17 — blå")));
        Assert.Equal(ControllerKind.Automated, state.ControllerFor(new ActivationToken(UnitTypeIds.Grunt, "violet-99")));
        Assert.Equal(EdgeKind.ClosedDoor, state.Physical.Board.EdgeBetween(new(1, 2), new(2, 2)));
        Assert.Equal(EdgeKind.OpenDoor, state.Physical.Board.EdgeBetween(new(3, 2), new(3, 3)));
    }

    [Fact]
    public void EncodeUsesExistingJsonBytes()
    {
        var definition = Example();
        var encoded = ScenarioDefinitionTransport.Encode(definition);
        var payload = encoded[ScenarioDefinitionTransport.Prefix.Length..].Replace('-', '+').Replace('_', '/');
        using var bytes = new MemoryStream(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
        using var gzip = new GZipStream(bytes, CompressionMode.Decompress);
        using var json = new MemoryStream();
        gzip.CopyTo(json);
        Assert.Equal(Encoding.UTF8.GetBytes(ScenarioDefinitionJson.ToJson(definition)), json.ToArray());
    }

    [Fact]
    public void NullInputsFailClearly()
    {
        Assert.Throws<ArgumentNullException>(() => ScenarioDefinitionTransport.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => ScenarioDefinitionTransport.Decode(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void BlankInputFailsClearly(string value) =>
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionTransport.Decode(value));

    [Theory]
    [InlineData("abc", "Missing")]
    [InlineData("DELVE1", "Missing")]
    [InlineData("DELVE2:abc", "Unsupported")]
    [InlineData("delve1:abc", "Unsupported")]
    [InlineData("OTHER:abc", "Unsupported")]
    [InlineData("DELVE1:", "empty")]
    public void PrefixAndEmptyPayloadFailClearly(string value, string message) =>
        Assert.Contains(message, Assert.Throws<FormatException>(() => ScenarioDefinitionTransport.Decode(value)).Message);

    [Theory]
    [InlineData("A")]
    [InlineData("AA+")]
    [InlineData("AA/")]
    [InlineData("AA=")]
    [InlineData("AA ")]
    [InlineData("AA\n")]
    [InlineData("éé")]
    public void MalformedBase64UrlFails(string payload) =>
        Assert.Throws<FormatException>(() => ScenarioDefinitionTransport.Decode("DELVE1:" + payload));

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void NonGzipBytesFail(int length) =>
        Assert.Throws<InvalidDataException>(() => ScenarioDefinitionTransport.Decode(Base64Url(new byte[length])));

    [Fact]
    public void CorruptGzipFails()
    {
        var bytes = Compress(Encoding.UTF8.GetBytes("{}"));
        bytes[^8] ^= 0xff; // Corrupt the CRC, preserving the header and deflate stream.
        Assert.Throws<InvalidDataException>(() => ScenarioDefinitionTransport.Decode(Base64Url(bytes)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void TruncatedGzipFails(int removedBytes)
    {
        var bytes = Compress(Encoding.UTF8.GetBytes(ScenarioDefinitionJson.ToJson(Example())));
        Assert.Throws<InvalidDataException>(() => ScenarioDefinitionTransport.Decode(Base64Url(bytes[..^removedBytes])));
    }

    [Fact]
    public void InvalidUtf8Fails() =>
        Assert.Throws<DecoderFallbackException>(() => ScenarioDefinitionTransport.Decode(Base64Url(Compress([0xc3, 0x28]))));

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    public void CompressedInvalidJsonRetainsJsonException(string json) =>
        Assert.Throws<JsonException>(() => ScenarioDefinitionTransport.Decode(Base64Url(Compress(Encoding.UTF8.GetBytes(json)))));

    [Fact]
    public void InvalidScenarioRetainsExistingValidationException()
    {
        var valid = Example();
        var invalid = valid with { Board = valid.Board with { Width = 0 } };
        var json = ScenarioDefinitionJson.ToJson(invalid);
        var expected = Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(json));
        var actual = Assert.Throws<ArgumentException>(() => ScenarioDefinitionTransport.Decode(Base64Url(Compress(Encoding.UTF8.GetBytes(json)))));
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.ParamName, actual.ParamName);
    }

    [Fact]
    public void OversizedInputFailsBeforeDecoding() =>
        Assert.Contains("characters", Assert.Throws<ArgumentException>(() => ScenarioDefinitionTransport.Decode(
            "DELVE1:" + new string('A', ScenarioDefinitionTransport.MaxTransportCharacters))).Message);

    [Fact]
    public void OversizedDecompressedOutputFailsBeforeJsonParsing()
    {
        var value = Base64Url(Compress(new byte[ScenarioDefinitionTransport.MaxJsonBytes + 1]));
        Assert.Contains("Decompressed JSON exceeds", Assert.Throws<InvalidDataException>(() => ScenarioDefinitionTransport.Decode(value)).Message);
    }

    [Fact]
    public void OversizedJsonCannotBeEncoded()
    {
        var valid = Example();
        var oversized = valid with { UnitTypeIds = [new string('a', ScenarioDefinitionTransport.MaxJsonBytes)] };
        Assert.Contains("UTF-8 bytes", Assert.Throws<ArgumentException>(() => ScenarioDefinitionTransport.Encode(oversized)).Message);
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var result = new MemoryStream();
        using (var gzip = new GZipStream(result, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return result.ToArray();
    }

    private static string Base64Url(byte[] bytes) => "DELVE1:" + Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
