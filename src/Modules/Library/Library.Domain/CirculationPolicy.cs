namespace Library.Domain;

/// <summary>
/// The library's circulation rules. Pure constants today (a real deployment would make these
/// configuration, possibly varying by item type or borrower category).
/// </summary>
public static class CirculationPolicy
{
    /// <summary>How long a loan runs, and how much each renewal extends it.</summary>
    public const int LoanPeriodDays = 21;

    /// <summary>Fine accrued for each day a returned item is past its due date.</summary>
    public const decimal FinePerDayOverdue = 0.25m;

    /// <summary>How many times a loan may be renewed.</summary>
    public const int MaxRenewals = 2;

    /// <summary>How many active loans a borrower may hold at once.</summary>
    public const int BorrowerLoanLimit = 5;

    /// <summary>How long a returned copy is held on the hold shelf for a reservation to be picked up.</summary>
    public const int PickupWindowDays = 3;
}
