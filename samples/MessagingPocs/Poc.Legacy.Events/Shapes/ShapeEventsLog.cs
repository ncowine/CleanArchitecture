using Prism.Events;
using Payloads = Poc.Shapes;

namespace Poc.Legacy.Events.Shapes;

/// <summary>
/// Subscribes to the ten shape events and writes each one received to <c>log</c>, on the UI thread. Subscribing is what
/// binds the app's queue to them.
/// </summary>
/// <remarks>
/// ✅ DO keep the instance for as long as the window lives: Prism holds these subscriptions weakly, so they end when
///    this object is collected.
/// </remarks>
public sealed class ShapeEventsLog
{
    private readonly Action<string> log;

    public ShapeEventsLog(IEventAggregator events, Action<string> log)
    {
        this.log = log;

        events.GetEvent<StatusChanged>().Subscribe(OnStatusChanged, ThreadOption.UIThread);
        events.GetEvent<MaintenanceScheduled>().Subscribe(OnMaintenanceScheduled, ThreadOption.UIThread);
        events.GetEvent<CostRecorded>().Subscribe(OnCostRecorded, ThreadOption.UIThread);
        events.GetEvent<WarrantyChecked>().Subscribe(OnWarrantyChecked, ThreadOption.UIThread);
        events.GetEvent<TagsChanged>().Subscribe(OnTagsChanged, ThreadOption.UIThread);
        events.GetEvent<LocationMoved>().Subscribe(OnLocationMoved, ThreadOption.UIThread);
        events.GetEvent<AttributesChanged>().Subscribe(OnAttributesChanged, ThreadOption.UIThread);
        events.GetEvent<UsageReported>().Subscribe(OnUsageReported, ThreadOption.UIThread);
        events.GetEvent<BatchCounted>().Subscribe(OnBatchCounted, ThreadOption.UIThread);
        events.GetEvent<AuditRequested>().Subscribe(OnAuditRequested, ThreadOption.UIThread);
    }

    private void OnStatusChanged(Payloads.StatusChanged message)
    {
        log($"StatusChanged: {message}");
    }

    private void OnMaintenanceScheduled(Payloads.MaintenanceScheduled message)
    {
        log($"MaintenanceScheduled: {message}");
    }

    private void OnCostRecorded(Payloads.CostRecorded message)
    {
        log($"CostRecorded: {message}");
    }

    private void OnWarrantyChecked(Payloads.WarrantyChecked message)
    {
        log($"WarrantyChecked: {message}");
    }

    private void OnTagsChanged(Payloads.TagsChanged message)
    {
        log($"TagsChanged: {message}");
    }

    private void OnLocationMoved(Payloads.LocationMoved message)
    {
        log($"LocationMoved: {message}");
    }

    private void OnAttributesChanged(Payloads.AttributesChanged message)
    {
        log($"AttributesChanged: {message}");
    }

    private void OnUsageReported(Payloads.UsageReported message)
    {
        log($"UsageReported: {message}");
    }

    private void OnBatchCounted(Payloads.BatchCounted message)
    {
        log($"BatchCounted: {message}");
    }

    private void OnAuditRequested(Payloads.AuditRequested message)
    {
        log($"AuditRequested: {message}");
    }
}
