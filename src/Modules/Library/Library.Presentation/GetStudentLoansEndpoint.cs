using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Library.Application.Loans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Library.Presentation;

internal sealed class GetStudentLoansEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/library/students/{studentId:guid}/loans", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var response = await sender.Send(new GetStudentLoans.Query(studentId), cancellationToken);
            return Results.Ok(response);
        })
        .WithName("GetStudentLoans")
        .WithSummary("Compose a student's loans (Library DB) with their identity (main Students DB).");
}
