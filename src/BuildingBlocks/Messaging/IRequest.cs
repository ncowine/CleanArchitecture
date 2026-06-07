namespace BuildingBlocks.Messaging;

/// <summary>
/// A command or query that produces a <typeparamref name="TResponse"/>.
/// Use <c>Unit</c> (or any sentinel type) as the response for commands with no return value.
/// </summary>
public interface IRequest<out TResponse>;
