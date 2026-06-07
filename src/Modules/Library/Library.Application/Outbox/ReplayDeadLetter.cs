using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;

namespace Library.Application.Outbox;

/// <summary>
/// Requeues a dead-lettered message for redelivery. Not an <c>ILibraryCommand</c>: it's a standalone
/// admin operation, so the shared OutboxReplayer commits its own change.
/// </summary>
public static class ReplayDeadLetter
{
    public sealed record Command(Guid MessageId) : IRequest<Result>;

    public sealed record Result(bool Requeued);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IOutboxReplayer _replayer;

        public Handler(IOutboxReplayer replayer)
        {
            _replayer = replayer;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var requeued = await _replayer.RequeueAsync(command.MessageId, cancellationToken);
            return new Result(requeued);
        }
    }
}
