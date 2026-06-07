namespace Students.Domain;

public sealed class Student
{
    private readonly List<EmergencyContact> _emergencyContacts = [];
    private readonly List<Enrollment> _enrollments = [];

    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public DateOnly DateOfBirth { get; private set; }
    public DateOnly EnrolledOn { get; private set; }
    public StudentStatus Status { get; private set; }
    public Address? Address { get; private set; }

    public IReadOnlyCollection<EmergencyContact> EmergencyContacts => _emergencyContacts.AsReadOnly();
    public IReadOnlyCollection<Enrollment> Enrollments => _enrollments.AsReadOnly();

    private Student() { }

    private Student(Guid id, string firstName, string lastName, string email, DateOnly dateOfBirth, DateOnly enrolledOn)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        DateOfBirth = dateOfBirth;
        EnrolledOn = enrolledOn;
        Status = StudentStatus.Active;
    }

    public static Student Create(
        string firstName,
        string lastName,
        string email,
        DateOnly dateOfBirth,
        DateOnly enrolledOn)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("Last name is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");
        if (!email.Contains('@'))
            throw new DomainException("Email is not a valid address.");
        if (dateOfBirth >= enrolledOn)
            throw new DomainException("Date of birth must be before the enrollment date.");

        return new Student(
            id: Guid.NewGuid(),
            firstName: firstName.Trim(),
            lastName: lastName.Trim(),
            email: email.Trim().ToLowerInvariant(),
            dateOfBirth: dateOfBirth,
            enrolledOn: enrolledOn);
    }

    public void UpdateAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
    }

    /// <summary>Marks the student as withdrawn. Idempotent.</summary>
    public void Withdraw()
    {
        Status = StudentStatus.Withdrawn;
    }

    public EmergencyContact AddEmergencyContact(string name, string relationship, string phoneNumber)
    {
        var contact = EmergencyContact.Create(name, relationship, phoneNumber);
        _emergencyContacts.Add(contact);
        return contact;
    }

    public Enrollment EnrollIn(Guid programId, string term, DateOnly enrolledOn)
    {
        if (Status is StudentStatus.Graduated or StudentStatus.Withdrawn)
            throw new DomainException($"A {Status} student cannot enroll in a program.");

        if (_enrollments.Any(enrollment =>
                enrollment.ProgramId == programId && enrollment.Status == EnrollmentStatus.Enrolled))
            throw new DomainException("The student is already actively enrolled in this program.");

        var enrollment = Enrollment.Create(programId, term, enrolledOn);
        _enrollments.Add(enrollment);
        Status = StudentStatus.Active;

        return enrollment;
    }
}
