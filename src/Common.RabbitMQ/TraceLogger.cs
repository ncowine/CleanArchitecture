using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Common.RabbitMQ
{
    /// <summary>Used when an application sets no logger, so a legacy app still gets its messages in Trace output.</summary>
    internal sealed class TraceLogger : ILogger
    {
        public static readonly TraceLogger Instance = new TraceLogger();

        private TraceLogger()
        {
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Trace.WriteLine($"[RabbitMQ] {formatter(state, exception)}");
        }
    }
}
