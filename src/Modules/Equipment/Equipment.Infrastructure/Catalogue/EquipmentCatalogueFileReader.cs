using System.Text.Json;
using Equipment.Application.Abstractions;
using Equipment.Application.Inventory;

namespace Equipment.Infrastructure.Catalogue;

/// <summary>
/// Reads the equipment catalogue straight from a JSON file shipped alongside the app — no
/// <c>EquipmentDbContext</c> anywhere in this class. Demonstrates that an application service doesn't
/// always need persistence: this one answers "what can we buy, and at what price?", a question the
/// inventory database (which only knows what's actually owned) can't answer.
/// </summary>
internal sealed class EquipmentCatalogueFileReader : IEquipmentCatalogueReader
{
    private static readonly string CataloguePath = Path.Combine(AppContext.BaseDirectory, "Catalogue", "equipment-catalogue.json");

    public async Task<IReadOnlyList<GetEquipmentCatalogue.CatalogueItem>> GetCatalogueAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(CataloguePath);
        var rows = await JsonSerializer.DeserializeAsync<List<CatalogueRow>>(
            stream, JsonOptions, cancellationToken) ?? [];

        return rows
            .Select(row => new GetEquipmentCatalogue.CatalogueItem(row.Model, row.Category, row.EstimatedCost, row.Description))
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record CatalogueRow(string Model, string Category, decimal EstimatedCost, string? Description);
}
