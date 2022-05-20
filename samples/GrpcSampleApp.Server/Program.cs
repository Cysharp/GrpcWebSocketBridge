using GrpcSampleApp.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Additional configuration is required to successfully run gRPC on macOS.
// For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // WARN: Do not apply following policies to your production.
        //       If not configured carefully, it may cause security problems.
        policy.AllowAnyMethod();
        policy.AllowAnyOrigin();
        policy.AllowAnyHeader();

        // NOTE: "grpc-status" and "grpc-message" headers are required by gRPC. so, we need expose these headers to the client.
        policy.WithExposedHeaders("grpc-status", "grpc-message");
    });
});

var app = builder.Build();

// NOTE: Enables static file serving only for Unity WebGL demo app.
app.UseStaticFiles(new StaticFileOptions()
{
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
    OnPrepareResponse = (ctx) =>
    {
        if (ctx.File.Name.EndsWith(".br"))
        {
            ctx.Context.Response.Headers.ContentEncoding = "br";
        }
        if (ctx.File.Name.Contains(".wasm")) ctx.Context.Response.Headers.ContentType = "application/wasm";
        if (ctx.File.Name.Contains(".js")) ctx.Context.Response.Headers.ContentType = "application/javascript";
    }
});

// Configure the HTTP request pipeline.
// Enables CORS, WebSocket, GrpcWebSocketRequestRoutingEnabler
// NOTE: These need to be called before `UseRouting`.  
app.UseCors();
app.UseWebSockets();
app.UseGrpcWebSocketRequestRoutingEnabler();

app.UseRouting();

// NOTE: `UseGrpcWebSocketBridge` must be called after calling `UseRouting`.
app.UseGrpcWebSocketBridge();

app.MapGrpcService<GreeterService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
