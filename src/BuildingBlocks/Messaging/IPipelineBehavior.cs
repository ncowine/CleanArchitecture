namespace BuildingBlocks.Messaging;

/// <summary>
/// The continuation that invokes the next behavior in the pipeline, or the handler itself.
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// A cross-cutting step that wraps every request of the matching type — validation, logging,
/// transactions, caching, etc. Behaviors run in registration order, outermost first.
/// Call <paramref name="next"/> to continue the pipeline; skip it to short-circuit.
/// </summary>
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
