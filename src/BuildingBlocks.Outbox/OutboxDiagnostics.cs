using System.Diagnostics.Metrics;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Metrics for the outbox dispatcher, published on the <see cref="MeterName"/> meter. Subscribe to that
/// meter in OpenTelemetry to see delivery/failure/dead-letter counts.
/// </summary>
public static class OutboxDiagnostics
{
    public const string MeterName = "CleanArch.Outbox";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Delivered = Meter.CreateCounter<long>("outbox.delivered");
    public static readonly Counter<long> Failed = Meter.CreateCounter<long>("outbox.failed");
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("outbox.dead_lettered");
}
