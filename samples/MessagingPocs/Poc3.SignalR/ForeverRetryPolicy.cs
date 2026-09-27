using Microsoft.AspNetCore.SignalR.Client;

namespace Poc3.SignalR;

/// <summary>
/// Keeps reconnecting for as long as the app runs: quickly at first, then every 10 seconds.
/// </summary>
/// <remarks>
/// ❌ DON'T rely on <c>WithAutomaticReconnect()</c> with no arguments for a desktop app. It tries four times (after 0, 2,
///    10 and 30 seconds) and then gives up for good: the connection closes, and the window silently stops updating
///    until the user restarts it. An API restart or a network blip of more than about 45 seconds is enough.
/// </remarks>
public sealed class ForeverRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] FirstDelays = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];
    private static readonly TimeSpan SteadyDelay = TimeSpan.FromSeconds(10);

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < FirstDelays.Length ? FirstDelays[retryContext.PreviousRetryCount] : SteadyDelay;
}
