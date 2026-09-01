using CleanArch.Api.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// What a real host does at boot with On-Behalf-Of downstreams registered. These cover the promise the
/// design makes to whoever deploys this: a downstream that is wired in code but unconfigured in THIS
/// environment stops the application immediately, naming what to set — instead of starting cleanly and
/// failing later on a user's request, as a 401 from a service that was handed a token it never accepted.
/// </summary>
public sealed class OnBehalfOfStartupTests
{
    private const string TokenEndpoint = "https://idp.example/oauth2/token";

    [Fact]
    public async Task Names_every_configured_downstream_at_startup()
    {
        var logs = new List<string>();
        using var host = BuildHost(
            logs,
            Credentials(new Dictionary<string, string?>
            {
                ["OnBehalfOf:Downstreams:billing:Audience"] = "api://billing",
                ["OnBehalfOf:Downstreams:billing:Scope"] = "invoices.read",
                ["OnBehalfOf:Downstreams:grading:Audience"] = "api://grading",
            }),
            "billing", "grading");

        await host.StartAsync();

        // The line an operator reads to answer "what will this deployment actually ask Okta for?".
        Assert.Contains(logs, line => line.Contains("'billing'", StringComparison.Ordinal)
                                      && line.Contains("api://billing", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("'grading'", StringComparison.Ordinal)
                                      && line.Contains("api://grading", StringComparison.Ordinal));

        await host.StopAsync();
    }

    [Fact]
    public async Task Refuses_to_start_when_a_registered_downstream_has_no_configuration()
    {
        // 'grading' is wired in code but absent from this environment's configuration — the mistake that
        // otherwise only shows up as a puzzling 401 from the grading service.
        using var host = BuildHost(
            [],
            Credentials(new Dictionary<string, string?>
            {
                ["OnBehalfOf:Downstreams:billing:Audience"] = "api://billing",
            }),
            "billing", "grading");

        var failure = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.NotNull(failure);
        Assert.Contains("OnBehalfOf:Downstreams:grading", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_to_start_when_downstreams_are_registered_but_the_credentials_are_missing()
    {
        // The audience is set, but this API has nothing to authenticate itself to the token endpoint with.
        using var host = BuildHost(
            [],
            new Dictionary<string, string?>
            {
                ["OnBehalfOf:Downstreams:billing:Audience"] = "api://billing",
            },
            "billing");

        var failure = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.NotNull(failure);
        Assert.Contains("TokenEndpoint", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starts_normally_when_no_downstream_is_registered()
    {
        // An application that never calls a downstream must not be forced to configure a token endpoint,
        // even though AddOnBehalfOf has run.
        using var host = BuildHost([], new Dictionary<string, string?>());

        await host.StartAsync();
        await host.StopAsync();
    }

    /// <summary>The shared client credentials, plus whatever downstream settings a test supplies.</summary>
    private static Dictionary<string, string?> Credentials(Dictionary<string, string?> downstreams)
    {
        downstreams["OnBehalfOf:TokenEndpoint"] = TokenEndpoint;
        downstreams["OnBehalfOf:ClientId"] = "students-api";
        downstreams["OnBehalfOf:ClientSecret"] = "super-secret";
        return downstreams;
    }

    private static IHost BuildHost(
        List<string> logs, Dictionary<string, string?> settings, params string[] downstreamNames)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new CapturingLoggerProvider(logs)));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHybridCache();

        builder.Services.AddOnBehalfOf(builder.Configuration);

        foreach (var name in downstreamNames)
        {
            builder.Services
                .AddHttpClient(name, client => client.BaseAddress = new Uri($"https://{name}.example/"))
                .AddOnBehalfOf();
        }

        return builder.Build();
    }

    /// <summary>Collects rendered log messages so a test can assert on what an operator would see.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages;

        public CapturingLoggerProvider(List<string> messages) => _messages = messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _messages;

            public CapturingLogger(List<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (_messages)
                {
                    _messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
