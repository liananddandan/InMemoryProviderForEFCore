using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Transactions;
using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Helpers;
using CustomMemoryEFProvider.Core.Interfaces;

namespace CustomMemoryEFProvider.Core.Implementations;

/// <summary>
/// In-memory database implementation that manages entity tables and transactions
/// Supports table isolation by actual entity type (handles parent/child generic scenarios)
/// </summary>
public class MemoryDatabase : IMemoryDatabase
{
    private readonly ConcurrentDictionary<Type, object> _tables = new();

    private Dictionary<Type, object>? _transactionTables;
    private IMemoryTransaction? _currentTransaction;
    private bool _disposed;

    /// <summary>
    /// Gets or creates a dedicated in-memory table for the specified entity type
    /// Supports parent/child generic scenarios (e.g., TEntity=BaseEntity, entityType=DerivedEntity)
    /// </summary>
    /// <typeparam name="TEntity">Generic entity type (can be parent class)</typeparam>
    /// <param name="entityType">Actual entity type (subclass of TEntity, null = use TEntity)</param>
    /// <returns>Type-specific MemoryTable instance (singleton per actual entity type)</returns>
    /// <exception cref="ArgumentException">If entityType is not compatible with TEntity</exception>
    public IMemoryTable<TEntity> GetTable<TEntity>(Type? entityType = null) where TEntity : class
    {
        // Step 1: ensure the type of memorytable
        Type actualEntityType = entityType ?? typeof(TEntity);

        // Step 2: check compatability
        if (!typeof(TEntity).IsAssignableFrom(actualEntityType))
        {
            throw new ArgumentException(
                $"Entity type '{actualEntityType.FullName}' is not compatible with generic type '{typeof(TEntity).FullName}'",
                nameof(entityType));
        }

        return (IMemoryTable<TEntity>)GetTableCore(actualEntityType, useTransaction: true);
    }

    /// <summary>
    /// Begins a new transaction on the database
    /// Note: Simplified implementation (single active transaction)
    /// </summary>
    /// <returns>New MemoryTransaction instance</returns>
    /// <exception cref="InvalidOperationException">If a transaction is already active</exception>
    public IMemoryTransaction BeginTransaction()
    {
        // single transaction
        if (_currentTransaction != null && _currentTransaction.State == TransactionState.Active)
        {
            throw new InvalidOperationException(
                "A transaction is already active. Complete the current transaction before starting a new one.");
        }

        _transactionTables = new Dictionary<Type, object>();
        _currentTransaction = new MemoryTransaction(this);
        return _currentTransaction;
    }

    internal void CommitTransaction()
    {
        if (_currentTransaction != null && _currentTransaction.State != TransactionState.Active)
        {
            throw new TransactionException("No active transaction to commit");
        }

        if (_transactionTables == null) return;

        foreach (var (tableType, tableObj) in _transactionTables)
        {
            if (tableObj is IMemoryTable transactionalTable)
            {
                var baseTableObj = _tables.GetOrAdd(
                    tableType,
                    type => Activator.CreateInstance(typeof(MemoryTable<>).MakeGenericType(type), type)!);

                if (baseTableObj is not IMemoryTable baseTable) continue;

                baseTable.Clear();
                foreach (var entity in transactionalTable.GetAllEntities())
                {
                    var addMethod = baseTable.GetType().GetMethod("Add");
                    addMethod.Invoke(baseTable, new[] { entity });
                }

                baseTable.SaveChanges(); // 提交到基础表
            }
        }

        _transactionTables = null;
    }

    internal void RollbackTransaction()
    {
        if (_currentTransaction == null || _currentTransaction.State != TransactionState.Active)
        {
            throw new TransactionException("No active transaction to rollback");
        }

        // 直接丢弃临时表，基础表数据不变
        _transactionTables?.Clear();
        _transactionTables = null;
        // _currentTransaction.State = TransactionState.RolledBack;
    }

    private IMemoryTable<TEntity> GetBaseTable<TEntity>(Type actualEntityType) where TEntity : class
    {
        var table = (IMemoryTable<TEntity>)_tables.GetOrAdd(actualEntityType, type =>
        {
            var t = Activator.CreateInstance(typeof(MemoryTable<>).MakeGenericType(type), type)!;
            Console.WriteLine($"[Create BaseTable] type={type.FullName} tableObjHash={t.GetHashCode()}");
            return t;
        });

        Console.WriteLine($"[Get BaseTable] type={actualEntityType.FullName} tableObjHash={table.GetHashCode()}");
        return table;
    }

    /// <summary>
    /// Saves all pending changes across all tables
    /// </summary>
    /// <returns>Total number of affected entities across all tables</returns>
    public int SaveChanges()
    {
        int totalAffected = 0;

        foreach (var tableEntry in _tables.Values)
        {
            if (tableEntry is IMemoryTable table)
            {
                totalAffected += table.SaveChanges();
            }
        }

        return totalAffected;
    }

    /// <summary>
    /// Asynchronously saves all pending changes (sync wrapped in async for EF Core compatibility)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (honors cancellation request)</param>
    /// <returns>Task with total affected entity count</returns>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var affectedCount = SaveChanges();
        return Task.FromResult(affectedCount);
    }

    public IMemoryTable GetTable(Type entityClrType)
    {
        if (entityClrType == null) throw new ArgumentNullException(nameof(entityClrType));
        return GetTableCore(entityClrType, useTransaction: true);
    }
    
    private IMemoryTable GetTableCore(Type entityClrType, bool useTransaction)
    {
        if (entityClrType == null) throw new ArgumentNullException(nameof(entityClrType));

        // 事务态：返回事务表（按需创建）
        if (useTransaction
            && _currentTransaction != null
            && _currentTransaction.State == TransactionState.Active)
        {
            _transactionTables ??= new Dictionary<Type, object>();

            if (!_transactionTables.TryGetValue(entityClrType, out var tableObj))
            {
                // 基础表
                var baseObj = _tables.GetOrAdd(entityClrType, CreateTableInstance);

                // 事务表（复制基础表）
                tableObj = CreateTransactionalClone(entityClrType, (IMemoryTable)baseObj);
                _transactionTables[entityClrType] = tableObj;
            }

            return (IMemoryTable)tableObj;
        }

        // 非事务：返回基础表
        return (IMemoryTable)_tables.GetOrAdd(entityClrType, CreateTableInstance);
    }
    
    private object CreateTableInstance(Type clrType)
    {
        var obj = Activator.CreateInstance(typeof(MemoryTable<>).MakeGenericType(clrType), clrType)!;
        Console.WriteLine($"[Create BaseTable] type={clrType.FullName} tableObjHash={obj.GetHashCode()}");
        return obj;
    }
    
    private object CreateTransactionalClone(Type clrType, IMemoryTable baseTable)
    {
        var txObj = Activator.CreateInstance(typeof(MemoryTable<>).MakeGenericType(clrType), clrType)!;

        // 把 baseTable 的数据复制到 txTable
        foreach (var entity in baseTable.GetAllEntities())
        {
            var clone = ObjectCloner.DeepClone(entity);
            // 这里为了不改 IMemoryTable 接口，仍然用一次反射调用 Add
            var add = txObj.GetType().GetMethod("Add")!;
            add.Invoke(txObj, new[] { clone });
        }

        ((IMemoryTable)txObj).SaveChanges();
        return txObj;
    }

    /// <summary>
    /// Disposes the database and all underlying tables/transactions
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 1. dispose current transaction
        _currentTransaction?.Dispose();
        _currentTransaction = null;

        // 2. release all table resources
        // foreach (var tableEntry in _tables.Values)
        // {
        //     if (tableEntry is IDisposable disposableTable)
        //     {
        //         disposableTable.Dispose();
        //     }
        // }

        // 3. clear tables
        // _tables.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Internal method to clear all tables (for transaction rollback)
    /// </summary>
    public void ClearAllTables()
    {
        foreach (var tableEntry in _tables.Values)
        {
            if (tableEntry is IMemoryTable table)
            {
                table.Clear();
            }
        }
    }

    /// <summary>
    /// Internal method to get all table types (for transaction snapshot)
    /// </summary>
    internal Type[] GetAllTableTypes() => _tables.Keys.ToArray();
}