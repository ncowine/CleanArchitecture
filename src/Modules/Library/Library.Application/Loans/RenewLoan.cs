using BuildingBlocks.Messaging;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Loans;

/// <summary>Renew the active loan on a copy, extending its due date up to the renewal limit.</summary>
public static class RenewLoan
{
    public sealed record Command(Guid CopyId) : IRequest<Result>, ILibraryCommand, IAuditableRequest;

    public sealed record Result(Guid LoanId, DateOnly DueOn, int RenewalCount);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly ILoanRepository _loans;

        public Handler(ILoanRepository loans)
        {
            _loans = loans;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var loan = await _loans.GetActiveByCopyAsync(command.CopyId, cancellationToken)
                ?? throw new DomainException($"No active loan exists for copy '{command.CopyId}'.");

            loan.Renew(CirculationPolicy.LoanPeriodDays, CirculationPolicy.MaxRenewals);
            return new Result(loan.Id, loan.DueOn, loan.RenewalCount);
        }
    }
}
