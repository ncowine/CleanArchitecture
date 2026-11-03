namespace Poc.Api.NoOutbox;

public sealed record CreateEquipmentRequest(string Name, string Category, string AssetTag);
