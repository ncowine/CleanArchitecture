using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Application.Students;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Reads;

/// <summary>
/// Implements the Students read projections. The two methods show the core idea: the <b>projection</b>
/// decides what's fetched. The summary touches only a few columns and no related data; the detail pulls
/// the owned address, the contact/enrollment collections, and a SQL-side computed count.
/// </summary>
internal sealed class StudentReadService : IStudentReadService
{
    private readonly StudentsDbContext _db;

    public StudentReadService(StudentsDbContext db)
    {
        _db = db;
    }

    public Task<GetStudent.Response?> GetSummaryAsync(Guid studentId, CancellationToken cancellationToken) =>
        _db.Students
            .AsNoTracking()
            .Where(student => student.Id == studentId)
            .Select(student => new GetStudent.Response(
                student.Id,
                student.FirstName + " " + student.LastName,
                student.Email,
                student.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<GetStudentDetail.Response?> GetDetailAsync(Guid studentId, CancellationToken cancellationToken) =>
        _db.Students
            .AsNoTracking()
            .Where(student => student.Id == studentId)
            .Select(student => new GetStudentDetail.Response(
                student.Id,
                student.FirstName,
                student.LastName,
                student.Email,
                student.Status.ToString(),
                student.Address == null
                    ? null
                    : new GetStudentDetail.AddressDto(
                        student.Address.Line1,
                        student.Address.Line2,
                        student.Address.City,
                        student.Address.State,
                        student.Address.PostalCode,
                        student.Address.Country),
                student.EmergencyContacts
                    .Select(contact => new GetStudentDetail.EmergencyContactDto(
                        contact.Name, contact.Relationship, contact.PhoneNumber))
                    .ToList(),
                student.Enrollments
                    .Select(enrollment => new GetStudentDetail.EnrollmentDto(
                        enrollment.ProgramId,
                        enrollment.Term,
                        enrollment.Status.ToString(),
                        enrollment.EnrolledOn,
                        enrollment.Grade))
                    .ToList(),
                // Computed in SQL — no entities materialized just to count.
                student.Enrollments.Count(enrollment => enrollment.Status == EnrollmentStatus.Enrolled)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<GetStudent.Response>> SearchAsync(
        int page, int pageSize, StudentStatus? status, CancellationToken cancellationToken)
    {
        var query = _db.Students.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(student => student.Status == status.Value);
        }

        // Count and page in SQL — same filtered query, one COUNT + one windowed SELECT.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(student => student.LastName)
            .ThenBy(student => student.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(student => new GetStudent.Response(
                student.Id,
                student.FirstName + " " + student.LastName,
                student.Email,
                student.Status.ToString()))
            .ToListAsync(cancellationToken);

        return new PagedResult<GetStudent.Response>(items, page, pageSize, totalCount);
    }
}
