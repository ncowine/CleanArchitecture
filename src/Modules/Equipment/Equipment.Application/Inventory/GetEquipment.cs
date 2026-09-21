using BuildingBlocks.Messaging;
using Equipment.Application.Abstractions;

namespace Equipment.Application.Inventory;

/// <summary>
/// A single piece of equipment by id. Cache-aside: <see cref="IEquipmentDirectory"/>'s caching decorator
/// checks the cache first, falls back to the database on a miss, and populates the cache before
/// returning — the flow is entirely inside the Infrastructure decorator, invisible here.
/// </summary>
public static class GetEquipment
{
    public sealed record Query(Guid EquipmentId) : IRequest<Response?>;

    public sealed record Response(
        Guid Id, string Name, string Category, string AssetTag, string Status, string? SiteName, DateTime CreatedOnUtc);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IEquipmentDirectory _directory;

        public Handler(IEquipmentDirectory directory)
        {
            _directory = directory;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _directory.GetAsync(query.EquipmentId, cancellationToken);
    }
}
