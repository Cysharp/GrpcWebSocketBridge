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
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Web.Internal
{
    internal static class GrpcWebProtocolHelpers
    {
        private const byte Cr = (byte)'\r';
        private const byte Lf = (byte)'\n';
        private const byte Colon = (byte)':';
        private const byte Space = (byte)' ';

        // Special trailers byte with eigth most significant bit set.
        // Parsers will use this to identify regular messages vs trailers.
        private static readonly byte TrailersSignifier = 0x80;

        private static readonly int HeaderSize = 5;

        public static async Task WriteTrailersAsync(HttpHeaders trailers, PipeWriter output)
        {
            // Flush so the last message is written as its own base64 segment
            await output.FlushAsync();

            WriteTrailers(trailers, output);

            await output.FlushAsync();
        }

        internal static void WriteTrailers(HttpHeaders trailers, IBufferWriter<byte> output)
        {
            // Precalculate trailer size. Required for trailers header metadata
            var contentSize = CalculateHeaderSize(trailers);

            var totalSize = contentSize + HeaderSize;
            var buffer = output.GetSpan(totalSize);

            WriteTrailersHeader(buffer, contentSize);
            WriteTrailersContent(buffer.Slice(HeaderSize), trailers);

            output.Advance(totalSize);
        }

        private static void WriteTrailersHeader(Span<byte> buffer, int length)
        {
            buffer[0] = TrailersSignifier;
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(1), (uint)length);
        }

        private static int CalculateHeaderSize(HttpHeaders trailers)
        {
            // Calculate the header size and validate keys and values only contain value characters.
            var total = 0;
            foreach (var kv in trailers)
            {
                var name = kv.Key;

                var invalidNameIndex = HttpCharacters.IndexOfInvalidTokenChar(name);
                if (invalidNameIndex != -1)
                {
                    ThrowInvalidHeaderCharacter(name[invalidNameIndex]);
                }

                foreach (var value in kv.Value)
                {
                    if (value != null)
                    {
                        var invalidFieldIndex = HttpCharacters.IndexOfInvalidFieldValueChar(value);
                        if (invalidFieldIndex != -1)
                        {
                            ThrowInvalidHeaderCharacter(value[invalidFieldIndex]);
                        }

                        // Key + value + 2 (': ') + 2 (\r\n)
                        total += name.Length + value.Length + 4;
                    }
                }
            }

            return total;
        }

        private static void ThrowInvalidHeaderCharacter(char ch)
        {
            throw new InvalidOperationException($"Invalid non-ASCII or control character in header: 0x{((ushort)ch):X4}");
        }

        private static void WriteTrailersContent(Span<byte> buffer, HttpHeaders trailers)
        {
            var currentBuffer = buffer;

            foreach (var kv in trailers)
            {
                foreach (var value in kv.Value)
                {
                    if (value != null)
                    {
                        // Get lower-case ASCII bytes for the key.
                        // gRPC-Web protocol says that names should be lower-case and grpc-web JS client
                        // will check for 'grpc-status' and 'grpc-message' in trailers with lower-case key.
                        // https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-WEB.md#protocol-differences-vs-grpc-over-http2
                        for (var i = 0; i < kv.Key.Length; i++)
                        {
                            char c = kv.Key[i];
                            currentBuffer[i] = (byte)((uint)(c - 'A') <= ('Z' - 'A') ? c | 0x20 : c);
                        }

                        var position = kv.Key.Length;

                        currentBuffer[position++] = Colon;
                        currentBuffer[position++] = Space;

#if NETSTANDARD2_0
                        var tmpBuffer = ArrayPool<byte>.Shared.Rent(currentBuffer.Slice(position).Length);
                        try
                        {
                            var written = Encoding.ASCII.GetBytes(value, 0, value.Length, tmpBuffer, 0);
                            tmpBuffer.AsSpan(0, written).CopyTo(currentBuffer.Slice(position));
                            position += written;
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(tmpBuffer);
                        }
#else
                        position += Encoding.ASCII.GetBytes(value, currentBuffer.Slice(position));
#endif

                        currentBuffer[position++] = Cr;
                        currentBuffer[position++] = Lf;

                        currentBuffer = currentBuffer.Slice(position);
                    }
                }
            }
        }
    }
}
