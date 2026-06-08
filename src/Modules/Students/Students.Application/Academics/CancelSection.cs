using BuildingBlocks.Messaging;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>Cancel a section so no further enrollment is possible.</summary>
public static class CancelSection
{
    public sealed record Command(Guid SectionId) : IRequest<Guid>, IStudentsCommand, IAuditableRequest;

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

            section.Cancel();
            return section.Id;
        }
    }
}
