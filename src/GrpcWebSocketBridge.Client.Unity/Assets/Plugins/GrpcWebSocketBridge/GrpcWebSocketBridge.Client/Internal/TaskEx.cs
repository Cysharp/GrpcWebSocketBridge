using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

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
    }
}
