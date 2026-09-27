using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Abstractions;
using Equipment.Messages;

namespace Equipment.Application.Inventory;

public static class DeleteEquipment
{
    public sealed record Command(Guid EquipmentId) : IRequest<bool>, IEquipmentCommand, IAuditableRequest
    {
        public string AuditResource => $"Equipment/{EquipmentId}";
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

            _equipment.Remove(asset);
            await _cache.RemoveAsync(asset.Id, cancellationToken);
            _changeNotifier.Notify(asset.Id);

            // Same event to both channels; both wait for the commit (see CreateEquipment for the full picture).
            var deleted = new EquipmentDeleted { Id = asset.Id };
            _outbox.Enqueue(deleted);
            _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent(nameof(EquipmentDeleted), deleted));

            return true;
        }
    }
}
