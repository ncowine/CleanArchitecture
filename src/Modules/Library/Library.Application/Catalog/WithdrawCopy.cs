using BuildingBlocks.Messaging;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Catalog;

/// <summary>Withdraw a copy from the collection (not allowed while it is on loan).</summary>
public static class WithdrawCopy
{
    public sealed record Command(Guid CopyId) : IRequest<Guid>, ILibraryCommand, IAuditableRequest;

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IBookCopyRepository _copies;

        public Handler(IBookCopyRepository copies)
        {
            _copies = copies;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var copy = await _copies.GetAsync(command.CopyId, cancellationToken)
                ?? throw new DomainException($"No copy exists with id '{command.CopyId}'.");

            copy.Withdraw();
            return copy.Id;
        }
    }
}
