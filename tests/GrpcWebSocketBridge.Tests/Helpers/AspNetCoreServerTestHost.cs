using System.Net;
using System.Net.Sockets;
using Grpc.Net.Client;
using GrpcWebSocketBridge.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrpcWebSocketBridge.Tests.Helpers;

public static class AspNetCoreServerTestHost
{
    private readonly static HashSet<int> UsedPortInSession = new();

    public static AspNetCoreServerTestHost<TStartup> Create<TStartup>(ITestOutputHelper testOutputHelper, AspNetCoreServerTestHostOptions? options, CancellationToken shutdownToken)
        where TStartup : class, IStartup, new()
    {
        var insecureHttp1OnlyPort = GetUnusedEphemeralPort();
        var insecureHttp2OnlyPort = GetUnusedEphemeralPort();
        var secureHttp1AndHttp2Port = GetUnusedEphemeralPort();

        options ??= new AspNetCoreServerTestHostOptions();

        var host = new AspNetCoreServerTestHost<TStartup>(insecureHttp1OnlyPort, insecureHttp2OnlyPort, secureHttp1AndHttp2Port, testOutputHelper, options, shutdownToken);
        return host;
    }

    private static int GetUnusedEphemeralPort()
    {
        lock (UsedPortInSession)
        {
            var retryCount = 5;
            do
            {
                var port = GetUnusedEphemeralPortCore();
                if (!UsedPortInSession.Contains(port))
                {
                    UsedPortInSession.Add(port);
                    return port;
                }
            } while (retryCount-- > 0);

            throw new Exception("Cannot allocate unused port in this test session.");
        }

        static int GetUnusedEphemeralPortCore()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }
}

public class AspNetCoreServerTestHostOptions
{
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(1);
    public ChannelKind DefaultChannel { get; set; } = ChannelKind.InsecureHttp1;
    public bool EnableLogging { get; set; } = true;
}

public enum ChannelKind
{
    /// <summary>Use HTTP/1 (gRPC over WebSocket)</summary>
    InsecureHttp1,
    /// <summary>Use HTTP/2 (gRPC over HTTP/2)</summary>
    InsecureHttp2,
    /// <summary>Use HTTP/1 (gRPC over WebSocket over TLS)</summary>
    SecureHttp1,
    /// <summary>Use HTTP/2 (gRPC over HTTP/2 over TLS)</summary>
    SecureHttp2,
}

public class AspNetCoreServerTestHost<TStartup> : IAsyncDisposable
    where TStartup : class, IStartup, new()
{
    private IHost _host;
    private ITestOutputHelper _testOutputHelper;
    private readonly CancellationToken _shutdownToken;
    private readonly CancellationTokenRegistration _shutdownCancellationRegistration;
    private readonly AspNetCoreServerTestHostOptions _hostOptions;
    private readonly EventCaptureLoggerProvider _eventCaptureLoggerProvider;

    public int InsecureHttp1Port { get; }
    public int InsecureHttp2Port { get; }
    public int SecureHttp1AndHttp2Port { get; }

    public ILogger Logger { get; }

    public FunctionTestLastRequestStorage LastRequest => _host.Services.GetRequiredService<FunctionTestLastRequestStorage>();
    public IReadOnlyCollection<EventCaptureLoggerProvider.LogEvent> GetAllLogEvents() => _eventCaptureLoggerProvider.GetAllEvents();

    public AspNetCoreServerTestHost(int insecureHttp1OnlyPort, int insecureHttp2OnlyPort, int secureHttp1AndHttp2Port, ITestOutputHelper testOutputHelper, AspNetCoreServerTestHostOptions testServerOptions, CancellationToken shutdownToken)
    {
        InsecureHttp1Port = insecureHttp1OnlyPort;
        InsecureHttp2Port = insecureHttp2OnlyPort;
        SecureHttp1AndHttp2Port = secureHttp1AndHttp2Port;

        _eventCaptureLoggerProvider = new EventCaptureLoggerProvider();
        _hostOptions = testServerOptions;
        _testOutputHelper = testOutputHelper;
        _shutdownToken = shutdownToken;
        _shutdownCancellationRegistration = _shutdownToken.Register(async () => await this.DisposeAsync());

        var builder = CreateHostBuilder(Array.Empty<string>());
        var host = builder
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options =>
                {
                    options.ShutdownTimeout = testServerOptions.ShutdownTimeout;
                });

                services.AddSingleton<HostStaticStorage>();
            })
            .ConfigureLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                if (testServerOptions.EnableLogging)
                {
                    loggingBuilder.AddProvider(new TestOutputLoggerProvider(testOutputHelper));
                    loggingBuilder.AddProvider(_eventCaptureLoggerProvider);
                }
            })
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder.UseKestrel(options =>
                {
                    options.ListenLocalhost(insecureHttp1OnlyPort, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;
                    });
                    options.ListenLocalhost(insecureHttp2OnlyPort, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                    options.ListenLocalhost(secureHttp1AndHttp2Port, listenOptions =>
                    {
                        listenOptions.UseHttps();
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    });
                });
            })
            .Build();

        Logger = host.Services.GetRequiredService<ILogger<AspNetCoreServerTestHost<TStartup>>>();

        _host = host;
        _ = host.StartAsync();
    }

    public GrpcChannel CreateChannel(ChannelKind? kind = default, bool disposeHttpClient = false, bool forceWebSocketMode = false)
    {
        kind ??= _hostOptions.DefaultChannel;

        var options = new GrpcChannelOptions()
        {
            HttpHandler = (kind == ChannelKind.InsecureHttp1 || kind == ChannelKind.SecureHttp1) ? new GrpcWebSocketBridgeHandler(forceWebSocketMode) : default,
            DisposeHttpClient = disposeHttpClient,
        };

        return kind switch
        {
            ChannelKind.InsecureHttp1 => GrpcChannel.ForAddress($"http://localhost:{InsecureHttp1Port}", options),
            ChannelKind.InsecureHttp2 => GrpcChannel.ForAddress($"http://localhost:{InsecureHttp2Port}", options),
            ChannelKind.SecureHttp1 => GrpcChannel.ForAddress($"https://localhost:{SecureHttp1AndHttp2Port}", options),
            ChannelKind.SecureHttp2 => GrpcChannel.ForAddress($"https://localhost:{SecureHttp1AndHttp2Port}", options),
            _ => throw new NotImplementedException(),
        };
    }

    public EventWaitHandle CreateSignal(string signalName)
    {
        var signal = new EventWaitHandle(false, EventResetMode.ManualReset);

        _host.Services.GetRequiredService<HostStaticStorage>().Items[signalName] = signal;

        return signal;
    }

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _host, _host, null) != null)
        {
            await _shutdownCancellationRegistration.DisposeAsync();
            await _host.StopAsync(cancellationToken);
            _host.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
    }

    // Additional configuration is required to successfully run gRPC on macOS.
    // For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup(context =>
                {
                    var startup = new TStartup();
                    startup.Initialize(context);
                    return startup;
                });
            });
}

public interface IStartup
{
    void Initialize(WebHostBuilderContext context);
}

public class Startup : IStartup
{
    protected bool EnableWebSocketBridge { get; set; } = true;

    public virtual void Initialize(WebHostBuilderContext context)
    {
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddWebSockets(options => { });
        services.AddGrpc();
        services.AddSingleton<FunctionTestLastRequestStorage>();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        if (EnableWebSocketBridge)
        {
            app.UseWebSockets();
            app.UseGrpcWebSocketRequestRoutingEnabler();
        }

        app.UseRouting();

        if (EnableWebSocketBridge)
        {
            app.UseGrpcWebSocketBridge();
        }

        app.UseMiddleware<FunctionTestLastRequestStorageMiddleware>();

        app.UseEndpoints(endpoints =>
        {
            OnConfigureEndpoints(app, env, endpoints);

            endpoints.MapGet("/", async context =>
            {
                await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
            });
        });
    }

    protected virtual void OnConfigureEndpoints(IApplicationBuilder app, IWebHostEnvironment env, IEndpointRouteBuilder endpoints)
    {
    }
}

class StartupWithGrpcService<TService> : Startup
    where TService : class
{
    protected override void OnConfigureEndpoints(IApplicationBuilder app, IWebHostEnvironment env, IEndpointRouteBuilder endpoints)
        => endpoints.MapGrpcService<TService>();
}

class StartupWithGrpcService<TService1, TService2> : Startup
    where TService1 : class
    where TService2 : class
{
    protected override void OnConfigureEndpoints(IApplicationBuilder app, IWebHostEnvironment env, IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<TService1>();
        endpoints.MapGrpcService<TService2>();
    }
}

class StartupWithGrpcService<TService1, TService2, TService3> : Startup
    where TService1 : class
    where TService2 : class
    where TService3 : class
{
    protected override void OnConfigureEndpoints(IApplicationBuilder app, IWebHostEnvironment env, IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<TService1>();
        endpoints.MapGrpcService<TService2>();
        endpoints.MapGrpcService<TService3>();
    }
}
