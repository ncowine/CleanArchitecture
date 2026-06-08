using Students.Domain;

namespace Students.Application.Abstractions;

public interface IInstructorRepository
{
    Task AddAsync(Instructor instructor, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid instructorId, CancellationToken cancellationToken);
}
