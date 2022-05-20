using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Grpc.Net.Client;
using GrpcWebSocketBridge.Client;
using MagicOnion.Client;
using MagicOnionSampleApp.Shared.Hubs;
using UnityEngine;
using UnityEngine.UI;

namespace MagicOnionSampleApp.Client.Unity
{
    public class SampleScene : MonoBehaviour
    {
        private SampleSceneViewState _viewState;

        public GameObject PanelConnect;
        public InputField InputNickname;
        public Button ButtonConnect;

        public GameObject PanelConnecting;

        public GameObject PanelSendMessage;
        public InputField InputMessage;
        public Button ButtonSendMessage;

        public MessageItem PrefabMessageItem;
        public GameObject MessagesContainer;

        private void Start()
        {
            _viewState = new SampleSceneViewState();

            ButtonConnect.OnClickAsAsyncEnumerable()
                .Subscribe(_ => _viewState.ConnectAsync())
                .AddTo(this.GetCancellationTokenOnDestroy());
            InputNickname.OnValueChangedAsAsyncEnumerable()
                .Where(x => _viewState.InputNickname.Value != x)
                .Subscribe(x => _viewState.InputNickname.Value = x)
                .AddTo(this.GetCancellationTokenOnDestroy());
            _viewState.InputNickname
                .Subscribe(x => InputNickname.text = x)
                .AddTo(this.GetCancellationTokenOnDestroy());

            _viewState.State
                .Subscribe(x =>
                {
                    PanelConnect.SetActive(x == ConnectionState.Disconnected);
                    PanelConnecting.SetActive(x == ConnectionState.Connecting);
                    PanelSendMessage.SetActive(x == ConnectionState.Connected);
                })
                .AddTo(this.GetCancellationTokenOnDestroy());

            InputMessage.OnValueChangedAsAsyncEnumerable()
                .Where(x => _viewState.InputMessage.Value != x)
                .Subscribe(x => _viewState.InputMessage.Value = x)
                .AddTo(this.GetCancellationTokenOnDestroy());
            InputMessage.OnEndEditAsAsyncEnumerable()
                .SubscribeAwait(async _ => await _viewState.SendAsync())
                .AddTo(this.GetCancellationTokenOnDestroy());
            _viewState.InputMessage
                .Subscribe(x => InputMessage.text = x)
                .AddTo(this.GetCancellationTokenOnDestroy());

            ButtonSendMessage.OnClickAsAsyncEnumerable()
                .SubscribeAwait(async _ => await _viewState.SendAsync())
                .AddTo(this.GetCancellationTokenOnDestroy());

            _viewState.Messages
                .Subscribe(x =>
                {
                    foreach (Transform child in MessagesContainer.transform)
                    {
                        Destroy(child.gameObject);
                    }

                    foreach (var message in x)
                    {
                        var messageItem = Instantiate(PrefabMessageItem);
                        messageItem.Initialize(message);
                        messageItem.transform.SetParent(MessagesContainer.transform);
                        messageItem.transform.SetAsFirstSibling();
                    }
                })
                .AddTo(this.GetCancellationTokenOnDestroy());
        }
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    public class SampleSceneViewState : IChatHubReceiver
    {
        private IChatHub _chatHub;

        private readonly AsyncReactiveProperty<ConnectionState> _state = new AsyncReactiveProperty<ConnectionState>(ConnectionState.Disconnected);
        public IReadOnlyAsyncReactiveProperty<ConnectionState> State => _state;
        public AsyncReactiveProperty<string> InputMessage { get; } = new AsyncReactiveProperty<string>("");
        public AsyncReactiveProperty<string> InputNickname { get; } = new AsyncReactiveProperty<string>(GetRandomNickname());

        private readonly AsyncReactiveProperty<IReadOnlyList<(Guid MessageId, DateTimeOffset ReceivedAt, string Nickname, string Message)>> _messages = new AsyncReactiveProperty<IReadOnlyList<(Guid MessageId, DateTimeOffset ReceivedAt, string Nickname, string Message)>>(Array.Empty<(Guid MessageId, DateTimeOffset ReceivedAt, string Nickname, string Message)>());
        public IReadOnlyAsyncReactiveProperty<IReadOnlyList<(Guid MessageId, DateTimeOffset ReceivedAt, string Nickname, string Message)>> Messages => _messages;

        public async UniTaskVoid ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(InputNickname.Value)) return;
            _state.Value = ConnectionState.Connecting;

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var builder = new UriBuilder(GrpcSampleApp.Client.Unity.GrpcSampleAppJsInterop.GetCurrentLocation());
                builder.Path = "/";
                var endpoint = builder.ToString();
#else
                var endpoint = "http://localhost:7069";
#endif
                var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions()
                {
                    // NOTE: Use `GrpcWebSocketBridgeHandler` instead of HttpSocketHandler.
                    HttpHandler = new GrpcWebSocketBridgeHandler(),
                });
                _chatHub = await StreamingHubClient.ConnectAsync<IChatHub, IChatHubReceiver>(channel, this);
                await _chatHub.JoinAsync(InputNickname.Value);

            }
            catch (Exception e)
            {
                AddMessage("<Exception>", e.ToString());
                _state.Value = ConnectionState.Disconnected;
                return;
            }
            _state.Value = ConnectionState.Connected;

            AddMessage("<System>", "Connected.");

            WaitForDisconnected().Forget();

            async UniTaskVoid WaitForDisconnected()
            {
                await _chatHub.WaitForDisconnect();
                AddMessage("<System>", "Disconnected.");
                _state.Value = ConnectionState.Disconnected;
            }
        }

        public async UniTask SendAsync()
        {
            if (string.IsNullOrWhiteSpace(InputMessage.Value)) return;
            try
            {
                await _chatHub.SendAsync(InputMessage.Value);
                InputMessage.Value = "";
            }
            catch (Exception e)
            {
                AddMessage("<Exception>", e.ToString());
            }
        }

        private void AddMessage(string nickname, string message)
            => _messages.Value = _messages.Value.Append((Guid.NewGuid(), DateTimeOffset.Now, nickname, message)).ToArray();

        private static string GetRandomNickname()
            => new[] { "Normal", "Rare", "SuperRare", "UltraRare" }.OrderBy(x => Guid.NewGuid()).First() +
               new[] { "Dog", "Cat", "Fish", "Monkey", "Bird" }.OrderBy(x => Guid.NewGuid()).First();

        public void OnMemberJoined(string nickName)
            => AddMessage("<System>", $"{nickName} has joined.");

        public void OnMessageReceived(string nickName, string message)
            => AddMessage(nickName, message);

        public void OnMemberLeft(string nickName)
            => AddMessage("<System>", $"{nickName} has left.");
    }
}
