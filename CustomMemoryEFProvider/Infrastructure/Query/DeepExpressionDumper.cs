using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;

namespace CustomMemoryEFProvider.Infrastructure.Query;
public static class DeepExpressionDumper
{
    public static void Dump(Expression expr, int maxDepth = 30)
    {
        var seen = new HashSet<object>(RefEq.Instance);
        DumpObj(expr, 0, maxDepth, seen, label: "root");
    }

    private static void DumpObj(object? obj, int depth, int maxDepth, HashSet<object> seen, string? label = null)
    {
        var indent = new string(' ', depth * 2);

        if (obj is null)
        {
            Console.WriteLine($"{indent}{label}: <null>");
            return;
        }

        if (depth > maxDepth)
        {
            Console.WriteLine($"{indent}{label}: ... <maxDepth reached>");
            return;
        }

        // avoid cycles
        if (!IsSimple(obj) && !seen.Add(obj))
        {
            Console.WriteLine($"{indent}{label}: <cycle> {obj.GetType().FullName}");
            return;
        }

        // Expression nodes
        if (obj is Expression e)
        {
            Console.WriteLine($"{indent}{label}: [{e.NodeType}] {e.Type.FullName}  ({e.GetType().FullName})  CanReduce={SafeCanReduce(e)}");

            // Common subtypes with children
            switch (e)
            {
                case MethodCallExpression mc:
                    Console.WriteLine($"{indent}  Method: {mc.Method.DeclaringType?.FullName}.{mc.Method.Name}{FormatGenericArgs(mc.Method)}");
                    if (mc.Object != null) DumpObj(mc.Object, depth + 1, maxDepth, seen, "Object");
                    for (int i = 0; i < mc.Arguments.Count; i++)
                        DumpObj(mc.Arguments[i], depth + 1, maxDepth, seen, $"Arg[{i}]");
                    return;

                case LambdaExpression lam:
                    Console.WriteLine($"{indent}  Params: {string.Join(", ", lam.Parameters.Select(p => $"{p.Type.Name} {p.Name}"))}");
                    DumpObj(lam.Body, depth + 1, maxDepth, seen, "Body");
                    return;

                case MemberExpression me:
                    Console.WriteLine($"{indent}  Member: {me.Member.DeclaringType?.FullName}.{me.Member.Name}");
                    if (me.Expression != null) DumpObj(me.Expression, depth + 1, maxDepth, seen, "Instance");
                    return;

                case UnaryExpression u:
                    Console.WriteLine($"{indent}  Unary: {u.NodeType}");
                    DumpObj(u.Operand, depth + 1, maxDepth, seen, "Operand");
                    return;

                case BinaryExpression b:
                    Console.WriteLine($"{indent}  Binary: {b.NodeType}");
                    DumpObj(b.Left, depth + 1, maxDepth, seen, "Left");
                    DumpObj(b.Right, depth + 1, maxDepth, seen, "Right");
                    return;

                case NewExpression ne:
                    Console.WriteLine($"{indent}  Ctor: {ne.Constructor}");
                    for (int i = 0; i < ne.Arguments.Count; i++)
                        DumpObj(ne.Arguments[i], depth + 1, maxDepth, seen, $"Arg[{i}]");
                    return;

                case ConditionalExpression ce:
                    DumpObj(ce.Test, depth + 1, maxDepth, seen, "Test");
                    DumpObj(ce.IfTrue, depth + 1, maxDepth, seen, "IfTrue");
                    DumpObj(ce.IfFalse, depth + 1, maxDepth, seen, "IfFalse");
                    return;

                case BlockExpression be:
                    Console.WriteLine($"{indent}  Vars: {be.Variables.Count}, Exprs: {be.Expressions.Count}");
                    for (int i = 0; i < be.Variables.Count; i++)
                        DumpObj(be.Variables[i], depth + 1, maxDepth, seen, $"Var[{i}]");
                    for (int i = 0; i < be.Expressions.Count; i++)
                        DumpObj(be.Expressions[i], depth + 1, maxDepth, seen, $"Expr[{i}]");
                    return;
            }

            // Extension nodes: IMPORTANT PART
            if (e.NodeType == ExpressionType.Extension)
            {
                // If reducible, show reduced form too
                if (SafeCanReduce(e))
                {
                    try
                    {
                        var reduced = e.Reduce();
                        DumpObj(reduced, depth + 1, maxDepth, seen, "ReducedTo");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{indent}  Reduce() threw: {ex.GetType().Name} {ex.Message}");
                    }
                }

                // Regardless reducible or not, reflect its members
                ReflectMembers(e, depth + 1, maxDepth, seen);
                return;
            }

            // Default: reflect members too (useful)
            ReflectMembers(e, depth + 1, maxDepth, seen);
            return;
        }

        // IEnumerable (but avoid string)
        if (obj is IEnumerable en && obj is not string)
        {
            Console.WriteLine($"{indent}{label}: IEnumerable<{obj.GetType().FullName}>");
            int i = 0;
            foreach (var item in en)
            {
                if (i >= 20) { Console.WriteLine($"{indent}  ... <truncated>"); break; }
                DumpObj(item, depth + 1, maxDepth, seen, $"[{i}]");
                i++;
            }
            return;
        }

        // Simple
        Console.WriteLine($"{indent}{label}: {obj}  ({obj.GetType().FullName})");
    }

    private static void ReflectMembers(object target, int depth, int maxDepth, HashSet<object> seen)
    {
        var indent = new string(' ', depth * 2);
        var t = target.GetType();

        Console.WriteLine($"{indent}-- reflect {t.FullName} --");

        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Properties
        foreach (var p in t.GetProperties(flags).OrderBy(p => p.Name))
        {
            if (p.GetIndexParameters().Length != 0) continue;

            object? val = null;
            try { val = p.GetValue(target); }
            catch { /* ignore */ }

            if (val is null)
            {
                Console.WriteLine($"{indent}{p.Name}: <null>");
                continue;
            }

            // Keep it readable: only dive for Expression / INavigation / nested custom objects
            if (val is Expression || (val is IEnumerable && val is not string) || ShouldDive(val))
            {
                DumpObj(val, depth + 1, maxDepth, seen, p.Name);
            }
            else
            {
                Console.WriteLine($"{indent}{p.Name}: {val}  ({val.GetType().FullName})");
            }
        }

        // Fields
        foreach (var f in t.GetFields(flags).OrderBy(f => f.Name))
        {
            object? val = null;
            try { val = f.GetValue(target); }
            catch { /* ignore */ }

            if (val is null)
            {
                Console.WriteLine($"{indent}{f.Name}: <null>");
                continue;
            }

            if (val is Expression || (val is IEnumerable && val is not string) || ShouldDive(val))
            {
                DumpObj(val, depth + 1, maxDepth, seen, f.Name);
            }
            else
            {
                Console.WriteLine($"{indent}{f.Name}: {val}  ({val.GetType().FullName})");
            }
        }
    }

    private static bool ShouldDive(object val)
    {
        var t = val.GetType();
        // 你最关心的是 EF 的 include/navigation 之类：这些通常是 interface/complex object
        if (t.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true) return true;
        if (t.Name.Contains("Navigation", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Name.Contains("Include", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsSimple(object obj)
    {
        var t = obj.GetType();
        return t.IsPrimitive
               || t.IsEnum
               || t == typeof(string)
               || t == typeof(decimal)
               || t == typeof(DateTime)
               || t == typeof(DateTimeOffset)
               || t == typeof(Guid);
    }

    private static bool SafeCanReduce(Expression e)
    {
        try { return e.CanReduce; }
        catch { return false; }
    }

    private static string FormatGenericArgs(MethodInfo m)
    {
        if (!m.IsGenericMethod) return "";
        var args = m.GetGenericArguments().Select(a => a.Name);
        return "<" + string.Join(",", args) + ">";
    }

    private sealed class RefEq : IEqualityComparer<object>
    {
        public static readonly RefEq Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}