using System.Collections;
using CustomMemoryEFProvider.Core.Implementations;

namespace CustomMemoryEFProvider.Core.Interfaces;

/// <summary>
/// Defines CRUD operations for an in-memory table of a specific entity type
/// </summary>
/// <typeparam name="TEntity">Type of the entity stored in the table</typeparam>
public interface IMemoryTable<TEntity> : IMemoryTable where TEntity : class
{
    IQueryable<SnapshotRow> QueryRows { get; }

    /// <summary>
    /// Gets all entities in the table as queryable (supports LINQ)
    /// </summary>
    IQueryable<TEntity> Query { get; }

    /// <summary>
    /// Adds a new entity to the table
    /// </summary>
    /// <param name="entity">Entity to add</param>
    /// <exception cref="ArgumentNullException">Thrown when entity is null</exception>
    void Add(TEntity entity);

    /// <summary>
    /// Updates an existing entity in the table
    /// </summary>
    /// <param name="entity">Entity to update</param>
    /// <exception cref="KeyNotFoundException">Thrown when entity does not exist</exception>
    void Update(TEntity entity);

    /// <summary>
    /// Removes an entity from the table
    /// </summary>
    /// <param name="entity">Entity to remove</param>
    /// <exception cref="KeyNotFoundException">Thrown when entity does not exist</exception>
    void Remove(TEntity entity);

    /// <summary>
    /// Finds an entity by its primary key values
    /// </summary>
    /// <param name="keyValues">Primary key values (supports composite keys)</param>
    /// <returns>Found entity or null if not exists</returns>
    TEntity? Find(object[] keyValues);
    
    IEnumerable IMemoryTable.GetAllEntities() => Query;
}