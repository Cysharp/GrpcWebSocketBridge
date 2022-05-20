using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GrpcSampleApp.Client.Unity
{
#if UNITY_WEBGL
    /// <summary>
    /// See <c>Plugins/GrpcSampleAppJsInterop/GrpcSampleAppJsInterop.jslib</c>
    /// </summary>
    public static class GrpcSampleAppJsInterop
    {
        [DllImport("__Internal", EntryPoint = "GrpcSampleAppJsInterop_GetCurrentLocation")]
        public static extern string GetCurrentLocation();
    }
#endif
}
