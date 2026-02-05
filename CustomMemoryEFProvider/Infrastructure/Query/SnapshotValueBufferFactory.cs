using System.Collections.Concurrent;
using CustomMemoryEFProvider.Core.Implementations;
using CustomMemoryEFProvider.Infrastructure.Query;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

public sealed class SnapshotValueBufferFactory
{
    private readonly ConcurrentDictionary<IEntityType, IProperty[]> _scalarPropsCache = new();

    public ValueBuffer Create(IEntityType entityType, ScalarSnapshot snapshot)
    {
        var props = _scalarPropsCache.GetOrAdd(entityType, static et =>
            et.GetProperties().ToArray());

        var values = new object?[props.Length];
        for (var i = 0; i < props.Length; i++)
        {
            var name = props[i].Name; // EF model property name
            snapshot.ValuesByName.TryGetValue(name, out var v);
            values[i] = v;
        }

        return new ValueBuffer(values);
    }

    // 给 TrackFromRow 用：构建 originalValues 的 ISnapshot（同样要按 EF 顺序）
    public ISnapshot CreateOriginalValuesSnapshot(IEntityType entityType, ScalarSnapshot snapshot)
    {
        var props = _scalarPropsCache.GetOrAdd(entityType, static et =>
            et.GetProperties().ToArray());

        var values = new object?[props.Length];
        for (var i = 0; i < props.Length; i++)
        {
            var name = props[i].Name;
            snapshot.ValuesByName.TryGetValue(name, out var v);
            values[i] = v;
        }

        return new ArraySnapshot(values);
    }
}