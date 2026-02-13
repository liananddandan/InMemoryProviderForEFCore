using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using CustomMemoryEFProvider.Core.Diagnostics;
using CustomMemoryEFProvider.Core.Implementations;

namespace CustomMemoryEFProvider.Core.Helpers;

public static class ScalarEntityCloner
{
    // Cache: per CLR type, which properties are "scalar and writable"
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _scalarPropsCache = new();

    public static T CloneScalar<T>(T source) where T : class
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var type = source.GetType();

        // Create a new instance (requires parameterless ctor)
        // If you have entities without parameterless ctor, tell me, we can adjust via FormatterServices or compiled factory.
        var clone = (T)Activator.CreateInstance(type)!;

        var props = _scalarPropsCache.GetOrAdd(type, GetScalarWritableProperties);

        foreach (var p in props)
        {
            var value = p.GetValue(source);
            p.SetValue(clone, value);
        }

        return clone;
    }

    private static PropertyInfo[] GetScalarWritableProperties(Type type)
    {
        // public instance properties only; skip indexers; require getter+setter
        // We intentionally skip reference navigations and collections by scalar-type check.
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p =>
                p.GetIndexParameters().Length == 0 &&
                p.CanRead &&
                p.CanWrite &&
                IsScalarType(p.PropertyType))
            .ToArray();
    }

    private static bool IsScalarType(Type t)
    {
        // unwrap Nullable<T>
        var u = Nullable.GetUnderlyingType(t) ?? t;

        if (u.IsEnum) return true;

        // primitives + common structs
        if (u.IsPrimitive) return true;

        // common scalar types
        if (u == typeof(string)) return true;
        if (u == typeof(decimal)) return true;
        if (u == typeof(DateTime)) return true;
        if (u == typeof(DateTimeOffset)) return true;
        if (u == typeof(TimeSpan)) return true;
        if (u == typeof(Guid)) return true;

        // Anything else is treated as non-scalar:
        // - reference nav (class)
        // - owned/complex type (class/struct not listed)
        // - collections
        // Note: value types not in list (e.g. custom structs) are NOT scalar by default.
        if (typeof(IEnumerable).IsAssignableFrom(u) && u != typeof(string))
            return false;

        return false;
    }
    
    public static PropertyInfo[] GetScalarProps(Type type) => _scalarPropsCache.GetOrAdd(type, GetScalarWritableProperties);

    public static ScalarSnapshot ExtractSnapshot(object source)
    {
        var type = source.GetType();
        var props = GetScalarProps(type);
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var p in props)
            dict[p.Name] = p.GetValue(source);
        return new ScalarSnapshot { ValuesByName = dict };
    }

    public static T MaterializeFromSnapshot<T>(ScalarSnapshot snap) where T : class
    {
        ProviderDiagnostics.MaterializeCalled++;
        var type = typeof(T);
        var props = GetScalarProps(type);

        var obj = (T)Activator.CreateInstance(type)!;
        foreach (var p in props)
        {
            if (!p.CanWrite) continue;

            if (snap.ValuesByName.TryGetValue(p.Name, out var v))
            {
                if (v != null && !p.PropertyType.IsInstanceOfType(v))
                {
                    var targetType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    try
                    {
                        v = Convert.ChangeType(v, targetType);
                    }
                    catch
                    {
                    }
                }

                p.SetValue(obj, v);
            }
        }

        return obj;
    }
}