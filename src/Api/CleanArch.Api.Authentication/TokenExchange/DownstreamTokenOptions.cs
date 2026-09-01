using Microsoft.Extensions.Configuration;

namespace CleanArch.Api.Authentication;

/// <summary>
/// The per-downstream half of the On-Behalf-Of configuration. The credential half — token endpoint,
/// client id, secret or signing key — is shared by every downstream and lives in
/// <see cref="OnBehalfOfOptions"/>, because this API has ONE identity at the provider. What differs per
/// downstream is only who the exchanged token is FOR: its audience and scopes.
/// <para>
/// Bound as NAMED options from <c>OnBehalfOf:Downstreams:{name}</c>, where <c>{name}</c> is the name of
/// the <c>HttpClient</c> that calls that downstream. One entry per downstream API:
/// </para>
/// <code>
/// "OnBehalfOf": {
///   "TokenEndpoint": "https://org.okta.com/oauth2/default/v1/token",
///   "ClientId": "cleanarch-api",
///   "Downstreams": {
///     "billing": { "Audience": "api://billing", "Scope": "invoices.read" },
///     "grading": { "Audience": "api://grading", "Scope": "grades.write" }
///   }
/// }
/// </code>
/// </summary>
internal sealed class DownstreamTokenOptions
{
    /// <summary>The configuration section holding one child per downstream.</summary>
    public const string SectionName = $"{OnBehalfOfOptions.SectionName}:Downstreams";

    /// <summary>The section a single named downstream binds from.</summary>
    public static string SectionFor(string downstreamName) => $"{SectionName}:{downstreamName}";

    /// <summary>
    /// The downstream API the exchanged token is for — becomes the new token's <c>aud</c>. This is the
    /// value that must differ per downstream: a token minted for one audience is (correctly) rejected by
    /// every other service.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>Space-delimited scopes to request for calls to this downstream. Optional.</summary>
    public string? Scope { get; set; }

    /// <summary>
    /// A downstream entry that sets NEITHER audience nor scope cannot produce a usefully-scoped token —
    /// it is almost always a missing or misspelled configuration section. Rejected at startup.
    /// </summary>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Audience) || !string.IsNullOrWhiteSpace(Scope);
}

/// <summary>
/// Registration-time bookkeeping for On-Behalf-Of: the configuration root the downstream sections bind
/// from, plus the names of the downstreams registered so far. Populated while the service collection is
/// being built (single-threaded), then read at startup to log what was wired and by whom.
/// </summary>
internal sealed class OnBehalfOfRegistry
{
    public OnBehalfOfRegistry(IConfiguration configuration) => Configuration = configuration;

    /// <summary>The configuration passed to <c>AddOnBehalfOf</c>, used to bind each named downstream.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Every downstream name registered against an <c>HttpClient</c>, in registration order.</summary>
    public List<string> Names { get; } = [];
}
