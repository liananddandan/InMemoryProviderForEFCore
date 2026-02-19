using System.Linq.Expressions;
using System.Reflection;
using CustomMemoryEFProvider.Core.Diagnostics;
using CustomMemoryEFProvider.Core.Implementations;
using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public sealed class CustomMemoryShapedQueryCompilingExpressionVisitor
    : ShapedQueryCompilingExpressionVisitor
{
    private readonly IMemoryDatabase _db;
    private readonly SnapshotValueBufferFactory _vbFactory;
    private readonly IEntityMaterializerSource _materializerSource;

    public CustomMemoryShapedQueryCompilingExpressionVisitor(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        IMemoryDatabase db,
        SnapshotValueBufferFactory vbFactory)
        : base(dependencies, queryCompilationContext)
    {
        _db = db;
        _vbFactory = vbFactory;
        _materializerSource = dependencies.EntityMaterializerSource
                              ?? throw new InvalidOperationException(
                                  "EntityMaterializerSource is not available in dependencies.");
    }

    private static Expression BuildQueryRowsExpression(Expression tableExpr, Type clrType)
    {
        // tableExpr.Type == IMemoryTable<BlogPost> 这种
        var prop = tableExpr.Type.GetProperty("QueryRows");
        if (prop == null)
            throw new InvalidOperationException($"IMemoryTable<{clrType.Name}> has no QueryRows property.");

        return Expression.Property(tableExpr, prop);
    }

    private static MethodInfo GetQueryableMethodExact(
        string name,
        int genericArgCount,
        Func<MethodInfo, bool> extraPredicate)
    {
        var methods = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == name)
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == genericArgCount)
            .Where(extraPredicate)
            .ToList();

        if (methods.Count != 1)
            throw new InvalidOperationException(
                $"Ambiguous {nameof(Queryable)}.{name}<{genericArgCount}>: {methods.Count} matches");

        return methods[0];
    }

    private static bool IsQueryableOfT(ParameterInfo p)
    {
        var t = p.ParameterType;
        return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IQueryable<>);
    }

    private static bool IsPredicateExpression(ParameterInfo p)
    {
        // Expression<Func<TSource,bool>>
        var t = p.ParameterType;
        if (!t.IsGenericType) return false;
        if (t.GetGenericTypeDefinition() != typeof(Expression<>)) return false;

        var inner = t.GetGenericArguments()[0];
        if (!inner.IsGenericType) return false;
        if (inner.GetGenericTypeDefinition() != typeof(Func<,>)) return false;

        return inner.GetGenericArguments()[1] == typeof(bool);
    }

    private static MethodInfo GetQueryableMethod(
        string name,
        int genericArgCount,
        params int[] parameterCountOptions)
    {
        var candidates = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == name
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == genericArgCount
                        && parameterCountOptions.Contains(m.GetParameters().Length))
            .ToList();

        // 如果允许 2 参数版本（带 predicate），就把第二参数形状锁死为 Expression<Func<TSource,bool>>
        // 这样 Any/First/Single/Where 这类就不会撞。
        candidates = candidates.Where(m =>
        {
            var ps = m.GetParameters();

            // 1参版本：只要求第一个参数是 IQueryable<T>
            if (ps.Length == 1)
            {
                return ps[0].ParameterType.IsGenericType
                       && ps[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>);
            }

            // 2参版本：第一个参数 IQueryable<T>，第二个参数 Expression<Func<T,bool>>
            if (ps.Length == 2)
            {
                if (!(ps[0].ParameterType.IsGenericType
                      && ps[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)))
                    return false;

                if (!(ps[1].ParameterType.IsGenericType
                      && ps[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)))
                    return false;

                var lambdaType = ps[1].ParameterType.GetGenericArguments()[0];
                if (!(lambdaType.IsGenericType && lambdaType.GetGenericTypeDefinition() == typeof(Func<,>)))
                    return false;

                // Func<TSource,bool>
                return lambdaType.GetGenericArguments()[1] == typeof(bool);
            }

            // 其它参数个数：你当前不需要的话直接排除，避免误选
            return false;
        }).ToList();

        if (candidates.Count != 1)
            throw new InvalidOperationException(
                $"Ambiguous Queryable.{name}<{genericArgCount}>: {candidates.Count} matches");

        return candidates[0];
    }

    private static TEntity IncludeCollection<TEntity, TElement>(
        QueryContext qc,
        TEntity entity,
        string navigationName,
        IEnumerable<TElement> elements,
        bool setLoaded)
        where TEntity : class
        where TElement : class
    {
        if (entity == null) return entity;

        // 1) 把 elements 拉到内存（避免多次枚举）
        var list = elements as IList<TElement> ?? elements.ToList();

        // 2) 尝试把 list 写回到 entity 的导航属性（你的 Blog.Posts 大概率有 setter）
        var prop = typeof(TEntity).GetProperty(navigationName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop == null)
            throw new NotSupportedException(
                $"Navigation property '{typeof(TEntity).Name}.{navigationName}' not found.");

        // 支持 ICollection<T> / List<T> / IEnumerable<T> 的常见情况
        var targetType = prop.PropertyType;

        object valueToSet;
        if (targetType.IsAssignableFrom(list.GetType()))
        {
            valueToSet = list;
        }
        else
        {
            // 如果是 ICollection<T>，用 List<T> 也通常可赋值（只要是接口）
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(ICollection<>))
                valueToSet = list;
            else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                valueToSet = list;
            else
                throw new NotSupportedException($"Unsupported navigation type {targetType} for '{navigationName}'.");
        }

        prop.SetValue(entity, valueToSet);

        // 3) 设置 IsLoaded（仅当 IncludeExpression.SetLoaded = true）
        if (setLoaded)
        {
            var entry = qc.Context.Entry(entity);
            entry.Navigation(navigationName).IsLoaded = true;
        }

        return entity;
    }

    private sealed class IncludeMaterializationRewriter : ExpressionVisitor
    {
        private readonly ParameterExpression _qc;
        private readonly EntityQueryRootRewritingVisitor _entityRootRewriter;
        private readonly EfPropertyRewritingVisitor _efPropRewriter;
        private readonly QueryParameterRewritingVisitor _paramRewriter;

        public IncludeMaterializationRewriter(
            ParameterExpression qc,
            EntityQueryRootRewritingVisitor entityRootRewriter,
            EfPropertyRewritingVisitor efPropRewriter,
            QueryParameterRewritingVisitor paramRewriter)
        {
            _qc = qc;
            _entityRootRewriter = entityRootRewriter;
            _efPropRewriter = efPropRewriter;
            _paramRewriter = paramRewriter;
        }

        public Expression Rewrite(Expression body) => Visit(body);

        protected override Expression VisitExtension(Expression node)
        {
            // ✅ 只处理 IncludeExpression
            if (node is IncludeExpression ie)
            {
                // 只做 collection include（你的 smoke test 就是这个）
                if (ie.Navigation is not null && ie.Navigation.IsCollection)
                {
                    // EntityExpression 应该就是参数 b
                    var entityExpr = Visit(ie.EntityExpression);

                    // NavigationExpression 是 MaterializeCollectionNavigationExpression
                    var mce = ie.NavigationExpression;

                    // 反射取 Subquery（EF Core 的类型是 internal，不能直接强依赖成员）
                    var subqueryProp = mce.GetType().GetProperty("Subquery",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (subqueryProp == null)
                        throw new NotSupportedException(
                            $"MaterializeCollectionNavigationExpression missing Subquery property. Type={mce.GetType().FullName}");

                    var subqueryObj = subqueryProp.GetValue(mce);
                    if (subqueryObj is not Expression subqueryExpr)
                        throw new NotSupportedException(
                            $"Subquery is not Expression. Actual={subqueryObj?.GetType().FullName}");

                    // ✅ 把 subquery 也做同样的 rewrite（参数化、root rewrite、EF.Property）
                    Expression RewriteExpr(Expression e)
                    {
                        e = _paramRewriter.Visit(e)!;
                        e = _entityRootRewriter.Visit(e)!;
                        e = _efPropRewriter.Visit(e)!;
                        return e;
                    }

                    subqueryExpr = RewriteExpr(subqueryExpr);

                    // subqueryExpr 通常是 ShapedQueryExpression / IQueryable<T> 之类
                    // 我们要把它变成 IQueryable<TElement>，并且在这里 enumerate 出来填充到实体上
                    // 取 elementType：从 Subquery.Type 找 IEnumerable<T> 的 T
                    var elemType = TryGetIEnumerableElementType(subqueryExpr.Type)
                                   ?? throw new NotSupportedException(
                                       $"Include subquery must be IEnumerable<T>. Type={subqueryExpr.Type}");

                    // 调用：IncludeCollection<TEntity, TElement>(qc, entity, "Posts", (IEnumerable<TElement>)subquery, setLoaded)
                    var includeMethod = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                        .GetMethod(nameof(IncludeCollection), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(entityExpr.Type, elemType);

                    Expression subqueryAsEnum = subqueryExpr;
                    var ienum = typeof(IEnumerable<>).MakeGenericType(elemType);
                    if (subqueryAsEnum.Type != ienum)
                        subqueryAsEnum = Expression.Convert(subqueryAsEnum, ienum);

                    return Expression.Call(
                        includeMethod,
                        _qc,
                        entityExpr,
                        Expression.Constant(ie.Navigation.Name),
                        subqueryAsEnum,
                        Expression.Constant(ie.SetLoaded));
                }

                // reference include：你现在先不做（不影响当前 smoke test）
                return Visit(ie.EntityExpression);
            }

            // 其它 non-reducible extension：别下探，保持原样（否则会 must be reducible）
            if (!node.CanReduce)
                return node;

            return base.VisitExtension(node);
        }

        private static Type? TryGetIEnumerableElementType(Type t)
        {
            if (t == typeof(string)) return null;
            if (t.IsArray) return t.GetElementType();

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t.GetGenericArguments()[0];

            var iface = t.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return iface?.GetGenericArguments()[0];
        }
    }

    static IncludeExpression? FindInclude(Expression e)
    {
        IncludeExpression? found = null;

        new FindIncludeVisitor(x =>
        {
            if (found == null) found = x;
        }).Visit(e);

        return found;
    }

    sealed class FindIncludeVisitor : ExpressionVisitor
    {
        private readonly Action<IncludeExpression> _onFound;

        public FindIncludeVisitor(Action<IncludeExpression> onFound) => _onFound = onFound;

        protected override Expression VisitExtension(Expression node)
        {
            // ✅关键：IncludeExpression 是 Extension 且 CanReduce=false
            if (node is IncludeExpression ie)
            {
                _onFound(ie);

                // 继续向下找嵌套 theninclude：ie.EntityExpression 里可能还有 IncludeExpression
                Visit(ie.EntityExpression);
                Visit(ie.NavigationExpression);
                return node;
            }

            // MaterializeCollectionNavigationExpression 也是 Extension
            if (node is MaterializeCollectionNavigationExpression mc)
            {
                // mc.Subquery 里也可能有 IncludeExpression
                Visit(mc.Subquery);
                return node;
            }

            return base.VisitExtension(node);
        }
    }

// ---------- MAIN: QueryRows pipeline compiler ----------
    private Expression CompileQueryRowsPipeline(
        CustomMemoryQueryExpression q)
    {
        // We only support "sequence" queries here (no scalar terminals) for now.
        // if (q.TerminalOperator != CustomMemoryTerminalOperator.None)
        //     throw new NotSupportedException("QueryRows pipeline: terminal operators not migrated yet.");

        var entityType = q.EntityType;
        var clrType = entityType.ClrType;

        // 1) Build source: IMemoryTable<TEntity>.QueryRows  (IQueryable<SnapshotRow>)
        var tableExpr = BuildTableExpression(clrType);
        var sourceRowsExpr =
            BuildQueryRowsExpression(tableExpr, clrType); // returns Expression of IQueryable<SnapshotRow>

        // 2) SnapshotRow -> tracked entity (identity resolution happens here)
        var qcParam = QueryCompilationContext.QueryContextParameter;

        // 2) SnapshotRow -> ValueBuffer (NO entity creation here)
        var rowParam = Expression.Parameter(typeof(SnapshotRow), "row");

        // var trackOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
        //     .GetMethodÏ(nameÏof(TrackFromRow), BindingFlags.Static | BindingFlags.NonPublic)!;
        // var trackClosed = trackOpen.MakeGenericMethod(clrType);
        var rowToEntity = GetRowMaterializerMethod(clrType);

        var trackCall = Expression.Call(
            rowToEntity,
            qcParam,
            Expression.Constant(entityType, typeof(IEntityType)),
            rowParam,
            Expression.Constant(_vbFactory),
            Expression.Constant(_materializerSource)
        );

        var selectorType = typeof(Func<,>).MakeGenericType(typeof(SnapshotRow), clrType);
        var selector = Expression.Lambda(selectorType, trackCall, rowParam);

        var selectRowsClosed = QueryableSelect_NoIndex().MakeGenericMethod(typeof(SnapshotRow), clrType);

        Expression sourceExpr = Expression.Call(selectRowsClosed, sourceRowsExpr, Expression.Quote(selector));
        var currentElementType = clrType;

        // 3) Replay steps (Where/OrderBy/SelectMany/...) over IQueryable<TEntity>
        //    IMPORTANT: any place you build an "inner table query" must be QueryRows-based too,
        //    otherwise you'll still increment QueryCalled via fallback.
        var rewriter = new QueryParameterRewritingVisitor(qcParam);
        var efPropRewriter = new EfPropertyRewritingVisitor();
        var entityRootRewriter = new EntityQueryRootRewritingVisitor(
            qcParam,
            (clrType, qcExpr, efEntityType)
                => BuildQueryRowsEntityQueryable(clrType, qcExpr, efEntityType));

        foreach (var step in q.Steps)
        {
            switch (step)
            {
                case WhereStep w:
                {
                    var whereOpen = QueryableWhere_NoIndex();
                    var whereClosed = whereOpen.MakeGenericMethod(currentElementType);

                    var body = efPropRewriter.Visit(rewriter.Visit(w.Predicate.Body)!)!;
                    var lam = Expression.Lambda(w.Predicate.Type, body, w.Predicate.Parameters);

                    sourceExpr = Expression.Call(whereClosed, sourceExpr, Expression.Quote(lam));
                    break;
                }

                case OrderStep o:
                {
                    string methodName = o.ThenBy
                        ? (o.Descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy))
                        : (o.Descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy));

                    var expectedSourceOpenType = o.ThenBy ? typeof(IOrderedQueryable<>) : typeof(IQueryable<>);

                    var open = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == methodName
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 2
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == expectedSourceOpenType);

                    var keyBody = efPropRewriter.Visit(rewriter.Visit(o.KeySelector.Body)!)!;
                    var keyLam = Expression.Lambda(o.KeySelector.Type, keyBody, o.KeySelector.Parameters);

                    var closed = open.MakeGenericMethod(currentElementType, keyLam.ReturnType);
                    sourceExpr = Expression.Call(closed, sourceExpr, Expression.Quote(keyLam));
                    break;
                }

                case SkipStep sk:
                {
                    var skipOpen = QueryableSkip_NoIndex();

                    var skipClosed = skipOpen.MakeGenericMethod(currentElementType);

                    var countExpr = rewriter.Visit(sk.Count)!;
                    if (countExpr.Type != typeof(int))
                        countExpr = Expression.Convert(countExpr, typeof(int));

                    sourceExpr = Expression.Call(skipClosed, sourceExpr, countExpr);
                    break;
                }

                case TakeStep tk:
                {
                    var takeOpen = QueryableTake_NoIndex();

                    var takeClosed = takeOpen.MakeGenericMethod(currentElementType);

                    var countExpr = rewriter.Visit(tk.Count)!;
                    if (countExpr.Type != typeof(int))
                        countExpr = Expression.Convert(countExpr, typeof(int));

                    sourceExpr = Expression.Call(takeClosed, sourceExpr, countExpr);
                    break;
                }

                case SelectStep s:
                {
                    // NOTE: Select is used for projection; identity resolution already happened above.
                    var selectOpen = QueryableSelect_NoIndex();

                    var rewrittenBody =
                        entityRootRewriter.Visit(efPropRewriter.Visit(rewriter.Visit(s.Selector.Body)!)!)!;
                    var rewrittenSelector = Expression.Lambda(s.Selector.Type, rewrittenBody, s.Selector.Parameters);
                    bool hasInclude = FindInclude(rewrittenSelector.Body) != null;
                    if (hasInclude)
                    {
                        Expression RewriteSubquery(Expression expr)
                        {
                            expr = rewriter.Visit(expr)!;
                            expr = entityRootRewriter.Visit(expr)!;
                            expr = efPropRewriter.Visit(expr)!;

                            if (expr is ShapedQueryExpression sqe)
                                expr = VisitShapedQuery(sqe);
                            else if (expr is CustomMemoryQueryExpression cmqe)
                                expr = CompileQueryRowsPipeline(cmqe);
                            expr = IncludeInQueryRewriting.RewriteSelectLambdas(
                                expr,
                                QueryCompilationContext.QueryContextParameter,
                                RewriteSubquery);
                            // Console.WriteLine("[DBG] subqueryExpr = " + expr);

                            return expr;
                        }

                        static Type? TryGetElementType(Type t)
                        {
                            if (t.IsGenericType)
                            {
                                var def = t.GetGenericTypeDefinition();
                                if (def == typeof(IEnumerable<>) || def == typeof(IQueryable<>))
                                    return t.GetGenericArguments()[0];
                            }

                            var iface = t.GetInterfaces().FirstOrDefault(i =>
                                i.IsGenericType &&
                                (i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                 || i.GetGenericTypeDefinition() == typeof(IQueryable<>)));

                            return iface?.GetGenericArguments()[0];
                        }

                        rewrittenSelector = IncludeRewriting.RewriteIncludeSelector(
                            rewrittenSelector,
                            QueryCompilationContext.QueryContextParameter,
                            RewriteSubquery);
                    }

                    // 3) Select
                    var selectClosed = selectOpen.MakeGenericMethod(currentElementType, rewrittenSelector.ReturnType);
                    sourceExpr = Expression.Call(selectClosed, sourceExpr, Expression.Quote(rewrittenSelector));
                    currentElementType = rewrittenSelector.ReturnType;
                    break;
                }

                case SelectManyStep sm:
                {
                    // This is the key for your smoke test:
                    // the collectionSelector is usually a correlated subquery over ctx.Set<PostComment>(),
                    // so EntityQueryRootRewritingVisitor MUST rewrite it to QueryRows-based inner source.
                    var selectManyOpen = QueryableSelectMany_CollectionSelector_NoIndex();

                    Expression Rewrite(Expression expr)
                    {
                        expr = rewriter.Visit(expr)!;
                        expr = entityRootRewriter.Visit(expr)!;
                        expr = efPropRewriter.Visit(expr)!;
                        return expr;
                    }

                    var outerType = currentElementType;

                    // collectionSelector: Func<TOuter, IEnumerable<TCollection>>
                    var colBody = Rewrite(sm.CollectionSelector.Body);
                    var colElemType = TryGetIEnumerableElementType(colBody.Type)
                                      ?? throw new InvalidOperationException(
                                          $"SelectMany collectionSelector must return IEnumerable<T>. Type={colBody.Type}");

                    var ienumCol = typeof(IEnumerable<>).MakeGenericType(colElemType);
                    if (colBody.Type != ienumCol)
                        colBody = Expression.Convert(colBody, ienumCol);

                    var colDel = typeof(Func<,>).MakeGenericType(outerType, ienumCol);
                    var colLam = Expression.Lambda(colDel, colBody, sm.CollectionSelector.Parameters);

                    // resultSelector: Func<TOuter, TCollection, TResult>
                    var resBody = Rewrite(sm.ResultSelector.Body);
                    var resType = resBody.Type;

                    var resDel = typeof(Func<,,>).MakeGenericType(outerType, colElemType, resType);
                    var resLam = Expression.Lambda(resDel, resBody, sm.ResultSelector.Parameters);

                    var selectManyClosed = selectManyOpen.MakeGenericMethod(outerType, colElemType, resType);

                    sourceExpr = Expression.Call(selectManyClosed, sourceExpr, Expression.Quote(colLam),
                        Expression.Quote(resLam));
                    currentElementType = resType;
                    break;
                }

                case LeftJoinStep lj:
                {
                    // OUTER = currentElementType
                    var outerType = currentElementType;

                    // EF already tells you TInner from lambda parameter type
                    var innerType = lj.InnerKeySelector.Parameters[0].Type;

                    // Build inner IQueryable<TInner> from your stored CustomMemoryQueryExpression
                    // (recursively: root + its steps)
                    var innerSourceExpr = BuildQueryableFromQueryExpression(
                        lj.InnerQuery,
                        innerType,
                        qcParam,
                        rewriter,
                        entityRootRewriter,
                        efPropRewriter);

                    // Rewrite key selectors + result selector (parameterization + root rewrite + EF.Property)
                    var outerKey = RewriteLambda(
                        lj.OuterKeySelector,
                        rewriter,
                        entityRootRewriter,
                        efPropRewriter);

                    var innerKey = RewriteLambda(
                        lj.InnerKeySelector,
                        rewriter,
                        entityRootRewriter,
                        efPropRewriter);

                    var resultSel = RewriteLambda(
                        lj.ResultSelector,
                        rewriter,
                        entityRootRewriter,
                        efPropRewriter);

                    var keyType = outerKey.ReturnType; // should match innerKey.ReturnType

                    // IMPORTANT: Queryable has no LeftJoin. Implement LeftJoin via GroupJoin + SelectMany + DefaultIfEmpty.
                    //
                    // outer.GroupJoin(inner, ok, ik, (o, inners) => (o, inners))
                    //      .SelectMany(t => inners.DefaultIfEmpty(), (t, i) => resultSelector(t.o, i))

                    // ---- GroupJoin ----
                    var tupleType = typeof(ValueTuple<,>).MakeGenericType(outerType,
                        typeof(IEnumerable<>).MakeGenericType(innerType));
                    var tupleCtor = tupleType.GetConstructor(new[]
                    {
                        outerType,
                        typeof(IEnumerable<>).MakeGenericType(innerType)
                    })!;

                    var oParam = Expression.Parameter(outerType, "o");
                    var innersParam = Expression.Parameter(typeof(IEnumerable<>).MakeGenericType(innerType), "inners");

                    var groupJoinResultSelector = Expression.Lambda(
                        Expression.New(tupleCtor, oParam, innersParam),
                        oParam,
                        innersParam);

                    var ms = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == nameof(Queryable.GroupJoin))
                        .ToList();

                    var groupJoinOpen = GetQueryableGroupJoin(withComparer: false);
                    var groupJoin = groupJoinOpen.MakeGenericMethod(outerType, innerType, keyType, tupleType);
                    var afterGroupJoin = Expression.Call(
                        groupJoin,
                        sourceExpr, // outer source (IQueryable<TOuter>)
                        innerSourceExpr, // inner source (IQueryable<TInner>)
                        Expression.Quote(outerKey), // outer key
                        Expression.Quote(innerKey), // inner key
                        Expression.Quote(groupJoinResultSelector));

                    // ---- SelectMany over (o, inners) with DefaultIfEmpty ----
                    // collectionSelector: t => ((IEnumerable<TInner>)t.Item2).DefaultIfEmpty()
                    var tParam = Expression.Parameter(tupleType, "t");

                    var item1 = GetValueTupleItem(tParam, 1); // outer
                    var item2 = GetValueTupleItem(tParam, 2); // IEnumerable<TInner>

                    var defaultIfEmptyOpen = typeof(Enumerable).GetMethods()
                        .Single(m => m.Name == nameof(Enumerable.DefaultIfEmpty)
                                     && m.IsGenericMethodDefinition
                                     && m.GetParameters().Length == 1);
                    var defaultIfEmpty = defaultIfEmptyOpen.MakeGenericMethod(innerType);

                    var defIfEmptyCall = Expression.Call(defaultIfEmpty, item2);
                    var collectionSelector = Expression.Lambda(
                        defIfEmptyCall, // IEnumerable<TInner>
                        tParam);

                    // resultSelector: (t, i) => resultSel(t.Item1, i)
                    var iParam = Expression.Parameter(innerType, "i");

                    // resultSel is LambdaExpression (outer, inner) => TResult
                    var invoked = Expression.Invoke(resultSel, item1, iParam);
                    var resSelector = Expression.Lambda(invoked, tParam, iParam);

                    var selectManyOpen = QueryableSelectMany_CollectionSelector_NoIndex();
                    var selectMany = selectManyOpen.MakeGenericMethod(tupleType, innerType, invoked.Type);

                    sourceExpr = Expression.Call(
                        selectMany,
                        afterGroupJoin,
                        Expression.Quote(collectionSelector),
                        Expression.Quote(resSelector));

                    currentElementType = invoked.Type;
                    // Console.WriteLine("=== [DBG] LEFTJOIN AFTER sourceExpr ===");
                    // Console.WriteLine(sourceExpr.ToString());

                    var ext = FindFirstExtension(sourceExpr);
                    if (ext != null)
                    {
                        Console.WriteLine(
                            $"[DBG] FIRST EXT NODE: {ext.GetType().FullName}, CanReduce={ext.CanReduce}, Type={ext.Type}");
                    }

                    break;
                }
                default:
                    throw new NotSupportedException($"QueryRows pipeline: step '{step.Kind}' not migrated.");
            }
        }

        var elementType = sourceExpr.Type.GetGenericArguments().Single();
        Expression body1 = sourceExpr;

        switch (q.TerminalOperator)
        {
            case CustomMemoryTerminalOperator.None:
                break;

            case CustomMemoryTerminalOperator.Any:
            {
                if (q.TerminalPredicate == null)
                {
                    var anyOpen = GetQueryableMethod(nameof(Queryable.Any), genericArgCount: 1, 1); // ✅只允许1参
                    var any = anyOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(any, body1);
                }
                else
                {
                    var anyOpen = GetQueryableMethod(nameof(Queryable.Any), genericArgCount: 1, 2); // ✅只允许2参
                    var any = anyOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(any, body1, Expression.Quote(q.TerminalPredicate));
                }

                break;
            }

            case CustomMemoryTerminalOperator.Single:
            {
                if (q.TerminalPredicate == null)
                {
                    var singleOpen = GetQueryableMethod(nameof(Queryable.Single), genericArgCount: 1, 1);
                    var single = singleOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(single, body1);
                }
                else
                {
                    var singleOpen = GetQueryableMethod(nameof(Queryable.Single), genericArgCount: 1, 2);
                    var single = singleOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(single, body1, Expression.Quote(q.TerminalPredicate));
                }

                break;
            }

            case CustomMemoryTerminalOperator.SingleOrDefault:
            {
                if (q.TerminalPredicate == null)
                {
                    // 选 1 参数版本：SingleOrDefault<T>(IQueryable<T>)
                    var open = GetQueryableMethodExact(
                        nameof(Queryable.SingleOrDefault),
                        genericArgCount: 1,
                        m =>
                        {
                            var ps = m.GetParameters();
                            return ps.Length == 1 && IsQueryableOfT(ps[0]);
                        });

                    var closed = open.MakeGenericMethod(elementType);
                    body1 = Expression.Call(closed, body1);
                }
                else
                {
                    // 选 predicate 版本：SingleOrDefault<T>(IQueryable<T>, Expression<Func<T,bool>>)
                    var open = GetQueryableMethodExact(
                        nameof(Queryable.SingleOrDefault),
                        genericArgCount: 1,
                        m =>
                        {
                            var ps = m.GetParameters();
                            return ps.Length == 2 && IsQueryableOfT(ps[0]) && IsPredicateExpression(ps[1]);
                        });

                    var closed = open.MakeGenericMethod(elementType);
                    body1 = Expression.Call(closed, body1, Expression.Quote(q.TerminalPredicate));
                }

                break;
            }

            case CustomMemoryTerminalOperator.First:
            {
                if (q.TerminalPredicate == null)
                {
                    var firstOpen = GetQueryableMethod(nameof(Queryable.First), genericArgCount: 1, 1);
                    var first = firstOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(first, body1);
                }
                else
                {
                    var firstOpen = GetQueryableMethod(nameof(Queryable.First), genericArgCount: 1, 2);
                    var first = firstOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(first, body1, Expression.Quote(q.TerminalPredicate));
                }

                break;
            }

            case CustomMemoryTerminalOperator.FirstOrDefault:
            {
                if (q.TerminalPredicate == null)
                {
                    // ✅ 只选 1 参的 FirstOrDefault(source)
                    var fodOpen = GetQueryableMethod(nameof(Queryable.FirstOrDefault), genericArgCount: 1, 1);
                    var fod = fodOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(fod, body1);
                }
                else
                {
                    // ✅ 明确选 predicate 的 2 参 overload
                    var fod = GetQueryableFirstOrDefaultWithPredicate(elementType); // 已 close
                    body1 = Expression.Call(fod, body1, Expression.Quote(q.TerminalPredicate));
                }

                break;
            }

            case CustomMemoryTerminalOperator.Count:
            {
                if (q.TerminalPredicate == null)
                {
                    var countOpen = GetQueryableMethod(nameof(Queryable.Count), genericArgCount: 1, 1); // 只选 1 参
                    var count = countOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(count, body1);
                }
                else
                {
                    var countOpen = GetQueryableMethod(nameof(Queryable.Count), genericArgCount: 1, 2); // 只选 2 参
                    var count = countOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(count, body1, Expression.Quote(q.TerminalPredicate));
                }

                break;
            }

            case CustomMemoryTerminalOperator.LongCount:
            {
                if (q.TerminalPredicate == null)
                {
                    var lcOpen = GetQueryableMethod(nameof(Queryable.LongCount), genericArgCount: 1, 1);
                    var lc = lcOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(lc, body1);
                }
                else
                {
                    var lcOpen = GetQueryableMethod(nameof(Queryable.LongCount), genericArgCount: 1, 2);
                    var lc = lcOpen.MakeGenericMethod(elementType);
                    body1 = Expression.Call(lc, body1, Expression.Quote(q.TerminalPredicate));
                }

                break;
            }
            case CustomMemoryTerminalOperator.Min:
            {
                if (q.TerminalSelector == null)
                {
                    // Min(source)
                    var open = GetQueryableMethod(nameof(Queryable.Min), genericArgCount: 1, 1, 2); // 1参 or comparer版2参
                    var closed = open.MakeGenericMethod(elementType);
                    body1 = Expression.Call(closed, body1);
                }
                else
                {
                    // Min(source, selector)
                    // selector 的返回类型就是 TResult
                    var valueType = q.TerminalSelector.ReturnType; // LambdaExpression.ReturnType

                    var min = GetQueryableMinWithSelector(elementType, valueType); // 已经 close 完毕
                    body1 = Expression.Call(min, body1, Expression.Quote(q.TerminalSelector));
                }

                break;
            }
            case CustomMemoryTerminalOperator.Max:
            {
                if (q.TerminalSelector == null)
                {
                    // Max(source) or Max(source, comparer)
                    var open = GetQueryableMethod(nameof(Queryable.Max), genericArgCount: 1, 1, 2);
                    var closed = open.MakeGenericMethod(elementType);
                    body1 = Expression.Call(closed, body1);
                }
                else
                {
                    // Max(source, selector)
                    var valueType = q.TerminalSelector.ReturnType;
                    var max = GetQueryableMaxWithSelector(elementType, valueType); // 已 close
                    body1 = Expression.Call(max, body1, Expression.Quote(q.TerminalSelector));
                }

                break;
            }
            case CustomMemoryTerminalOperator.All:
            {
                if (q.TerminalPredicate == null)
                    throw new Exception("All() requires a predicate, but TerminalPredicate is null.");

                var all = GetQueryableAll(elementType);
                var pred = q.TerminalPredicate ?? throw new ArgumentNullException(nameof(q.TerminalPredicate));
                var predBody = efPropRewriter.Visit(rewriter.Visit(pred.Body));
                var preLam = Expression.Lambda(predBody, pred.Parameters);
                body1 = Expression.Call(all, body1, Expression.Quote(preLam));
                break;
            }

            case CustomMemoryTerminalOperator.Sum:
            {
                // Sum 必须有 selector（EF 生成的聚合基本都是带 selector 的）
                if (q.TerminalSelector == null)
                    throw new NotSupportedException("Sum without selector is not supported (expected Sum(x => ...)).");

                var selector1 = q.TerminalSelector; // LambdaExpression, e.g. x => (long)x.Id

                // ✅ selector body 也要 rewrite（捕获变量、EF.Property 等）
                var selBody = efPropRewriter.Visit(rewriter.Visit(selector1.Body)!)!;
                var selLam = Expression.Lambda(selector1.Type, selBody, selector1.Parameters);

                // selector.ReturnType 决定 Sum 返回类型 / overload
                var sumOpen = GetQueryableSumWithSelector(elementType, selector1.ReturnType);
                var sum = sumOpen
                    .MakeGenericMethod(
                        elementType); // Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, ...>>)

                body1 = Expression.Call(sum, body1, Expression.Quote(selLam));
                break;
            }

            case CustomMemoryTerminalOperator.Average:
            {
                if (q.TerminalSelector == null)
                    throw new NotSupportedException(
                        "Average without selector is not supported (expected Average(x => ...)).");

                var selector2 = q.TerminalSelector; // LambdaExpression: x => (double)x.Id / or x => x.Id

                // ✅ selector 也要 rewrite（EF.Property / captured variables）
                var selBody = efPropRewriter.Visit(rewriter.Visit(selector2.Body)!)!;
                var selLam = Expression.Lambda(selector2.Type, selBody, selector2.Parameters);

                var avgOpen = GetQueryableAverageWithSelector(elementType, selector2.ReturnType);
                var avg = avgOpen.MakeGenericMethod(elementType);

                body1 = Expression.Call(avg, body1, Expression.Quote(selLam));
                break;
            }

            default:
                throw new NotSupportedException($"Terminal '{q.TerminalOperator}' not supported yet.");
        }

        return body1;

        static Type? TryGetIEnumerableElementType(Type t)
        {
            if (t == typeof(string)) return null;
            if (t.IsArray) return t.GetElementType();

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t.GetGenericArguments()[0];

            var iface = t.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return iface?.GetGenericArguments()[0];
        }
    }

    private static MethodInfo GetQueryableGroupJoin(bool withComparer)
    {
        var methods = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.GroupJoin))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 4)
            .ToList();

        int expectedParams = withComparer ? 6 : 5;

        MethodInfo? match = null;

        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            if (ps.Length != expectedParams) continue;

            // 0) IQueryable<TOuter>
            if (!IsGeneric(ps[0].ParameterType, typeof(IQueryable<>))) continue;

            // 1) IEnumerable<TInner>  <-- 这就是你通用 GetQueryableMethod 常常过滤掉的点
            if (!IsGeneric(ps[1].ParameterType, typeof(IEnumerable<>))) continue;

            // 2) Expression<Func<TOuter, TKey>>
            if (!IsExpressionOfFunc(ps[2].ParameterType, 2)) continue;

            // 3) Expression<Func<TInner, TKey>>
            if (!IsExpressionOfFunc(ps[3].ParameterType, 2)) continue;

            // 4) Expression<Func<TOuter, IEnumerable<TInner>, TResult>>
            if (!IsExpressionOfFunc(ps[4].ParameterType, 3)) continue;

            // 5) IEqualityComparer<TKey>
            if (withComparer && !IsGeneric(ps[5].ParameterType, typeof(IEqualityComparer<>))) continue;

            if (match != null)
                throw new InvalidOperationException($"GroupJoin overload resolution ambiguous: {match} vs {m}");

            match = m;
        }

        if (match == null)
            throw new InvalidOperationException($"Queryable.GroupJoin overload not found. withComparer={withComparer}");

        return match;

        static bool IsGeneric(Type t, Type openGeneric)
            => t.IsGenericType && t.GetGenericTypeDefinition() == openGeneric;

        static bool IsExpressionOfFunc(Type t, int funcArgCount)
        {
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(Expression<>)) return false;

            var inner = t.GetGenericArguments()[0];
            if (!inner.IsGenericType) return false;

            var def = inner.GetGenericTypeDefinition();
            if (def != typeof(Func<,>) && def != typeof(Func<,,>)) return false;

            return inner.GetGenericArguments().Length == funcArgCount;
        }
    }

    private static MethodInfo QueryableTake_NoIndex()
    {
        // IQueryable<TSource> Take<TSource>(IQueryable<TSource>, int)
        var m = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(x => x.Name == nameof(Queryable.Take) && x.IsGenericMethodDefinition)
            .Where(x => x.GetGenericArguments().Length == 1)
            .Single(x =>
            {
                var ps = x.GetParameters();
                if (ps.Length != 2) return false;

                // param0: IQueryable<TSource>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // param1: int
                return ps[1].ParameterType == typeof(int);
            });

        return m;
    }

    private static LambdaExpression RewriteLambda(
        LambdaExpression lam,
        QueryParameterRewritingVisitor rewriter,
        EntityQueryRootRewritingVisitor entityRootRewriter,
        EfPropertyRewritingVisitor efPropRewriter)
    {
        // Keep original parameters, rewrite body only
        var body = lam.Body;
        body = rewriter.Visit(body)!;
        body = entityRootRewriter.Visit(body)!;
        body = efPropRewriter.Visit(body)!;
        body = AlignLambdaBodyToReturnType(body, lam.ReturnType);

        return Expression.Lambda(lam.Type, body, lam.Parameters);
    }

    private static Expression AlignLambdaBodyToReturnType(Expression body, Type returnType)
    {
        if (body.Type == returnType) return body;

        // direct assignable
        if (returnType.IsAssignableFrom(body.Type))
            return Expression.Convert(body, returnType);

        // int -> int? 这类：T -> Nullable<T>
        var nullableUnderlying = Nullable.GetUnderlyingType(returnType);
        if (nullableUnderlying != null && body.Type == nullableUnderlying)
            return Expression.Convert(body, returnType);

        // 一般 numeric widening（可选：你也可以先不做，遇到再加）
        // if (body.Type.IsValueType && returnType.IsValueType)
        //     return Expression.Convert(body, returnType);

        throw new InvalidOperationException(
            $"Cannot align lambda body type {body.Type} to return type {returnType}.");
    }

    private static Expression GetValueTupleItem(Expression tuple, int index /*1-based*/)
    {
        // ValueTuple uses fields: Item1, Item2, ...
        var name = "Item" + index;
        var field = tuple.Type.GetField(name);
        if (field == null)
            throw new InvalidOperationException($"Field '{name}' not found on {tuple.Type}.");
        return Expression.Field(tuple, field);
    }

    /// <summary>
    /// Build an IQueryable expression for a (possibly nested) CustomMemoryQueryExpression:
    /// 1) Build the root IQueryable<TEntity> using QueryRows+Track (EF identity map reuse)
    /// 2) Apply q.Steps in order to produce the final IQueryable
    /// </summary>
    private Expression BuildQueryableFromQueryExpression(
        CustomMemoryQueryExpression q,
        Type elementType,
        ParameterExpression qcParam,
        QueryParameterRewritingVisitor rewriter,
        EntityQueryRootRewritingVisitor entityRootRewriter,
        EfPropertyRewritingVisitor efPropRewriter)
    {
        // 1) Build root queryable (IQueryable<TEntity>) using your existing root builder.
        // IMPORTANT: this must return IQueryable of elementType (not non-generic IQueryable).
        // Assumption: q has an IEntityType (EF metadata) for elementType.
        Expression sourceExpr = BuildQueryRowsEntityQueryable(elementType, qcParam, q.EntityType);
        var currentElementType = elementType;

        // 2) Apply steps (same order EF produced)
        foreach (var step in q.Steps)
        {
            // Reuse your existing switch for Where/OrderBy/Select/SelectMany/Take/Skip etc.
            // Only difference: we must call Rewrite(...) on step lambdas/bodies.
            switch (step)
            {
                case WhereStep w:
                {
                    var whereOpen = QueryableWhere_NoIndex();
                    var whereClosed = whereOpen.MakeGenericMethod(currentElementType);

                    var body = w.Predicate.Body;
                    body = rewriter.Visit(body)!;
                    body = entityRootRewriter.Visit(body)!;
                    body = efPropRewriter.Visit(body)!;

                    var lam = Expression.Lambda(w.Predicate.Type, body, w.Predicate.Parameters);

                    sourceExpr = Expression.Call(whereClosed, sourceExpr, Expression.Quote(lam));
                    break;
                }

                case OrderStep ob:
                {
                    var keyBody = ob.KeySelector.Body;
                    keyBody = rewriter.Visit(keyBody)!;
                    keyBody = entityRootRewriter.Visit(keyBody)!;
                    keyBody = efPropRewriter.Visit(keyBody)!;

                    var keyLam = Expression.Lambda(ob.KeySelector.Type, keyBody, ob.KeySelector.Parameters);
                    var keyType = keyLam.ReturnType;

                    var name = ob.Descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
                    var orderOpen = GetQueryableMethod(name, genericArgCount: 2, 2);
                    var orderClosed = orderOpen.MakeGenericMethod(currentElementType, keyType);

                    sourceExpr = Expression.Call(orderClosed, sourceExpr, Expression.Quote(keyLam));
                    break;
                }

                case SelectStep s:
                {
                    var body = s.Selector.Body;
                    body = rewriter.Visit(body)!;
                    body = entityRootRewriter.Visit(body)!;
                    body = efPropRewriter.Visit(body)!;

                    var lam = Expression.Lambda(s.Selector.Type, body, s.Selector.Parameters);

                    var resultType = lam.ReturnType;

                    var selectOpen = QueryableSelect_NoIndex();
                    var selectClosed = selectOpen.MakeGenericMethod(currentElementType, resultType);

                    sourceExpr = Expression.Call(selectClosed, sourceExpr, Expression.Quote(lam));
                    currentElementType = resultType;
                    break;
                }

                case SelectManyStep sm:
                {
                    Expression Rewrite(Expression expr)
                    {
                        expr = rewriter.Visit(expr)!;
                        expr = entityRootRewriter.Visit(expr)!;
                        expr = efPropRewriter.Visit(expr)!;
                        return expr;
                    }

                    var outerType = currentElementType;

                    // collectionSelector: Func<TOuter, IEnumerable<TCollection>>
                    var colBody = Rewrite(sm.CollectionSelector.Body);
                    var colElemType = TryGetIEnumerableElementType(colBody.Type)
                                      ?? throw new InvalidOperationException(
                                          $"SelectMany collectionSelector must return IEnumerable<T>. Type={colBody.Type}");

                    var ienumCol = typeof(IEnumerable<>).MakeGenericType(colElemType);
                    if (colBody.Type != ienumCol)
                        colBody = Expression.Convert(colBody, ienumCol);

                    var colDel = typeof(Func<,>).MakeGenericType(outerType, ienumCol);
                    var colLam = Expression.Lambda(colDel, colBody, sm.CollectionSelector.Parameters);

                    // resultSelector: Func<TOuter, TCollection, TResult>
                    var resBody = Rewrite(sm.ResultSelector.Body);
                    var resType = resBody.Type;

                    var resDel = typeof(Func<,,>).MakeGenericType(outerType, colElemType, resType);
                    var resLam = Expression.Lambda(resDel, resBody, sm.ResultSelector.Parameters);

                    var selectManyOpen = GetQueryableMethod(nameof(Queryable.SelectMany), genericArgCount: 3, 3);
                    var selectManyClosed = selectManyOpen.MakeGenericMethod(outerType, colElemType, resType);

                    sourceExpr = Expression.Call(selectManyClosed, sourceExpr, Expression.Quote(colLam),
                        Expression.Quote(resLam));
                    currentElementType = resType;
                    break;
                }

                case LeftJoinStep lj:
                {
                    // The LeftJoin step can appear inside a nested query too, so we just reuse the main case.
                    // Easiest: call a local helper that uses the same code as the main switch case.
                    sourceExpr = ApplyLeftJoinStep(
                        sourceExpr,
                        currentElementType,
                        lj,
                        qcParam,
                        rewriter,
                        entityRootRewriter,
                        efPropRewriter,
                        out currentElementType);
                    break;
                }

                default:
                    throw new NotSupportedException(
                        $"Nested query step '{step.Kind}' not supported in BuildQueryableFromQueryExpression yet.");
            }
        }

        return sourceExpr;
    }

    private static Type? TryGetIEnumerableElementType(Type type)
    {
        // string implements IEnumerable<char> but is NOT a collection in LINQ sense
        if (type == typeof(string))
            return null;

        // IEnumerable<T>
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        // IQueryable<T>
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(IQueryable<>))
        {
            return type.GetGenericArguments()[0];
        }

        // Look for IEnumerable<T> on interfaces
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType &&
                iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private Expression ApplyLeftJoinStep(
        Expression sourceExpr,
        Type currentElementType,
        LeftJoinStep lj,
        ParameterExpression qcParam,
        QueryParameterRewritingVisitor rewriter,
        EntityQueryRootRewritingVisitor entityRootRewriter,
        EfPropertyRewritingVisitor efPropRewriter,
        out Type newElementType)
    {
        var outerType = currentElementType;
        var innerType = lj.InnerKeySelector.Parameters[0].Type;

        var innerSourceExpr = BuildQueryableFromQueryExpression(
            lj.InnerQuery,
            innerType,
            qcParam,
            rewriter,
            entityRootRewriter,
            efPropRewriter);

        var outerKey = RewriteLambda(lj.OuterKeySelector, rewriter, entityRootRewriter, efPropRewriter);
        var innerKey = RewriteLambda(lj.InnerKeySelector, rewriter, entityRootRewriter, efPropRewriter);
        var resultSel = RewriteLambda(lj.ResultSelector, rewriter, entityRootRewriter, efPropRewriter);

        var keyType = outerKey.ReturnType;

        var tupleType =
            typeof(ValueTuple<,>).MakeGenericType(outerType, typeof(IEnumerable<>).MakeGenericType(innerType));
        var tupleCtor = tupleType.GetConstructor(new[]
        {
            outerType,
            typeof(IEnumerable<>).MakeGenericType(innerType)
        })!;

        var oParam = Expression.Parameter(outerType, "o");
        var innersParam = Expression.Parameter(typeof(IEnumerable<>).MakeGenericType(innerType), "inners");

        var groupJoinResultSelector = Expression.Lambda(
            Expression.New(tupleCtor, oParam, innersParam),
            oParam,
            innersParam);

        var groupJoinOpen = GetQueryableGroupJoin(withComparer: false);
        var groupJoin = groupJoinOpen.MakeGenericMethod(outerType, innerType, keyType, tupleType);

        var afterGroupJoin = Expression.Call(
            groupJoin,
            sourceExpr,
            innerSourceExpr,
            Expression.Quote(outerKey),
            Expression.Quote(innerKey),
            Expression.Quote(groupJoinResultSelector));

        var tParam = Expression.Parameter(tupleType, "t");
        var item1 = GetValueTupleItem(tParam, 1);
        var item2 = GetValueTupleItem(tParam, 2);

        var defaultIfEmptyOpen = typeof(Enumerable).GetMethods()
            .Single(m => m.Name == nameof(Enumerable.DefaultIfEmpty)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters().Length == 1);
        var defaultIfEmpty = defaultIfEmptyOpen.MakeGenericMethod(innerType);

        var defIfEmptyCall = Expression.Call(defaultIfEmpty, item2);
        var collectionSelector = Expression.Lambda(defIfEmptyCall, tParam);

        var iParam = Expression.Parameter(innerType, "i");
        var invoked = Expression.Invoke(resultSel, item1, iParam);
        var resSelector = Expression.Lambda(invoked, tParam, iParam);

        var selectManyOpen = GetQueryableMethod(nameof(Queryable.SelectMany), genericArgCount: 3, 3);
        var selectMany = selectManyOpen.MakeGenericMethod(tupleType, innerType, invoked.Type);

        newElementType = invoked.Type;
        return Expression.Call(selectMany, afterGroupJoin, Expression.Quote(collectionSelector),
            Expression.Quote(resSelector));
    }

    private static MethodInfo QueryableSkip_NoIndex()
    {
        // IQueryable<TSource> Skip<TSource>(IQueryable<TSource>, int)
        var m = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(x => x.Name == nameof(Queryable.Skip) && x.IsGenericMethodDefinition)
            .Where(x => x.GetGenericArguments().Length == 1)
            .Single(x =>
            {
                var ps = x.GetParameters();
                if (ps.Length != 2) return false;

                // param0: IQueryable<TSource>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // param1: int
                return ps[1].ParameterType == typeof(int);
            });

        return m;
    }

    private static MethodInfo GetQueryableAverageWithSelector(Type sourceType, Type valueType)
    {
        var candidates = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Average))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 1) // Average<TSource>(...)
            .Where(m => m.GetParameters().Length == 2) // (source, selector)
            .ToList();

        static Type? TryGetSelectorReturnType(MethodInfo m)
        {
            var p1 = m.GetParameters()[1].ParameterType; // Expression<Func<TSource, X>>
            if (!p1.IsGenericType) return null;

            var exprArg = p1.GetGenericArguments()[0]; // Func<TSource, X>
            if (!exprArg.IsGenericType) return null;

            var funcArgs = exprArg.GetGenericArguments();
            return funcArgs.Length == 2 ? funcArgs[1] : null; // X
        }

        // 精确匹配
        var matches = candidates
            .Where(m => TryGetSelectorReturnType(m) == valueType)
            .ToList();

        if (matches.Count == 1) return matches[0];

        // nullable-lift 兜底：int vs int? 等
        matches = candidates
            .Where(m =>
            {
                var rt = TryGetSelectorReturnType(m);
                if (rt == null) return false;

                if (rt == valueType) return true;

                var rtUnder = Nullable.GetUnderlyingType(rt);
                var vtUnder = Nullable.GetUnderlyingType(valueType);

                return (rtUnder != null && rtUnder == valueType)
                       || (vtUnder != null && vtUnder == rt);
            })
            .ToList();

        if (matches.Count == 1) return matches[0];

        throw new InvalidOperationException(
            $"Average(selector) overload not found/ambiguous. source={sourceType}, value={valueType}, matches={matches.Count}");
    }

    private static MethodInfo GetQueryableSumWithSelector(Type sourceType, Type valueType)
    {
        // Queryable.Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, X>>) 其中 X 是数值类型
        // 这个方法是泛型定义：genArgs=1 (TSource)，params=2
        var candidates = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Sum))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 1)
            .Where(m => m.GetParameters().Length == 2)
            .ToList();

        // 第二个参数类型形如 Expression<Func<TSource, X>>
        // 我们要匹配 X == valueType（考虑 Nullable 数值也要支持）
        static Type? TryGetSelectorReturnType(MethodInfo m)
        {
            var p1 = m.GetParameters()[1].ParameterType;
            if (!p1.IsGenericType) return null; // Expression<...>

            var exprArg = p1.GetGenericArguments()[0]; // Func<TSource, X>
            if (!exprArg.IsGenericType) return null;

            var funcArgs = exprArg.GetGenericArguments();
            if (funcArgs.Length != 2) return null;

            return funcArgs[1]; // X
        }

        var matches = candidates
            .Where(m =>
            {
                var rt = TryGetSelectorReturnType(m);
                return rt == valueType;
            })
            .ToList();

        if (matches.Count == 1) return matches[0];

        // 兜底：有时 EF 会给你 valueType = int，但 overload 是 Nullable<int> 或反过来
        // 这里做一个“可赋值/可提升”的宽松匹配
        matches = candidates
            .Where(m =>
            {
                var rt = TryGetSelectorReturnType(m);
                if (rt == null) return false;

                // exact or nullable-lift
                if (rt == valueType) return true;

                var rtUnder = Nullable.GetUnderlyingType(rt);
                var vtUnder = Nullable.GetUnderlyingType(valueType);

                return (rtUnder != null && rtUnder == valueType)
                       || (vtUnder != null && vtUnder == rt);
            })
            .ToList();

        if (matches.Count == 1) return matches[0];

        throw new InvalidOperationException(
            $"Sum(selector) overload not found/ambiguous. source={sourceType}, value={valueType}, matches={matches.Count}");
    }

    private static MethodInfo GetQueryableAll(Type elementType)
    {
        var m = typeof(Queryable).GetMethods()
            .Where(mi => mi.Name == nameof(Queryable.All))
            .Where(mi => mi.IsGenericMethodDefinition)
            .Where(mi => mi.GetGenericArguments().Length == 1)
            .Select(mi => new { mi, ps = mi.GetParameters() })
            .Where(x => x.ps.Length == 2)
            .Where(x => x.ps[0].ParameterType.IsGenericType
                        && x.ps[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            .Where(x => x.ps[1].ParameterType.IsGenericType
                        && x.ps[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>))
            .Select(x => x.mi)
            .Single();

        return m.MakeGenericMethod(elementType);
    }

    private static MethodInfo GetQueryableFirstOrDefaultWithPredicate(Type elementType)
    {
        var matches = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.FirstOrDefault))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 1)
            .Where(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length != 2) return false;

                // param0: IQueryable<T>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // param1: Expression<Func<T,bool>>  (排除 defaultValue: T)
                var p1 = ps[1].ParameterType;
                if (!p1.IsGenericType || p1.GetGenericTypeDefinition() != typeof(Expression<>)) return false;

                var del = p1.GetGenericArguments()[0];
                return del.IsGenericType
                       && del.GetGenericTypeDefinition() == typeof(Func<,>)
                       && del.GetGenericArguments()[1] == typeof(bool);
            })
            .ToList();

        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"FirstOrDefault(source,predicate) overload not found/ambiguous. matches={matches.Count}");

        return matches[0].MakeGenericMethod(elementType);
    }

    private static MethodInfo GetQueryableMaxWithSelector(Type sourceType, Type valueType)
    {
        var matches = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Max))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 2) // ✅ Max<TSource,TResult>
            .Where(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length != 2) return false;

                // IQueryable<TSource>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // Expression<Func<TSource, TResult>>
                var p1 = ps[1].ParameterType;
                if (!p1.IsGenericType || p1.GetGenericTypeDefinition() != typeof(Expression<>)) return false;

                var del = p1.GetGenericArguments()[0];
                if (!del.IsGenericType || del.GetGenericTypeDefinition() != typeof(Func<,>)) return false;

                return true;
            })
            .ToList();

        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"Max(selector) overload not found/ambiguous. source={sourceType}, value={valueType}, matches={matches.Count}");

        return matches[0].MakeGenericMethod(sourceType, valueType);
    }

    private static MethodInfo GetQueryableMinWithSelector(Type sourceType, Type valueType)
    {
        var matches = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Min))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 2) // ✅关键：2 个泛型参数
            .Where(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length != 2) return false;

                // IQueryable<TSource>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // Expression<Func<TSource, TResult>>
                var p1 = ps[1].ParameterType;
                if (!p1.IsGenericType || p1.GetGenericTypeDefinition() != typeof(Expression<>)) return false;

                var del = p1.GetGenericArguments()[0];
                if (!del.IsGenericType || del.GetGenericTypeDefinition() != typeof(Func<,>)) return false;

                var args = del.GetGenericArguments();
                if (args.Length != 2) return false;

                // 这里不用死盯 args[0]==sourceType，因为它是 open generic (TSource)，我们只需要确保是 Func<,>
                // 返回值类型也不用在 open method 上比对（同样是 TResult），close 之后自然匹配
                return true;
            })
            .ToList();

        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"Min(selector) overload not found/ambiguous. source={sourceType}, value={valueType}, matches={matches.Count}");

        // ✅ close 两个泛型参数
        return matches[0].MakeGenericMethod(sourceType, valueType);
    }

    private static void DumpQueryableMinOverloads()
    {
        var mins = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Min))
            .OrderBy(m => m.GetParameters().Length)
            .ThenBy(m => m.IsGenericMethodDefinition ? m.GetGenericArguments().Length : 0)
            .ToList();

        Console.WriteLine("---- Queryable.Min overloads ----");
        foreach (var m in mins)
        {
            var ga = m.IsGenericMethodDefinition ? m.GetGenericArguments().Length : 0;
            var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.ToString()));
            Console.WriteLine(
                $"  genDef={m.IsGenericMethodDefinition} genArgs={ga} params={m.GetParameters().Length}  {m.ReturnType} {m.Name}({ps})");
        }

        Console.WriteLine("---------------------------------");
    }

    private static MethodInfo GetQueryableNonGenericMin(Type elementType)
    {
        // Queryable.Min(IQueryable<int>) / IQueryable<int?> / IQueryable<decimal> ... 等
        // 这里用参数类型精确匹配：IQueryable<elementType>
        var wantedParam = typeof(IQueryable<>).MakeGenericType(elementType);

        var candidates = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Min))
            .Where(m => !m.IsGenericMethodDefinition)
            .Where(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 1 && ps[0].ParameterType == wantedParam;
            })
            .ToList();

        if (candidates.Count != 1)
            throw new InvalidOperationException(
                $"Min non-generic overload not found or ambiguous for elementType={elementType}. Matches={candidates.Count}");

        return candidates[0];
    }

// Build IMemoryTable<T>. instance expression
    private Expression BuildTableExpression(Type clrType)
    {
        var getTableOpen = _db.GetType().GetMethods()
            .Single(m =>
                m.Name == "GetTable"
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length == 1);

        var getTable = getTableOpen.MakeGenericMethod(clrType);

        return Expression.Call(
            Expression.Constant(_db),
            getTable,
            Expression.Constant(clrType, typeof(Type)));
    }

    private static IQueryable<SnapshotRow> GetQueryRowsFromTable(object table, Type clrType)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // 1) direct
        var prop = table.GetType().GetProperty("QueryRows", flags);
        if (prop != null)
            return (IQueryable<SnapshotRow>)prop.GetValue(table)!;

        // 2) interface (explicit impl)
        var ifaceProp = table.GetType()
            .GetInterfaces()
            .Select(i => i.GetProperty("QueryRows", flags))
            .FirstOrDefault(p => p != null);

        if (ifaceProp == null)
            throw new InvalidOperationException(
                $"Table<{clrType.Name}> has no QueryRows property (even on interfaces).");

        var getter = ifaceProp.GetGetMethod(true)!;
        return (IQueryable<SnapshotRow>)getter.Invoke(table, Array.Empty<object>())!;
    }

    private static readonly MethodInfo GetTableGenericDef =
        typeof(IMemoryDatabase)
            .GetMethods()
            .Single(m =>
                m.Name == nameof(IMemoryDatabase.GetTable)
                && m.IsGenericMethodDefinition // ✅只要 GetTable<TEntity>
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(Type)
                && m.ReturnType.IsGenericType
                && m.ReturnType.GetGenericTypeDefinition() == typeof(IMemoryTable<>));

    // Used by EntityQueryRootRewritingVisitor to replace ctx.Set<T>() roots:
    // it must return IQueryable<T> AND it must be QueryRows-based (to keep QueryCalled==0).
    private Expression BuildQueryRowsEntityQueryable(Type clrType, Expression qcExpr, IEntityType efEntityType)
    {
        // 1) tableExpr = _db.GetTable<TEntity>(typeof(TEntity))
        var tableExpr = BuildTableExpression(clrType);

        // 2) rowsExpr = tableExpr.QueryRows   (IQueryable<SnapshotRow>)
        var rowsExpr = BuildQueryRowsExpression(tableExpr, clrType);

        // 3) rowsExpr.Select(r => TrackFromRow<TEntity>((QueryContext)qcExpr, efEntityType, r, _vbFactory, _materializerSource))
        var rowParam = Expression.Parameter(typeof(SnapshotRow), "r");

        // var trackOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
        //     .GetMethod(nameof(TrackFromRow), BindingFlags.Static | BindingFlags.NonPublic)!;
        // var trackClosed = trackOpen.MakeGenericMethod(clrType);
        var rowToEntity = GetRowMaterializerMethod(clrType);

        // ✅ qcExpr 是 QueryCompilationContext.QueryContextParameter（类型就是 QueryContext）
        var trackCall = Expression.Call(
            rowToEntity,
            qcExpr, // 关键：不要 Expression.Constant(null) / 也不要 runtime qc
            Expression.Constant(efEntityType, typeof(IEntityType)),
            rowParam,
            Expression.Constant(_vbFactory),
            Expression.Constant(_materializerSource));

        var selector = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(typeof(SnapshotRow), clrType),
            trackCall,
            rowParam);

        var selectMethod = QueryableSelect_NoIndex().MakeGenericMethod(typeof(SnapshotRow), clrType);

        // 返回 IQueryable<clrType> 的表达式
        return Expression.Call(selectMethod, rowsExpr, Expression.Quote(selector));
    }

    private static void DumpQuerySteps(CustomMemoryQueryExpression q)
    {
        Console.WriteLine("=== [DBG] Dump CustomMemoryQueryExpression.Steps lambdas ===");

        var idx = 0;
        foreach (var step in q.Steps)
        {
            Console.WriteLine($"--- Step[{idx}] {step.Kind}  CLR={step.GetType().Name} ---");

            switch (step)
            {
                case SelectStep s:
                    Console.WriteLine("Selector (full): " + s.Selector);
                    ExprTreeDumper.Dump($"Step[{idx}].SelectStep.Selector.Body", s.Selector.Body);
                    break;

                case WhereStep w:
                    Console.WriteLine("Predicate (full): " + w.Predicate);
                    ExprTreeDumper.Dump($"Step[{idx}].WhereStep.Predicate.Body", w.Predicate.Body);
                    break;

                case OrderStep o:
                    Console.WriteLine("KeySelector (full): " + o.KeySelector);
                    ExprTreeDumper.Dump($"Step[{idx}].OrderStep.KeySelector.Body", o.KeySelector.Body);
                    break;

                // 需要的话你再加其它 Step（LeftJoinStep / SelectManyStep 之类）
                default:
                    Console.WriteLine("(no lambda dump for this step yet)");
                    break;
            }

            idx++;
        }

        Console.WriteLine("=== [DBG] Dump Steps END ===");
    }

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        // Console.WriteLine("=== [FULL DUMP] ShapedQueryExpression.QueryExpression (deep) ===");
        // DeepExpressionDumper.Dump(shapedQueryExpression.QueryExpression);
        //
        // Console.WriteLine("=== [FULL DUMP] ShapedQueryExpression.ShaperExpression (deep) ===");
        // DeepExpressionDumper.Dump(shapedQueryExpression.ShaperExpression);
        //
        // // 你也可以把整个 shapedQueryExpression 本身的公开属性打印一下（有时有用）
        // Console.WriteLine("=== [FULL DUMP] ShapedQueryExpression (surface) ===");
        // Console.WriteLine($"QueryExpression.Type={shapedQueryExpression.QueryExpression.Type}");
        // Console.WriteLine($"ShaperExpression.Type={shapedQueryExpression.ShaperExpression.Type}");

        if (shapedQueryExpression.QueryExpression is not CustomMemoryQueryExpression q)
        {
            throw new NotSupportedException(
                "CustomMemory provider can only compile CustomMemoryQueryExpression at the moment. " +
                "This query shape requires additional translation/compilation support.");
        }

        // DumpQuerySteps(q);

        // 先从 shaper 里提取 include 的导航名（在剥离之前）
        var includeNavs = ExtractIncludeNavigationNames(shapedQueryExpression.ShaperExpression);
        var includeNavArrayExpr = Expression.Constant(includeNavs.Distinct().ToArray(), typeof(string[]));
        // NEW: QueryRows-based path (incremental rollout)
        var executor = CompileQueryRowsPipeline(q);
        // executor = new IncludeStrippingVisitor().Visit(executor)!;
        executor = ApplyMarkLoadedWrapper(executor, QueryCompilationContext.QueryContextParameter, includeNavArrayExpr);

        return executor;
    }

    private static Expression ApplyMarkLoadedWrapper(
        Expression executor,
        ParameterExpression qcParam,
        ConstantExpression includeNavArrayExpr)
    {
        // 没 include 就别包
        if (includeNavArrayExpr.Value is string[] arr && arr.Length == 0)
            return executor;

        // 情况 A：executor 是 IQueryable<TEntity>（TerminalOperator.None）
        // 我们在 IQueryable 上加一个 Select(e => MarkNavigationsLoaded(qc, e, navs))
        if (executor.Type.IsGenericType && executor.Type.GetGenericTypeDefinition() == typeof(IQueryable<>))
        {
            var elementType = executor.Type.GetGenericArguments()[0];

            var eParam = Expression.Parameter(elementType, "e");

            var markOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                .GetMethod(nameof(MarkNavigationsLoaded), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(elementType);

            var markCall = Expression.Call(markOpen, qcParam, eParam, includeNavArrayExpr);

            var selector = Expression.Lambda(
                typeof(Func<,>).MakeGenericType(elementType, elementType),
                markCall,
                eParam);

            var select = QueryableSelect_NoIndex().MakeGenericMethod(elementType, elementType);

            return Expression.Call(select, executor, Expression.Quote(selector));
        }

        // 情况 B：executor 是 scalar（Single/First/... 已经在 CompileQueryRowsPipeline 里做了 terminal）
        // 我们把结果拿出来 mark 一下再返回
        // 注意：只有引用类型 entity 才有意义（否则不用包）
        if (!executor.Type.IsClass || executor.Type == typeof(string))
            return executor;

        var t = executor.Type;

        var markOpen2 = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
            .GetMethod(nameof(MarkNavigationsLoaded), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(t);

        // MarkNavigationsLoaded(qc, (T)executor, navs)
        var call2 = Expression.Call(markOpen2, qcParam, executor, includeNavArrayExpr);
        return call2;
    }

    private static TEntity MarkNavigationsLoaded<TEntity>(
        QueryContext qc,
        TEntity entity,
        string[] navNames)
        where TEntity : class
    {
        if (entity == null) return entity;

        // qc.Context 是当前 DbContext
        var ctx = qc.Context;

        var entry = ctx.Entry(entity);

        // Navigation(string) 对 reference / collection 都可以拿到 NavigationEntry（collection会是 CollectionEntry 派生）
        foreach (var nav in navNames)
        {
            entry.Navigation(nav).IsLoaded = true;
        }

        return entity;
    }

    private static IReadOnlyCollection<string> ExtractIncludeNavigationNames(Expression shaperExpression)
    {
        var v = new IncludeNavCollector();
        v.Visit(shaperExpression);
        return v.Names;
    }

    private sealed class IncludeNavCollector : ExpressionVisitor
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Names => _names;

        protected override Expression VisitExtension(Expression node)
        {
            // 1) 真正的 Include 节点：收集 navigation 名字，并继续往里走
            if (node is IncludeExpression ie)
            {
                // ie.Navigation 是 INavigationBase（包含 collection/ref）
                _names.Add(ie.Navigation.Name);

                // 继续遍历它内部（EntityExpression / NavigationExpression）
                Visit(ie.EntityExpression);
                Visit(ie.NavigationExpression);
                return node;
            }

            // 2) 其它 Extension：如果不能 Reduce，就“跳过”它（别 VisitChildren！）
            if (!node.CanReduce)
            {
                // 关键：这里不能 base.VisitExtension(node)
                // 因为 base 最终会走 VisitChildren -> 抛 must be reducible node
                return node;
            }

            // 3) 可 Reduce 的 Extension：Reduce 后再继续遍历
            return Visit(node.Reduce());
        }
    }

    private sealed class IncludeStrippingVisitor : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is IncludeExpression ie)
            {
                // 追踪 fixup 路线：我们只要 inner 被 materialize + tracked；
                // IncludeExpression 本身不能留到最终 expression tree。
                return Visit(ie.EntityExpression);
            }

            return base.VisitExtension(node);
        }
    }

// --- THIS is where EF identity resolution happens ---
    private static TEntity TrackFromRow<TEntity>(
        QueryContext qc,
        IEntityType entityType,
        SnapshotRow row,
        SnapshotValueBufferFactory factory,
        IEntityMaterializerSource materializerSource)
        where TEntity : class
    {
        // 0) make sure QueryContext has state manager
        qc.InitializeStateManager(standAlone: false);

        var primaryKey = entityType.FindPrimaryKey()
                         ?? throw new InvalidOperationException(
                             $"Entity '{entityType.DisplayName()}' has no primary key.");

        // 1) identity resolution: try get existing tracked entry
        var entry = qc.TryGetEntry(
            primaryKey,
            row.Key,
            throwOnNullKey: false,
            out var hasNullKey);

        if (entry != null)
        {
            ProviderDiagnostics.IdentityHit++;
            return (TEntity)entry.Entity;
        }

        ProviderDiagnostics.IdentityMiss++;
        // 2) materialize entity using EF materializer (so types/conversions match EF)
        var vb = factory.Create(entityType, row.Snapshot);

        var materializer = materializerSource.GetMaterializer(entityType);
        var mc = new MaterializationContext(vb, qc.Context);
        var instance = (TEntity)materializer(mc);

        // 3) original values snapshot must align to EF property order
        var originalValues = factory.CreateOriginalValuesSnapshot(entityType, row.Snapshot);

        // 4) start tracking from query (this is EF identity map insertion)
        var trackedEntry = qc.StartTracking(entityType, instance, originalValues);
        ProviderDiagnostics.StartTrackingCalled++;
        return (TEntity)trackedEntry.Entity;
    }

    private static MethodInfo QueryableSelect_NoIndex()
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

    private static MethodInfo QueryableWhere_NoIndex()
    {
        return typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.Where))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 1)
            .Single(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length != 2) return false;

                // ps[0] : IQueryable<TSource>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // ps[1] : Expression<Func<TSource, bool>>  (NOT Func<TSource,int,bool>)
                if (!ps[1].ParameterType.IsGenericType) return false;
                if (ps[1].ParameterType.GetGenericTypeDefinition() != typeof(Expression<>)) return false;

                var lambdaType = ps[1].ParameterType.GetGenericArguments()[0];
                if (!lambdaType.IsGenericType) return false;

                return lambdaType.GetGenericTypeDefinition() == typeof(Func<,>);
            });
    }

    private static MethodInfo QueryableSelectMany_CollectionSelector_NoIndex()
    {
        // SelectMany<TSource, TCollection, TResult>(
        //   IQueryable<TSource>,
        //   Expression<Func<TSource, IEnumerable<TCollection>>>,
        //   Expression<Func<TSource, TCollection, TResult>>)
        // 这里排除带 index 的 overload
        return typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Queryable.SelectMany))
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m => m.GetGenericArguments().Length == 3)
            .Single(m =>
            {
                var ps = m.GetParameters();
                if (ps.Length != 3) return false;

                // ps[0] IQueryable<TSource>
                if (!ps[0].ParameterType.IsGenericType) return false;
                if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                // ps[1] Expression<Func<TSource, IEnumerable<TCollection>>>
                if (!IsExpressionOfFunc(ps[1].ParameterType, funcArity: 2)) return false;
                var colRet = ps[1].ParameterType.GetGenericArguments()[0].GetGenericArguments()[1];
                if (!IsIEnumerableOfT(colRet)) return false;

                // ps[2] Expression<Func<TSource, TCollection, TResult>>
                if (!IsExpressionOfFunc(ps[2].ParameterType, funcArity: 3)) return false;

                return true;
            });

        static bool IsExpressionOfFunc(Type exprType, int funcArity)
        {
            if (!exprType.IsGenericType) return false;
            if (exprType.GetGenericTypeDefinition() != typeof(Expression<>)) return false;
            var lambdaType = exprType.GetGenericArguments()[0];
            if (!lambdaType.IsGenericType) return false;

            var def = lambdaType.GetGenericTypeDefinition();
            return funcArity switch
            {
                2 => def == typeof(Func<,>),
                3 => def == typeof(Func<,,>),
                _ => false
            };
        }

        static bool IsIEnumerableOfT(Type t)
        {
            if (t == typeof(string)) return false;
            if (t.IsArray) return true;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return true;
            return t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }
    }

    private static Expression? FindFirstExtension(Expression expr)
    {
        Expression? found = null;
        new ExtensionFinder(x => found ??= x).Visit(expr);
        return found;
    }

    sealed class ExtensionFinder : ExpressionVisitor
    {
        private readonly Action<Expression> _hit;
        public ExtensionFinder(Action<Expression> hit) => _hit = hit;

        public override Expression Visit(Expression? node)
        {
            if (node != null && node.NodeType == ExpressionType.Extension)
            {
                _hit(node);
                return node; // 立刻停
            }

            return base.Visit(node);
        }
    }

    private static void DumpNonReducibleExtensions(string tag, Expression expr)
    {
        Console.WriteLine($"=== [DBG] NON-REDUCIBLE EXTENSIONS ({tag}) ===");
        var v = new NonReducibleExtensionDumper();
        v.Visit(expr);
        if (v.Count == 0) Console.WriteLine("[DBG] (none)");
        Console.WriteLine("=== [DBG] END ===");
    }

    private sealed class NonReducibleExtensionDumper : ExpressionVisitor
    {
        public int Count { get; private set; }

        public override Expression Visit(Expression? node)
        {
            if (!node.CanReduce)
            {
                Count++;
                Console.WriteLine(
                    $"[DBG] EXT#{Count}: type={node.GetType().FullName} clrType={node.Type} canReduce={node.CanReduce}");
                Console.WriteLine($"        text={node}");
                return node;
            }

            return base.VisitExtension(node);
        }
    }
    
    private static TEntity MaterializeFromRow<TEntity>(
        QueryContext qc,
        IEntityType entityType,
        SnapshotRow row,
        SnapshotValueBufferFactory factory,
        IEntityMaterializerSource materializerSource)
        where TEntity : class
    {
        // 不 InitializeStateManager，不 TryGetEntry，不 StartTracking
        var vb = factory.Create(entityType, row.Snapshot);

        var materializer = materializerSource.GetMaterializer(entityType);
        var mc = new MaterializationContext(vb, qc.Context);
        return (TEntity)materializer(mc);
    }
    
    private MethodInfo GetRowMaterializerMethod(Type clrType)
    {
        // 你只说“支持 AsNoTracking”，那就只分 TrackAll vs NoTracking
        // 如果以后想支持 NoTrackingWithIdentityResolution，再加一支。
        var behavior = QueryCompilationContext.QueryTrackingBehavior;

        var open = behavior == QueryTrackingBehavior.TrackAll
            ? typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                .GetMethod(nameof(TrackFromRow), BindingFlags.Static | BindingFlags.NonPublic)!
            : typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                .GetMethod(nameof(MaterializeFromRow), BindingFlags.Static | BindingFlags.NonPublic)!;

        return open.MakeGenericMethod(clrType);
    }
}