#if UNITY_2018_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace GrpcWebSocketBridge.Client.Unity
{
    public class UnityWebRequestHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var unityWebRequest = new UnityWebRequest(request.RequestUri, request.Method.Method);
            foreach (var header in request.Headers)
            {
                try
                {
                    unityWebRequest.SetRequestHeader(header.Key, string.Join(", ", header.Value));
                }
                catch (InvalidOperationException)
                {
                    // Ignore: InvalidOperationException: Cannot override system-specified headers 
                }
            }
            foreach (var header in request.Content.Headers)
            {
                try
                {
                    unityWebRequest.SetRequestHeader(header.Key, string.Join(", ", header.Value));
                }
                catch (InvalidOperationException)
                {
                    // Ignore: InvalidOperationException: Cannot override system-specified headers 
                }
            }

            unityWebRequest.uploadHandler = new UploadHandlerRaw(await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            unityWebRequest.downloadHandler = new DownloadHandlerBuffer();

            await unityWebRequest.SendWebRequest();

            var response = new HttpResponseMessage((HttpStatusCode)(int)unityWebRequest.responseCode)
            {
                RequestMessage = request,
                Version = HttpVersion.Version11,
                Content = new ByteArrayContent(unityWebRequest.downloadHandler.data),
            };

            foreach (var responseHeader in unityWebRequest.GetResponseHeaders())
            {
                if (responseHeader.Key.StartsWith("content-", StringComparison.OrdinalIgnoreCase))
                {
                    response.Content.Headers.TryAddWithoutValidation(responseHeader.Key, responseHeader.Value);
                }
                else
                {
                   response.Headers.TryAddWithoutValidation(responseHeader.Key, responseHeader.Value);
                }
            }

            return response;
        }
    }
}
#endif
