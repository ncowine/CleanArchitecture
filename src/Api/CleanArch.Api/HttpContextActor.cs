using BuildingBlocks.Auditing;

namespace CleanArch.Api;

/// <summary>
/// Stub actor source until real authentication exists: reads an <c>X-Actor</c> request header, falling
/// back to "system". Replace with one that reads the authenticated user once auth is in place.
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
            var actor = _accessor.HttpContext?.Request.Headers["X-Actor"].ToString();
            return string.IsNullOrWhiteSpace(actor) ? "system" : actor;
        }
    }
}
