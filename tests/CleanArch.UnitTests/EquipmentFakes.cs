using BuildingBlocks.RealTime;
using Equipment.Application.Abstractions;
using Equipment.Domain;

namespace CleanArch.UnitTests;

internal sealed class FakeEquipmentRepository : IEquipmentRepository
{
    private readonly Dictionary<Guid, EquipmentAsset> _assets = new();

    public List<EquipmentAsset> Added { get; } = new();
    public List<EquipmentAsset> Removed { get; } = new();

    public void Seed(EquipmentAsset asset) => _assets[asset.Id] = asset;

    public Task AddAsync(EquipmentAsset asset, CancellationToken cancellationToken)
    {
        Added.Add(asset);
        _assets[asset.Id] = asset;
        return Task.CompletedTask;
    }

    public Task<EquipmentAsset?> GetAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        Task.FromResult(_assets.TryGetValue(equipmentId, out var asset) ? asset : null);

    public void Remove(EquipmentAsset asset)
    {
        Removed.Add(asset);
        _assets.Remove(asset.Id);
    }
}

internal sealed class FakeEquipmentCacheInvalidator : IEquipmentCacheInvalidator
{
    public List<Guid> Invalidated { get; } = new();

    public Task RemoveAsync(Guid equipmentId, CancellationToken cancellationToken)
    {
        Invalidated.Add(equipmentId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeEquipmentChangeNotifier : IEquipmentChangeNotifier
{
    public List<(Guid EquipmentId, Guid? SiteId)> Notified { get; } = new();

    public void Notify(Guid equipmentId, Guid? siteId = null) => Notified.Add((equipmentId, siteId));
}

internal sealed class FakeRealtimeDispatch : IRealtimeDispatch
{
    public List<(string Group, RealtimeEvent Event)> Published { get; } = new();

    public void Publish(string group, RealtimeEvent realtimeEvent) => Published.Add((group, realtimeEvent));
}
