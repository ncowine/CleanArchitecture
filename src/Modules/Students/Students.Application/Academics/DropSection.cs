using BuildingBlocks.Messaging;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>Drop a student from a section. If they held a seat, the section promotes the next waitlisted student.</summary>
public static class DropSection
{
    public sealed record Command(Guid SectionId, Guid StudentId)
        : IRequest<Guid>, IStudentsCommand, IAuditableRequest;

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ICourseSectionRepository _sections;

        public Handler(ICourseSectionRepository sections)
        {
            _sections = sections;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var section = await _sections.GetAsync(command.SectionId, cancellationToken)
                ?? throw new DomainException($"No section exists with id '{command.SectionId}'.");

            section.Drop(command.StudentId);
            return section.Id;
        }
    }
}
