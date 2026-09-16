namespace Onboarding.Application;

/// <summary>
/// Marks a request as an Onboarding-module write that must run inside an <c>OnboardingDbContext</c>
/// transaction. The Onboarding transaction behavior wraps only requests carrying this marker, so queries
/// — and other modules' requests — are left untouched. Each module has its own marker and its own
/// behavior because a transaction can only span one database.
/// </summary>
public interface IOnboardingCommand;
