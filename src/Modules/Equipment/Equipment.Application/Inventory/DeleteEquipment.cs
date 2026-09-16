using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Abstractions;

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

            _equipment.Remove(asset);
            await _cache.RemoveAsync(asset.Id, cancellationToken);

            _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentDeleted", new
            {
                id = asset.Id,
            }));

            return true;
        }
    }
}
