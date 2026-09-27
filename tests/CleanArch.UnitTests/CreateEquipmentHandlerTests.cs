using Equipment.Application.Inventory;
using Equipment.Domain;
using Equipment.Messages;
using Xunit;

namespace CleanArch.UnitTests;

public class CreateEquipmentHandlerTests
{
    [Fact]
    public async Task Creates_the_asset_and_publishes_an_EquipmentCreated_event()
    {
        var repository = new FakeEquipmentRepository();
        var changeNotifier = new FakeEquipmentChangeNotifier();
        var realtime = new FakeRealtimeDispatch();
        var outbox = new FakeEquipmentOutbox();
        var handler = new CreateEquipment.Handler(repository, changeNotifier, realtime, outbox);

        var siteId = Guid.NewGuid();
        var id = await handler.Handle(
            new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001", siteId), default);

        var added = Assert.Single(repository.Added);
        Assert.Equal(id, added.Id);
        Assert.Equal(EquipmentStatus.Available, added.Status);

        var notified = Assert.Single(changeNotifier.Notified);
        Assert.Equal(id, notified.EquipmentId);
        Assert.Equal(siteId, notified.SiteId);

        var (group, evt) = Assert.Single(realtime.Published);
        Assert.Equal("equipment", group);
        Assert.Equal("EquipmentCreated", evt.Type);

        // The same event goes to both channels: the outbox (RabbitMQ) and real-time (SignalR).
        var created = Assert.IsType<EquipmentCreated>(Assert.Single(outbox.Enqueued));
        Assert.Same(created, evt.Payload);
        Assert.Equal(id, created.Id);
        Assert.Equal("ThinkPad X1", created.Name);
        Assert.Equal("Laptop", created.Category);
        Assert.Equal("LAP-001", created.AssetTag);
        Assert.Equal("Available", created.Status);
    }
}
