using Prism.Events;

namespace Shared.Events
{
    /// <summary>A common event: published to whichever application's exchange sends it.</summary>
    public class UserChanged : PubSubEvent<User>
    {
    }
}
