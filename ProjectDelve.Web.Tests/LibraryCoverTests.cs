using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class LibraryCoverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-cover-test-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(root, "local");
    private string Bundle => Path.Combine(root, "bundle");
    private LibraryCoverStore Store => new(Local, Bundle);

    [Fact]
    public void ImportedCoverIsImmutableDeduplicatedAndReadableAfterRestart()
    {
        var first = Store.Import(Png(), "image/png");
        var second = Store.Import(Png(), "image/png");
        Assert.Equal(first, second);
        var name = first.CoverArt.Split('/').Last();
        Assert.Matches("^[0-9a-f]{64}\\.png$", name);
        Assert.Single(Directory.GetFiles(Path.Combine(Local, "covers")));
        var actual = new LibraryCoverStore(Local, Bundle).Read(name);
        Assert.NotNull(actual);
        Assert.Equal("image/png", actual.ContentType);
        Assert.Equal(Store.Import(actual.Bytes, "image/png"), first);
    }

    [Fact]
    public void NonRenderingMetadataIsRemovedAndDoesNotChangeIdentity()
    {
        var clean = Store.Import(Png(), "image/png");
        var privateMetadata = Store.Import(Png(metadata: "Secret map labels must not ship"), "image/png");
        Assert.Equal(clean, privateMetadata);
        Assert.DoesNotContain("Secret", Encoding.ASCII.GetString(Store.Read(clean.CoverArt.Split('/').Last())!.Bytes));
    }

    [Fact]
    public void SameUrlResolvesFromBundleAfterPromotionWithoutLocalFile()
    {
        var imported = Store.Import(Png(), "image/png");
        var name = imported.CoverArt.Split('/').Last();
        Directory.CreateDirectory(Path.Combine(Bundle, "covers"));
        File.Copy(Store.FindPath(name)!, Path.Combine(Bundle, "covers", name));
        var cleanInstallation = new LibraryCoverStore(Path.Combine(root, "new-computer"), Bundle);
        Assert.Equal(Store.Read(name)!.Bytes, cleanInstallation.Read(name)!.Bytes);
        Assert.Equal(Path.Combine(Bundle, "covers", name), cleanInstallation.FindPath(name));
    }

    [Theory]
    [InlineData("../library.json")]
    [InlineData("file.png")]
    [InlineData("C:\\secret.png")]
    [InlineData("foo:bar")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png\n")]
    public void NamesCannotEscapeTheCoverDirectory(string name)
    {
        Assert.False(LibraryCoverStore.IsCoverName(name));
        Assert.Throws<ArgumentException>(() => Store.Read(name));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ImportedContentMustBePngWithValidChecksumsAndCompletePixelData()
    {
        Assert.Throws<ArgumentException>(() => Store.Import(Png(), "image/jpeg"));
        Assert.Throws<ArgumentException>(() => Store.Import(Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>"), "image/png"));
        var brokenCrc = Png(); brokenCrc[20] ^= 1;
        Assert.Throws<ArgumentException>(() => Store.Import(brokenCrc, "image/png"));
        var truncated = Png()[..^1];
        Assert.Throws<ArgumentException>(() => Store.Import(truncated, "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(pixels: [0, 255]), "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(pixels: [0, 1, 2, 3, 4, 99]), "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(pixels: [5, 1, 2, 3, 4]), "image/png"));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ImportLimitsBothUploadSizeAndDecompressedDimensions()
    {
        Assert.Throws<ArgumentException>(() => Store.Import(new byte[LibraryCoverStore.MaxBytes + 1], "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(width: 4097), "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(height: 4097), "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(width: 0), "image/png"));
    }

    [Fact]
    public void AlphaPngImportsAndUnsupportedEncodingRequestsConversion()
    {
        Assert.NotNull(Store.Import(Png(), "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(interlace: 1), "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(depth: 16), "image/png"));
        Assert.Throws<ArgumentException>(() => Store.Import(Png(color: 3), "image/png"));
    }

    [Fact]
    public void StoredCorruptionCannotBeServedOrOverwrittenAsAnImmutableCover()
    {
        var imported = Store.Import(Png(), "image/png");
        var name = imported.CoverArt.Split('/').Last();
        File.WriteAllText(Store.FindPath(name)!, "changed");
        Assert.Throws<InvalidDataException>(() => Store.Read(name));
        Assert.Throws<InvalidDataException>(() => Store.Import(Png(), "image/png"));
    }

    [Fact]
    public async Task CoverApiImportsAndServesValidatedBytesWithSafeHeaders()
    {
        var builder = WebApplication.CreateBuilder(new[] { "--urls", "http://127.0.0.1:0" });
        builder.Services.AddSingleton(Store);
        await using var app = builder.Build();
        LibraryCoverApi.Map(app);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var body = new ByteArrayContent(Png()); body.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await client.PostAsync("/api/library/covers", body);
        response.EnsureSuccessStatusCode();
        var imported = await response.Content.ReadFromJsonAsync<ImportedLibraryCover>();
        var image = await client.GetAsync(imported!.CoverArt);
        image.EnsureSuccessStatusCode();
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Contains("immutable", image.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/library/covers/no.png")).StatusCode);
        using var svg = new StringContent("<svg/>", Encoding.UTF8, "image/svg+xml");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/library/covers", svg)).StatusCode);
        using var large = new ByteArrayContent(new byte[LibraryCoverStore.MaxBytes + 1]);
        large.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var rejected = await client.PostAsync("/api/library/covers", large);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        Assert.Contains("Cover uploads must be at most 5 MB", await rejected.Content.ReadAsStringAsync());
        using var chunked = new ChunkedContent(new byte[LibraryCoverStore.MaxBytes + 1]);
        chunked.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var chunkedRejected = await client.PostAsync("/api/library/covers", chunked);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunkedRejected.StatusCode);
        Assert.Contains("Cover uploads must be at most 5 MB", await chunkedRejected.Content.ReadAsStringAsync());
        await app.StopAsync();
    }

    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();
    }

    internal static byte[] Png(uint width = 1, uint height = 1, byte depth = 8, byte color = 6, byte interlace = 0, byte[]? pixels = null, string? metadata = null)
    {
        using var output = new MemoryStream(); output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth; header[9] = color; header[12] = interlace;
        Chunk(output, "IHDR", header);
        if (metadata is not null) Chunk(output, "tEXt", Encoding.UTF8.GetBytes("Comment\0" + metadata));
        using var compressed = new MemoryStream();
        using (var encoder = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) encoder.Write(pixels ?? [0, 100, 120, 140, 80]);
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
        return output.ToArray();
    }
    private static void Chunk(Stream stream, string type, byte[] bytes)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); stream.Write(number);
        var body = Encoding.ASCII.GetBytes(type).Concat(bytes).ToArray(); stream.Write(body);
        var crc = uint.MaxValue;
        foreach (var value in body) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320u : crc >> 1; }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
