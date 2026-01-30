using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace CustomMemoryEFProvider.Infrastructure.Query;

/// <summary>
/// Rewrites EF.Property&lt;T&gt;(entity, "Prop") into CLR member access (entity.Prop),
/// and fixes common EF-generated nullable/equality patterns so the expression becomes executable
/// in a lightweight provider (no shadow properties / no EFProperty support).
///
/// This visitor focuses on:
/// 1) EF.Property rewrite -> Property/Field access
/// 2) Equality/inequality alignment: T vs Nullable&lt;T&gt;, and (non-nullable value) vs null
/// 3) Typing null constants for Nullable&lt;T&gt; comparisons
/// </summary>
public sealed class EfPropertyRewritingVisitor : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Match: EF.Property<TProperty>(object entity, string propertyName)
        if (node.Method.DeclaringType == typeof(EF)
            && node.Method.Name == nameof(EF.Property)
            && node.Method.IsGenericMethod
            && node.Arguments.Count == 2)
        {
            // entity expression (may be typed as object)
            var instance = Visit(node.Arguments[0])!;
            var nameExpr = node.Arguments[1] as ConstantExpression;

            if (nameExpr?.Value is not string propName)
                throw new NotSupportedException(
                    "EF.Property: propertyName must be a constant string in CustomMemory provider.");

            // Use the expression static type (usually the entity CLR type after other rewrites)
            var instanceType = instance.Type;

            // Prefer CLR property; if not found, try field (some people use backing fields)
            var pi = instanceType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null)
            {
                Expression typedInstance = instance;

                // If instance is object or base type, convert to declaring type
                if (pi.DeclaringType != null && typedInstance.Type != pi.DeclaringType)
                    typedInstance = Expression.Convert(typedInstance, pi.DeclaringType);

                return Expression.Property(typedInstance, pi);
            }

            var fi = instanceType.GetField(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null)
            {
                Expression typedInstance = instance;

                if (fi.DeclaringType != null && typedInstance.Type != fi.DeclaringType)
                    typedInstance = Expression.Convert(typedInstance, fi.DeclaringType);

                return Expression.Field(typedInstance, fi);
            }

            // Shadow property not supported (unless you add a model-backed dictionary lookup)
            throw new NotSupportedException(
                $"EF.Property: CLR member '{instanceType.Name}.{propName}' not found (shadow properties not supported).");
        }

        return base.VisitMethodCall(node);
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        // Visit children first (so EF.Property inside them gets rewritten)
        var left = Visit(node.Left);
        var right = Visit(node.Right);

        // Only specialize == / != (this is the failure you hit)
        if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            // Case A: non-nullable value type compared to null
            // EF may generate: EF.Property(b,"Id") != null even when Id is int.
            // After rewrite: b.Id != null is invalid; semantically:
            //   int == null  => false
            //   int != null  => true
            if (IsNullConstant(right) && IsNonNullableValueType(left.Type))
                return Expression.Constant(node.NodeType == ExpressionType.NotEqual);

            if (IsNullConstant(left) && IsNonNullableValueType(right.Type))
                return Expression.Constant(node.NodeType == ExpressionType.NotEqual);

            // Case B: T vs Nullable<T> => lift T to Nullable<T>
            if (TryLiftToNullable(ref left, ref right))
                return Expression.MakeBinary(node.NodeType, left, right);

            // Case C: Nullable<T> vs null => ensure the null constant is typed as Nullable<T>
            if (IsNullConstant(right) && IsNullableValueType(left.Type) && right.Type != left.Type)
            {
                right = Expression.Constant(null, left.Type);
                return Expression.MakeBinary(node.NodeType, left, right);
            }

            if (IsNullConstant(left) && IsNullableValueType(right.Type) && left.Type != right.Type)
            {
                left = Expression.Constant(null, right.Type);
                return Expression.MakeBinary(node.NodeType, left, right);
            }

            // For == / !=, do NOT reuse EF's original lift/conversion metadata.
            // After rewrite, those flags can reintroduce invalid type combinations.
            return Expression.MakeBinary(node.NodeType, left, right);
        }

        // Other binary ops: preserve original lifting/method if possible
        return Expression.MakeBinary(
            node.NodeType,
            left,
            right,
            node.IsLiftedToNull,
            node.Method,
            node.Conversion);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        // Optional: simplify Convert(object) that EF sometimes introduces around EF.Property
        // Keep it conservative: only remove Convert/ConvertChecked when it doesn't change semantics.
        var operand = Visit(node.Operand);

        if (node.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            // Convert(T -> T) is pointless
            if (operand.Type == node.Type) return operand;
        }

        return base.VisitUnary(node.Update(operand));
    }

    private static bool IsNullConstant(Expression e)
        => e is ConstantExpression c && c.Value == null;

    private static bool IsNonNullableValueType(Type t)
        => t.IsValueType && Nullable.GetUnderlyingType(t) == null;

    private static bool IsNullableValueType(Type t)
        => Nullable.GetUnderlyingType(t) != null;

    private static bool TryLiftToNullable(ref Expression left, ref Expression right)
    {
        var lt = left.Type;
        var rt = right.Type;

        var lUnder = Nullable.GetUnderlyingType(lt);
        var rUnder = Nullable.GetUnderlyingType(rt);

        // left is Nullable<T>, right is T
        if (lUnder != null && rUnder == null && rt == lUnder)
        {
            right = Expression.Convert(right, lt);
            return true;
        }

        // right is Nullable<T>, left is T
        if (rUnder != null && lUnder == null && lt == rUnder)
        {
            left = Expression.Convert(left, rt);
            return true;
        }

        return false;
    }
}