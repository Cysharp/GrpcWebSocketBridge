using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcWebSocketBridge.Client.Internal
{
    internal class ConnectionContext
    {
        private readonly TaskCompletionSource<bool> _responseHeaderTcs;
        private readonly CancellationTokenSource _connectionAborted;
        private readonly Pipe _pipeRequest;
        private readonly Pipe _pipeResponse;
        private bool _responseCompleted;
        private bool _requestCompleted;

        public Pipe RequestPipe => _pipeRequest;
        public Pipe ResponsePipe => _pipeResponse;
        public Task ResponseHeaderReceived => _responseHeaderTcs.Task;
        public CancellationToken ConnectionAborted => _connectionAborted.Token;
        public Exception AbortReason { get; private set; }
        public bool ResponseCompleted => _responseCompleted;

        public ConnectionContext(Pipe pipeRequest, Pipe pipeResponse)
        {
            _pipeRequest = pipeRequest;
            _pipeResponse = pipeResponse;
            _responseHeaderTcs = new TaskCompletionSource<bool>(
#if !UNITY_WEBGL
                TaskCreationOptions.RunContinuationsAsynchronously
#endif
            );
            _connectionAborted = new CancellationTokenSource();
        }

        public void SignalResponseHeaderHasReceived()
        {
            _responseHeaderTcs.TrySetResult(true);
        }

        public async ValueTask RequestAbortedAsync(Exception e = default)
        {
            if (!_connectionAborted.IsCancellationRequested)
            {
                AbortReason = e;
                _connectionAborted.Cancel();

                await RequestPipe.Reader.CompleteAsync(new IOException("The request was aborted.")).ConfigureAwait(false);
                await ResponsePipe.Writer.CompleteAsync(new IOException("The request was aborted.")).ConfigureAwait(false);

                _responseCompleted = true;
                _requestCompleted = true;
            }
        }

        public async ValueTask CompleteRequestAsync()
        {
            if (!_requestCompleted)
            {
                await _pipeRequest.Reader.CompleteAsync().ConfigureAwait(false);
                _requestCompleted = true;
            }
        }

        public async ValueTask CompleteResponseAsync()
        {
            if (!_responseCompleted)
            {
                await _pipeResponse.Writer.CompleteAsync().ConfigureAwait(false);
                _responseCompleted = true;
            }
        }
    }
}
