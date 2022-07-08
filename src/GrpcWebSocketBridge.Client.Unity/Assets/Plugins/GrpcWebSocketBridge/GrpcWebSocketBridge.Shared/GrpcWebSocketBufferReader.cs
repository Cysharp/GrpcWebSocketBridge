#region Copyright notice and license

// Copyright 2021 Cysharp, Inc.
// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

// NET_STANDARD is .NET Standard 2.1 on Unity
#if NET_STANDARD_2_0
#define NETSTANDARD2_0
#endif
#if NET_STANDARD || NET_STANDARD_2_1
#define NETSTANDARD2_1
#undef NETSTANDARD2_0 // NOTE: Same symbols defined as in .NET SDK
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
#endif

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcWebSocketBridge
{
    // GrpcWebSocketBufferReader class is based on GrpcWebResponseStream
    // https://github.com/grpc/grpc-dotnet/blob/master/src/Grpc.Net.Client.Web/Internal/GrpcWebResponseStream.cs
    //
    // gRPC over WebSocket wire protocol:
    //   - [ 0b10000000: Headers ]
    //   - [ 0b00000000: gRPC Payload ]
    //   - ...
    //   - [ 0b00000000: gRPC Payload ]
    //   - [ 0b10000000: Trailers ]
    //
    // GrpcWebSocketBridge treats trailers as Headers for the first time.
    //
    internal struct GrpcWebSocketBufferReader
    {
        // This uses C# compiler's ability to refer to static data directly. For more information see https://vcsjones.dev/2019/02/01/csharp-readonly-span-bytes-static
        private static ReadOnlySpan<byte> BytesNewLine => new byte[] { (byte)'\r', (byte)'\n' };

        private int _contentRemaining;
        private StreamState _state;
        private bool _hasHeadersReceived;

        public enum BufferReadResultType
        {
            Unknown,
            Header,
            Content,
            Trailer,
        }

        public readonly struct BufferReadResult
        {
            public BufferReadResultType Type { get; }
#if NETSTANDARD2_0
            public HttpHeaders HeadersOrTrailers { get; }
#else
            public HttpHeaders? HeadersOrTrailers { get; }
#endif
            public ReadOnlyMemory<byte> Data { get; }
            public int Consumed { get; }

#if NETSTANDARD2_0
            public BufferReadResult(BufferReadResultType type, int consumed, HttpHeaders headersOrTrailers)
#else
            public BufferReadResult(BufferReadResultType type, int consumed, HttpHeaders? headersOrTrailers)
#endif
            {
                Type = type;
                Consumed = consumed;
                HeadersOrTrailers = headersOrTrailers;
                Data = default;
            }

            public BufferReadResult(BufferReadResultType type, int consumed, ReadOnlyMemory<byte> data)
            {
                Type = type;
                Consumed = consumed;
                HeadersOrTrailers = default;
                Data = data;
            }
        }

        public bool TryRead(ReadOnlyMemory<byte> data, out BufferReadResult result)
        {
            if (_state == StreamState.Complete)
            {
                throw new InvalidOperationException("The stream has already reached to the end. The reading state is Complete.");
            }

            if (data.Length == 0)
            {
                result = default;
                return false;
            }

            switch (_state)
            {
                case StreamState.Ready:
                    // Read the header first
                    // - 1 byte flag for compression
                    // - 4 bytes for the content length
                    ReadOnlyMemory<byte> headerBuffer;

                    if (data.Length >= 5)
                    {
                        headerBuffer = data.Slice(0, 5);
                    }
                    else
                    {
                        result = default;
                        return false;
                    }

                    var compressed = headerBuffer.Span[0];
                    var length = (int)BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.Span.Slice(1));

                    var isTrailerOrHeader = IsBitSet(compressed, pos: 7);
                    if (isTrailerOrHeader)
                    {
                        // NOTE(Cysharp): GrpcWebSocketBridge treats them as Headers for the first time.
                        if (TryReadHeaders(length, data.Slice(5), out var headersOrTrailers))
                        {
                            if (_hasHeadersReceived)
                            {
                                result = new BufferReadResult(BufferReadResultType.Trailer, 5 + length, headersOrTrailers);
                                _state = StreamState.Complete;
                            }
                            else
                            {
                                result = new BufferReadResult(BufferReadResultType.Header, 5 + length, headersOrTrailers);
                                _state = StreamState.Ready;
                                _hasHeadersReceived = true;
                            }
                            return true;
                        }

                        result = default;
                        return false;
                    }

                    _contentRemaining = length;
                    // If there is no content then state is still ready
                    _state = _contentRemaining > 0 ? StreamState.Content : StreamState.Ready;
                    result = new BufferReadResult(BufferReadResultType.Content, 5, headerBuffer);
                    return true;
                case StreamState.Content:
                    if (data.Length >= _contentRemaining)
                    {
                        data = data.Slice(0, _contentRemaining);
                    }

                    _contentRemaining -= data.Length;
                    if (_contentRemaining == 0)
                    {
                        _state = StreamState.Ready;
                    }

                    result = new BufferReadResult(BufferReadResultType.Content, data.Length, data);
                    return true;
                default:
                    throw new InvalidOperationException("Unexpected state.");
            }
        }

#if NETSTANDARD2_0
        private bool TryReadHeaders(int headerLength, ReadOnlyMemory<byte> data, out HttpHeaders headers)
#else
        private bool TryReadHeaders(int headerLength, ReadOnlyMemory<byte> data, out HttpHeaders? headers)
#endif
        {
            var newHeaders = new GrpcWebSocketHttpHeaders();
            if (headerLength > 0)
            {
                if (headerLength > data.Length)
                {
                    headers = default;
                    return false;
                }
                else if (headerLength < data.Length)
                {
                    data = data.Slice(0, headerLength);
                }

                ParseHeaders(newHeaders, data.Span);
            }

            headers = newHeaders;
            return true;
        }

        private static void ParseHeaders(HttpHeaders headers, ReadOnlySpan<byte> span)
        {
            // Key-value pairs encoded as a HTTP/1 headers block (without the terminating newline),
            // per https://tools.ietf.org/html/rfc7230#section-3.2
            //
            // This parsing logic doesn't support line folding.
            //
            // JavaScript gRPC-Web trailer parsing logic for comparison:
            // https://github.com/grpc/grpc-web/blob/55ebde4719c7ad5e58aaa5205cdbd77a76ea9de3/javascript/net/grpc/web/grpcwebclientreadablestream.js#L292-L309

            var remainingContent = span;
            while (remainingContent.Length > 0)
            {
                ReadOnlySpan<byte> line;

                var lineEndIndex = remainingContent.IndexOf(BytesNewLine);
                if (lineEndIndex == -1)
                {
                    line = remainingContent;
                    remainingContent = ReadOnlySpan<byte>.Empty;
                }
                else
                {
                    line = remainingContent.Slice(0, lineEndIndex);
                    remainingContent = remainingContent.Slice(lineEndIndex + 2);
                }

                if (line.Length > 0)
                {
                    var headerDelimiterIndex = line.IndexOf((byte)':');
                    if (headerDelimiterIndex == -1)
                    {
                        throw new InvalidOperationException("Error parsing badly formatted trailing header.");
                    }

                    var name = GetString(Trim(line.Slice(0, headerDelimiterIndex)));
                    var value = GetString(Trim(line.Slice(headerDelimiterIndex + 1)));

                    headers.Add(name, value);
                }
            }
        }

        private static string GetString(ReadOnlySpan<byte> span)
        {
#if NETSTANDARD2_0
            return Encoding.ASCII.GetString(span.ToArray());
#else
            return Encoding.ASCII.GetString(span);
#endif
        }

        internal static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
        {
            var startIndex = -1;
            for (var i = 0; i < span.Length; i++)
            {
                if (!char.IsWhiteSpace((char)span[i]))
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex == -1)
            {
                return ReadOnlySpan<byte>.Empty;
            }

            var endIndex = span.Length - 1;
            for (var i = endIndex; i >= startIndex; i--)
            {
                if (!char.IsWhiteSpace((char)span[i]))
                {
                    endIndex = i;
                    break;
                }
            }

            return span.Slice(startIndex, (endIndex - startIndex) + 1);
        }

        private static bool IsBitSet(byte b, int pos)
        {
            return ((b >> pos) & 1) != 0;
        }

        private enum StreamState
        {
            Ready,
            Content,
            Complete
        }
    }

    internal class GrpcWebSocketHttpHeaders : HttpHeaders {}
}
