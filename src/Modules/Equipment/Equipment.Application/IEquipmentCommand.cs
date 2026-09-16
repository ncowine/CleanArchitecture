namespace Equipment.Application;

/// <summary>
/// Marks a request as an Equipment-module write that must run inside an <c>EquipmentDbContext</c>
/// transaction. The Equipment transaction behavior wraps only requests carrying this marker, so queries
/// — and other modules' requests — are left untouched. Each module has its own marker and its own
/// behavior because a transaction can only span one database.
/// </summary>
public interface IEquipmentCommand;
