using System.Collections.Concurrent;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GrpcWebSocketBridge.Tests.Helpers;

public class HostStaticStorage
{
    public ConcurrentDictionary<string, object> Items { get; } = new();
}

public static class HostStaticStorageExtensions
{
    public static IDictionary<string, object> GetHostStaticItems(this ServerCallContext serverCallContext)
    {
        return serverCallContext.GetHttpContext().RequestServices.GetRequiredService<HostStaticStorage>().Items;
    }

    public static T GetHostStaticItem<T>(this ServerCallContext serverCallContext, string name)
    {
        return (T)serverCallContext.GetHttpContext().RequestServices.GetRequiredService<HostStaticStorage>().Items[name];
    }
}
