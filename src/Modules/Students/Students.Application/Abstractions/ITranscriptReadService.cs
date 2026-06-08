using Students.Application.Academics;

namespace Students.Application.Abstractions;

/// <summary>Read-side projections for a student's academic record: transcript and standing (both GPA-derived).</summary>
public interface ITranscriptReadService
{
    Task<GetTranscript.Response> GetTranscriptAsync(Guid studentId, CancellationToken cancellationToken);

    Task<GetAcademicStanding.Response> GetStandingAsync(Guid studentId, CancellationToken cancellationToken);
}
