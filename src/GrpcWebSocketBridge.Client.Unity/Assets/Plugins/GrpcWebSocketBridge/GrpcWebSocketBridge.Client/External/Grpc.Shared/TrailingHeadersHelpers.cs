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
using System.Net.Http;
using System.Net.Http.Headers;

namespace Grpc.Shared
{
    internal static class TrailingHeadersHelpers
    {
        public static HttpHeaders TrailingHeaders(this HttpResponseMessage responseMessage)
        {
#if NETSTANDARD2_0
            if (responseMessage.RequestMessage.Properties.TryGetValue(ResponseTrailersKey, out var headers) &&
                headers is HttpHeaders httpHeaders)
            {
                return httpHeaders;
            }

            // App targets .NET Standard 2.0 and the handler hasn't set trailers
            // in RequestMessage.Properties with known key. Return empty collection.
            // Client call will likely fail because it is unable to get a grpc-status.
            return ResponseTrailers.Empty;
#else
            return responseMessage.TrailingHeaders;
#endif
        }

#if NETSTANDARD2_0
        public static void EnsureTrailingHeaders(this HttpResponseMessage responseMessage)
        {
            if (!responseMessage.RequestMessage.Properties.ContainsKey(ResponseTrailersKey))
            {
                responseMessage.RequestMessage.Properties[ResponseTrailersKey] = new ResponseTrailers();
            }
        }

        public static readonly string ResponseTrailersKey = "__ResponseTrailers";

        private class ResponseTrailers : HttpHeaders
        {
            public static readonly ResponseTrailers Empty = new ResponseTrailers();
        }
#endif
    }
}
