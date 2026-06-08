using BuildingBlocks.Outbox;
using Library.Domain;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Persistence;

public sealed class LibraryDbContext : DbContext
{
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options)
    {
    }

    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCopy> Copies => Set<BookCopy>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LibraryDbContext).Assembly);
        modelBuilder.ApplyOutboxConfiguration();
    }
}
