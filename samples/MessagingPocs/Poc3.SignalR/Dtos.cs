namespace Poc3.SignalR;

// The shapes the API sends. Property names match the server's JSON (camelCase on the wire; both HttpClient's JSON
// helpers and the SignalR client match names without regard to case).
//
// ❌ DON'T share the server's classes with the client project just to avoid writing these. The client would pull in
//    server assemblies and their dependencies, and every server refactoring would become a client rebuild. Share a
//    small contracts assembly, or keep a copy like this and treat the JSON as the contract.
//
// Today the API sends these events as anonymous objects (see CreateEquipment.cs). That works, but nothing tells the
// server-side developer that renaming "assetTag" breaks this app. Named event classes in Equipment.Contracts would.

/// <summary>A row from POST /equipment/search, and the payload of EquipmentCreated / EquipmentUpdated.</summary>
public sealed record EquipmentDto(Guid Id, string Name, string Category, string AssetTag, string Status);

/// <summary>The payload of EquipmentDeleted.</summary>
public sealed record EquipmentDeletedDto(Guid Id);

/// <summary>The hub's "presence" message: who is watching a group.</summary>
public sealed record PresenceDto(string Group, List<string> Users);

/// <summary>The API's paged list response.</summary>
public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);
