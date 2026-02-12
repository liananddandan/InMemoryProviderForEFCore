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
        Func<Expression, Expression> rewriteSubqueryToEnumerable // 只做 rewrite，不做 compile
    )
    {
        // ✅把外层 selector 的第一个参数（通常是 TransparentIdentifier 或实体）传进去
        var outerParam = selector.Parameters[0];

        var v = new IncludeSelectorRewriter(queryContextParam, outerParam, rewriteSubqueryToEnumerable);
        var newBody = v.Visit(selector.Body)!;
        return Expression.Lambda(selector.Type, newBody, selector.Parameters);
    }

    private sealed class IncludeSelectorRewriter : ExpressionVisitor
    {
        private readonly ParameterExpression _qc;
        private readonly ParameterExpression _outer; // ✅ selector.Parameters[0]
        private readonly Func<Expression, Expression> _rewriteSubqueryToEnumerable;

        public IncludeSelectorRewriter(
            ParameterExpression qc,
            ParameterExpression outer,
            Func<Expression, Expression> rewriteSubqueryToEnumerable)
        {
            _qc = qc;
            _outer = outer;
            _rewriteSubqueryToEnumerable = rewriteSubqueryToEnumerable;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is not IncludeExpression ie)
                return base.VisitExtension(node);

            // collection include：NavigationExpression = MaterializeCollectionNavigationExpression
            if (ie.NavigationExpression is MaterializeCollectionNavigationExpression mc)
            {
                // 1) rewrite subquery（把 ShapedQuery / CustomMemoryQuery 编译成“可执行表达式”）
                var subqueryExpr = _rewriteSubqueryToEnumerable(mc.Subquery);

                var elementType = GetSequenceElementType(subqueryExpr.Type)
                                  ?? throw new InvalidOperationException(
                                      $"Include subquery is not a sequence. Type={subqueryExpr.Type}");

                // 2) 统一成 IEnumerable<TElement>
                var ienum = typeof(IEnumerable<>).MakeGenericType(elementType);
                if (!ienum.IsAssignableFrom(subqueryExpr.Type))
                    throw new InvalidOperationException(
                        $"Include subquery must be IEnumerable<T>. Got {subqueryExpr.Type}");

                if (subqueryExpr.Type != ienum)
                    subqueryExpr = Expression.Convert(subqueryExpr, ienum);

                // 3) ✅关键：编译成 Func<QueryContext, TOuter, IEnumerable<TElement>>
                var delType = typeof(Func<,,>).MakeGenericType(typeof(QueryContext), _outer.Type, ienum);
                var lam = Expression.Lambda(delType, subqueryExpr, _qc, _outer);
                var compiled = lam.Compile();

                // 4) RunCompiledSubquery(qc, outer, compiled) -> IEnumerable<TElement>
                var runOpen = typeof(IncludeRewriting).GetMethod(
                    nameof(RunCompiledSubqueryWithOuter),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

                var runClosed = runOpen.MakeGenericMethod(_outer.Type, elementType);

                var subqueryEnumerable = Expression.Call(
                    runClosed,
                    _qc,
                    _outer,
                    Expression.Constant(compiled, delType));

                // 5) 调用 LoadCollection<TEntity,TElement>(qc, entity, nav, subqueryEnumerable, setLoaded)
                var entityExpr = Visit(ie.EntityExpression)!; // 例如 b.Outer（Blog）
                var loadOpen = typeof(IncludeRewriting).GetMethod(
                    nameof(LoadCollection),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

                var loadClosed = loadOpen.MakeGenericMethod(entityExpr.Type, elementType);

                return Expression.Call(
                    loadClosed,
                    _qc,
                    entityExpr,
                    Expression.Constant(ie.Navigation, typeof(INavigationBase)),
                    subqueryEnumerable, // IEnumerable<TElement>
                    Expression.Constant(ie.SetLoaded));
            }

            // reference include：先不做主动装填，最小语义：继续返回 entityExpression，让你 join pipeline 处理
            return Visit(ie.EntityExpression)!;
        }
    }

    private static IEnumerable<TElement> RunCompiledSubqueryWithOuter<TOuter, TElement>(
        QueryContext qc,
        TOuter outer,
        Func<QueryContext, TOuter, IEnumerable<TElement>> compiled)
        where TElement : class
        => compiled(qc, outer);

    private static Type? GetSequenceElementType(Type t)
    {
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(IQueryable<>) || def == typeof(IEnumerable<>))
                return t.GetGenericArguments()[0];
        }

        var iface = t.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                 (i.GetGenericTypeDefinition() == typeof(IQueryable<>)
                                  || i.GetGenericTypeDefinition() == typeof(IEnumerable<>)));

        return iface?.GetGenericArguments()[0];
    }

    private static TEntity LoadCollection<TEntity, TElement>(
        QueryContext qc,
        TEntity entity,
        INavigationBase navBase,
        IEnumerable<TElement> subqueryEnumerable,
        bool setLoaded)
        where TEntity : class
        where TElement : class
    {
        if (entity == null) return entity;

        var entry = qc.Context.Entry(entity);

        var navName = navBase.Name;
        var collection = entry.Collection(navName);

        if (collection.IsLoaded)
            return entity;

        // ✅这里枚举的是 IEnumerable<T>，但它来自“已编译的 delegate”，不会再触发 ReduceExtensions
        var list = subqueryEnumerable.ToList();

        var current = GetOrCreateCollection<TEntity, TElement>(entity, navBase);
        current.Clear();
        foreach (var x in list) current.Add(x);

        if (setLoaded)
            collection.IsLoaded = true;

        return entity;
    }

    private static ICollection<TElement> GetOrCreateCollection<TEntity, TElement>(
        TEntity entity,
        INavigationBase navBase)
        where TEntity : class
        where TElement : class
    {
        var pi = entity.GetType().GetProperty(navBase.Name)
                 ?? throw new InvalidOperationException(
                     $"Cannot find navigation property '{navBase.Name}' on '{entity.GetType()}'.");

        var val = pi.GetValue(entity);

        if (val is ICollection<TElement> ok)
            return ok;

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