namespace Students.Domain;

/// <summary>
/// A person to contact in an emergency. Part of the <see cref="Student"/> aggregate — created and
/// owned by the student, never independently. Construction is therefore <c>internal</c>.
/// </summary>
public sealed class EmergencyContact
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Relationship { get; private set; } = null!;
    public string PhoneNumber { get; private set; } = null!;

    private EmergencyContact() { }

    private EmergencyContact(Guid id, string name, string relationship, string phoneNumber)
    {
        Id = id;
        Name = name;
        Relationship = relationship;
        PhoneNumber = phoneNumber;
    }

    internal static EmergencyContact Create(string name, string relationship, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Emergency contact name is required.");
        if (string.IsNullOrWhiteSpace(relationship))
            throw new DomainException("Emergency contact relationship is required.");
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new DomainException("Emergency contact phone number is required.");

        return new EmergencyContact(Guid.NewGuid(), name.Trim(), relationship.Trim(), phoneNumber.Trim());
    }
}
