namespace TesterGuide.Domain;

/// <summary>Lifecycle of a guide config: authored (Draft), in use (Active), or finished (Closed).</summary>
public enum ConfigStatus
{
    Draft,
    Active,
    Closed,
}
