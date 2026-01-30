namespace CustomMemoryEFProvider.Core.Exceptions;

/// <summary>
/// Base exception for all in-memory database related errors
/// </summary>
public class MemoryDatabaseException : Exception
{
    /// <summary>
    /// Initializes a new instance of MemoryDatabaseException
    /// </summary>
    /// <param name="message">Error message</param>
    public MemoryDatabaseException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of MemoryDatabaseException with inner exception
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public MemoryDatabaseException(string message, Exception innerException) : base(message, innerException) { }
}