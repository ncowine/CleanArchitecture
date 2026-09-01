using System.Diagnostics;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Tracing for the On-Behalf-Of flow. A token exchange is a network hop to the identity provider that
/// happens INSIDE someone else's request, so without a span of its own it shows up as unexplained latency
/// on the downstream call. Subscribe to <see cref="ActivitySourceName"/> in OpenTelemetry to see it:
/// <code>tracing.AddSource(OnBehalfOfDiagnostics.ActivitySourceName)</code>
/// <para>
/// A span appears only when an exchange actually happens. A downstream call with NO child exchange span
/// was served from the token cache — which is the quickest way to tell the two apart in Tempo.
/// </para>
/// </summary>
public static class OnBehalfOfDiagnostics
{
    public const string ActivitySourceName = "CleanArch.OnBehalfOf";

    /// <summary>The span emitted around one token exchange.</summary>
    public const string ExchangeActivityName = "OnBehalfOf.Exchange";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>Tag names, so the tracing code and the troubleshooting docs cannot drift apart.</summary>
    internal static class Tags
    {
        /// <summary>The downstream's registered name — the same string as its <c>HttpClient</c> name.</summary>
        public const string Downstream = "obo.downstream";

        /// <summary>The audience requested for the exchanged token.</summary>
        public const string Audience = "obo.audience";

        /// <summary>The scopes requested for the exchanged token.</summary>
        public const string Scope = "obo.scope";

        /// <summary>Which side failed — see <see cref="TokenExchangeFailure"/>.</summary>
        public const string Failure = "obo.failure";

        /// <summary>The provider's HTTP status when it rejected or errored.</summary>
        public const string StatusCode = "obo.token_endpoint.status_code";
    }
}
