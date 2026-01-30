using System.Collections;

namespace CustomMemoryEFProvider.Core.Helpers;

/// <summary>
/// Equality comparer for object arrays (used for composite primary keys)
/// </summary>
public class ArrayComparer : IEqualityComparer<object[]>, IEqualityComparer
{
    public static readonly ArrayComparer Instance = new();
    
    /// <inheritdoc/>
    public bool Equals(object[]? x, object[]? y)
    {
        if (x == null || y == null) return x == y;
        if (x.Length != y.Length) return false;
        
        for (int i = 0; i < x.Length; i++)
        {
            if (!object.Equals(x[i], y[i])) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public int GetHashCode(object[] obj)
    {
        if (obj == null) return 0;
        
        // 合并数组所有元素的 HashCode（避免相同内容不同 Hash）
        int hash = 17;
        foreach (var item in obj)
        {
            hash = hash * 31 + (item?.GetHashCode() ?? 0);
        }
        return hash;
    }

    public bool Equals(object? x, object? y)
    {
        return Equals(x as object[], y as object[]);
    }

    public int GetHashCode(object obj)
    {
        return GetHashCode(obj as object[] ?? throw new ArgumentException("Object is not an array", nameof(obj)));
    }
}