using System.Globalization;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Behaviors;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CleanArch.UnitTests;

/// <summary>
/// Naming a request in a log line or an audit record. Features are vertical slices, so every request
/// type is nested and called <c>Command</c> or <c>Query</c> — the enclosing class is the only part that
/// says what actually ran.
/// </summary>
public class RequestNameTests
{
    private static class CreateStudent
    {
        internal sealed record Command : IRequest<Guid>;
    }

    private static class GetStudentLoans
    {
        internal sealed record Query : IRequest<string>;
    }

    [Fact]
    public void Display_names_the_feature_and_the_kind_rather_than_just_the_kind()
    {
        Assert.Equal("CreateStudent.Command", RequestName.Display(typeof(CreateStudent.Command)));
        Assert.Equal("GetStudentLoans.Query", RequestName.Display(typeof(GetStudentLoans.Query)));
    }

    [Fact]
    public void Feature_is_the_operation_alone_because_that_is_what_the_audit_trail_records()
    {
        Assert.Equal("CreateStudent", RequestName.Feature(typeof(CreateStudent.Command)));
        Assert.Equal("GetStudentLoans", RequestName.Feature(typeof(GetStudentLoans.Query)));
    }

    [Fact]
    public void A_request_that_is_not_nested_is_named_by_itself()
    {
        // Not every request has to follow the vertical-slice shape; a top-level one keeps its own name.
        Assert.Equal("TopLevelPing", RequestName.Display(typeof(TopLevelPing)));
        Assert.Equal("TopLevelPing", RequestName.Feature(typeof(TopLevelPing)));
    }
}

/// <summary>A request declared outside a feature class — nested in nothing, so it names itself.</summary>
internal sealed record TopLevelPing : IRequest<string>;

/// <summary>
/// The outermost behaviour: what a log reader sees for every request that passes through the mediator.
/// </summary>
public class LoggingBehaviorTests
{
    private static class WithdrawStudent
    {
        internal sealed record Command : IRequest<string>;
    }

    [Fact]
    public async Task Logs_which_feature_ran_not_just_that_a_Command_ran()
    {
        var logger = new CapturingLogger<LoggingBehavior<WithdrawStudent.Command, string>>();
        var behavior = new LoggingBehavior<WithdrawStudent.Command, string>(logger);

        await behavior.Handle(new WithdrawStudent.Command(), () => Task.FromResult("ok"), default);

        // The regression this guards: both lines used to read "Command", for every request in the system.
        Assert.Collection(
            logger.Messages,
            handling => Assert.Equal("Handling WithdrawStudent.Command", handling),
            handled => Assert.StartsWith("Handled WithdrawStudent.Command in ", handled, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_completion_line_carries_how_long_the_request_took()
    {
        var logger = new CapturingLogger<LoggingBehavior<WithdrawStudent.Command, string>>();
        var behavior = new LoggingBehavior<WithdrawStudent.Command, string>(logger);

        await behavior.Handle(
            new WithdrawStudent.Command(),
            async () =>
            {
                await Task.Delay(15);
                return "ok";
            },
            default);

        var elapsed = logger.State(1)["ElapsedMs"];
        Assert.True(Convert.ToInt64(elapsed, CultureInfo.InvariantCulture) >= 10, $"expected a measured duration, got {elapsed}");
    }

    [Fact]
    public async Task The_response_is_passed_through_untouched()
    {
        var logger = new CapturingLogger<LoggingBehavior<WithdrawStudent.Command, string>>();
        var behavior = new LoggingBehavior<WithdrawStudent.Command, string>(logger);

        var response = await behavior.Handle(new WithdrawStudent.Command(), () => Task.FromResult("payload"), default);

        Assert.Equal("payload", response);
    }
}

/// <summary>Captures rendered messages and their structured state, so tests can assert on both.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(string Message, IReadOnlyList<KeyValuePair<string, object?>> State)> _entries = new();

    public IReadOnlyList<string> Messages => _entries.Select(entry => entry.Message).ToList();

    public IReadOnlyDictionary<string, object?> State(int index)
        => _entries[index].State.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var structured = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        _entries.Add((formatter(state, exception), structured));
    }
}
