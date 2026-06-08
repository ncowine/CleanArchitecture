namespace Students.Domain;

/// <summary>
/// Billing rules. Pure constants today (a real deployment would make these configuration, possibly varying
/// by program or residency).
/// </summary>
public static class BillingPolicy
{
    /// <summary>Owing at or above this places a financial hold on the student.</summary>
    public const decimal FinancialHoldThreshold = 1000m;

    /// <summary>Tuition charged per credit when a student takes a seat in a section.</summary>
    public const decimal TuitionPerCredit = 500m;

    /// <summary>True when a charge moves the balance from under the hold threshold to at/over it.</summary>
    public static bool CrossesHoldThreshold(decimal priorBalance, decimal newBalance) =>
        priorBalance < FinancialHoldThreshold && newBalance >= FinancialHoldThreshold;
}
