using Equipment.Application.Inventory;
using Equipment.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class UpdateEquipmentHandlerTests
{
    private static EquipmentAsset Seeded(FakeEquipmentRepository repository)
    {
        var asset = EquipmentAsset.Create("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001");
        repository.Seed(asset);
        return asset;
    }

    [Fact]
    public async Task Updates_the_asset_invalidates_its_cache_entry_and_publishes_an_event()
    {
        var repository = new FakeEquipmentRepository();
        var asset = Seeded(repository);
        var cache = new FakeEquipmentCacheInvalidator();
        var realtime = new FakeRealtimeDispatch();
        var handler = new UpdateEquipment.Handler(repository, cache, realtime);

        var updated = await handler.Handle(
            new UpdateEquipment.Command(asset.Id, "ThinkPad X1 Carbon", EquipmentCategory.Other, "LAP-002"), default);

        Assert.True(updated);
        Assert.Equal("ThinkPad X1 Carbon", asset.Name);
        Assert.Equal(EquipmentCategory.Other, asset.Category);
        Assert.Equal("LAP-002", asset.AssetTag);
        Assert.Equal(asset.Id, Assert.Single(cache.Invalidated));

        var (group, evt) = Assert.Single(realtime.Published);
        Assert.Equal("equipment", group);
        Assert.Equal("EquipmentUpdated", evt.Type);
    }

    [Fact]
    public async Task Missing_asset_returns_false_and_touches_nothing()
    {
        var repository = new FakeEquipmentRepository();
        var cache = new FakeEquipmentCacheInvalidator();
        var realtime = new FakeRealtimeDispatch();
        var handler = new UpdateEquipment.Handler(repository, cache, realtime);

        var updated = await handler.Handle(
            new UpdateEquipment.Command(Guid.NewGuid(), "Name", EquipmentCategory.Laptop, "TAG"), default);

        Assert.False(updated);
        Assert.Empty(cache.Invalidated);
        Assert.Empty(realtime.Published);
    }
}
