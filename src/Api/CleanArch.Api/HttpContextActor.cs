using BuildingBlocks.Auditing;

namespace CleanArch.Api;

/// <summary>
/// The actor for the current request: the authenticated user's name when present, else the dev
/// <c>X-Actor</c> header, else "system" (background work / unauthenticated reads).
/// </summary>
internal sealed class HttpContextActor : ICurrentActor
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextActor(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string Current
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user?.Identity is { IsAuthenticated: true })
            {
                return user.Identity.Name ?? "authenticated";
            }

            var header = _accessor.HttpContext?.Request.Headers["X-Actor"].ToString();
            return string.IsNullOrWhiteSpace(header) ? "system" : header;
        }
    }
}
