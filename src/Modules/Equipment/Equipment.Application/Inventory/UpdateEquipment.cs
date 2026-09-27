using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Abstractions;
using Equipment.Domain;
using Equipment.Messages;
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
        private readonly IEquipmentChangeNotifier _changeNotifier;
        private readonly IRealtimeDispatch _realtime;
        private readonly IEquipmentOutbox _outbox;

        public Handler(
            IEquipmentRepository equipment,
            IEquipmentCacheInvalidator cache,
            IEquipmentChangeNotifier changeNotifier,
            IRealtimeDispatch realtime,
            IEquipmentOutbox outbox)
        {
            _equipment = equipment;
            _cache = cache;
            _changeNotifier = changeNotifier;
            _realtime = realtime;
            _outbox = outbox;
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
            _changeNotifier.Notify(asset.Id);

            // Same event to both channels; both wait for the commit (see CreateEquipment for the full picture).
            var updated = new EquipmentUpdated
            {
                Id = asset.Id,
                Name = asset.Name,
                Category = asset.Category.ToString(),
                AssetTag = asset.AssetTag,
                Status = asset.Status.ToString(),
            };
            _outbox.Enqueue(updated);
            _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent(nameof(EquipmentUpdated), updated));

            return true;
        }
    }
}
