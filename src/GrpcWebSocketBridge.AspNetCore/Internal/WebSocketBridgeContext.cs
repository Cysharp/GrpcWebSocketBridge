using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace GrpcWebSocketBridge.AspNetCore.Internal
{
    internal class WebSocketBridgeContext
    {
        public WebSocket WebSocket { get; }
        public PipeWriter Writer { get; }
        public PipeReader Reader { get; }
        public Task WriterTask { get; }
        public Task ReaderTask { get; }
        public Task RequestHeaderReceivedTask { get; }

        public WebSocketBridgeContext(WebSocket webSocket, PipeWriter writer, PipeReader reader, Task writerTask, Task readerTask, Task requestHeaderReceivedTask)
        {
            WebSocket = webSocket;
            Writer = writer;
            Reader = reader;
            WriterTask = writerTask;
            ReaderTask = readerTask;
            RequestHeaderReceivedTask = requestHeaderReceivedTask;
        }
    }
}
