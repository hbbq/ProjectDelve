using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ProjectDelve.Engine;

/// <summary>A portable DELVE1 string around the existing scenario JSON boundary.</summary>
public static class ScenarioDefinitionTransport
{
    public const string Prefix = "DELVE1:";
    public const int MaxTransportCharacters = 1024 * 1024;
    public const int MaxJsonBytes = 4 * 1024 * 1024;

    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Encode(ScenarioDefinition scenario)
    {
        var json = ScenarioDefinitionJson.ToJson(scenario);
        if (Utf8.GetByteCount(json) > MaxJsonBytes)
            throw new ArgumentException($"Scenario JSON exceeds {MaxJsonBytes} UTF-8 bytes.", nameof(scenario));

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Utf8.GetBytes(json));

        var value = Prefix + Convert.ToBase64String(compressed.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (value.Length > MaxTransportCharacters)
            throw new ArgumentException($"Transport exceeds {MaxTransportCharacters} characters.", nameof(scenario));
        return value;
    }

    /// <summary>
    /// Rejects malformed or oversized transport data. JSON and scenario validation exceptions
    /// propagate from ScenarioDefinitionJson.FromJson; invalid UTF-8 throws DecoderFallbackException.
    /// </summary>
    public static ScenarioDefinition Decode(string value)
        => Decode(value, null);

    public static ScenarioDefinition Decode(string value, Action<ScenarioDefinition>? beforeCreation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxTransportCharacters)
            throw new ArgumentException($"Transport exceeds {MaxTransportCharacters} characters.", nameof(value));
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException(value.Contains(':')
                ? "Unsupported transport prefix/version; expected DELVE1:."
                : "Missing transport prefix; expected DELVE1:.");

        var payload = value[Prefix.Length..];
        if (payload.Length == 0)
            throw new FormatException("DELVE1 payload is empty.");
        if (payload.Length % 4 == 1 || payload.Any(c =>
                !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new FormatException("Invalid unpadded base64url payload.");

        var base64 = payload.Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
        // GZipStream can accept an empty/truncated header as an empty stream. Require a gzip envelope.
        if (bytes.Length < 18 || bytes[0] != 0x1f || bytes[1] != 0x8b || bytes[2] != 8)
            throw new InvalidDataException("DELVE1 payload must contain gzip data.");

        using var compressed = new MemoryStream(bytes);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var json = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = gzip.Read(buffer)) != 0)
        {
            if (json.Length + count > MaxJsonBytes)
                throw new InvalidDataException($"Decompressed JSON exceeds {MaxJsonBytes} bytes.");
            json.Write(buffer, 0, count);
        }

        // DELVE1 emits one gzip member. GZipStream may silently accept a truncated footer;
        // verify its uncompressed-size field (our byte limit is well below the 32-bit wrap point).
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4)) != json.Length)
            throw new InvalidDataException("Invalid or truncated gzip footer.");

        return ScenarioDefinitionJson.FromJson(Utf8.GetString(json.ToArray()), beforeCreation);
    }
}
