using BuildingBlocks.Messaging;
using TesterGuide.Application.Abstractions;

namespace TesterGuide.Application.Templates;

/// <summary>List all config templates.</summary>
public static class ListConfigTemplates
{
    public sealed record TemplateView(
        Guid Id, string Name, string Description, Guid FocusId, string FocusName, string Mode, bool SyncEnabled);

    public sealed record Query : IRequest<IReadOnlyList<TemplateView>>;

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<TemplateView>>
    {
        private readonly IGuideReadService _reads;

        public Handler(IGuideReadService reads)
        {
            _reads = reads;
        }

        public Task<IReadOnlyList<TemplateView>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.ListTemplatesAsync(cancellationToken);
    }
}
