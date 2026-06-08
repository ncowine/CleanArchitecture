using Students.Domain;

namespace Students.Application.Abstractions;

/// <summary>
/// Posts a charge to a student's account and places a financial hold if the charge pushes the balance
/// across the hold threshold. The single place that ties charging to holds, reused by every charge path
/// (manual charge, enrollment tuition, …). Returns the new balance.
/// </summary>
public interface IAccountCharger
{
    Task<decimal> ChargeAsync(
        Guid studentId, decimal amount, ChargeCategory category, string description, CancellationToken cancellationToken);
}
