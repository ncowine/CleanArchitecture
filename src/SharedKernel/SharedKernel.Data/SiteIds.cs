namespace SharedKernel.Data;

/// <summary>Fixed ids for the seeded <see cref="Models.Site"/> rows — HasData needs stable keys across migrations.</summary>
internal static class SiteIds
{
    public static readonly Guid London = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid NewYork = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Remote = new("33333333-3333-3333-3333-333333333333");
}
