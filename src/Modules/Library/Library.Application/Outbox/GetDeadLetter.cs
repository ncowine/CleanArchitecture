using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;

namespace Library.Application.Outbox;

/// <summary>Lists outbox messages that failed past the retry cap and were dead-lettered.</summary>
public static class GetDeadLetter
{
    public sealed record Query : IRequest<IReadOnlyList<DeadLetterEntry>>;

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<DeadLetterEntry>>
    {
        private readonly IDeadLetterReader _deadLetters;

        public Handler(IDeadLetterReader deadLetters)
        {
            _deadLetters = deadLetters;
        }

        public Task<IReadOnlyList<DeadLetterEntry>> Handle(Query query, CancellationToken cancellationToken) =>
            _deadLetters.GetAsync(cancellationToken);
    }
}
