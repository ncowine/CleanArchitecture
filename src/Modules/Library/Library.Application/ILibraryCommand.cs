namespace Library.Application;

/// <summary>
/// Marks a request as a Library-module write that must run inside a <c>LibraryDbContext</c>
/// transaction. Mirrors the Students module: the Library transaction behavior wraps only requests
/// carrying this marker, so queries — and other modules' requests — are left untouched. Each module
/// has its own marker and its own behavior because a transaction can span only one database.
/// </summary>
public interface ILibraryCommand;
