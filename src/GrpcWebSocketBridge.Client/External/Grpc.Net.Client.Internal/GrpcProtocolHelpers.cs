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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http.Headers;
using Grpc.Core;

namespace Grpc.Net.Client.Internal
{
    internal static class GrpcProtocolHelpers
    {
        public static byte[] ParseBinaryHeader(string base64)
        {
            string decodable;
            switch (base64.Length % 4)
            {
                case 0:
                    // base64 has the required padding 
                    decodable = base64;
                    break;
                case 2:
                    // 2 chars padding
                    decodable = base64 + "==";
                    break;
                case 3:
                    // 3 chars padding
                    decodable = base64 + "=";
                    break;
                default:
                    // length%4 == 1 should be illegal
                    throw new FormatException("Invalid Base-64 header value.");
            }

            return Convert.FromBase64String(decodable);
        }

        public static Metadata BuildMetadata(HttpHeaders responseHeaders)
        {
            var headers = new Metadata();

            foreach (var header in responseHeaders)
            {
                if (ShouldSkipHeader(header.Key))
                {
                    continue;
                }

                foreach (var value in header.Value)
                {
                    if (header.Key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        headers.Add(header.Key, ParseBinaryHeader(value));
                    }
                    else
                    {
                        headers.Add(header.Key, value);
                    }
                }
            }

            return headers;
        }

        internal static bool ShouldSkipHeader(string name)
        {
            if (name.Length == 0)
            {
                return false;
            }

            switch (name[0])
            {
                case ':':
                    // ASP.NET Core includes pseudo headers in the set of request headers
                    // whereas, they are not in gRPC implementations. We will filter them
                    // out when we construct the list of headers on the context.
                    return true;
                case 'g':
                case 'G':
                    // Exclude known grpc headers. This matches Grpc.Core client behavior.
                    return string.Equals(name, GrpcProtocolConstants.StatusTrailer, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, GrpcProtocolConstants.MessageTrailer, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, GrpcProtocolConstants.MessageEncodingHeader, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, GrpcProtocolConstants.MessageAcceptEncodingHeader, StringComparison.OrdinalIgnoreCase);
                case 'c':
                case 'C':
                    // Exclude known HTTP headers. This matches Grpc.Core client behavior.
                    return string.Equals(name, "content-encoding", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, "content-type", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

#if NETSTANDARD2_0
        public static string GetHeaderValue(HttpHeaders headers, string name)
#else
        public static string GetHeaderValue(HttpHeaders? headers, string name)
#endif
        {
            if (headers == null)
            {
                return null;
            }

            if (!headers.TryGetValues(name, out var values))
            {
                return null;
            }

            // HttpHeaders appears to always return an array, but fallback to converting values to one just in case
            var valuesArray = values as string[] ?? values.ToArray();

            switch (valuesArray.Length)
            {
                case 0:
                    return null;
                case 1:
                    return valuesArray[0];
                default:
                    throw new InvalidOperationException($"Multiple {name} headers.");
            }
        }

#if NETSTANDARD2_0
        public static bool TryGetStatusCore(HttpHeaders headers, out Status? status)
#else
        public static bool TryGetStatusCore(HttpHeaders headers, [NotNullWhen(true)] out Status? status)
#endif
        {
            var grpcStatus = GrpcProtocolHelpers.GetHeaderValue(headers, GrpcProtocolConstants.StatusTrailer);

            // grpc-status is a required trailer
            if (grpcStatus == null)
            {
                status = null;
                return false;
            }

            int statusValue;
            if (!int.TryParse(grpcStatus, out statusValue))
            {
                throw new InvalidOperationException("Unexpected grpc-status value: " + grpcStatus);
            }

            // grpc-message is optional
            // Always read the gRPC message from the same headers collection as the status
            var grpcMessage = GrpcProtocolHelpers.GetHeaderValue(headers, GrpcProtocolConstants.MessageTrailer);

            if (!string.IsNullOrEmpty(grpcMessage))
            {
                // https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#responses
                // The value portion of Status-Message is conceptually a Unicode string description of the error,
                // physically encoded as UTF-8 followed by percent-encoding.
                grpcMessage = Uri.UnescapeDataString(grpcMessage);
            }

            status = new Status((StatusCode)statusValue, grpcMessage);
            return true;
        }

    }
}
