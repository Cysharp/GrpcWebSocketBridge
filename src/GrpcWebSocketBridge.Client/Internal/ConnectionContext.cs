using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
#if !NON_UNITY
using Cysharp.Threading.Tasks;
#endif

namespace GrpcWebSocketBridge.Client.Internal
{
    internal class ConnectionContext
    {
#if NON_UNITY
        private readonly TaskCompletionSource<bool> _responseHeaderTcs;
#else
        private readonly UniTaskCompletionSource<bool> _responseHeaderTcs;
#endif
        private readonly CancellationTokenSource _connectionAborted;
        private readonly Pipe _pipeRequest;
        private readonly Pipe _pipeResponse;
        private bool _responseCompleted;
        private bool _requestCompleted;

        public Pipe RequestPipe => _pipeRequest;
        public Pipe ResponsePipe => _pipeResponse;
#if NON_UNITY
        public Task ResponseHeaderReceived => _responseHeaderTcs.Task;
#else
        public UniTask ResponseHeaderReceived => _responseHeaderTcs.Task;
#endif
        public CancellationToken ConnectionAborted => _connectionAborted.Token;
        public Exception AbortReason { get; private set; }
        public bool ResponseCompleted => _responseCompleted;

        public ConnectionContext(Pipe pipeRequest, Pipe pipeResponse)
        {
            _pipeRequest = pipeRequest;
            _pipeResponse = pipeResponse;
#if NON_UNITY
            _responseHeaderTcs = new TaskCompletionSource<bool>();
#else
            _responseHeaderTcs = new UniTaskCompletionSource<bool>();
#endif
            _connectionAborted = new CancellationTokenSource();
        }

        public void SignalResponseHeaderHasReceived()
        {
            _responseHeaderTcs.TrySetResult(true);
        }

#if NON_UNITY
        public async Task RequestAbortedAsync(Exception e = default)
#else
        public async UniTask RequestAbortedAsync(Exception e = default)
#endif
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


#if NON_UNITY
        public async Task CompleteRequestAsync()
#else
        public async UniTask CompleteRequestAsync()
#endif
        {
            if (!_requestCompleted)
            {
                await _pipeRequest.Reader.CompleteAsync().ConfigureAwait(false);
                _requestCompleted = true;
            }
        }

#if NON_UNITY
        public async Task CompleteResponseAsync()
#else
        public async UniTask CompleteResponseAsync()
#endif
        {
            if (!_responseCompleted)
            {
                await _pipeResponse.Writer.CompleteAsync().ConfigureAwait(false);
                _responseCompleted = true;
            }
        }
    }
}
