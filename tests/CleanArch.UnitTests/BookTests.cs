using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class BookTests
{
    [Fact]
    public void Create_normalizes_isbn_and_trims()
    {
        var book = Book.Create("978-0-13-468599-1", "  Clean Code ", " Robert Martin ", BookCategory.Technology, 2008, null);

        Assert.Equal("9780134685991", book.Isbn);
        Assert.Equal("Clean Code", book.Title);
        Assert.Equal("Robert Martin", book.Author);
        Assert.Equal(BookCategory.Technology, book.Category);
    }

    [Theory]
    [InlineData("", "Title", "Author", 2008)]
    [InlineData("ISBN", "", "Author", 2008)]
    [InlineData("ISBN", "Title", "", 2008)]
    [InlineData("ISBN", "Title", "Author", 1000)]
    [InlineData("ISBN", "Title", "Author", 3000)]
    public void Create_with_invalid_input_throws(string isbn, string title, string author, int year) =>
        Assert.Throws<DomainException>(() => Book.Create(isbn, title, author, BookCategory.Fiction, year, null));
}
