using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CustomMemoryEFProvider.Infrastructure;


public sealed class CustomMemoryIntValueGenerator : ValueGenerator<int>
{
    private static readonly ConcurrentDictionary<string, int> _counters = new();
    private readonly string _key;

    public CustomMemoryIntValueGenerator(string key) => _key = key;

    public override bool GeneratesTemporaryValues => false;

    public override int Next(EntityEntry entry)
        => _counters.AddOrUpdate(_key, 1, (_, cur) => checked(cur + 1));
}