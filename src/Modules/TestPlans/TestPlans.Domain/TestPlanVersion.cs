namespace TestPlans.Domain;

/// <summary>
/// A version of a <see cref="TestPlan"/>, as a flat <c>(Version, SubVersion)</c> pair. Results are tracked
/// per version/sub-version, so the same task can carry independent outcomes across versions.
/// </summary>
public sealed class TestPlanVersion
{
    public Guid Id { get; private set; }
    public Guid TestPlanId { get; private set; }
    public int Version { get; private set; }
    public int SubVersion { get; private set; }

    /// <summary>Human-readable "Version.SubVersion" label (e.g. "2.14").</summary>
    public string Label => $"{Version}.{SubVersion}";

    private TestPlanVersion() { }

    private TestPlanVersion(Guid id, Guid testPlanId, int version, int subVersion)
    {
        Id = id;
        TestPlanId = testPlanId;
        Version = version;
        SubVersion = subVersion;
    }

    public static TestPlanVersion Create(Guid testPlanId, int version, int subVersion)
    {
        if (testPlanId == Guid.Empty)
            throw new DomainException("A version must belong to a test plan.");
        if (version < 0)
            throw new DomainException("Version cannot be negative.");
        if (subVersion < 0)
            throw new DomainException("Sub-version cannot be negative.");

        return new TestPlanVersion(Guid.NewGuid(), testPlanId, version, subVersion);
    }
}
