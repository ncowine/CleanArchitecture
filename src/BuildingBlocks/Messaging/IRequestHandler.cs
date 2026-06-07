namespace BuildingBlocks.Messaging;

/// <summary>
/// Handles a single <typeparamref name="TRequest"/>. Exactly one handler per request type.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
