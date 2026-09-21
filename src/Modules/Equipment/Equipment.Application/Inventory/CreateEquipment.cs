using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Abstractions;
using Equipment.Domain;
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

        public Handler(IEquipmentRepository equipment, IEquipmentChangeNotifier changeNotifier, IRealtimeDispatch realtime)
        {
            _equipment = equipment;
            _changeNotifier = changeNotifier;
            _realtime = realtime;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var asset = EquipmentAsset.Create(command.Name, command.Category, command.AssetTag, command.SiteId);
            await _equipment.AddAsync(asset, cancellationToken);
            _changeNotifier.Notify(asset.Id, command.SiteId);

            _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentCreated", new
            {
                id = asset.Id,
                name = asset.Name,
                category = asset.Category.ToString(),
                assetTag = asset.AssetTag,
                status = asset.Status.ToString(),
            }));

            return asset.Id;
        }
    }
}
