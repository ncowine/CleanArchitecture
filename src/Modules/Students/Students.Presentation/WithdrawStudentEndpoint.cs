using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Students;

namespace Students.Presentation;

internal sealed class WithdrawStudentEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/students/{studentId:guid}/withdraw", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new WithdrawStudent.Command(studentId), cancellationToken);
            return Results.Ok(new { id, status = "Withdrawn" });
        })
        .WithName("WithdrawStudent")
        .WithSummary("Withdraw a student. A withdrawn student causes later hold requests to be rejected.")
        .RequireAuthorization();
}
