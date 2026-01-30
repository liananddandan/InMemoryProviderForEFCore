using System.ComponentModel.DataAnnotations;
using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Interfaces;

namespace CustomMemoryEFProvider.Core.Exceptions;

/// <summary>
/// Exception thrown for invalid transaction operations
/// </summary>
public class TransactionException : InvalidOperationException
{
    /// <summary>
    /// Current state of the transaction when the exception occurred
    /// </summary>
    public TransactionState CurrentState { get; }

    /// <summary>
    /// Initialize exception with message and transaction state
    /// </summary>
    public TransactionException(string message, TransactionState currentState) 
        : base(message)
    {
        CurrentState = currentState;
    }
}