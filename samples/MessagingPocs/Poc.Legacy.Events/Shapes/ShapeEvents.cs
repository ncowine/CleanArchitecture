using Common.RabbitMQ;
using Prism.Events;
using Payloads = Poc.Shapes;

namespace Poc.Legacy.Events.Shapes;

/// <summary>Sends one of each of the ten shape events, with values chosen to hit each shape's edge cases.</summary>
public static class ShapeEvents
{
    public static void PublishAll(IEventAggregator events)
    {
        events.GetEvent<StatusChanged>().PublishRemote(new Payloads.StatusChanged { EquipmentId = Guid.NewGuid(), Status = Payloads.EquipmentState.InRepair });
        events.GetEvent<MaintenanceScheduled>().PublishRemote(new Payloads.MaintenanceScheduled { DueUtc = DateTime.UtcNow.AddDays(14), BookedAt = DateTimeOffset.Now });
        events.GetEvent<CostRecorded>().PublishRemote(new Payloads.CostRecorded { Amount = 1299.95m, Shipping = 12m, Currency = "EUR" });
        events.GetEvent<WarrantyChecked>().PublishRemote(new Payloads.WarrantyChecked { ExpiresUtc = null, DaysLeft = 0, Note = null });
        events.GetEvent<TagsChanged>().PublishRemote(new Payloads.TagsChanged { Tags = { "laptop", "", "Büro <3>" } });
        events.GetEvent<LocationMoved>().PublishRemote(new Payloads.LocationMoved { From = new Payloads.Location { Building = "HQ", Floor = 2 }, To = new Payloads.Location { Building = "Annex", Floor = -1 } });
        events.GetEvent<AttributesChanged>().PublishRemote(new Payloads.AttributesChanged { Attributes = { ["RAM"] = "32 GB", ["cpu"] = "x64", ["Screen.Size"] = "14\"" } });
        events.GetEvent<UsageReported>().PublishRemote(new Payloads.UsageReported { Hours = 5_000_000_000L, TemperatureC = 71.25, Overheated = true });
        events.GetEvent<BatchCounted>().PublishRemote(new Payloads.BatchCounted { Lines = { new Payloads.CountLine { Category = "Laptop", Count = 12 }, new Payloads.CountLine { Category = "Monitor", Count = 0 } } });
        events.GetEvent<AuditRequested>().PublishRemote(new Payloads.AuditRequested());
    }
}
