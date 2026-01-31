using System.Collections;
using CustomMemoryEFProvider.Core.Diagnostics;
using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Exceptions;
using CustomMemoryEFProvider.Core.Helpers;
using CustomMemoryEFProvider.Core.Interfaces;

namespace CustomMemoryEFProvider.Core.Implementations;

public readonly record struct SnapshotRow(object[] Key, ScalarSnapshot Snapshot);

/// <summary>
/// In-memory table implementation using Dictionary for storage (supports composite primary keys)
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
public class MemoryTable<TEntity> : IMemoryTable<TEntity> where TEntity : class
{
    /// <summary>
    /// Stores entities indexed by their primary key values (supports composite keys via object array)
    /// </summary>
    private readonly Dictionary<object[], ScalarSnapshot> _committedData
        = new(ArrayComparer.Instance);

    private readonly Dictionary<object[], (ScalarSnapshot, EntityState State)> _pendingChanges
        = new(ArrayComparer.Instance);

    private readonly Type _entityType;

    /// <summary>
    /// Initializes a new instance of MemoryTable<TEntity>
    /// </summary>
    /// <param name="entityType">Entity type metadata</param>
    public MemoryTable(Type entityType)
    {
        _entityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        if (!typeof(TEntity).IsAssignableFrom(entityType))
        {
            throw new ArgumentException(
                $"Entity type '{entityType.FullName}' is not compatible with generic type '{typeof(TEntity).FullName}'",
                nameof(entityType));
        }
    }

    public IQueryable<SnapshotRow> QueryRows
    {
        get
        {
            ProviderDiagnostics.QueryRowsCalled++;
            // start from committed
            var rows = _committedData
                .Select(kv => new SnapshotRow(kv.Key, kv.Value))
                .ToList();

            // merge pending changes
            foreach (var (key, pending) in _pendingChanges)
            {
                var (snap, state) = pending;

                switch (state)
                {
                    case EntityState.Added:
                        rows.Add(new SnapshotRow(key, snap!));
                        break;

                    case EntityState.Deleted:
                        rows.RemoveAll(r => ArrayComparer.Instance.Equals(r.Key, key));
                        break;

                    case EntityState.Modified:
                    {
                        var idx = rows.FindIndex(r => ArrayComparer.Instance.Equals(r.Key, key));
                        if (idx >= 0)
                            rows[idx] = new SnapshotRow(key, snap!);
                        else
                            rows.Add(new SnapshotRow(key, snap!)); // be tolerant
                        break;
                    }
                }
            }

            return rows.AsQueryable();
        }
    }

    /// <inheritdoc/>
    [Obsolete("Do not use in provider pipeline. Use QueryRows and let EF Core materialize entities.")]
    public IQueryable<TEntity> Query {
        get
        {
            ProviderDiagnostics.QueryCalled++;
            return QueryRows.Select(r
                => ScalarEntityCloner.MaterializeFromSnapshot<TEntity>(r.Snapshot)).AsQueryable();
        }
    }

/// <inheritdoc/>
    public void Add(TEntity entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity), "Entity cannot be null.");

        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, _entityType);
        if (_committedData.ContainsKey(keyValues))
        {
            throw new MemoryDatabaseException(
                $"Entity with key [{string.Join(",", keyValues)}] already exists in committed data");
        }

        if (_pendingChanges.ContainsKey(keyValues) &&
            (_pendingChanges[keyValues].State == EntityState.Added
             || _pendingChanges[keyValues].State == EntityState.Modified))
        {
            throw new MemoryDatabaseException(
                $"Entity with key [{string.Join(",", keyValues)}] is already pending addition/modification");
        }

        var snap = ScalarEntityCloner.ExtractSnapshot(entity);
        _pendingChanges[keyValues] = (snap, EntityState.Added);
    }

    /// <inheritdoc/>
    public void Update(TEntity entity)
    {
        // Validate input
        if (entity == null)
            throw new ArgumentNullException(nameof(entity), "Entity cannot be null");

        // Extract primary key values from the entity
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, _entityType);

        // Check if entity exists in the table
        var existsInCommitted = _committedData.ContainsKey(keyValues);
        var existsInPending = _pendingChanges.TryGetValue(keyValues, out var pending) &&
                              pending.State != EntityState.Deleted;

        if (!existsInCommitted && !existsInPending)
        {
            throw new KeyNotFoundException(
                $"Entity with key [{string.Join(",", keyValues)}] not found (cannot update non-existent entity)");
        }

        var snap = ScalarEntityCloner.ExtractSnapshot(entity);
        // Update the entity (replace the value for the existing key)
        _pendingChanges[keyValues] = (snap, EntityState.Modified);
    }

    /// <inheritdoc/>
    public void Remove(TEntity entity)
    {
        // Validate input
        if (entity == null)
            throw new ArgumentNullException(nameof(entity), "Entity cannot be null");

        // Extract primary key values from the entity
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, _entityType);

        // Check if entity exists in the table
        var existsInCommitted = _committedData.ContainsKey(keyValues);
        var existsInPending = _pendingChanges.TryGetValue(keyValues, out var pending) &&
                              pending.State != EntityState.Deleted;
        
        if (!existsInCommitted && !existsInPending)
        {
            throw new KeyNotFoundException(
                $"Entity with key [{string.Join(",", keyValues)}] not found (cannot delete non-existent entity)");
        }

        _pendingChanges[keyValues] = (null, EntityState.Deleted);
    }

    /// <inheritdoc/>
    /// Find returns a materialized entity. This is NOT used for query pipeline,
    /// but is useful for internal lookups.
    public TEntity? Find(object[] keyValues)
    {
        if (keyValues == null) throw new ArgumentNullException(nameof(keyValues));
        if (_pendingChanges.TryGetValue(keyValues, out var pending))
        {
            if (pending.State == EntityState.Deleted) return null;
            return ScalarEntityCloner.MaterializeFromSnapshot<TEntity>(pending.Item1);
        }

        if (_committedData.TryGetValue(keyValues, out var snap))
            return ScalarEntityCloner.MaterializeFromSnapshot<TEntity>(snap);

        return null;
    }

    object? IMemoryTable.Find(object[] keyValues) => Find(keyValues);

    public Type EntityType { get; }

    public int SaveChanges()
    {
        Console.WriteLine($"[Table SaveChanges] tableType={_entityType.FullName} this={GetHashCode()} " +
                          $"pending={_pendingChanges.Count} committedBefore={_committedData.Count}");
        int changedCount = 0;
        foreach (var (key, (snap, state)) in _pendingChanges)
        {
            Console.WriteLine($"  - {state} key=[{string.Join(",", key)}]");

            switch (state)
            {
                case EntityState.Added:
                case EntityState.Modified:
                    _committedData[key] = snap;
                    changedCount++;
                    break;
                case EntityState.Deleted:
                    _committedData.Remove(key);
                    changedCount++;
                    break;
            }
        }

        _pendingChanges.Clear();
        Console.WriteLine(
            $"[Table SaveChanges] tableType={_entityType.FullName} this={GetHashCode()} committedAfter={_committedData.Count}");

        return changedCount;
    }

    public void Clear()
    {
        _committedData.Clear();
        _pendingChanges.Clear();
    }

    public void Dispose()
    {
        Clear();
    }

    IEnumerable IMemoryTable.GetAllEntities() => QueryRows;
}