namespace Students.Domain;

/// <summary>
/// A postal address. A value object — it has no identity of its own and is owned by the entity
/// that holds it (stored as columns on that entity's table).
/// </summary>
public sealed class Address
{
    public string Line1 { get; private set; } = null!;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;
    public string Country { get; private set; } = null!;

    private Address() { }

    private Address(string line1, string? line2, string city, string state, string postalCode, string country)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
    }

    public static Address Create(
        string line1,
        string? line2,
        string city,
        string state,
        string postalCode,
        string country)
    {
        if (string.IsNullOrWhiteSpace(line1))
            throw new DomainException("Address line 1 is required.");
        if (string.IsNullOrWhiteSpace(city))
            throw new DomainException("City is required.");
        if (string.IsNullOrWhiteSpace(state))
            throw new DomainException("State is required.");
        if (string.IsNullOrWhiteSpace(postalCode))
            throw new DomainException("Postal code is required.");
        if (string.IsNullOrWhiteSpace(country))
            throw new DomainException("Country is required.");

        return new Address(
            line1.Trim(),
            string.IsNullOrWhiteSpace(line2) ? null : line2.Trim(),
            city.Trim(),
            state.Trim(),
            postalCode.Trim(),
            country.Trim());
    }
}
