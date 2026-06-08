namespace Students.Domain;

/// <summary>
/// A letter grade and its grade-point value on the standard 4.0 scale. A value object owned by
/// <see cref="SectionEnrollment"/>. F earns 0 points and no credit; everything else earns credit.
/// </summary>
public sealed class Grade
{
    private static readonly Dictionary<string, decimal> Scale =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 4.0m, ["A-"] = 3.7m,
            ["B+"] = 3.3m, ["B"] = 3.0m, ["B-"] = 2.7m,
            ["C+"] = 2.3m, ["C"] = 2.0m, ["C-"] = 1.7m,
            ["D+"] = 1.3m, ["D"] = 1.0m, ["D-"] = 0.7m,
            ["F"] = 0.0m,
        };

    public string Letter { get; private set; } = null!;
    public decimal Points { get; private set; }

    /// <summary>True when the grade earns credit (anything above F).</summary>
    public bool IsPassing => Points > 0m;

    private Grade() { }

    private Grade(string letter, decimal points)
    {
        Letter = letter;
        Points = points;
    }

    public static Grade FromLetter(string letter)
    {
        if (string.IsNullOrWhiteSpace(letter))
            throw new DomainException("A grade is required.");

        var normalized = letter.Trim().ToUpperInvariant();
        if (!Scale.TryGetValue(normalized, out var points))
            throw new DomainException($"'{letter}' is not a valid letter grade.");

        return new Grade(normalized, points);
    }
}
