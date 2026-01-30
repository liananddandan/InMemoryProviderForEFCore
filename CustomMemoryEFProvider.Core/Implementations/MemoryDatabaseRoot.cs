using System.Collections.Concurrent;

namespace CustomMemoryEFProvider.Core.Implementations;

public sealed class MemoryDatabaseRoot
{
    private readonly ConcurrentDictionary<string, MemoryDatabase> _stores = new();

    public MemoryDatabase GetOrAdd(string databaseName)
        => _stores.GetOrAdd(databaseName, _ => new MemoryDatabase());
}