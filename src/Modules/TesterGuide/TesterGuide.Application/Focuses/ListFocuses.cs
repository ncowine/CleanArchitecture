using BuildingBlocks.Messaging;
using TesterGuide.Application.Abstractions;

namespace TesterGuide.Application.Focuses;

/// <summary>List all focuses.</summary>
public static class ListFocuses
{
    public sealed record FocusView(Guid Id, string Name, string Description);

    public sealed record Query : IRequest<IReadOnlyList<FocusView>>;

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<FocusView>>
    {
        private readonly IGuideReadService _reads;

        public Handler(IGuideReadService reads)
        {
            _reads = reads;
        }

        public Task<IReadOnlyList<FocusView>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.ListFocusesAsync(cancellationToken);
    }
}
