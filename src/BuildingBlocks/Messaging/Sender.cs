using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Default <see cref="ISender"/>. Resolves the handler for the request's runtime type and wraps it
/// in the registered pipeline behaviors. Reflection is confined to this class; everything else is
/// strongly typed. For most apps the per-call reflection cost is negligible; if it ever shows up in
/// a profile, cache the resolved method/pipeline by request type.
/// </summary>
internal sealed class Sender : ISender
{
    private readonly IServiceProvider _provider;

    public Sender(IServiceProvider provider)
    {
        _provider = provider;
    }

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var responseType = typeof(TResponse);

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
        var handler = _provider.GetRequiredService(handlerType);
        var handleMethod = handlerType.GetMethod("Handle")!;

        RequestHandlerDelegate<TResponse> pipeline = () =>
            (Task<TResponse>)handleMethod.Invoke(handler, [request, cancellationToken])!;

        // Wrap behaviors outermost-first: the first registered behavior is the outer layer,
        // so we fold the resolved list in reverse to build the chain from the inside out.
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);
        var behaviorMethod = behaviorType.GetMethod("Handle")!;

        foreach (var behavior in _provider.GetServices(behaviorType).Reverse())
        {
            var next = pipeline;
            var current = behavior!;
            pipeline = () => (Task<TResponse>)behaviorMethod.Invoke(current, [request, next, cancellationToken])!;
        }

        return pipeline();
    }
}
