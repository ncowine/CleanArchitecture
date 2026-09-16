using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Abstractions;
using Equipment.Domain;
using FluentValidation;

namespace Equipment.Application.Inventory;

public static class UpdateEquipment
{
    public sealed record Command(Guid EquipmentId, string Name, EquipmentCategory Category, string AssetTag)
        : IRequest<bool>, IEquipmentCommand, IAuditableRequest
    {
        public string AuditResource => $"Equipment/{EquipmentId}";
    }

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.EquipmentId).NotEmpty();
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Category).IsInEnum();
            RuleFor(command => command.AssetTag).NotEmpty().MaximumLength(50);
        }
    }

    public sealed class Handler : IRequestHandler<Command, bool>
    {
        private readonly IEquipmentRepository _equipment;
        private readonly IEquipmentCacheInvalidator _cache;
        private readonly IRealtimeDispatch _realtime;

        public Handler(IEquipmentRepository equipment, IEquipmentCacheInvalidator cache, IRealtimeDispatch realtime)
        {
            _equipment = equipment;
            _cache = cache;
            _realtime = realtime;
        }

        public async Task<bool> Handle(Command command, CancellationToken cancellationToken)
        {
            var asset = await _equipment.GetAsync(command.EquipmentId, cancellationToken);
            if (asset is null)
            {
                return false;
            }

            asset.Update(command.Name, command.Category, command.AssetTag);
            await _cache.RemoveAsync(asset.Id, cancellationToken);

            _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentUpdated", new
            {
                id = asset.Id,
                name = asset.Name,
                category = asset.Category.ToString(),
                assetTag = asset.AssetTag,
                status = asset.Status.ToString(),
            }));

            return true;
        }
    }
}
