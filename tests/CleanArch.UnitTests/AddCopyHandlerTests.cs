using Library.Application.Catalog;
using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class AddCopyHandlerTests
{
    [Fact]
    public async Task Adds_a_copy_when_the_book_exists()
    {
        var book = Book.Create("123456789", "Title", "Author", BookCategory.Science, 2000, null);
        var books = new FakeBookRepository();
        books.Seed(book);
        var copies = new FakeBookCopyRepository();
        var handler = new AddCopy.Handler(copies, books);

        var id = await handler.Handle(new AddCopy.Command(book.Id, "BC-1", CopyCondition.New), default);

        var added = Assert.Single(copies.Added);
        Assert.Equal(id, added.Id);
        Assert.Equal(book.Id, added.BookId);
        Assert.Equal(CopyStatus.Available, added.Status);
    }

    [Fact]
    public async Task Throws_when_the_book_does_not_exist()
    {
        var handler = new AddCopy.Handler(new FakeBookCopyRepository(), new FakeBookRepository());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new AddCopy.Command(Guid.NewGuid(), "BC-1", CopyCondition.New), default));
    }
}
