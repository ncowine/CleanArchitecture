namespace Students.Domain;

/// <summary>
/// An instructor who teaches course sections. A separate aggregate root — <see cref="CourseSection"/>s
/// reference it by id only.
/// </summary>
public sealed class Instructor
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string DepartmentName { get; private set; } = null!;
    public InstructorRank Rank { get; private set; }

    private Instructor() { }

    private Instructor(
        Guid id, string firstName, string lastName, string email, string departmentName, InstructorRank rank)
    {
        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        DepartmentName = departmentName;
        Rank = rank;
    }

    public static Instructor Create(
        string firstName, string lastName, string email, string departmentName, InstructorRank rank)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("Last name is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");
        if (!email.Contains('@'))
            throw new DomainException("Email is not a valid address.");
        if (string.IsNullOrWhiteSpace(departmentName))
            throw new DomainException("Department name is required.");

        return new Instructor(
            id: Guid.NewGuid(),
            firstName: firstName.Trim(),
            lastName: lastName.Trim(),
            email: email.Trim().ToLowerInvariant(),
            departmentName: departmentName.Trim(),
            rank: rank);
    }
}
