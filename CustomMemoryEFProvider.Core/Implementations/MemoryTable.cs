using System.Collections;
using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Exceptions;
using CustomMemoryEFProvider.Core.Helpers;
using CustomMemoryEFProvider.Core.Interfaces;

namespace CustomMemoryEFProvider.Core.Implementations;

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

    /// <inheritdoc/>
    public IQueryable<TEntity> Query
    {
        get
        {
            var committedEntities = _committedData.ToList();
            foreach (var (key, (snap, state)) in _pendingChanges)
            {
                switch (state)
                {
                    case EntityState.Added:
                        committedEntities.Add(new KeyValuePair<object[], ScalarSnapshot>(key, snap));
                        break;
                    case EntityState.Deleted:
                        committedEntities.RemoveAll(kv => ArrayComparer.Instance.Equals(kv.Key, snap));
                        break;
                    case EntityState.Modified:
                        var idx = committedEntities.FindIndex(kv => ArrayComparer.Instance.Equals(kv.Key, key));
                        if (idx >= 0)
                            committedEntities[idx] = new KeyValuePair<object[], ScalarSnapshot>(key, snap);
                        break;
                }
            }
            return committedEntities
                .Select(kv => ScalarEntityCloner.MaterializeFromSnapshot<TEntity>(kv.Value))
                .AsQueryable();
        }
    }

    /// <inheritdoc/>
    public void Add(TEntity entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity), "Entity cannot be null.");
        
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, _entityType);
        if (_committedData.ContainsKey(keyValues))
        {
            throw new MemoryDatabaseException($"Entity with key [{string.Join(",", keyValues)}] already exists in committed data");
        }
        
        if (_pendingChanges.ContainsKey(keyValues) && 
            (_pendingChanges[keyValues].State == EntityState.Added 
             || _pendingChanges[keyValues].State == EntityState.Modified))
        {
            throw new MemoryDatabaseException($"Entity with key [{string.Join(",", keyValues)}] is already pending addition/modification");
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
        var existsInPendingAdded = _pendingChanges.ContainsKey(keyValues) && 
                                   (_pendingChanges[keyValues].State == EntityState.Added || 
                                    _pendingChanges[keyValues].State == EntityState.Modified);

        if (!existsInCommitted && !existsInPendingAdded)
        {
            Console.WriteLine($"[Update MISS] entityType={_entityType.FullName}");
            Console.WriteLine($"[Update MISS] incoming key = {string.Join(",", keyValues.Select(v => $"{v}({v?.GetType().Name ?? "null"})"))}");

            Console.WriteLine($"[Update MISS] committed keys count = {_committedData.Count}");
            foreach (var k in _committedData.Keys)
            {
                Console.WriteLine($"  committed key = {string.Join(",", k.Select(v => $"{v}({v?.GetType().Name ?? "null"})"))} " +
                                  $" equalsIncoming={ArrayComparer.Instance.Equals(k, keyValues)}");
            }
            throw new KeyNotFoundException($"Entity with key [{string.Join(",", keyValues)}] not found (cannot update non-existent entity)");
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
        var existsInPending = _pendingChanges.ContainsKey(keyValues) && 
                              (_pendingChanges[keyValues].State == EntityState.Added || _pendingChanges[keyValues].State == EntityState.Modified);
        if (!existsInCommitted && !existsInPending)
        {
            throw new KeyNotFoundException($"Entity with key [{string.Join(",", keyValues)}] not found (cannot delete non-existent entity)");
        }
        
        _pendingChanges[keyValues] = (new ScalarSnapshot() {Values = Array.Empty<object?>()}, EntityState.Deleted);
    }

    /// <inheritdoc/>
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
        Console.WriteLine($"[Table SaveChanges] tableType={_entityType.FullName} this={GetHashCode()} committedAfter={_committedData.Count}");

        return changedCount;
    }

    public void Clear()
    {
        _committedData.Clear();
        _pendingChanges.Clear();
    }

    object? IMemoryTable.Find(object[] keyValues)
    {
        return Find(keyValues);
    }

    public void Dispose()
    {
        Clear();
    }

    IEnumerable IMemoryTable.GetAllEntities() => Query;
}