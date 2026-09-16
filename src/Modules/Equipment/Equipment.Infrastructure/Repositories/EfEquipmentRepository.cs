using Equipment.Application.Abstractions;
using Equipment.Domain;
using Equipment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Equipment.Infrastructure.Repositories;

internal sealed class EfEquipmentRepository : IEquipmentRepository
{
    private readonly EquipmentDbContext _db;

    public EfEquipmentRepository(EquipmentDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(EquipmentAsset asset, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Equipment.AddAsync(asset, cancellationToken);
    }

    public Task<EquipmentAsset?> GetAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        _db.Equipment.FirstOrDefaultAsync(asset => asset.Id == equipmentId, cancellationToken);

    public void Remove(EquipmentAsset asset) => _db.Equipment.Remove(asset);
}
