using BuildingBlocks.Messaging;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Catalog;

/// <summary>Add a physical copy of a catalog title to the collection (starts available).</summary>
public static class AddCopy
{
    public sealed record Command(Guid BookId, string Barcode, CopyCondition Condition)
        : IRequest<Guid>, ILibraryCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.BookId).NotEmpty();
            RuleFor(command => command.Barcode).NotEmpty().MaximumLength(50);
            RuleFor(command => command.Condition).IsInEnum();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IBookCopyRepository _copies;
        private readonly IBookRepository _books;

        public Handler(IBookCopyRepository copies, IBookRepository books)
        {
            _copies = copies;
            _books = books;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _books.ExistsAsync(command.BookId, cancellationToken))
                throw new DomainException($"No book exists with id '{command.BookId}'.");

            var copy = BookCopy.Create(
                command.BookId, command.Barcode, command.Condition, DateOnly.FromDateTime(DateTime.UtcNow));

            await _copies.AddAsync(copy, cancellationToken);
            return copy.Id;
        }
    }
}
