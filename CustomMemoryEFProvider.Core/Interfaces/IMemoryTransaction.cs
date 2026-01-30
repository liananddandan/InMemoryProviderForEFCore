using CustomMemoryEFProvider.Core.Enums;

namespace CustomMemoryEFProvider.Core.Interfaces;

/// <summary>
/// Defines transaction behavior for in-memory database operations
/// </summary>
public interface IMemoryTransaction : IDisposable
{
    /// <summary>
    /// Current state of the transaction
    /// </summary>
    TransactionState State { get; set; }
    
    Guid TransactionId { get; }

    /// <summary>
    /// Commits all changes made in the transaction
    /// </summary>
    /// <exception cref="TransactionException">Thrown if transaction is not active</exception>
    void Commit();

    /// <summary>
    /// Rolls back all changes made in the transaction
    /// </summary>
    /// <exception cref="TransactionException">Thrown if transaction is not active</exception>
    void Rollback();
}