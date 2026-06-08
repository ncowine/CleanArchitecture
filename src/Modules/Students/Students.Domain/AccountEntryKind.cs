namespace Students.Domain;

public enum AccountEntryKind
{
    /// <summary>Increases the balance owed (tuition, fee, fine).</summary>
    Charge,

    /// <summary>Reduces the balance (money received).</summary>
    Payment,

    /// <summary>Reduces the balance without payment (a charge written off).</summary>
    Waiver,
}
