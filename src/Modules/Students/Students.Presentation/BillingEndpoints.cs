using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Billing;
using Students.Domain;

namespace Students.Presentation;

/// <summary>Student billing: the account ledger of charges, payments, and waivers.</summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var billing = app.MapGroup("")
            .WithTags("Billing")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        billing.MapGet("/students/{studentId:guid}/account", async (
            Guid studentId,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var account = await sender.Send(
                new GetStudentAccount.Query(studentId, page ?? 1, pageSize ?? 20), cancellationToken);
            return Results.Ok(account);
        })
        .WithName("GetStudentAccount")
        .WithSummary("A student's account balance and paged statement. Paged via ?page=&pageSize= (default 1/20, max 100).");

        billing.MapPost("/students/{studentId:guid}/account/charges", async (
            Guid studentId,
            ChargeRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new ChargeAccount.Command(studentId, request.Amount, request.Category, request.Description), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ChargeAccount")
        .WithSummary("Post a charge (tuition, fee, fine) to a student's account; opens the account on first use.")
        .RequireAuthorization();

        billing.MapPost("/students/{studentId:guid}/account/payments", async (
            Guid studentId,
            PaymentRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new RecordPayment.Command(studentId, request.Amount, request.Description), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("RecordPayment")
        .WithSummary("Record a payment against a student's account.")
        .RequireAuthorization();

        billing.MapPost("/students/{studentId:guid}/account/waivers", async (
            Guid studentId,
            WaiveRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new WaiveCharge.Command(studentId, request.Amount, request.Description), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("WaiveCharge")
        .WithSummary("Waive an amount on a student's account (write it off without payment).")
        .RequireAuthorization();

        return app;
    }

    public sealed record ChargeRequest(decimal Amount, ChargeCategory Category, string Description);

    public sealed record PaymentRequest(decimal Amount, string? Description);

    public sealed record WaiveRequest(decimal Amount, string Description);
}
