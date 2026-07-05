using BuildingBlocks.Auditing;
using BuildingBlocks.RealTime;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TestPlans.Contracts;

namespace CleanArch.UnitTests;

internal sealed class FakeRealtimeDispatch : IRealtimeDispatch
{
    public List<(string Group, RealtimeEvent Event)> Published { get; } = new();

    public void Publish(string group, RealtimeEvent realtimeEvent) => Published.Add((group, realtimeEvent));
}

internal sealed class FakeFocusRepository : IFocusRepository
{
    private readonly Dictionary<Guid, Focus> _focuses = new();

    public List<Focus> Added { get; } = new();
    public List<Focus> Removed { get; } = new();

    public void Seed(Focus focus) => _focuses[focus.Id] = focus;

    public Task AddAsync(Focus focus, CancellationToken cancellationToken)
    {
        Added.Add(focus);
        _focuses[focus.Id] = focus;
        return Task.CompletedTask;
    }

    public Task<Focus?> GetAsync(Guid focusId, CancellationToken cancellationToken) =>
        Task.FromResult(_focuses.TryGetValue(focusId, out var focus) ? focus : null);

    public void Remove(Focus focus)
    {
        Removed.Add(focus);
        _focuses.Remove(focus.Id);
    }

    public Task<bool> ExistsAsync(Guid focusId, CancellationToken cancellationToken) =>
        Task.FromResult(_focuses.ContainsKey(focusId));
}

internal sealed class FakeGuideConfigRepository : IGuideConfigRepository
{
    private readonly Dictionary<Guid, GuideConfig> _configs = new();
    private readonly List<(Guid ConfigId, string UserId)> _assignments = new();

    public List<GuideConfig> Added { get; } = new();
    public List<ConfigAssignment> AddedAssignments { get; } = new();

    public void SeedConfig(GuideConfig config) => _configs[config.Id] = config;

    public Task AddAsync(GuideConfig config, CancellationToken cancellationToken)
    {
        Added.Add(config);
        _configs[config.Id] = config;
        return Task.CompletedTask;
    }

    public Task<GuideConfig?> GetAsync(Guid configId, CancellationToken cancellationToken) =>
        Task.FromResult(_configs.TryGetValue(configId, out var config) ? config : null);

    public Task<bool> ExistsAsync(Guid configId, CancellationToken cancellationToken) =>
        Task.FromResult(_configs.ContainsKey(configId));

    public Task AddAssignmentAsync(ConfigAssignment assignment, CancellationToken cancellationToken)
    {
        AddedAssignments.Add(assignment);
        _assignments.Add((assignment.GuideConfigId, assignment.UserId));
        return Task.CompletedTask;
    }

    public Task<bool> AssignmentExistsAsync(Guid configId, string userId, CancellationToken cancellationToken) =>
        Task.FromResult(_assignments.Any(a => a.ConfigId == configId && a.UserId == userId));
}

internal sealed class FakeGuideActionLogRepository : IGuideActionLogRepository
{
    public List<GuideActionLogEntry> Added { get; } = new();

    public Task AddAsync(GuideActionLogEntry entry, CancellationToken cancellationToken)
    {
        Added.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class FakeTesterGuideOutbox : ITesterGuideOutbox
{
    public List<object> Events { get; } = new();

    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class => Events.Add(integrationEvent);
}

internal sealed class FakeConfigTemplateRepository : IConfigTemplateRepository
{
    private readonly Dictionary<Guid, ConfigTemplate> _templates = new();

    public List<ConfigTemplate> Added { get; } = new();

    public void Seed(ConfigTemplate template) => _templates[template.Id] = template;

    public Task AddAsync(ConfigTemplate configTemplate, CancellationToken cancellationToken)
    {
        Added.Add(configTemplate);
        _templates[configTemplate.Id] = configTemplate;
        return Task.CompletedTask;
    }

    public Task<ConfigTemplate?> GetAsync(Guid templateId, CancellationToken cancellationToken) =>
        Task.FromResult(_templates.TryGetValue(templateId, out var template) ? template : null);
}

/// <summary>Fake of the Test Plans module's published catalog contract, for cross-module handler tests.</summary>
internal sealed class FakeTestPlanCatalog : ITestPlanCatalog
{
    public bool VersionExists { get; set; }
    public TestPlanTree? Tree { get; set; }
    public IReadOnlyList<VersionSummary> Versions { get; set; } = Array.Empty<VersionSummary>();

    public Task<TestPlanTree?> GetTreeAsync(Guid testPlanId, CancellationToken cancellationToken) =>
        Task.FromResult(Tree);

    public Task<IReadOnlyList<VersionSummary>> GetVersionsAsync(Guid testPlanId, CancellationToken cancellationToken) =>
        Task.FromResult(Versions);

    public Task<IReadOnlyList<PlatformSummary>> GetPlatformsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformSummary>>(Array.Empty<PlatformSummary>());

    public Task<bool> VersionExistsAsync(Guid testPlanId, Guid versionId, CancellationToken cancellationToken) =>
        Task.FromResult(VersionExists);
}

internal sealed class FakeCurrentActor : ICurrentActor
{
    public FakeCurrentActor(string current = "tester") => Current = current;

    public string Current { get; }
}
