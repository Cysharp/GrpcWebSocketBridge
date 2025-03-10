using System.Buffers;
using System.Text;

namespace GrpcWebSocketBridge.Tests.UnitTests;

public class GrpcWebSocketBufferReaderTest : TimeoutTestBase
{
    [Fact]
    public async Task ReadEmpty()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        arrayBuffer.Write([
        ]);

        reader.TryRead(arrayBuffer.WrittenMemory, out var result).ShouldBeFalse();
    }

    [Fact]
    public async Task ReadContent()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        // gRPC data payload
        arrayBuffer.Write(new byte[]
        {
            // No-compression
            0b00000000,
            // 5
            0x00, 0x00, 0x00, 0x0b,
            // "Hello"
            0x48, 0x65, 0x6c, 0x6c, 0x6f
        });

        reader.TryRead(arrayBuffer.WrittenMemory, out var result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(1 + 4);

        reader.TryRead(arrayBuffer.WrittenMemory.Slice(result.Consumed), out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(5);
    }

    [Fact]
    public async Task ReadSmallContent()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        // gRPC data payload
        arrayBuffer.Write(new byte[]
        {
            // No-compression
            0b00000000,
            // 1
            0x00, 0x00, 0x00, 0x0b,
            // "A"
            0x41
        });

        reader.TryRead(arrayBuffer.WrittenMemory, out var result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(1 + 4);

        reader.TryRead(arrayBuffer.WrittenMemory.Slice(result.Consumed), out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(1);
    }

    [Fact]
    public async Task ReadHeaders()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        // Header(Trailer)
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 22
            0x00, 0x00, 0x00, 0x16,
            // "Foo: Bar" + "\r\n" + "x-hoge: fuga"
            0x46, 0x6f, 0x6f, 0x3a, 0x20, 0x42, 0x61, 0x72, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });
        // gRPC data payload
        arrayBuffer.Write(new byte[]
        {
            // No-compression
            0b00000000,
            // 11
            0x00, 0x00, 0x00, 0x0b,
            // "Hello Alice"
            0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x20, 0x41, 0x6c, 0x69, 0x63, 0x65,
        });

        // Read the header of gRPC data payload.
        reader.TryRead(arrayBuffer.WrittenMemory, out var result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Header);
        result.Consumed.ShouldBe(1 + 4 + 22);

        var headers = result.HeadersOrTrailers;
        headers.ShouldNotBeNull();
        headers.ShouldContain(x => x.Key == "Foo");
        headers.ShouldContain(x => x.Key == "x-hoge");
    }

    [Fact]
    public async Task ReadHeadersDuplicateKey()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        // Header(Trailer)
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 36
            0x00, 0x00, 0x00, 0x24,
            // "Foo: Bar" + "\r\n" + "x-hoge: fuga" + "\r\n" + "x-hoge: hoge"
            0x46, 0x6f, 0x6f, 0x3a, 0x20, 0x42, 0x61, 0x72,
            0x0d, 0x0a,
            0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
            0x0d, 0x0a,
            0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x68, 0x6f, 0x67, 0x65,
        });
        // gRPC data payload
        arrayBuffer.Write(new byte[]
        {
            // No-compression
            0b00000000,
            // 11
            0x00, 0x00, 0x00, 0x0b,
            // "Hello Alice"
            0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x20, 0x41, 0x6c, 0x69, 0x63, 0x65,
        });

        // Read the header of gRPC data payload.
        reader.TryRead(arrayBuffer.WrittenMemory, out var result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Header);
        result.Consumed.ShouldBe(1 + 4 + 36);

        var headers = result.HeadersOrTrailers;
        headers.ShouldNotBeNull();
        headers.ShouldContain(x => x.Key == "Foo");
        headers.ShouldContain(x => x.Key == "x-hoge");
        headers.GetValues("x-hoge").ShouldBe(["fuga", "hoge"]);
    }

    [Fact]
    public async Task ReadIncompleteHeaders()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        // Header (Partial)
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 22
            0x00, 0x00, 0x00, 0x16,
        });
        // Read the header of gRPC data payload.
        reader.TryRead(arrayBuffer.WrittenMemory, out var result).ShouldBeFalse();

        // Remain
        arrayBuffer.Write(new byte[]
        {
            // "Foo: Bar" + "\r\n" + "x-hoge: fuga"
            0x46, 0x6f, 0x6f, 0x3a, 0x20, 0x42, 0x61, 0x72, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });
        reader.TryRead(arrayBuffer.WrittenMemory, out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Header);
        result.Consumed.ShouldBe(1 + 4 + 22);

        var headers = result.HeadersOrTrailers;
        headers.ShouldNotBeNull();
        headers.ShouldContain(x => x.Key == "Foo");
        headers.ShouldContain(x => x.Key == "x-hoge");
    }

    [Fact]
    public async Task ReadHeadersAndFirstContent()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        // Header(Trailer)
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 22
            0x00, 0x00, 0x00, 0x16,
            // "Foo: Bar" + "\r\n" + "x-hoge: fuga"
            0x46, 0x6f, 0x6f, 0x3a, 0x20, 0x42, 0x61, 0x72, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });
        // gRPC data payload
        arrayBuffer.Write(new byte[]
        {
            // No-compression
            0b00000000,
            // 11
            0x00, 0x00, 0x00, 0x0b,
            // "Hello Alice"
            0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x20, 0x41, 0x6c, 0x69, 0x63, 0x65,
        });
        // Trailer
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 28
            0x00, 0x00, 0x00, 0x1c,
            // "grpc-status: 0" + "\r\n" + "x-hoge: fuga"
            0x67, 0x72, 0x70, 0x63, 0x2d, 0x73, 0x74, 0x61, 0x74, 0x75, 0x73, 0x3a, 0x20, 0x30, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });

        // Read the header of gRPC data payload.
        var memory = arrayBuffer.WrittenMemory;
        reader.TryRead(memory, out var result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Header);
        result.Consumed.ShouldBe(1 + 4 + 22);

        var headers = result.HeadersOrTrailers;
        headers.ShouldNotBeNull();
        headers.ShouldContain(x => x.Key == "Foo");
        headers.ShouldContain(x => x.Key == "x-hoge");

        // Advance
        memory = memory.Slice(result.Consumed);

        // Read the data content.
        reader.TryRead(memory, out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(1 + 4);

        // Advance
        memory = memory.Slice(result.Consumed);

        reader.TryRead(memory, out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(11);
        Encoding.UTF8.GetString(result.Data.Span).ShouldBe("Hello Alice");
    }

    [Fact]
    public async Task ReadHeadersAndFirstContentAndTrailers()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();

        // Header(Trailer)
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 22
            0x00, 0x00, 0x00, 0x16,
            // "Foo: Bar" + "\r\n" + "x-hoge: fuga"
            0x46, 0x6f, 0x6f, 0x3a, 0x20, 0x42, 0x61, 0x72, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });
        // gRPC data payload
        arrayBuffer.Write(new byte[]
        {
            // No-compression
            0b00000000,
            // 11
            0x00, 0x00, 0x00, 0x0b,
            // "Hello Alice"
            0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x20, 0x41, 0x6c, 0x69, 0x63, 0x65,
        });
        // Trailer
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 28
            0x00, 0x00, 0x00, 0x1c,
            // "grpc-status: 0" + "\r\n" + "x-hoge: fuga"
            0x67, 0x72, 0x70, 0x63, 0x2d, 0x73, 0x74, 0x61, 0x74, 0x75, 0x73, 0x3a, 0x20, 0x30, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });

        // Read the header of gRPC data payload.
        var memory = arrayBuffer.WrittenMemory;
        reader.TryRead(memory, out var result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Header);
        result.Consumed.ShouldBe(1 + 4 + 22);

        var headers = result.HeadersOrTrailers;
        headers.ShouldNotBeNull();
        headers.ShouldContain(x => x.Key == "Foo");
        headers.ShouldContain(x => x.Key == "x-hoge");

        // Advance
        memory = memory.Slice(result.Consumed);

        // Read the data content.
        reader.TryRead(memory, out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(1 + 4);

        // Advance
        memory = memory.Slice(result.Consumed);

        reader.TryRead(memory, out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(11);
        Encoding.UTF8.GetString(result.Data.Span).ShouldBe("Hello Alice");

        // Advance
        memory = memory.Slice(result.Consumed);

        reader.TryRead(memory, out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Trailer);
        result.Consumed.ShouldBe(1 + 4 + 28);

        var trailers = result.HeadersOrTrailers;
        trailers.ShouldNotBeNull();
        trailers.ShouldContain(x => x.Key == "grpc-status");
        trailers.ShouldContain(x => x.Key == "x-hoge");

        // Advance
        memory = memory.Slice(result.Consumed);

        // End of stream
        Should.Throw<InvalidOperationException>(() => reader.TryRead(memory, out result));
    }

    [Fact]
    public async Task ReadChunkedContent()
    {
        var arrayBuffer = new ArrayBufferWriter<byte>();
        var reader = new GrpcWebSocketBufferReader();
        var consumed = 0;

        // Header(Trailer)
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 22
            0x00, 0x00, 0x00, 0x16,
            // "Foo: Bar" + "\r\n" + "x-hoge: fuga"
            0x46, 0x6f, 0x6f, 0x3a, 0x20, 0x42, 0x61, 0x72, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });

        // Read the header of gRPC data payload.
        var memory = arrayBuffer.WrittenMemory;
        reader.TryRead(memory, out var result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Header);
        result.Consumed.ShouldBe(1 + 4 + 22);
        consumed += result.Consumed;

        // gRPC data payload
        arrayBuffer.Write(new byte[]
        {
            // No-compression
            0b00000000,
        });
        reader.TryRead(arrayBuffer.WrittenMemory.Slice(consumed), out result).ShouldBeFalse();

        arrayBuffer.Write(new byte[]
        {
            // 11
            0x00, 0x00, 0x00, 0x0b,
        });

        reader.TryRead(arrayBuffer.WrittenMemory.Slice(consumed), out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(1 + 4);
        consumed += result.Consumed;

        arrayBuffer.Write(new byte[]
        {
            // "Hello"
            0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x20,
        });
        reader.TryRead(arrayBuffer.WrittenMemory.Slice(consumed), out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(6);
        consumed += result.Consumed;

        arrayBuffer.Write(new byte[]
        {
            // "Alice"
            0x41, 0x6c, 0x69, 0x63, 0x65,
        });
        reader.TryRead(arrayBuffer.WrittenMemory.Slice(consumed), out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Content);
        result.Consumed.ShouldBe(5);
        consumed += result.Consumed;

        // Trailer
        arrayBuffer.Write(new byte[]
        {
            // IsTrailer, No-compression
            0b10000000,
            // 28
            0x00, 0x00, 0x00, 0x1c,
            // "grpc-status: 0" + "\r\n" + "x-hoge: fuga"
            0x67, 0x72, 0x70, 0x63, 0x2d, 0x73, 0x74, 0x61, 0x74, 0x75, 0x73, 0x3a, 0x20, 0x30, 0x0d, 0x0a, 0x78, 0x2d, 0x68, 0x6f, 0x67, 0x65, 0x3a, 0x20, 0x66, 0x75, 0x67, 0x61,
        });

        reader.TryRead(arrayBuffer.WrittenMemory.Slice(consumed), out result).ShouldBeTrue();
        result.Type.ShouldBe(GrpcWebSocketBufferReader.BufferReadResultType.Trailer);
        result.Consumed.ShouldBe(1 + 4 + 28);
        consumed += result.Consumed;

        // Completed
        Should.Throw<InvalidOperationException>(() => reader.TryRead(memory, out result));
    }
}
