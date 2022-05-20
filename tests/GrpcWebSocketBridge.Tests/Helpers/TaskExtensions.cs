using System;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcWebSocketBridge.Tests.Helpers
{
    internal static class TaskExtensions
    {
        public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
        {
            await Task.WhenAny(task, cancellationToken.AsTask());
            cancellationToken.ThrowIfCancellationRequested();

            await task;
        }

        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            await Task.WhenAny(task, cancellationToken.AsTask());
            cancellationToken.ThrowIfCancellationRequested();

            return await task;
        }

        public static async Task WithTimeout(this Task task, TimeSpan timeout)
        {
            var winTask = await Task.WhenAny(task, Task.Delay(timeout));
            if (winTask != task)
            {
                throw new TimeoutException();
            }
        }

        public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            var winTask = await Task.WhenAny(task, Task.Delay(timeout));
            if (winTask != task)
            {
                throw new TimeoutException();
            }

            return await task;
        }

        public static Task AsTask(this CancellationToken cancellationToken)
        {
            return Task.Delay(-1, cancellationToken);
        }
    }
}
