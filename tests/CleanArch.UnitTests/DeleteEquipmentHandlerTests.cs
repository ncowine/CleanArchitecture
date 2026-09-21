using Equipment.Application.Inventory;
using Equipment.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class DeleteEquipmentHandlerTests
{
    [Fact]
    public async Task Removes_the_asset_invalidates_its_cache_entry_and_publishes_an_event()
    {
        var repository = new FakeEquipmentRepository();
        var asset = EquipmentAsset.Create("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001");
        repository.Seed(asset);
        var cache = new FakeEquipmentCacheInvalidator();
        var changeNotifier = new FakeEquipmentChangeNotifier();
        var realtime = new FakeRealtimeDispatch();
        var handler = new DeleteEquipment.Handler(repository, cache, changeNotifier, realtime);

        var deleted = await handler.Handle(new DeleteEquipment.Command(asset.Id), default);

        Assert.True(deleted);
        Assert.Equal(asset, Assert.Single(repository.Removed));
        Assert.Equal(asset.Id, Assert.Single(cache.Invalidated));
        Assert.Equal(asset.Id, Assert.Single(changeNotifier.Notified).EquipmentId);

        var (group, evt) = Assert.Single(realtime.Published);
        Assert.Equal("equipment", group);
        Assert.Equal("EquipmentDeleted", evt.Type);
    }

    [Fact]
    public async Task Missing_asset_returns_false_and_touches_nothing()
    {
        var repository = new FakeEquipmentRepository();
        var cache = new FakeEquipmentCacheInvalidator();
        var changeNotifier = new FakeEquipmentChangeNotifier();
        var realtime = new FakeRealtimeDispatch();
        var handler = new DeleteEquipment.Handler(repository, cache, changeNotifier, realtime);

        var deleted = await handler.Handle(new DeleteEquipment.Command(Guid.NewGuid()), default);

        Assert.False(deleted);
        Assert.Empty(cache.Invalidated);
        Assert.Empty(changeNotifier.Notified);
        Assert.Empty(realtime.Published);
    }
}
