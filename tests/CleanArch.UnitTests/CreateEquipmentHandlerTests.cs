using Equipment.Application.Inventory;
using Equipment.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class CreateEquipmentHandlerTests
{
    [Fact]
    public async Task Creates_the_asset_and_publishes_an_EquipmentCreated_event()
    {
        var repository = new FakeEquipmentRepository();
        var realtime = new FakeRealtimeDispatch();
        var handler = new CreateEquipment.Handler(repository, realtime);

        var id = await handler.Handle(
            new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);

        var added = Assert.Single(repository.Added);
        Assert.Equal(id, added.Id);
        Assert.Equal(EquipmentStatus.Available, added.Status);

        var (group, evt) = Assert.Single(realtime.Published);
        Assert.Equal("equipment", group);
        Assert.Equal("EquipmentCreated", evt.Type);
    }
}
