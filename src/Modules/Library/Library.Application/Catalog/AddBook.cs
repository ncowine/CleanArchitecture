using BuildingBlocks.Messaging;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Catalog;

/// <summary>Add a title to the catalog.</summary>
public static class AddBook
{
    public sealed record Command(
        string Isbn, string Title, string Author, BookCategory Category, int PublishedYear, string? Description)
        : IRequest<Guid>, ILibraryCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Isbn).NotEmpty().MaximumLength(20);
            RuleFor(command => command.Title).NotEmpty().MaximumLength(300);
            RuleFor(command => command.Author).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Category).IsInEnum();
            RuleFor(command => command.PublishedYear).InclusiveBetween(1450, 2100);
            RuleFor(command => command.Description).MaximumLength(2000);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IBookRepository _books;

        public Handler(IBookRepository books)
        {
            _books = books;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var book = Book.Create(
                command.Isbn, command.Title, command.Author, command.Category, command.PublishedYear, command.Description);

            await _books.AddAsync(book, cancellationToken);
            return book.Id;
        }
    }
}
