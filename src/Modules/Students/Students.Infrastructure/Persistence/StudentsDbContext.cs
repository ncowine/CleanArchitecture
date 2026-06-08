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
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<StudentAccount> StudentAccounts => Set<StudentAccount>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseSection> CourseSections => Set<CourseSection>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudentsDbContext).Assembly);
        modelBuilder.ApplyOutboxConfiguration();
    }
}
