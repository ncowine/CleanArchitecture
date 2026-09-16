using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Equipment.Application.Abstractions;
using Equipment.Domain;
using FluentValidation;

namespace Equipment.Application.Inventory;

/// <summary>Paged inventory search by category and/or status (paging/filters in the body).</summary>
public static class SearchEquipment
{
    public sealed record Query(int Page = 1, int PageSize = 20, string? Category = null, string? Status = null)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<EquipmentListItem>>;

    public sealed record EquipmentListItem(
        Guid Id, string Name, string Category, string AssetTag, string Status);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
            RuleFor(query => query.Category)
                .Must(category => category is null || Enum.TryParse<EquipmentCategory>(category, ignoreCase: true, out _))
                .WithMessage($"Category must be one of: {string.Join(", ", Enum.GetNames<EquipmentCategory>())}.");
            RuleFor(query => query.Status)
                .Must(status => status is null || Enum.TryParse<EquipmentStatus>(status, ignoreCase: true, out _))
                .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<EquipmentStatus>())}.");
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<EquipmentListItem>>
    {
        private readonly IEquipmentReadService _reads;

        public Handler(IEquipmentReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<EquipmentListItem>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.SearchAsync(query.Page, query.PageSize, query.Category, query.Status, cancellationToken);
    }
}
