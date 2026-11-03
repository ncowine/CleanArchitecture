using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Common.RabbitMQ.Tests
{
    /// <summary>Keeps every log line, with its level and exception, for a test to check.</summary>
    public sealed class RecordingLogger : ILogger
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new ConcurrentQueue<LogEntry>();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}
