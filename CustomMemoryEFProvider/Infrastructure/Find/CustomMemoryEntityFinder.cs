using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CustomMemoryEFProvider.Infrastructure.Find;

public sealed class CustomMemoryEntityFinder<TEntity> : IEntityFinder<TEntity> where TEntity : class
{
    private readonly IEntityType _entityType;
    private readonly IStateManager _stateManager;
    private readonly IMemoryDatabase _db;

    public CustomMemoryEntityFinder(IEntityType entityType, IStateManager stateManager, IMemoryDatabase db)
    {
        _entityType = entityType;
        _stateManager = stateManager;
        _db = db;
    }

    private static object[] ToObjectArray(object?[] keyValues)
    {
        // 你的表 key 是 object[]，这里确保类型一致
        var arr = new object[keyValues.Length];
        for (var i = 0; i < keyValues.Length; i++)
        {
            arr[i] = keyValues[i] ?? throw new ArgumentNullException($"keyValues[{i}]");
        }

        return arr;
    }

    private TEntity? FindCore(object?[]? keyValues)
    {
        Log("FindCore", $"keys=[{string.Join(",", keyValues ?? Array.Empty<object>())}]");
        if (keyValues is null) return null;

        var pk = _entityType.FindPrimaryKey()
                 ?? throw new InvalidOperationException($"Entity type '{_entityType.Name}' has no primary key.");

        if (keyValues.Length != pk.Properties.Count)
            throw new ArgumentException($"Incorrect number of key values. Expected {pk.Properties.Count}.",
                nameof(keyValues));

        if (keyValues.Any(v => v is null))
            return null;

        // 1) tracked hit
        var tracked = _stateManager.TryGetEntry(pk, keyValues);
        if (tracked != null)
            return tracked.EntityState == Microsoft.EntityFrameworkCore.EntityState.Deleted
                ? null
                : (TEntity)tracked.Entity;

        // 2) store lookup
        var table = _db.GetTable(_entityType.ClrType);
        var foundObj = table.Find(ToObjectArray(keyValues));
        if (foundObj is null)
        {
            Console.WriteLine($"[FINDER] STORE MISS: {_entityType.ClrType.Name} key=[{string.Join(",", keyValues)}]");
            return null;
        }

        var found = (TEntity)foundObj;

        // 3) attach
        Console.WriteLine(
            $"[FINDER] STORE HIT : {_entityType.ClrType.Name} key=[{string.Join(",", keyValues)}] -> attach");
        var internalEntry = _stateManager.GetOrCreateEntry(found, _entityType);
        // acceptChanges: true 表示把当前值当作“已保存到数据库”的值
        internalEntry.SetEntityState(EntityState.Unchanged, acceptChanges: true);
        return found;
    }


    ValueTask<TEntity?> IEntityFinder<TEntity>.FindAsync(object?[]? keyValues, CancellationToken cancellationToken)
        => new(FindCore(keyValues));

    IQueryable<TEntity> IEntityFinder<TEntity>.Query(INavigation navigation, InternalEntityEntry entry)
    {
        throw new NotImplementedException();
    }

    TEntity? IEntityFinder<TEntity>.Find(object?[]? keyValues) => FindCore(keyValues);

    public object? Find(object?[]? keyValues) => FindCore(keyValues);

    public InternalEntityEntry? FindEntry<TKey>(TKey keyValue)
    {
        Log(nameof(FindEntry), $"(TKey) key={keyValue}");
        throw new NotImplementedException();
    }

    public InternalEntityEntry? FindEntry<TProperty>(IProperty property, TProperty propertyValue)
    {
        Log(nameof(FindEntry), "(properties + values)");
        throw new NotImplementedException();
    }

    public IEnumerable<InternalEntityEntry> GetEntries<TProperty>(IProperty property, TProperty propertyValue)
    {
        Log(nameof(GetEntries), $"property={property.Name}, value={propertyValue}");
        throw new NotImplementedException();
    }

    public InternalEntityEntry? FindEntry(IEnumerable<object?> keyValues)
    {
        Log(nameof(FindEntry), $"(IEnumerable<object?>) keys=[{string.Join(",", keyValues)}]");
        throw new NotImplementedException();
    }

    public InternalEntityEntry? FindEntry(IEnumerable<IProperty> properties, IEnumerable<object?> propertyValues)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<InternalEntityEntry> GetEntries(IEnumerable<IProperty> properties,
        IEnumerable<object?> propertyValues)
    {
        throw new NotImplementedException();
    }

    public ValueTask<object?> FindAsync(object?[]? keyValues,
        CancellationToken cancellationToken = new CancellationToken())
        => new(FindCore(keyValues));

    public void Load(INavigation navigation, InternalEntityEntry entry, LoadOptions options)
    {
        throw new NotImplementedException();
    }

    public Task LoadAsync(INavigation navigation, InternalEntityEntry entry, LoadOptions options,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public IQueryable Query(INavigation navigation, InternalEntityEntry entry)
    {
        throw new NotImplementedException();
    }

    public object[]? GetDatabaseValues(InternalEntityEntry entry)
    {
        throw new NotImplementedException();
    }

    public Task<object[]?> GetDatabaseValuesAsync(InternalEntityEntry entry,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    private void Log(string method, string msg = "")
    {
        Console.WriteLine($"[FINDER] {method} {_entityType.ClrType.Name} {msg}");
    }
}