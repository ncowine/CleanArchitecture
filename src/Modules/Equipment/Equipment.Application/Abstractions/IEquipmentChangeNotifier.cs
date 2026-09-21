namespace Equipment.Application.Abstractions;

/// <summary>
/// Told about every equipment CRUD event so derived/computed caches (e.g. the per-site summary behind
/// <see cref="ISiteEquipmentSummaryDirectory"/>) can refresh themselves without the write path needing to
/// know they exist or how they key their own entries.
/// <para>
/// <paramref name="siteId"/> is only needed for a create: nothing has computed a summary that includes a
/// brand-new asset yet, so there's no tracked dependency to look up — the write path is the only place
/// that still knows which site it landed in. An update/delete can be resolved from what was already
/// tracked the last time a summary was computed, so it's omitted there.
/// </para>
/// </summary>
public interface IEquipmentChangeNotifier
{
    void Notify(Guid equipmentId, Guid? siteId = null);
}
