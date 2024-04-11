#if UNITY_WEBGL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;

namespace GrpcWebSocketBridge.Client.Unity
{
    public class JsClientWebSocket : IDisposable
    {
        private JsWebSocket _ws;

        public ClientWebSocketOptions Options { get; } = new ClientWebSocketOptions();
        public WebSocketState State { get; private set; } = WebSocketState.None;

        public async Task ConnectAsync(Uri url, CancellationToken cancellationToken)
        {
            _ws = new JsWebSocket(url.ToString(), string.Join(",", Options.SubProtocols));
            _ws.Connect();

            State = WebSocketState.Connecting;

            var cancelTask = new TaskCompletionSource<bool>();
            var cancellationTokenRegistration = cancellationToken.Register(x => ((TaskCompletionSource<bool>)x).SetResult(true), cancelTask, useSynchronizationContext: false);
            try
            {
                var win = await Task.WhenAny(_ws.Connected, _ws.Closed, cancelTask.Task).ConfigureAwait(false);
                if (win == _ws.Connected)
                {
                    // Connection established between the server and the client.
                    State = WebSocketState.Open;
                    WaitForClosedAsync().Forget();

                    async Task WaitForClosedAsync()
                    {
                        State = (await _ws.Closed.ConfigureAwait(false)).WasClean ? WebSocketState.Closed : WebSocketState.Aborted;
                    }
                }
                else if (win == cancelTask.Task)
                {
                    // Canceled
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    // Failed to connect to the server.
                    State = WebSocketState.Closed;
                    _ws.Dispose();
                    throw new WebSocketException(WebSocketError.NativeError, _ws.Closed.GetAwaiter().GetResult().Code, "Cannot connect to the server.");
                }

            }
            finally
            {
                cancellationTokenRegistration.Dispose();
            }
        }

        public Task SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            ThrowIfStateIsNotOpen();

            _ws.Send(buffer.ToArray());
            return Task.CompletedTask;
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            if (State == WebSocketState.Closed || State == WebSocketState.CloseSent)
            {
                return Task.CompletedTask;
            }

            ThrowIfStateIsNotOpen();

            _ws.Close((int)closeStatus, statusDescription);
            State = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public async Task<WebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            ThrowIfStateIsNotOpen();

            var cancelTask = new TaskCompletionSource<bool>();
            var cancellationTokenRegistration = cancellationToken.Register(x => ((TaskCompletionSource<bool>)x).SetResult(true), cancelTask, useSynchronizationContext: false);

            try
            {
                var receiveTask = _ws.ReceiveAsync();
                var win = await Task.WhenAny(receiveTask, _ws.Closed, cancelTask.Task).ConfigureAwait(false);
                if (win == receiveTask)
                {
                    // Received
                    receiveTask.Result.CopyTo(buffer);
                    return new WebSocketReceiveResult(receiveTask.Result.Length, WebSocketMessageType.Binary, true);
                }
                else if (win == cancelTask.Task)
                {
                    // Canceled
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    // Closed
                    ThrowIfStateIsNotOpen();
                }

                throw new WebSocketException(); // Never
            }
            finally
            {
                cancellationTokenRegistration.Dispose();
            }
        }

        private void ThrowIfStateIsNotOpen()
        {
            if (State == WebSocketState.Aborted) throw new WebSocketException("The WebSocket connection was aborted.");
            if (State != WebSocketState.Open) throw new WebSocketException("The WebSocket connection was closed.");
        }

        public void Dispose()
        {
            _ws.Dispose();
        }

        public class ClientWebSocketOptions
        {
            internal IList<string> SubProtocols { get; } = new List<string>();

            public void AddSubProtocol(string subProtocol)
            {
                SubProtocols.Add(subProtocol);
            }
        }

        public readonly struct ValueWebSocketReceiveResult
        {
            public int Count { get; }
            public bool EndOfMessage { get; }
            public WebSocketMessageType MessageType { get; }

            public ValueWebSocketReceiveResult(int count, WebSocketMessageType messageType, bool endOfMessage)
            {
                Count = count;
                MessageType = messageType;
                EndOfMessage = endOfMessage;
            }
        }
    }

    public class JsWebSocket
    {
        private static Dictionary<int, JsWebSocket> _instanceByHandle = new Dictionary<int, JsWebSocket>();
        
        private Queue<byte[]> _queue = new Queue<byte[]>();
        private TaskCompletionSource<byte[]> _receiveTcs;
        private int _handle;
        private TaskCompletionSource<bool> _connected;
        private TaskCompletionSource<(int Code, bool WasClean)> _closed;
        private bool _disposed;

        [DllImport("__Internal")]
        private static extern int JsWebSocket_Init(string url, string subProtocol);

        [DllImport("__Internal")]
        private static extern void JsWebSocket_Connect(int handle);

        [DllImport("__Internal")]
        private static extern void JsWebSocket_Send(int handle, byte[] bytes, int length);

        [DllImport("__Internal")]
        private static extern void JsWebSocket_Close(int handle, int code, string reason);

        [DllImport("__Internal")]
        private static extern void JsWebSocket_RegisterReceiveCallback(int handle, Action<int, IntPtr, int> action);

        [DllImport("__Internal")]
        private static extern int JsWebSocket_Dispose(int handle);

        [DllImport("__Internal")]
        private static extern void JsWebSocket_RegisterOnConnectedCallback(int handle, Action<int> action);
        [DllImport("__Internal")]
        private static extern void JsWebSocket_RegisterOnCloseCallback(int handle, Action<int, int, int> action);


#if UNITY_STANDALONE || UNITY_WEBGL
        [MonoPInvokeCallback(typeof(Action<int, IntPtr, int>))]
#endif
        private static void OnReceive(int handle, IntPtr bufferPtr, int length)
        {
            _instanceByHandle[handle].OnReceive(bufferPtr, length);
        }

#if UNITY_STANDALONE || UNITY_WEBGL
        [MonoPInvokeCallback(typeof(Action<int>))]
#endif
        private static void OnConnected(int handle)
        {
            _instanceByHandle[handle]._connected.TrySetResult(true);
        }

#if UNITY_STANDALONE || UNITY_WEBGL
        [MonoPInvokeCallback(typeof(Action<int, int, int>))]
#endif
        private static void OnClose(int handle, int code, int wasClean)
        {
            _instanceByHandle[handle]._closed.TrySetResult((code, wasClean != 0));
        }

        private void OnReceive(IntPtr bufferPtr, int length)
        {
            var bytes = new byte[length];
            Marshal.Copy(bufferPtr, bytes, 0, length);

            if (_receiveTcs != null)
            {
                var tcs = _receiveTcs;
                _receiveTcs = null;
                tcs.TrySetResult(bytes);
            }
            else
            {
                _queue.Enqueue(bytes);
            }
        }

        public JsWebSocket(string url, string subProtocol)
        {
            _connected = new TaskCompletionSource<bool>();
            _closed = new TaskCompletionSource<(int Code, bool WasClean)>();
            _handle = JsWebSocket_Init(url, subProtocol);
            _instanceByHandle[_handle] = this;

            JsWebSocket_RegisterReceiveCallback(_handle, OnReceive);
            JsWebSocket_RegisterOnConnectedCallback(_handle, OnConnected);
            JsWebSocket_RegisterOnCloseCallback(_handle, OnClose);
        }

        public Task Connected => _connected.Task;
        public Task<(int Code, bool WasClean)> Closed => _closed.Task;

        public void Connect()
            => JsWebSocket_Connect(_handle);

        public void Send(byte[] bytes)
            => JsWebSocket_Send(_handle, bytes, bytes.Length);

        public void Close(int code, string reason)
            => JsWebSocket_Close(_handle, code, reason);

        public Task<byte[]> ReceiveAsync()
        {
            if (_receiveTcs != null) throw new InvalidOperationException("There is already an operation in progress.");

            if (_queue.Count != 0)
            {
                _receiveTcs = null;
                return Task.FromResult(_queue.Dequeue());
            }

            _receiveTcs = new TaskCompletionSource<byte[]>();
            return _receiveTcs.Task;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                JsWebSocket_Dispose(_handle);
                _disposed = true;
            }
        }
    }
}
#endif
