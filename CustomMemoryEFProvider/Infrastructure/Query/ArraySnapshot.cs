using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;

namespace CustomMemoryEFProvider.Infrastructure.Query;

// Minimal snapshot implementation for EF Core tracking pipeline.
// Stores original values in an index-based array.
internal sealed class ArraySnapshot : ISnapshot
{
    private readonly object?[] _values;

    public ArraySnapshot(object?[] values)
        => _values = values ?? throw new ArgumentNullException(nameof(values));

    public object? this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    public T GetValue<T>(int index)
        => (T)_values[index]!;
}