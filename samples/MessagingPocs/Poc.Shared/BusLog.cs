using Microsoft.Extensions.Logging;

namespace Poc.Shared;

/// <summary>
/// An ILogger that hands each line to the window. Legacy apps set it as the router's Logger; the net10 app adds it as a
/// logging provider. A real app would use its usual logger instead.
/// </summary>
public sealed class BusLog : ILogger, ILoggerProvider
{
    public event Action<string>? Written;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Written?.Invoke(formatter(state, exception));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return this;
    }

    public void Dispose()
    {
    }
}
