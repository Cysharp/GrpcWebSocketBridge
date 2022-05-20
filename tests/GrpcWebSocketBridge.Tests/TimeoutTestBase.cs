using System;
using System.Diagnostics;
using System.Threading;

namespace GrpcWebSocketBridge.Tests
{
    public abstract class TimeoutTestBase
    {
        private readonly CancellationTokenSource _timeoutTokenSource;

        protected CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : _timeoutTokenSource.Token;

        protected virtual TimeSpan UnexpectedTimeout => Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10);


        protected TimeoutTestBase()
        {
            _timeoutTokenSource = new CancellationTokenSource(UnexpectedTimeout);
        }
    }
}
