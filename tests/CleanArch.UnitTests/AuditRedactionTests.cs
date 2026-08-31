using System.Globalization;
using BuildingBlocks.Auditing;
using Xunit;

namespace CleanArch.UnitTests;

/// <summary>
/// The safety policy that stands between a caller and the audit store: secrets redacted by name, long
/// values cut, and a bound on how much one record can carry — applied the same way to a hand-written
/// detail as to an intercepted column value.
/// </summary>
public class AuditRedactionTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("PasswordHash")]
    [InlineData("clientSecret")]
    [InlineData("accessToken")]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("PasswordSalt")]
    [InlineData("vendorCredential")]
    public void Names_that_look_like_secrets_are_redacted(string name)
    {
        Assert.True(AuditRedaction.IsSensitive(name));
        Assert.Equal(AuditRedaction.RedactedValue, AuditRedaction.Sanitize(name, "hunter2"));
    }

    [Theory]
    [InlineData("loansReturned")]
    [InlineData("identitySource")]
    [InlineData("bureauReference")]
    public void Ordinary_names_are_kept(string name)
    {
        Assert.False(AuditRedaction.IsSensitive(name));
        Assert.Equal("42", AuditRedaction.Sanitize(name, "42"));
    }

    [Fact]
    public void A_long_value_is_cut_and_marked_so_it_never_reads_as_whole()
    {
        var sanitized = AuditRedaction.Sanitize("payload", new string('x', AuditRedaction.MaxValueLength + 100));

        Assert.Equal(AuditRedaction.MaxValueLength + 1, sanitized!.Length);
        Assert.EndsWith("…", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_at_the_limit_is_left_alone()
    {
        var exact = new string('x', AuditRedaction.MaxValueLength);

        Assert.Equal(exact, AuditRedaction.Sanitize("payload", exact));
    }

    [Fact]
    public void A_null_value_stays_null_rather_than_becoming_a_redaction_marker()
    {
        Assert.Null(AuditRedaction.Sanitize("password", null));
    }

    [Fact]
    public void Nothing_worth_recording_produces_no_details_object()
    {
        Assert.Null(AuditRedaction.Sanitize(null));
        Assert.Null(AuditRedaction.Sanitize(new Dictionary<string, string?>()));
    }

    [Fact]
    public void Details_beyond_the_cap_are_dropped_and_the_record_says_how_many()
    {
        var details = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < AuditRedaction.MaxDetails + 5; i++)
        {
            details[$"key{i}"] = i.ToString(CultureInfo.InvariantCulture);
        }

        var sanitized = AuditRedaction.Sanitize(details)!;

        Assert.Equal(AuditRedaction.MaxDetails + 1, sanitized.Count);   // the kept details, plus the marker
        Assert.Equal("5", sanitized[AuditRedaction.DroppedKey]);
    }

    [Fact]
    public void A_secret_smuggled_into_a_detail_is_redacted_on_the_way_in()
    {
        var sanitized = AuditRedaction.Sanitize(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["vendorRequestId"] = "REQ-9",
            ["vendorApiKey"] = "sk-live-0123456789",
        })!;

        Assert.Equal("REQ-9", sanitized["vendorRequestId"]);
        Assert.Equal(AuditRedaction.RedactedValue, sanitized["vendorApiKey"]);
    }
}

/// <summary>
/// The per-request notepad. It is written by the handler and by the EF interceptor, which are not always
/// the same thread, and it must not let one request's record grow without bound.
/// </summary>
public class AuditScopeTests
{
    [Fact]
    public void An_annotated_secret_never_reaches_the_record()
    {
        var scope = new AuditScope();

        scope.Annotate("upstreamToken", "eyJhbGciOi...");

        Assert.Equal(AuditRedaction.RedactedValue, scope.Details["upstreamToken"]);
    }

    [Fact]
    public void A_long_annotation_is_truncated()
    {
        var scope = new AuditScope();

        scope.Annotate("response", new string('x', 5_000));

        Assert.Equal(AuditRedaction.MaxValueLength + 1, scope.Details["response"]!.Length);
    }

    [Fact]
    public void Re_annotating_a_key_updates_it_rather_than_consuming_the_budget()
    {
        var scope = new AuditScope();

        for (var i = 0; i < AuditRedaction.MaxDetails * 10; i++)
        {
            scope.Annotate("rowsSeen", i.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Single(scope.Details);
        Assert.Equal("319", scope.Details["rowsSeen"]);
        Assert.DoesNotContain(AuditRedaction.DroppedKey, scope.Details.Keys);
    }

    [Fact]
    public void A_handler_annotating_per_row_cannot_make_the_record_unbounded()
    {
        var scope = new AuditScope();

        for (var i = 0; i < 1_000; i++)
        {
            scope.Annotate($"row{i}", "seen");
        }

        var details = scope.Details;

        Assert.Equal(AuditRedaction.MaxDetails + 1, details.Count);
        Assert.Equal((1_000 - AuditRedaction.MaxDetails).ToString(CultureInfo.InvariantCulture), details[AuditRedaction.DroppedKey]);
    }

    [Fact]
    public void Nothing_recorded_means_no_details_at_all()
    {
        Assert.Empty(new AuditScope().Details);
    }

    [Fact]
    public async Task Concurrent_writers_lose_nothing_and_never_corrupt_the_notepad()
    {
        // The shape this guards: a handler fanning out with Task.WhenAll over two DbContexts, so the
        // interceptor and the handler are writing to the same scoped notepad at the same time.
        var scope = new AuditScope();
        var change = new EntityChange("Student", "1", ChangeOperation.Modified, []);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                scope.Add(change);
                scope.Annotate($"w{worker}-{i}", i.ToString(CultureInfo.InvariantCulture));
            }
        })));

        Assert.Equal(8 * 250, scope.Changes.Count);
        Assert.Equal(AuditRedaction.MaxDetails + 1, scope.Details.Count);
    }

    [Fact]
    public void Changes_are_snapshotted_so_a_reader_never_iterates_a_live_collection()
    {
        var scope = new AuditScope();
        scope.Add(new EntityChange("Student", "1", ChangeOperation.Added, []));

        var before = scope.Changes;
        scope.Add(new EntityChange("Student", "2", ChangeOperation.Added, []));

        Assert.Single(before);
        Assert.Equal(2, scope.Changes.Count);
    }
}
