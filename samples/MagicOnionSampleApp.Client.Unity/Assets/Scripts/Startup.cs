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
                MagicOnion.Resolvers.MagicOnionResolver.Instance,
                MessagePack.Resolvers.GeneratedResolver.Instance,
                BuiltinResolver.Instance,
                PrimitiveObjectResolver.Instance
            );

            MessagePackSerializer.DefaultOptions = MessagePackSerializer.DefaultOptions
                .WithResolver(StaticCompositeResolver.Instance);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeSynchronizationContext()
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
        }
#endif
    }
}
