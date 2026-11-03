using System;
using Microsoft.Extensions.Logging;

namespace Common.RabbitMQ.Tests
{
    public sealed class LogEntry
    {
        public LogEntry(LogLevel level, string message, Exception exception)
        {
            Level = level;
            Message = message;
            Exception = exception;
        }

        public LogLevel Level { get; }

        public string Message { get; }

        public Exception Exception { get; }
    }
}
