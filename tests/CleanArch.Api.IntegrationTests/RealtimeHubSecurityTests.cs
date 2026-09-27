using System.Reflection;
using CleanArch.Api.Realtime;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// The SignalR hub pushes every equipment change to whoever is connected, so an anonymous connection would leak
/// data. It must require an authenticated caller, through the same schemes as the rest of the API.
/// </summary>
public class RealtimeHubSecurityTests
{
    [Fact]
    public void The_realtime_hub_requires_an_authenticated_caller()
    {
        var authorize = typeof(PresenceHub).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Null(typeof(PresenceHub).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.DoesNotContain(typeof(PresenceHub).GetMethods(), method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    [Fact]
    public void The_realtime_hub_accepts_the_same_schemes_as_the_rest_of_the_API()
    {
        // No scheme list and no policy: the default scheme's selector decides (API key, Okta bearer, or AD Basic),
        // exactly as for the write endpoints.
        var authorize = typeof(PresenceHub).GetCustomAttribute<AuthorizeAttribute>()!;

        Assert.Null(authorize.AuthenticationSchemes);
        Assert.Null(authorize.Policy);
    }
}
