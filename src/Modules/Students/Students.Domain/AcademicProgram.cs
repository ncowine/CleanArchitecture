namespace Students.Domain;

/// <summary>
/// A program of study (a major / degree track) offered by the college. A separate aggregate root —
/// students reference it by id through their <see cref="Enrollment"/>s.
/// </summary>
public sealed class AcademicProgram
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public DegreeLevel Degree { get; private set; }
    public string DepartmentName { get; private set; } = null!;

    private AcademicProgram() { }

    private AcademicProgram(Guid id, string name, string code, DegreeLevel degree, string departmentName)
    {
        Id = id;
        Name = name;
        Code = code;
        Degree = degree;
        DepartmentName = departmentName;
    }

    public static AcademicProgram Create(string name, string code, DegreeLevel degree, string departmentName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Program name is required.");
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Program code is required.");
        if (string.IsNullOrWhiteSpace(departmentName))
            throw new DomainException("Department name is required.");

        return new AcademicProgram(
            id: Guid.NewGuid(),
            name: name.Trim(),
            code: code.Trim().ToUpperInvariant(),
            degree: degree,
            departmentName: departmentName.Trim());
    }
}
