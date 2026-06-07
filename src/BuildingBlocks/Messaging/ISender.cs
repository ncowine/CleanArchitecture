namespace BuildingBlocks.Messaging;

/// <summary>
/// Dispatches a request to its handler, running any registered pipeline behaviors around it.
/// Inject this wherever you need to send a command or query (e.g. endpoints).
/// </summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
