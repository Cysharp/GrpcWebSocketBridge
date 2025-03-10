using Microsoft.Extensions.Logging;

namespace GrpcWebSocketBridge.Tests.Helpers;

public class TestOutputLoggerProvider(ITestOutputHelper testOutputHelper) : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));

    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(_testOutputHelper, categoryName);
    }

    private class Logger(ITestOutputHelper testOutputHelper, string categoryName) : ILogger
    {
        private readonly ITestOutputHelper _testOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return AnonymousDisposable.Instance;
        }

        private class AnonymousDisposable : IDisposable
        {
            public static IDisposable Instance { get; } = new AnonymousDisposable();
            public void Dispose()
            {
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _testOutputHelper.WriteLine($"[{DateTime.Now}][{categoryName}][{eventId}][{logLevel}] {formatter(state, exception)}");
            if (exception != null)
            {
                _testOutputHelper.WriteLine(exception.ToString());
            }
        }
    }
}
