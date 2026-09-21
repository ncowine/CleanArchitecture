namespace SharedKernel.Models;

/// <summary>
/// A company office/site — shared, read-only reference data used by more than one module. Small and
/// effectively static: rows are shipped with the SharedKernel migration itself (see
/// SharedKernel.Data/EntityConfigurations/SiteConfiguration.cs) rather than created at runtime, so there
/// is no factory method here — just the shape.
/// <para>
/// One public constructor, deliberately: EF Core binds to it by matching parameter names to properties
/// (no separate parameterless constructor needed), and it's also what lets HybridCache's System.Text.Json
/// serializer round-trip cached instances — it requires either a public parameterless constructor or
/// exactly one parameterized one.
/// </para>
/// </summary>
public sealed class Site
{
    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string City { get; private set; }

    public Site(Guid id, string code, string name, string city)
    {
        Id = id;
        Code = code;
        Name = name;
        City = city;
    }
}
