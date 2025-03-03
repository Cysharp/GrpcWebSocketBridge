#if UNITY_WEBGL
using System.Reflection;
using System;
using UnityEngine.LowLevel;
using UnityEngine;

namespace Cysharp.Threading
{
    /// <summary>
    /// Workaround for the issue that Unity Player on WebGL does not support ThreadPool.
    /// It calls ThreadPool's PerformWaitCallback internal method in the Unity's main thread loop forcibly.
    /// </summary>
    /// <remarks>
    /// Especially when using a library that uses Task's ConfigureAwait(false), the continuation may be scheduled on the ThreadPool and stuck.
    /// This workaround forcibly executes the ThreadPool to avoid the stack.
    /// </remarks>
    public static class WebGLThreadPoolDispatcher
    {
        private static Func<bool> _performWaitCallback;

        public struct Dispatch { }

#if !UNITY_EDITOR
        // Enable the method to be called when running in a WebGL environment
        [RuntimeInitializeOnLoadMethod]
#endif
        public static void Initialize()
        {
            var type_ThreadPoolWaitCallback = Type.GetType("System.Threading._ThreadPoolWaitCallback");
            var methodPerformWaitCallback = type_ThreadPoolWaitCallback.GetMethod("PerformWaitCallback", BindingFlags.NonPublic | BindingFlags.Static);
            _performWaitCallback = (Func<bool>)methodPerformWaitCallback.CreateDelegate(typeof(Func<bool>));

            var playerLoopSystemForDispatch = new PlayerLoopSystem()
            {
                type = typeof(Dispatch),
                updateDelegate = PerformWaitCallback,
            };
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            for (var i = 0; i < playerLoop.subSystemList.Length; i++)
            {
                if (playerLoop.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.Update))
                {
                    var subSystemList = new PlayerLoopSystem[playerLoop.subSystemList[i].subSystemList.Length + 1];
                    Array.Copy(playerLoop.subSystemList[i].subSystemList, subSystemList, playerLoop.subSystemList[i].subSystemList.Length);
                    subSystemList[subSystemList.Length - 1] = playerLoopSystemForDispatch;
                    playerLoop.subSystemList[i].subSystemList = subSystemList;
                    break;
                }
            }

            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        private static void PerformWaitCallback()
        {
            _performWaitCallback();
        }
    }
}

#endif
