namespace CustomMemoryEFProvider.Core.Interfaces;

/// <summary>
/// Defines the core behavior of an in-memory database that manages tables and transactions
/// </summary>
public interface IMemoryDatabase : IDisposable
{
    /// <summary>
    /// Gets or creates an in-memory table for the specified entity type
    /// </summary>
    /// <typeparam name="TEntity">Type of the entity</typeparam>
    /// <param name="entityType">Metadata of the entity type (e.g., primary key info)</param>
    /// <returns>An instance of IMemoryTable<TEntity></returns>
    IMemoryTable<TEntity> GetTable<TEntity>(Type? entityType = null) where TEntity : class;
    
    /// <summary>
    /// Begins a new transaction on the database
    /// </summary>
    /// <returns>An instance of IMemoryTransaction</returns>
    IMemoryTransaction BeginTransaction();

    /// <summary>
    /// Saves all pending changes to the database
    /// </summary>
    /// <returns>Number of entities affected</returns>
    int SaveChanges();

    /// <summary>
    /// Asynchronously saves all pending changes to the database
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the asynchronous operation with affected entity count</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    IMemoryTable GetTable(Type entityClrType);
}