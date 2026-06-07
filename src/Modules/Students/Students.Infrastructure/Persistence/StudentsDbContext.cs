using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Students.Domain;

namespace Students.Infrastructure.Persistence;

public sealed class StudentsDbContext : DbContext
{
    public StudentsDbContext(DbContextOptions<StudentsDbContext> options) : base(options)
    {
    }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<AcademicProgram> Programs => Set<AcademicProgram>();
    public DbSet<StudentHold> Holds => Set<StudentHold>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudentsDbContext).Assembly);
        modelBuilder.ApplyOutboxConfiguration();
    }
}
