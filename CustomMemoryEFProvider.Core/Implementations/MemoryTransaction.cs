using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Exceptions;
using CustomMemoryEFProvider.Core.Interfaces;

namespace CustomMemoryEFProvider.Core.Implementations;

/// <summary>
/// In-memory transaction implementation (simplified version, expandable to snapshot)
/// </summary>
public class MemoryTransaction : IMemoryTransaction
{
    
    private readonly MemoryDatabase _database;
    private TransactionState _state;

    public TransactionState State
    {
        get => _state;
        set
        {
            if (!IsValidStateTransition(_state, value))
            {
                throw new TransactionException(
                    $"Cannot transition transaction state from {_state} to {value}", 
                    _state);
            }
            _state = value;
        }
    }

    public Guid TransactionId { get; } = Guid.NewGuid();

    public MemoryTransaction(MemoryDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        
        _state = TransactionState.Active;
    }

    /// <summary>
    /// Commits all transaction changes (marks state as Committed)
    /// </summary>
    /// <exception cref="TransactionException">If transaction is not Active</exception>
    public void Commit()
    {
        if (State != TransactionState.Active)
        {
            throw new TransactionException(
                $"Cannot commit transaction - current state is {State} (only Active transactions can be committed)", 
                State);
        }
        _database.CommitTransaction();
        State = TransactionState.Committed;
    }

    /// <summary>
    /// Rolls back transaction changes (restores snapshot, marks state as RolledBack)
    /// </summary>
    /// <exception cref="TransactionException">If transaction is not Active</exception>
    public void Rollback()
    {
        if (State != TransactionState.Active)
        {
            throw new TransactionException(
                $"Cannot rollback transaction - current state is {State} (only Active transactions can be rolled back)", 
                State);
        }

        _database.RollbackTransaction();
        
        State = TransactionState.RolledBack;    
    }
    
    public void Dispose()
    {
        if (State == TransactionState.Disposed)
        {
            return;
        }

        if (State == TransactionState.Active)
        {
            Rollback();
        }

        State = TransactionState.Disposed;
        
        GC.SuppressFinalize(this);    
    }
    
    #region 内部辅助方法
    /// <summary>
    /// Validates transaction state transitions (enforces state machine rules)
    /// </summary>
    /// <param name="currentState">Current transaction state</param>
    /// <param name="newState">Requested new state</param>
    /// <returns>True if transition is valid</returns>
    private bool IsValidStateTransition(TransactionState currentState, TransactionState newState)
    {
        // 状态流转规则：
        // Active → Committed/RolledBack/Disposed
        // Committed/RolledBack → Disposed
        // Disposed → 不可变更
        return (currentState, newState) switch
        {
            (TransactionState.Active, TransactionState.Committed) => true,
            (TransactionState.Active, TransactionState.RolledBack) => true,
            (TransactionState.Active, TransactionState.Disposed) => true,
            (TransactionState.Committed, TransactionState.Disposed) => true,
            (TransactionState.RolledBack, TransactionState.Disposed) => true,
            (TransactionState.Disposed, _) => false, // 已释放不可变更
            _ => false
        };
    }
    #endregion
}