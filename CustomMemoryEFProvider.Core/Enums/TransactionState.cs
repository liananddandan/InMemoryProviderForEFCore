namespace CustomMemoryEFProvider.Core.Enums;

/// <summary>
/// Represents the state of an in-memory transaction
/// </summary>
public enum TransactionState
{
    Active,
    Committed,
    RolledBack,
    Disposed
}