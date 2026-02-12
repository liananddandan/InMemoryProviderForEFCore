using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public static class IncludeInQueryRewriting
{
    public static Expression RewriteSelectLambdas(
        Expression expr,
        ParameterExpression queryContextParam,
        Func<Expression, Expression> rewriteSubquery)
        => new V(queryContextParam, rewriteSubquery).Visit(expr)!;

    private sealed class V : ExpressionVisitor
    {
        private readonly ParameterExpression _qc;
        private readonly Func<Expression, Expression> _rewriteSubquery;

        public V(ParameterExpression qc, Func<Expression, Expression> rewriteSubquery)
        {
            _qc = qc;
            _rewriteSubquery = rewriteSubquery;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // 先递归 visit children，保证里面的子树先处理
            var visited = (MethodCallExpression)base.VisitMethodCall(node);

            if (!visited.Method.IsGenericMethod) return visited;

            var def = visited.Method.GetGenericMethodDefinition();

            // 只抓 Queryable.Select(source, selector)
            if (def == QueryableSelect_NoIndex_Definition())
            {
                // 第二个参数通常是 Quote(Lambda)
                var selector = UnquoteLambda(visited.Arguments[1]);
                if (selector != null && FindInclude(selector.Body) != null)
                {
                    var rewrittenSelector = IncludeRewriting.RewriteIncludeSelector(
                        selector,
                        _qc,
                        _rewriteSubquery);

                    return Expression.Call(
                        visited.Method,
                        visited.Arguments[0],
                        Expression.Quote(rewrittenSelector));
                }
            }

            return visited;
        }

        private static LambdaExpression? UnquoteLambda(Expression e)
            => e is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lam }
                ? lam
                : e as LambdaExpression;

        // 你之前那版 FindInclude（能在 Extension 上找到 IncludeExpression 的）放这里复用
        private static IncludeExpression? FindInclude(Expression expr)
        {
            IncludeExpression? found = null;
            new FindIncludeVisitor(x =>
            {
                if (found == null) found = x;
            }).Visit(expr);
            return found;
        }

        sealed class FindIncludeVisitor : ExpressionVisitor
        {
            private readonly Action<IncludeExpression> _onFound;
            public FindIncludeVisitor(Action<IncludeExpression> onFound) => _onFound = onFound;

            protected override Expression VisitExtension(Expression node)
            {
                if (node is IncludeExpression ie)
                {
                    _onFound(ie);
                    Visit(ie.EntityExpression);
                    Visit(ie.NavigationExpression);
                    return node;
                }

                if (node is MaterializeCollectionNavigationExpression mc)
                {
                    Visit(mc.Subquery);
                    return node;
                }

                return base.VisitExtension(node);
            }
        }
    }

    // 缓存一下 Queryable.Select 的 generic definition（你项目里已有 QueryableSelect_NoIndex()，但这里要 def）
    private static MethodInfo QueryableSelect_NoIndex_Definition()
    {
        return typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Select))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 2)
            .Single(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length != 2) return false;

                // ps[0] : IQueryable<TSource>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // ps[1] : Expression<Func<TSource, TResult>>   (NOT Func<TSource,int,TResult>)
                if (!ps[1].ParameterType.IsGenericType) return false;
                if (ps[1].ParameterType.GetGenericTypeDefinition() != typeof(Expression<>)) return false;

                var lambdaType = ps[1].ParameterType.GetGenericArguments()[0];
                if (!lambdaType.IsGenericType) return false;

                // must be Func<,>
                return lambdaType.GetGenericTypeDefinition() == typeof(Func<,>);
            });
    }
}