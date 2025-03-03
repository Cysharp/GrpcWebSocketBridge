using MagicOnion.Client;
using MessagePack;
using MessagePack.Resolvers;
using UnityEngine;

namespace MagicOnionSampleApp.Client.Unity
{
    public static class Startup
    {
        [RuntimeInitializeOnLoadMethod]
        private static void RegisterResolvers()
        {
            // NOTE: Currently, CompositeResolver doesn't work on Unity IL2CPP build. Use StaticCompositeResolver instead of it.
            StaticCompositeResolver.Instance.Register(
                MagicOnionGeneratedClientInitializer.Resolver,
                StandardResolver.Instance
            );

            MessagePackSerializer.DefaultOptions = MessagePackSerializer.DefaultOptions
                .WithResolver(StaticCompositeResolver.Instance);
        }
    }

    [MagicOnionClientGeneration(typeof(MagicOnionSampleApp.Shared.Hubs.IChatHub))]
    public partial class MagicOnionGeneratedClientInitializer
    {
    }
}
