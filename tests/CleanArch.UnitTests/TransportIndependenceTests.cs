using System.Reflection;
using Equipment.Application.Inventory;
using Equipment.Domain;
using Equipment.Messages;
using Onboarding.Application.Requests;
using Onboarding.Domain;
using Xunit;

namespace CleanArch.UnitTests;

/// <summary>
/// The inner layers say WHAT happened; only the host decides HOW it travels (docs/messaging/adr/0003). These tests
/// fail the moment a domain, an application layer or a message contract starts using RabbitMQ, the messaging engine,
/// its hosting layer or SignalR directly.
/// </summary>
public class TransportIndependenceTests
{
    private static readonly string[] Transports =
    [
        "RabbitMQ.Client",
        "Messaging.RabbitMQ",
        "Messaging.Hosting",
        "Messaging.Prism",
        "Common.RabbitMQ",
        "BuildingBlocks.Outbox.Messaging",
        "Microsoft.AspNetCore.SignalR",
        "Microsoft.AspNetCore.SignalR.Core",
        "Microsoft.AspNetCore.SignalR.Client",
    ];

    public static TheoryData<string> InnerLayers() => new()
    {
        typeof(EquipmentAsset).Assembly.GetName().Name!,
        typeof(CreateEquipment).Assembly.GetName().Name!,
        typeof(OnboardingRequest).Assembly.GetName().Name!,
        typeof(ApproveOnboardingStandard).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(InnerLayers))]
    public void Inner_layers_do_not_reference_any_transport(string assemblyName)
    {
        var referenced = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(reference => reference.Name);

        Assert.Empty(referenced.Intersect(Transports));
    }

    [Fact]
    public void Message_contracts_reference_only_the_dependency_free_abstractions()
    {
        // Desktop apps reference this assembly, so anything it pulls in, they pull in too.
        var referenced = typeof(EquipmentCreated).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not ("netstandard" or "mscorlib" or "System.Runtime"));

        Assert.Equal(["Messaging.Abstractions"], referenced);
    }
}
