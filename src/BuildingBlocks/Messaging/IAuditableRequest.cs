namespace BuildingBlocks.Messaging;

/// <summary>
/// Marks a request whose execution should be recorded to the audit sink. The audit behavior wraps only
/// requests carrying this marker, so reads and other traffic aren't audited.
/// </summary>
public interface IAuditableRequest;
