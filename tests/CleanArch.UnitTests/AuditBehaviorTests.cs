using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Behaviors;
using Xunit;

namespace CleanArch.UnitTests;

/// <summary>
/// The pipeline half of auditing: commands are recorded as writes, marked queries as reads, and whatever
/// the handler annotated on the way through rides along on the same record.
/// </summary>
public class AuditBehaviorTests
{
    private sealed record Save(Guid Id) : IRequest<string>, IAuditableRequest
    {
        public string AuditResource => $"Student/{Id}";
    }

    private sealed record Look(Guid Id) : IRequest<string>, IAuditableRead
    {
        public string AuditResource => $"Student/{Id}";
    }

    private static (AuditBehavior<TRequest, string> Behavior, RecordingAuditSink Sink, AuditScope Scope)
        BehaviorFor<TRequest>()
        where TRequest : IRequest<string>, IAuditableRequest
    {
        var sink = new RecordingAuditSink();
        var scope = new AuditScope();
        return (new AuditBehavior<TRequest, string>(sink, new StubActor("dana"), new StubCorrelation("corr-1"), scope), sink, scope);
    }

    [Fact]
    public async Task Audits_a_command_as_a_write_with_its_committed_changes()
    {
        var (behavior, sink, scope) = BehaviorFor<Save>();
        var id = Guid.NewGuid();

        await behavior.Handle(new Save(id), () =>
        {
            // Stands in for the SaveChanges interceptor filling the scope inside the transaction.
            scope.Add(new EntityChange("Student", id.ToString(), ChangeOperation.Modified, []));
            return Task.FromResult("ok");
        }, default);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(AuditCategory.Write, entry.Category);
        Assert.Equal($"Student/{id}", entry.Resource);
        Assert.Single(entry.Changes);
    }

    [Fact]
    public async Task Audits_a_marked_query_as_a_read_carrying_the_handlers_annotations()
    {
        var (behavior, sink, scope) = BehaviorFor<Look>();
        var id = Guid.NewGuid();

        await behavior.Handle(new Look(id), () =>
        {
            scope.Annotate("rowsReturned", "3");
            scope.Annotate("servedFrom", "cache");
            return Task.FromResult("ok");
        }, default);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(AuditCategory.Read, entry.Category);
        Assert.Equal($"Student/{id}", entry.Resource);
        Assert.Empty(entry.Changes);
        Assert.Equal("3", entry.Details!["rowsReturned"]);
        Assert.Equal("cache", entry.Details["servedFrom"]);
    }

    [Fact]
    public async Task A_failed_request_is_audited_with_its_annotations_but_no_changes()
    {
        var (behavior, sink, scope) = BehaviorFor<Save>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(new Save(Guid.NewGuid()), () =>
        {
            scope.Annotate("stage", "vendor-call");
            throw new InvalidOperationException("nope");
        }, default));

        var entry = Assert.Single(sink.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("nope", entry.Error);
        Assert.Empty(entry.Changes);
        Assert.Equal("vendor-call", entry.Details!["stage"]);
    }

    [Fact]
    public async Task An_unannotated_record_carries_no_empty_details_object()
    {
        var (behavior, sink, _) = BehaviorFor<Save>();

        await behavior.Handle(new Save(Guid.NewGuid()), () => Task.FromResult("ok"), default);

        Assert.Null(Assert.Single(sink.Entries).Details);
    }
}
