using GrpcWebSocketBridge.Tests.Helpers;

namespace GrpcWebSocketBridge.Tests;

public abstract class UseTestServerTestBase(ITestOutputHelper testOutputHelper) : TimeoutTestBase
{
    protected ITestOutputHelper TestOutputHelper { get; } = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));

    protected virtual AspNetCoreServerTestHostOptions? DefaultHostOptions => default;

    protected AspNetCoreServerTestHost<Startup> CreateTestServer(AspNetCoreServerTestHostOptions? options = default)
        => CreateTestServer<Startup>(options);

    protected AspNetCoreServerTestHost<TStartup> CreateTestServer<TStartup>(AspNetCoreServerTestHostOptions? options = default)
        where TStartup : class, IStartup, new()
        => AspNetCoreServerTestHost.Create<TStartup>(TestOutputHelper, options ?? DefaultHostOptions, TimeoutToken);
}
