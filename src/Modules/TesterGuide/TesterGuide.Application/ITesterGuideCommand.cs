namespace TesterGuide.Application;

/// <summary>
/// Marks a request as a Tester Guide-module write that must run inside a <c>TesterGuideDbContext</c>
/// transaction. The module's transaction behavior wraps only requests carrying this marker, so queries —
/// and other modules' requests — are left untouched.
/// </summary>
public interface ITesterGuideCommand;
