using BuildingBlocks.Messaging;
using Equipment.Application.Abstractions;

namespace Equipment.Application.Inventory;

/// <summary>
/// The equipment catalogue — a reference list of purchasable models with an estimated cost, read from a
/// file. Deliberately bypasses the database: not every application service sits on top of persistence.
/// </summary>
public static class GetEquipmentCatalogue
{
    public sealed record Query : IRequest<IReadOnlyList<CatalogueItem>>;

    public sealed record CatalogueItem(string Model, string Category, decimal EstimatedCost, string? Description);

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<CatalogueItem>>
    {
        private readonly IEquipmentCatalogueReader _catalogue;

        public Handler(IEquipmentCatalogueReader catalogue)
        {
            _catalogue = catalogue;
        }

        public Task<IReadOnlyList<CatalogueItem>> Handle(Query query, CancellationToken cancellationToken) =>
            _catalogue.GetCatalogueAsync(cancellationToken);
    }
}
