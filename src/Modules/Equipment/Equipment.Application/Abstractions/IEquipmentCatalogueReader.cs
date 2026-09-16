using Equipment.Application.Inventory;

namespace Equipment.Application.Abstractions;

/// <summary>
/// Reads the equipment catalogue from a file (CSV/JSON), never the database — the demonstration that an
/// application service doesn't always sit on top of persistence.
/// </summary>
public interface IEquipmentCatalogueReader
{
    Task<IReadOnlyList<GetEquipmentCatalogue.CatalogueItem>> GetCatalogueAsync(CancellationToken cancellationToken);
}
