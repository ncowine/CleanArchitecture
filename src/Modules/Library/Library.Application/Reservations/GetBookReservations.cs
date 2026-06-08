using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Library.Application.Abstractions;

namespace Library.Application.Reservations;

/// <summary>The active hold queue for a book (pending + ready-for-pickup), in queue order.</summary>
public static class GetBookReservations
{
    public sealed record Query(Guid BookId, int Page = 1, int PageSize = 20)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<QueueEntry>>;

    public sealed record QueueEntry(
        Guid ReservationId,
        Guid StudentId,
        string Status,
        int? QueuePosition,
        DateOnly ReservedOn,
        DateOnly? ExpiresOn);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<QueueEntry>>
    {
        private readonly IReservationReadService _reads;

        public Handler(IReservationReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<QueueEntry>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetBookQueueAsync(query.BookId, query.Page, query.PageSize, cancellationToken);
    }
}
