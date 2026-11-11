using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Common.RabbitMQ.Tests
{
    /// <summary>Keeps every log line, with its level and exception, for a test to check.</summary>
    public sealed class RecordingLogger : ILogger
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new ConcurrentQueue<LogEntry>();

        /// <returns>The first matching entry, or null when none is logged within <paramref name="timeout"/>.</returns>
        public async Task<LogEntry> WaitForAsync(Func<LogEntry, bool> predicate, TimeSpan timeout)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                LogEntry match = Entries.FirstOrDefault(predicate);
                if (match != null || stopwatch.Elapsed > timeout)
                {
                    return match;
                }

                await Task.Delay(20).ConfigureAwait(false);
            }
        }

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
