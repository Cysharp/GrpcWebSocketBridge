using System;
using System.Threading.Tasks;
using Grpc.Core;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;
using Xunit.Abstractions;

namespace GrpcWebSocketBridge.Tests
{
    public abstract class UseTestServerTestBase : TimeoutTestBase
    {
        protected ITestOutputHelper TestOutputHelper { get; }

        protected virtual AspNetCoreServerTestHostOptions? DefaultHostOptions => default;

        protected UseTestServerTestBase(ITestOutputHelper testOutputHelper)
        {
            TestOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
        }

        protected AspNetCoreServerTestHost<Startup> CreateTestServer(AspNetCoreServerTestHostOptions? options = default)
            => CreateTestServer<Startup>(options);

        protected AspNetCoreServerTestHost<TStartup> CreateTestServer<TStartup>(AspNetCoreServerTestHostOptions? options = default)
            where TStartup : class, IStartup, new()
            => AspNetCoreServerTestHost.Create<TStartup>(TestOutputHelper, options ?? DefaultHostOptions, TimeoutToken);
    }
}
