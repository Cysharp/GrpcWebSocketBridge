using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GrpcWebSocketBridge.Tests.Helpers
{
    public class EventCaptureLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEvent> _events = new ConcurrentQueue<LogEvent>();

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

        private class Logger : ILogger
        {
            private readonly ConcurrentQueue<LogEvent> _events;
            private string _categoryName;

            public Logger(ConcurrentQueue<LogEvent> events, string categoryName)
            {
                _events = events ?? throw new ArgumentNullException(nameof(events));
                _categoryName = categoryName;
            }

            public IDisposable BeginScope<TState>(TState state)
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

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _events.Enqueue(new LogEvent(DateTime.Now, _categoryName, eventId, logLevel, formatter(state, exception), exception));
            }
        }

        [DebuggerDisplay("[{Timestamp.ToString(),nq}][{Category,nq}][{EventId.ToString(),nq}][{LogLevel,nq}] {Message,nq}")]
        public readonly struct LogEvent
        {
            public DateTime Timestamp { get; }
            public string Category { get; }
            public EventId EventId { get; }
            public LogLevel LogLevel { get; }
            public string Message { get; }
            public Exception? Exception { get; }

            public LogEvent(DateTime timestamp, string category, EventId eventId, LogLevel logLevel, string message, Exception? exception)
            {
                Timestamp = timestamp;
                Category = category;
                EventId = eventId;
                LogLevel = logLevel;
                Message = message;
                Exception = exception;
            }
        }
    }
}
