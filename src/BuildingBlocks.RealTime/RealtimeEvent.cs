namespace BuildingBlocks.RealTime;

/// <summary>
/// A realtime notification to push to a group of connected clients. <see cref="Type"/> is the client-facing
/// event name (the method clients subscribe to); <see cref="Payload"/> is the serialized body.
/// </summary>
public sealed record RealtimeEvent(string Type, object Payload);
