using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class AcademicStandingPolicyTests
{
    [Fact]
    public void No_attempted_credits_is_not_evaluated() =>
        Assert.Equal(AcademicStanding.NotEvaluated, AcademicStandingPolicy.Evaluate(0m, 0));

    [Fact]
    public void Gpa_below_two_is_probation() =>
        Assert.Equal(AcademicStanding.AcademicProbation, AcademicStandingPolicy.Evaluate(1.9m, 15));

    [Fact]
    public void Exactly_two_is_good_standing() =>
        Assert.Equal(AcademicStanding.GoodStanding, AcademicStandingPolicy.Evaluate(2.0m, 15));

    [Fact]
    public void High_gpa_with_enough_credits_is_deans_list() =>
        Assert.Equal(AcademicStanding.DeansList, AcademicStandingPolicy.Evaluate(3.8m, 12));

    [Fact]
    public void High_gpa_with_too_few_credits_is_good_standing() =>
        Assert.Equal(AcademicStanding.GoodStanding, AcademicStandingPolicy.Evaluate(3.8m, 6));
}
