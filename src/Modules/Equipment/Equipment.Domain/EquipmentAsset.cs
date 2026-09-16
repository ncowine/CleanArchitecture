namespace Equipment.Domain;

/// <summary>
/// A piece of company IT hardware (laptop, monitor, phone, …). Reservation is the only status
/// transition, modelled on the entity so an invalid one (reserving something already reserved) is a
/// domain rule, not a handler check. <see cref="ReservedForOnboardingRequestId"/> is both the "who holds
/// this" fact and the idempotency key the Onboarding module reserves/releases against.
/// </summary>
public sealed class EquipmentAsset
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public EquipmentCategory Category { get; private set; }
    public string AssetTag { get; private set; } = null!;
    public EquipmentStatus Status { get; private set; }
    public Guid? ReservedForOnboardingRequestId { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    private EquipmentAsset() { }

    private EquipmentAsset(Guid id, string name, EquipmentCategory category, string assetTag, DateTime createdOnUtc)
    {
        Id = id;
        Name = name;
        Category = category;
        AssetTag = assetTag;
        Status = EquipmentStatus.Available;
        CreatedOnUtc = createdOnUtc;
    }

    public static EquipmentAsset Create(string name, EquipmentCategory category, string assetTag)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");
        if (string.IsNullOrWhiteSpace(assetTag))
            throw new DomainException("Asset tag is required.");

        return new EquipmentAsset(Guid.NewGuid(), name.Trim(), category, assetTag.Trim(), DateTime.UtcNow);
    }

    public void Update(string name, EquipmentCategory category, string assetTag)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");
        if (string.IsNullOrWhiteSpace(assetTag))
            throw new DomainException("Asset tag is required.");

        Name = name.Trim();
        Category = category;
        AssetTag = assetTag.Trim();
    }

    public void Reserve(Guid onboardingRequestId)
    {
        if (Status != EquipmentStatus.Available)
            throw new DomainException($"Equipment '{Id}' is not available (status: {Status}).");

        Status = EquipmentStatus.Reserved;
        ReservedForOnboardingRequestId = onboardingRequestId;
    }

    // Idempotent: releasing an already-available asset is a no-op, not an error — a redelivered
    // compensation message must be safe to apply twice.
    public void Release()
    {
        Status = EquipmentStatus.Available;
        ReservedForOnboardingRequestId = null;
    }
}
