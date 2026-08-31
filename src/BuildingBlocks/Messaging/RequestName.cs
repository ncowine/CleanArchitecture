namespace BuildingBlocks.Messaging;

/// <summary>
/// How a request is named in logs and on the audit trail.
/// </summary>
/// <remarks>
/// Features here are vertical slices, so a request is a type nested inside the feature class —
/// <c>CreateStudent.Command</c>, <c>GetStudentLoans.Query</c>. That makes <see cref="Type.Name"/> the
/// useless string "Command" or "Query" for every request in the system, which is exactly what a log line
/// or an audit record must not say. The enclosing type is the part that names the operation.
/// </remarks>
public static class RequestName
{
    /// <summary>
    /// The operation, without the request-kind suffix: <c>CreateStudent</c>. This is what the audit trail
    /// records as its action, so it reads as something a person did rather than as a class name.
    /// </summary>
    public static string Feature(Type requestType)
        => requestType.DeclaringType?.Name ?? requestType.Name;

    /// <summary>
    /// The operation and its kind: <c>CreateStudent.Command</c>, <c>GetStudentLoans.Query</c>. Logs get
    /// the fuller form because a log line is read on its own, with no surrounding record to say whether
    /// what ran was a read or a write.
    /// </summary>
    public static string Display(Type requestType)
        => requestType.DeclaringType is { } feature
            ? $"{feature.Name}.{requestType.Name}"
            : requestType.Name;
}
