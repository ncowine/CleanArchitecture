namespace TestPlans.Application;

/// <summary>
/// Marks a request as a Test Plans-module write that must run inside a <c>TestPlansDbContext</c>
/// transaction. The module's transaction behavior wraps only requests carrying this marker, so queries —
/// and other modules' requests — are left untouched.
/// </summary>
public interface ITestPlansCommand;
