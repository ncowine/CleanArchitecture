using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Metrics for the outbox dispatcher, published on the <see cref="MeterName"/> meter, and a span per delivery
/// attempt on the <see cref="ActivitySourceName"/> source. Subscribe to both in OpenTelemetry.
/// </summary>
public static class OutboxDiagnostics
{
    public const string MeterName = "CleanArch.Outbox";

    /// <summary>
    /// The ActivitySource name. Each delivery attempt is a span continuing the trace of the request that wrote the
    /// row; without a listener there is no span and the publish starts a trace of its own.
    /// </summary>
    public const string ActivitySourceName = "CleanArch.Outbox";

    private static readonly Meter Meter = new(MeterName);

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Counter<long> Delivered = Meter.CreateCounter<long>("outbox.delivered");
    public static readonly Counter<long> Failed = Meter.CreateCounter<long>("outbox.failed");
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("outbox.dead_lettered");
}
