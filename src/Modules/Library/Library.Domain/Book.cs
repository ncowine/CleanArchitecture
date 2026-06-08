namespace Library.Domain;

/// <summary>
/// A catalog title. Aggregate root holding the bibliographic metadata. The physical items are
/// <see cref="BookCopy"/> aggregates that reference this book by id — so circulating a copy never has to
/// load (or lock) the catalog record.
/// </summary>
public sealed class Book
{
    public Guid Id { get; private set; }
    public string Isbn { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Author { get; private set; } = null!;
    public BookCategory Category { get; private set; }
    public int PublishedYear { get; private set; }
    public string? Description { get; private set; }

    private Book() { }

    private Book(
        Guid id, string isbn, string title, string author, BookCategory category, int publishedYear, string? description)
    {
        Id = id;
        Isbn = isbn;
        Title = title;
        Author = author;
        Category = category;
        PublishedYear = publishedYear;
        Description = description;
    }

    public static Book Create(
        string isbn, string title, string author, BookCategory category, int publishedYear, string? description)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            throw new DomainException("ISBN is required.");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Title is required.");
        if (string.IsNullOrWhiteSpace(author))
            throw new DomainException("Author is required.");
        if (publishedYear is < 1450 or > 2100)
            throw new DomainException("Published year is out of range.");

        return new Book(
            id: Guid.NewGuid(),
            isbn: NormalizeIsbn(isbn),
            title: title.Trim(),
            author: author.Trim(),
            category: category,
            publishedYear: publishedYear,
            description: string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    // Store ISBNs in a canonical form (no separators) so lookups and the unique index are reliable.
    private static string NormalizeIsbn(string isbn) =>
        isbn.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
}
