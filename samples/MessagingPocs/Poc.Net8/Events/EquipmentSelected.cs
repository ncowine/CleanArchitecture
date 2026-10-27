using Poc.Shared;
using Prism.Events;

namespace Poc.Net8.Events;

/// <summary>
/// The user selected a row (null: cleared the selection). LOCAL ONLY: raised and handled inside this app.
/// </summary>
/// <remarks>
/// It's local because of WHERE it lives, not because of an attribute: App.xaml.cs registers only the
/// Poc.Legacy.Events assembly with the legacy adapter, and this class is in the app's own assembly. So the adapter
/// never binds it, never receives it, and PublishRemote on it throws ("not registered"). Use Publish().
/// ✅ DO keep app-internal events in the app's own project, apart from the events assembly you register.
/// ❌ DON'T put a local event in Poc.Legacy.Events: everything there is registered, so it would be bound on the broker
///    and could be published to other apps by accident.
/// Because it never leaves the process, the payload needn't be serializable or stable: here it's a UI row record.
/// </remarks>
public class EquipmentSelected : PubSubEvent<EquipmentRow?>
{
}
