using Poc.Net472.Events;
using Poc.Shared;
using Prism.Events;
using Prism.Mvvm;

namespace Poc.Net472.ViewModels;

/// <summary>
/// The status bar. Knows nothing about the main view model: it only hears the local <see cref="EquipmentSelected"/>.
/// </summary>
public sealed class SelectionStatusViewModel : BindableBase
{
    private string text = "Nothing selected";

    public SelectionStatusViewModel(IEventAggregator events)
    {
        // Published on the UI thread (from a binding), so PublisherThread is enough here and runs synchronously.
        // Method group, so Prism's weak reference keeps working for as long as this view model lives.
        events.GetEvent<EquipmentSelected>().Subscribe(OnSelected, ThreadOption.PublisherThread);
    }

    public string Text
    {
        get => text;
        private set => SetProperty(ref text, value);
    }

    private void OnSelected(EquipmentRow? row)
    {
        Text = row is null ? "Nothing selected" : $"Selected {row.AssetTag} — {row.Name} ({row.Status})  [local event, never sent]";
    }
}
