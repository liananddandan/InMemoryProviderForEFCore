using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

internal static class IncludeRewriting
{
    // 入口：把 selector 里的 IncludeExpression 重写成“执行 subquery + 写回导航 + set IsLoaded”
    public static LambdaExpression RewriteIncludeSelector(
        LambdaExpression selector,
        ParameterExpression queryContextParam,
        Func<Expression, Expression> rewriteSubquery // 你已有的 rewriter 链：QueryParameter + EntityRoot + EF.Property
    )
    {
        var v = new IncludeSelectorRewriter(queryContextParam, rewriteSubquery);
        var newBody = v.Visit(selector.Body)!;
        return Expression.Lambda(selector.Type, newBody, selector.Parameters);
    }

    private sealed class IncludeSelectorRewriter : ExpressionVisitor
    {
        private readonly ParameterExpression _qc;
        private readonly Func<Expression, Expression> _rewriteSubquery;

        public IncludeSelectorRewriter(ParameterExpression qc, Func<Expression, Expression> rewriteSubquery)
        {
            _qc = qc;
            _rewriteSubquery = rewriteSubquery;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is IncludeExpression ie)
            {
                // 只处理 collection include：NavigationExpression = MaterializeCollectionNavigationExpression
                if (ie.NavigationExpression is MaterializeCollectionNavigationExpression mc)
                {
                    // mc.Subquery 是一个 IQueryable<TElement> 形状（通常是 correlated）
                    var subquery = _rewriteSubquery(mc.Subquery);

                    // 调用 helper：LoadCollection<TEntity, TElement>(qc, entity, navigation, subquery, setLoaded)
                    var entityExpr = Visit(ie.EntityExpression)!; // b
                    var elementType = GetIQueryableElementType(subquery.Type)
                                      ?? throw new InvalidOperationException($"Include subquery is not IQueryable<T>. Type={subquery.Type}");

                    var method = typeof(IncludeRewriting)
                        .GetMethod(nameof(LoadCollection), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                        .MakeGenericMethod(entityExpr.Type, elementType);

                    return Expression.Call(
                        method,
                        _qc,
                        entityExpr,
                        Expression.Constant(ie.Navigation, typeof(INavigationBase)),
                        subquery,
                        Expression.Constant(ie.SetLoaded));
                }

                // reference include：你先别做“装填”，至少别崩。最小语义：只返回 entity（让 reference 仍按你 join pipeline 工作）
                return Visit(ie.EntityExpression)!;
            }

            return base.VisitExtension(node);
        }

        private static Type? GetIQueryableElementType(Type t)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IQueryable<>))
                return t.GetGenericArguments()[0];

            var iface = t.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryable<>));

            return iface?.GetGenericArguments()[0];
        }
    }

    // 关键：这里放 IsLoaded guard，防止重复加载 / 追加两次
    private static TEntity LoadCollection<TEntity, TElement>(
        QueryContext qc,
        TEntity entity,
        INavigationBase navBase,
        IQueryable<TElement> subquery,
        bool setLoaded)
        where TEntity : class
    {
        if (entity == null) return entity;

        var entry = qc.Context.Entry(entity);

        // collection nav
        var navName = navBase.Name;
        var collection = entry.Collection(navName);

        // guard：避免重复装填（对“两个 include”/“重复 include”很关键）
        if (collection.IsLoaded)
            return entity;

        // 执行 subquery（你现在是 N+1 模式，先保证语义正确即可）
        var list = subquery.ToList();

        // 把结果写回到 entity 的集合属性
        // 这里不假设具体集合类型，只要它实现 ICollection<TElement>
        var current = GetOrCreateCollection<TEntity, TElement>(entity, navBase);
        current.Clear();
        foreach (var x in list) current.Add(x);

        if (setLoaded)
            collection.IsLoaded = true;

        return entity;
    }

    private static ICollection<TElement> GetOrCreateCollection<TEntity, TElement>(TEntity entity, INavigationBase navBase)
        where TEntity : class
    {
        // navBase 可能是 INavigation（collection）
        var pi = entity.GetType().GetProperty(navBase.Name)
                 ?? throw new InvalidOperationException($"Cannot find navigation property '{navBase.Name}' on '{entity.GetType()}'.");

        var val = pi.GetValue(entity);

        if (val is ICollection<TElement> ok)
            return ok;

        // 如果是 null，尝试 new List<TElement>() 赋回去
        if (val == null)
        {
            var created = new List<TElement>();
            pi.SetValue(entity, created);
            return created;
        }

        throw new InvalidOperationException(
            $"Navigation '{navBase.Name}' is not ICollection<{typeof(TElement).Name}>. Actual={val.GetType()}");
    }
}