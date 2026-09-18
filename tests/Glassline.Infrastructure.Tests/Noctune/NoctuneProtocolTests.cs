using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Glassline.Infrastructure.Noctune;

namespace Glassline.Infrastructure.Tests.Noctune;

public sealed class NoctuneProtocolTests
{
    [Fact]
    public async Task WriteMessageAsyncWritesLittleEndianLengthAndUtf8Json()
    {
        await using var stream = new MemoryStream();

        await NoctuneProtocol.WriteMessageAsync(
            stream,
            new { version = 1, type = "snapshot" },
            TestContext.Current.CancellationToken);

        var bytes = stream.ToArray();
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, sizeof(int)));
        Assert.Equal(bytes.Length - sizeof(int), length);

        using var document = JsonDocument.Parse(bytes.AsMemory(sizeof(int), length));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("snapshot", document!.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ReadMessageAsyncReadsPrefixAndPayloadAcrossPartialReads()
    {
        var json = Encoding.UTF8.GetBytes("{\"version\":1,\"type\":\"snapshot\",\"payload\":{}}");
        var framed = new byte[sizeof(int) + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(framed, json.Length);
        json.CopyTo(framed.AsSpan(sizeof(int)));
        await using var stream = new ChunkedReadStream(framed, maximumChunkSize: 2);

        using var document = await NoctuneProtocol.ReadMessageAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        Assert.Equal("snapshot", document!.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(NoctuneProtocol.MaximumMessageSize + 1)]
    public async Task ReadMessageAsyncRejectsInvalidLengths(int length)
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        await using var stream = new MemoryStream(prefix);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            _ = await NoctuneProtocol.ReadMessageAsync(
                stream,
                TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task ReadMessageAsyncReturnsNullAtCleanEndOfStream()
    {
        await using var stream = new MemoryStream();

        var document = await NoctuneProtocol.ReadMessageAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Null(document);
    }

    private sealed class ChunkedReadStream(byte[] contents, int maximumChunkSize)
        : MemoryStream(contents)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumChunkSize)], cancellationToken);
    }
}
