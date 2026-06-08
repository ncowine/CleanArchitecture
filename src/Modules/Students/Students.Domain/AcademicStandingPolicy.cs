namespace Students.Domain;

/// <summary>
/// Derives a student's <see cref="AcademicStanding"/> from their cumulative GPA and attempted credits.
/// Pure policy — no state — so it's the same whether evaluated on the read side or used in a rule.
/// </summary>
public static class AcademicStandingPolicy
{
    public const decimal ProbationThreshold = 2.0m;
    public const decimal DeansListThreshold = 3.5m;
    public const int DeansListMinimumCredits = 12;

    public static AcademicStanding Evaluate(decimal cumulativeGpa, int attemptedCredits)
    {
        if (attemptedCredits <= 0)
            return AcademicStanding.NotEvaluated;
        if (cumulativeGpa < ProbationThreshold)
            return AcademicStanding.AcademicProbation;
        if (cumulativeGpa >= DeansListThreshold && attemptedCredits >= DeansListMinimumCredits)
            return AcademicStanding.DeansList;

        return AcademicStanding.GoodStanding;
    }
}
