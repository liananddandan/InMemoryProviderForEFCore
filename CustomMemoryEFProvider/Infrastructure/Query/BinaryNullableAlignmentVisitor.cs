using System.Linq.Expressions;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public sealed class BinaryNullableAlignmentVisitor : ExpressionVisitor
{
    protected override Expression VisitBinary(BinaryExpression node)
    {
        // 先递归
        var left = Visit(node.Left);
        var right = Visit(node.Right);

        // 只先解决你现在踩到的最关键一类：Equal / NotEqual 的 nullable 对齐
        if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            // null constant 的情况
            if (IsNullConstant(left) && right != null)
            {
                right = EnsureNullable(right);
                return Expression.MakeBinary(node.NodeType, left!, right!, liftToNull: false, node.Method, node.Conversion);
            }

            if (IsNullConstant(right) && left != null)
            {
                left = EnsureNullable(left);
                return Expression.MakeBinary(node.NodeType, left!, right!, liftToNull: false, node.Method, node.Conversion);
            }

            if (left != null && right != null && left.Type != right.Type)
            {
                // int vs int? 这种：把非 nullable 升到 nullable
                if (IsNullableOf(right.Type, left.Type))
                {
                    left = Expression.Convert(left, right.Type);
                }
                else if (IsNullableOf(left.Type, right.Type))
                {
                    right = Expression.Convert(right, left.Type);
                }
                else
                {
                    // 最保守：两边都转成 object 再比（不推荐常态使用）
                    // 但比起直接炸掉要强。你如果想严格就 throw。
                    left = Expression.Convert(left, typeof(object));
                    right = Expression.Convert(right, typeof(object));
                }

                return Expression.MakeBinary(node.NodeType, left, right, liftToNull: false, node.Method, node.Conversion);
            }
        }

        // 其他 binary 保持原样（但用更新后的 left/right）
        if (left != node.Left || right != node.Right)
            return Expression.MakeBinary(node.NodeType, left!, right!, node.IsLiftedToNull, node.Method, node.Conversion);

        return node;
    }

    private static bool IsNullConstant(Expression? e)
        => e is ConstantExpression c && c.Value == null;

    private static bool IsNullableOf(Type nullableType, Type underlying)
        => Nullable.GetUnderlyingType(nullableType) == underlying;

    private static Expression EnsureNullable(Expression e)
    {
        if (e.Type.IsValueType && Nullable.GetUnderlyingType(e.Type) == null)
        {
            var nt = typeof(Nullable<>).MakeGenericType(e.Type);
            return Expression.Convert(e, nt);
        }
        return e;
    }
}