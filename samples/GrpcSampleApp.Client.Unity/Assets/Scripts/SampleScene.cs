using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Grpc.Net.Client;
using GrpcSampleApp.Server;
using GrpcWebSocketBridge.Client;
using UnityEngine;
using UnityEngine.UI;

namespace GrpcSampleApp.Client.Unity
{
    public class SampleScene : MonoBehaviour
    {
        public Text TextLog;
        public Button ButtonCallUnary;
        public Button ButtonCallDuplex;

        public void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var builder = new UriBuilder(GrpcSampleAppJsInterop.GetCurrentLocation());
            builder.Path = "/";
            var endpoint = builder.ToString();
#else
            var endpoint = "http://localhost:5172";
#endif

            ButtonCallUnary.OnClickAsAsyncEnumerable(this.GetCancellationTokenOnDestroy())
                .SubscribeAwait(async x =>
                {
                    try
                    {
                        ButtonCallDuplex.enabled = ButtonCallUnary.enabled = false;
                        TextLog.text = "Call Unary" + "\r\n";
                        var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions()
                        {
                            // NOTE: Use `GrpcWebSocketBridgeHandler` instead of HttpSocketHandler.
                            HttpHandler = new GrpcWebSocketBridgeHandler(),
                        });

                        var greeter = new Greeter.GreeterClient(channel);
                        var request = new HelloRequest() { Name = $"User@{DateTimeOffset.Now}" };
                        TextLog.text += $"[Client] --> [Server]: SayHelloAsync(Name = {request.Name})" + "\r\n";
                        var response = await greeter.SayHelloAsync(request);
                        TextLog.text += "[Server] --> [Client]: " + response.Message + "\r\n";
                        TextLog.text += "Done." + "\r\n";
                    }
                    finally
                    {
                        ButtonCallDuplex.enabled = ButtonCallUnary.enabled = true;
                    }
                });

            ButtonCallDuplex.OnClickAsAsyncEnumerable(this.GetCancellationTokenOnDestroy())
                .SubscribeAwait(async x =>
                {
                    try
                    {
                        ButtonCallDuplex.enabled = ButtonCallUnary.enabled = false;
                        TextLog.text = "Call Duplex" + "\r\n";
                        var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions()
                        {
                            // NOTE: Use `GrpcWebSocketBridgeHandler` instead of HttpSocketHandler.
                            HttpHandler = new GrpcWebSocketBridgeHandler(),
                        });

                        var greeter = new Greeter.GreeterClient(channel);
                        var duplexStreaming = greeter.SayHelloDuplex();

                        var ct = this.GetCancellationTokenOnDestroy();
                        var readerTask = UniTask.Create(async () =>
                        {
                            while (await duplexStreaming.ResponseStream.MoveNext(ct))
                            {
                                TextLog.text += "[Server] --> [Client]: " + duplexStreaming.ResponseStream.Current.Message + "\r\n";
                            }
                        });
                        var writerTask = UniTask.Create(async () =>
                        {
                            for (var i = 0; i < 5; i++)
                            {
                                var request = new HelloRequest() { Name = $"User{i}@{DateTimeOffset.Now}" };
                                TextLog.text += $"[Client] --> [Server]: HelloRequest(Name = {request.Name})" + "\r\n";
                                await duplexStreaming.RequestStream.WriteAsync(request);
                                await UniTask.Delay(1000, cancellationToken: ct);
                            }

                            await duplexStreaming.RequestStream.CompleteAsync();
                        });

                        await UniTask.WhenAll(readerTask, writerTask);
                        TextLog.text += "Done." + "\r\n";
                    }
                    finally
                    {
                        ButtonCallDuplex.enabled = ButtonCallUnary.enabled = true;
                    }
                });
        }
    }
}

