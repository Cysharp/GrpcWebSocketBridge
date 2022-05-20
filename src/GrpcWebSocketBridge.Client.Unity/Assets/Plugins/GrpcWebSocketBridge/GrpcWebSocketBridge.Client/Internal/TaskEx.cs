using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
#if !NON_UNITY
using Cysharp.Threading.Tasks;
#endif

// ReSharper disable once CheckNamespace
namespace GrpcWebSocketBridge.Client
{
    internal static class TaskEx
    {
        public static async void Forget(this Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

#if !NON_UNITY
        public static UniTask ConfigureAwait(this UniTask task, bool _)
            => task;
        public static UniTask<T> ConfigureAwait<T>(this UniTask<T> task, bool _)
            => task;
#endif
    }
}
