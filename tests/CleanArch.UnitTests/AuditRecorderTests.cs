using BuildingBlocks.Auditing;
using BuildingBlocks.Correlation;
using Xunit;

namespace CleanArch.UnitTests;

/// <summary>
/// The audit capture point for everything the command pipeline can't see: third-party API calls, reads
/// from other systems, security events — anything a caller wants on the audit trail.
/// </summary>
public class AuditRecorderTests
{
    private static (AuditRecorder Recorder, RecordingAuditSink Sink, AuditScope Scope) Build()
    {
        var sink = new RecordingAuditSink();
        var scope = new AuditScope();
        return (new AuditRecorder(sink, new StubActor("dana"), new StubCorrelation("corr-1"), scope), sink, scope);
    }

    [Fact]
    public async Task Records_a_standalone_entry_stamped_with_the_ambient_actor_and_correlation_id()
    {
        var (recorder, sink, _) = Build();

        await recorder.RecordAsync(
            new AuditFact("CreditScoreLookup")
            {
                Category = AuditCategory.External,
                Source = "Api:CreditBureau",
                Resource = "Student/42",
            }.With("bureauReference", "REF-9"),
            default);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal("CreditScoreLookup", entry.Action);
        Assert.Equal(AuditCategory.External, entry.Category);
        Assert.Equal("Api:CreditBureau", entry.Source);
        Assert.Equal("Student/42", entry.Resource);
        Assert.Equal("REF-9", entry.Details!["bureauReference"]);
        // Filled in for the caller, so a custom record correlates with the request that caused it.
        Assert.Equal("dana", entry.Actor);
        Assert.Equal("corr-1", entry.CorrelationId);
        // A non-database record carries no entity change-set.
        Assert.Empty(entry.Changes);
    }

    [Fact]
    public async Task Track_records_success_with_the_measured_duration_and_returns_the_result()
    {
        var (recorder, sink, _) = Build();

        var result = await recorder.TrackAsync(
            new AuditFact("VendorFetch") { Category = AuditCategory.External, Source = "Api:Vendor" },
            async token =>
            {
                await Task.Delay(5, token);
                return "payload";
            },
            default);

        Assert.Equal("payload", result);
        var entry = Assert.Single(sink.Entries);
        Assert.True(entry.Succeeded);
        Assert.Null(entry.Error);
        Assert.True(entry.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Track_records_the_failure_and_rethrows_untouched()
    {
        var (recorder, sink, _) = Build();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.TrackAsync(
            new AuditFact("VendorFetch") { Category = AuditCategory.External, Source = "Api:Vendor" },
            _ => throw new InvalidOperationException("vendor timed out"),
            default));

        Assert.Equal("vendor timed out", thrown.Message);
        var entry = Assert.Single(sink.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("vendor timed out", entry.Error);
    }

    [Fact]
    public async Task Track_records_the_failure_even_when_the_operation_was_cancelled()
    {
        var (recorder, sink, _) = Build();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recorder.TrackAsync(
            new AuditFact("VendorFetch"),
            token => Task.FromCanceled<string>(token),
            cancellation.Token));

        // The write to the sink must not be cancelled along with the operation — the abandoned attempt
        // is precisely what the audit trail needs to show.
        var entry = Assert.Single(sink.Entries);
        Assert.False(entry.Succeeded);
    }

    [Fact]
    public void Annotate_adds_to_the_current_requests_scope_rather_than_writing_a_new_record()
    {
        var (recorder, sink, scope) = Build();

        recorder.Annotate("servedFrom", "cache");

        Assert.Empty(sink.Entries);
        Assert.Equal("cache", scope.Details["servedFrom"]);
    }

    [Fact]
    public void With_leaves_the_original_fact_untouched()
    {
        var fact = new AuditFact("Lookup").With("a", "1");

        var extended = fact.With("b", "2");

        Assert.Single(fact.Details);
        Assert.Equal(2, extended.Details.Count);
    }
}

internal sealed class RecordingAuditSink : IAuditSink
{
    public List<AuditEntry> Entries { get; } = new();

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class StubActor : ICurrentActor
{
    public StubActor(string current) => Current = current;

    public string Current { get; }
}

internal sealed class StubCorrelation : ICorrelationContext
{
    public StubCorrelation(string correlationId) => CorrelationId = correlationId;

    public string CorrelationId { get; private set; }

    public void Set(string correlationId) => CorrelationId = correlationId;
}
