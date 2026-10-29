using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectDelve.Web;

public sealed record ImportedLibraryCover(string CoverArt);
public sealed record LibraryCoverContent(byte[] Bytes, string ContentType);

/// <summary>Immutable cover images shared by local and bundled records. Browser imports
/// normalize JPEG/WebP/PNG to PNG first; this boundary independently validates and rewrites
/// the PNG, so metadata, filenames and untrusted chunk payloads never become stored assets.</summary>
public sealed class LibraryCoverStore(string localDirectory, string? bundledDirectory = null)
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 4096;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly Regex NamePattern = new("\\A[0-9a-f]{64}\\.png\\z", RegexOptions.CultureInvariant);
    private readonly string localRoot = Path.GetFullPath(Path.Combine(localDirectory, "covers"));
    private readonly string? bundledRoot = bundledDirectory is null ? null : Path.GetFullPath(Path.Combine(bundledDirectory, "covers"));

    public static bool IsCoverName(string? name) => name is not null && NamePattern.IsMatch(name);

    public ImportedLibraryCover Import(byte[] bytes, string? contentType)
    {
        if (!string.Equals(contentType?.Split(';')[0].Trim(), "image/png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Import a PNG cover, or use the image picker to convert JPEG or WebP.");
        var canonical = CanonicalPng(bytes);
        var name = Convert.ToHexStringLower(SHA256.HashData(canonical)) + ".png";
        CheckNoLinks(localRoot);
        Directory.CreateDirectory(localRoot);
        var destination = Path.Combine(localRoot, name);
        CheckNoLinks(destination);
        if (File.Exists(destination))
        {
            VerifyFile(destination, name);
            return new("/api/library/covers/" + name);
        }
        var temporary = Path.Combine(localRoot, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(canonical);
                stream.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, destination, overwrite: false); }
            catch (IOException) when (File.Exists(destination)) { VerifyFile(destination, name); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new("/api/library/covers/" + name);
    }

    public string? FindPath(string name)
    {
        if (!IsCoverName(name)) throw new ArgumentException("Invalid cover identity.");
        foreach (var root in new[] { localRoot, bundledRoot })
        {
            if (root is null) continue;
            var path = Path.Combine(root, name);
            CheckNoLinks(path);
            if (!File.Exists(path)) continue;
            VerifyFile(path, name);
            return path;
        }
        return null;
    }

    public LibraryCoverContent? Read(string name)
    {
        var path = FindPath(name);
        return path is null ? null : new(VerifyFile(path, name), "image/png");
    }

    private static byte[] VerifyFile(string path, string name)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Stored cover exceeds the image limit.");
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".png" != name)
            throw new InvalidDataException("Stored cover does not match its immutable identity.");
        return bytes;
    }

    internal static byte[] CanonicalPng(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new ArgumentException("Cover images must be at most 5 MB after conversion to PNG.");
        if (bytes.Length < 45 || !bytes.AsSpan(0, 8).SequenceEqual(Signature)) throw new ArgumentException("The cover is not a PNG image.");
        using var compressed = new MemoryStream();
        byte[]? header = null;
        var ended = false;
        var dataStarted = false;
        var dataEnded = false;
        for (var offset = 8; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 12) throw InvalidPng();
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) throw InvalidPng();
            var count = (int)length;
            var type = bytes.AsSpan(offset + 4, 4);
            if (type.ToArray().Any(b => !(b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))) throw InvalidPng();
            var payload = bytes.AsSpan(offset + 8, count);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + count, 4));
            if (Crc(bytes.AsSpan(offset + 4, count + 4)) != crc) throw InvalidPng();
            var kind = Encoding.ASCII.GetString(type);
            if (header is null && kind != "IHDR") throw InvalidPng();
            switch (kind)
            {
                case "IHDR":
                    if (header is not null || count != 13 || offset != 8) throw InvalidPng();
                    header = payload.ToArray();
                    break;
                case "IDAT":
                    if (dataEnded) throw InvalidPng();
                    dataStarted = true;
                    compressed.Write(payload);
                    break;
                case "IEND":
                    if (count != 0 || !dataStarted || offset + 12 != bytes.Length) throw InvalidPng();
                    ended = true;
                    break;
                case "PLTE":
                    // Optional suggested palette in RGB/RGBA PNGs; it is not needed for rendering.
                    if (dataStarted || count == 0 || count > 768 || count % 3 != 0) throw InvalidPng();
                    break;
                case "acTL" or "fcTL" or "fdAT":
                    throw new ArgumentException("Animated PNG covers are not supported. Import a still image.");
                case "tRNS":
                    throw new ArgumentException("Use the image picker to convert palette or color-key transparency to PNG alpha.");
                default:
                    if ((type[0] & 32) == 0) throw InvalidPng(); // Unknown critical chunk.
                    if (dataStarted) dataEnded = true;
                    break;
            }
            offset += count + 12;
        }
        if (!ended || header is null) throw InvalidPng();
        var width = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
        if (width is 0 or > MaxDimension || height is 0 or > MaxDimension)
            throw new ArgumentException($"Cover images must be between 1 and {MaxDimension} pixels on each side.");
        var channels = header[9] switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (header[8] != 8 || channels == 0 || header[10] != 0 || header[11] != 0 || header[12] != 0)
            throw new ArgumentException("Use the image picker to convert this PNG to a non-interlaced 8-bit image.");
        var row = new byte[checked((int)width * channels + 1)];
        var zlib = compressed.ToArray();
        if (zlib.Length < 6 || (zlib[0] & 15) != 8 || (zlib[0] >> 4) > 7 ||
            ((zlib[0] << 8) + zlib[1]) % 31 != 0 || (zlib[1] & 32) != 0) throw InvalidPng();
        uint adlerA = 1, adlerB = 0;
        compressed.Position = 0;
        using var rewritten = new MemoryStream();
        try
        {
            using (var decoder = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
            using (var encoder = new ZLibStream(rewritten, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var y = 0; y < height; y++)
                {
                    decoder.ReadExactly(row);
                    if (row[0] > 4) throw InvalidPng();
                    foreach (var value in row) { adlerA = (adlerA + value) % 65521; adlerB = (adlerB + adlerA) % 65521; }
                    encoder.Write(row);
                }
                if (decoder.ReadByte() != -1) throw InvalidPng();
            }
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        { throw new ArgumentException("The PNG pixel data is incomplete or invalid.", error); }
        if (BinaryPrimitives.ReadUInt32BigEndian(zlib.AsSpan(zlib.Length - 4)) != ((adlerB << 16) | adlerA)) throw InvalidPng();
        using var output = new MemoryStream();
        output.Write(Signature);
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", rewritten.ToArray());
        WriteChunk(output, "IEND", []);
        if (output.Length > MaxBytes) throw new ArgumentException("Cover images must be at most 5 MB after conversion to PNG.");
        return output.ToArray();
    }

    private static ArgumentException InvalidPng() => new("The PNG image is malformed or incomplete.");
    private static void WriteChunk(Stream output, string type, byte[] bytes)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); output.Write(number);
        var body = new byte[bytes.Length + 4];
        Encoding.ASCII.GetBytes(type).CopyTo(body, 0); bytes.CopyTo(body, 4);
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(body)); output.Write(number);
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var value = uint.MaxValue;
        foreach (var b in bytes)
        {
            value ^= b;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) == 1 ? (value >> 1) ^ 0xedb88320u : value >> 1;
        }
        return ~value;
    }
    private static void CheckNoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Cover storage must not contain symbolic links or junctions.");
    }
}
