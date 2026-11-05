using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Abstractions;
using Equipment.Domain;
using Equipment.Messages;
using FluentValidation;

namespace Equipment.Application.Inventory;

/// <summary>Add a piece of hardware to inventory. Starts <see cref="EquipmentStatus.Available"/>.</summary>
public static class CreateEquipment
{
    public sealed record Command(string Name, EquipmentCategory Category, string AssetTag, Guid? SiteId = null)
        : IRequest<Guid>, IEquipmentCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Category).IsInEnum();
            RuleFor(command => command.AssetTag).NotEmpty().MaximumLength(50);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IEquipmentRepository _equipment;
        private readonly IEquipmentChangeNotifier _changeNotifier;
        private readonly IRealtimeDispatch _realtime;
        private readonly IEquipmentEvents _events;

        public Handler(
            IEquipmentRepository equipment,
            IEquipmentChangeNotifier changeNotifier,
            IRealtimeDispatch realtime,
            IEquipmentEvents events)
        {
            _equipment = equipment;
            _changeNotifier = changeNotifier;
            _realtime = realtime;
            _events = events;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var asset = EquipmentAsset.Create(command.Name, command.Category, command.AssetTag, command.SiteId);
            await _equipment.AddAsync(asset, cancellationToken);
            _changeNotifier.Notify(asset.Id, command.SiteId);

            // One event, two channels. Neither call sends anything yet; both wait for the transaction to commit, then
            // go out right away (best effort): to other applications over RabbitMQ, and to browsers over SignalR.
            // The handler doesn't know either transport. The host decides.
            var created = new EquipmentCreated
            {
                Id = asset.Id,
                Name = asset.Name,
                Category = asset.Category.ToString(),
                AssetTag = asset.AssetTag,
                Status = asset.Status.ToString(),
            };
            _events.Publish(created);
            _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent(nameof(EquipmentCreated), created));

            return asset.Id;
        }
    }
}
