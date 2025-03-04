using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GrpcWebSocketBridge.Tests.Helpers;

public class EventCaptureLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public EventCaptureLoggerProvider()
    {
    }

    public IReadOnlyCollection<LogEvent> GetAllEvents()
        => _events.ToArray();

    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(_events, categoryName);
    }

    private class Logger(ConcurrentQueue<LogEvent> events, string categoryName) : ILogger
    {
        private readonly ConcurrentQueue<LogEvent> _events = events ?? throw new ArgumentNullException(nameof(events));

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
            _events.Enqueue(new LogEvent(DateTime.Now, categoryName, eventId, logLevel, formatter(state, exception), exception));
        }
    }

    [DebuggerDisplay("[{Timestamp.ToString(),nq}][{Category,nq}][{EventId.ToString(),nq}][{LogLevel,nq}] {Message,nq}")]
    public readonly struct LogEvent(DateTime timestamp, string category, EventId eventId, LogLevel logLevel, string message, Exception? exception)
    {
        public DateTime Timestamp { get; } = timestamp;
        public string Category { get; } = category;
        public EventId EventId { get; } = eventId;
        public LogLevel LogLevel { get; } = logLevel;
        public string Message { get; } = message;
        public Exception? Exception { get; } = exception;
    }
}
