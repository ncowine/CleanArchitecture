namespace BuildingBlocks.Auditing.Elasticsearch;

/// <summary>
/// Configuration for shipping audit records to Elasticsearch (viewed in Kibana). Bound from the
/// <c>Audit:Elasticsearch</c> section. If <see cref="Uri"/> is empty, the Elasticsearch sink is not
/// registered and auditing falls back to the default logging sink — so a missing config is safe.
/// </summary>
public sealed class ElasticsearchAuditOptions
{
    public const string SectionName = "Audit:Elasticsearch";

    /// <summary>Elasticsearch endpoint, e.g. <c>http://localhost:9200</c>. Empty disables the ES sink.</summary>
    public string? Uri { get; set; }

    /// <summary>Optional base64 API key (preferred). Overrides username/password if set.</summary>
    public string? ApiKey { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Index name, <see cref="string.Format(string, object)"/>-formatted with the record's UTC timestamp,
    /// so records roll into a daily index (pairs well with an ILM policy for retention).
    /// </summary>
    public string IndexFormat { get; set; } = "cleanarch-audit-{0:yyyy.MM.dd}";

    /// <summary>Max buffered records before new ones are dropped-to-log (back-pressure guard).</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Max records per bulk request.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>Bulk retry attempts before falling back to logging the batch.</summary>
    public int MaxRetries { get; set; } = 3;
}
