using System.Diagnostics.Metrics;

namespace Common.RabbitMQ;

/// <summary>
/// Metrics for Common.RabbitMQ, published on the <see cref="MeterName"/> meter — subscribe to it in
/// OpenTelemetry the same way <c>BuildingBlocks.Outbox</c>'s meter already is.
/// </summary>
public static class RabbitMqDiagnostics
{
    public const string MeterName = "CleanArch.RabbitMQ";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Published = Meter.CreateCounter<long>("rabbitmq.published");
    public static readonly Counter<long> PublishFailed = Meter.CreateCounter<long>("rabbitmq.publish_failed");
    public static readonly Counter<long> Consumed = Meter.CreateCounter<long>("rabbitmq.consumed");
    public static readonly Counter<long> ConsumeFailed = Meter.CreateCounter<long>("rabbitmq.consume_failed");
    public static readonly Counter<long> ConsumeDuplicate = Meter.CreateCounter<long>("rabbitmq.consume_duplicate");
    public static readonly Counter<long> Retried = Meter.CreateCounter<long>("rabbitmq.retried");
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("rabbitmq.dead_lettered");
}
