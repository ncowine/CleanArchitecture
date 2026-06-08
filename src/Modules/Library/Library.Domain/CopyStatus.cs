namespace Library.Domain;

public enum CopyStatus
{
    Available,
    OnLoan,

    /// <summary>Returned but held on the hold shelf for the next reservation to pick up.</summary>
    Reserved,
    Lost,
    Withdrawn,
}
