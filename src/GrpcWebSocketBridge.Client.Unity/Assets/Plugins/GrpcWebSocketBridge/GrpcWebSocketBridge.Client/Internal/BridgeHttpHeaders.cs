using System.Collections.Generic;
using System.Net.Http.Headers;

namespace GrpcWebSocketBridge.Client.Internal
{
    internal class BridgeHttpHeaders : HttpHeaders
    {
        public BridgeHttpHeaders(IReadOnlyDictionary<string, string> dict)
        {
            foreach (var keyValue in dict)
            {
                Add(keyValue.Key, keyValue.Value);
            }
        }
    }
}
