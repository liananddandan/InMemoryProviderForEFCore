using System.Linq.Expressions;
using System.Reflection;
using CustomMemoryEFProvider.Core.Diagnostics;
using CustomMemoryEFProvider.Core.Implementations;
using CustomMemoryEFProvider.Core.Interfaces;
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

    // ---------- MAIN: QueryRows pipeline compiler ----------
    private Expression CompileQueryRowsPipeline(
        CustomMemoryQueryExpression q,
        ShapedQueryExpression shapedQueryExpression)
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

        var trackOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
            .GetMethod(nameof(TrackFromRow), BindingFlags.Static | BindingFlags.NonPublic)!;
        var trackClosed = trackOpen.MakeGenericMethod(clrType);
        var trackCall = Expression.Call(
            trackClosed,
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
        var entityRootRewriter = new EntityQueryRootRewritingVisitor(BuildQueryRowsEntityQueryable);

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
                    var skipOpen = GetQueryableMethod(nameof(Queryable.Skip), genericArgCount: 1, 2);

                    var skipClosed = skipOpen.MakeGenericMethod(currentElementType);

                    var countExpr = rewriter.Visit(sk.Count)!;
                    if (countExpr.Type != typeof(int))
                        countExpr = Expression.Convert(countExpr, typeof(int));

                    sourceExpr = Expression.Call(skipClosed, sourceExpr, countExpr);
                    break;
                }

                case TakeStep tk:
                {
                    var takeOpen = GetQueryableMethod(nameof(Queryable.Take), genericArgCount: 1, 2);

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

                    var body = entityRootRewriter.Visit(efPropRewriter.Visit(rewriter.Visit(s.Selector.Body)!)!)!;
                    var lam = Expression.Lambda(s.Selector.Type, body, s.Selector.Parameters);

                    var selectClosed = selectOpen.MakeGenericMethod(currentElementType, lam.ReturnType);
                    sourceExpr = Expression.Call(selectClosed, sourceExpr, Expression.Quote(lam));
                    currentElementType = lam.ReturnType;
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
                var sodOpen = GetQueryableMethod(nameof(Queryable.SingleOrDefault), genericArgCount: 1, 1, 2);
                var sod = sodOpen.MakeGenericMethod(elementType);

                body1 = q.TerminalPredicate == null
                    ? Expression.Call(sod, body1)
                    : Expression.Call(sod, body1, Expression.Quote(q.TerminalPredicate));

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
                var fodOpen = GetQueryableMethod(nameof(Queryable.FirstOrDefault), genericArgCount: 1, 1, 2);
                var fod = fodOpen.MakeGenericMethod(elementType);

                body1 = q.TerminalPredicate == null
                    ? Expression.Call(fod, body1)
                    : Expression.Call(fod, body1, Expression.Quote(q.TerminalPredicate));

                break;
            }

            case CustomMemoryTerminalOperator.Count:
            {
                var countOpen = GetQueryableMethod(nameof(Queryable.Count), genericArgCount: 1, 1, 2);
                var count = countOpen.MakeGenericMethod(elementType);

                body1 = q.TerminalPredicate == null
                    ? Expression.Call(count, body1)
                    : Expression.Call(count, body1, Expression.Quote(q.TerminalPredicate));

                break;
            }

            case CustomMemoryTerminalOperator.LongCount:
            {
                var lcOpen = GetQueryableMethod(nameof(Queryable.LongCount), genericArgCount: 1, 1, 2);
                var lc = lcOpen.MakeGenericMethod(elementType);

                body1 = q.TerminalPredicate == null
                    ? Expression.Call(lc, body1)
                    : Expression.Call(lc, body1, Expression.Quote(q.TerminalPredicate));

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

    // Used by EntityQueryRootRewritingVisitor to replace ctx.Set<T>() roots:
    // it must return IQueryable<T> AND it must be QueryRows-based (to keep QueryCalled==0).
    private IQueryable BuildQueryRowsEntityQueryable(Type clrType, QueryContext qc, IEntityType efEntityType)
    {
        var table = _db.GetType().GetMethods()
            .Single(m => m.Name == "GetTable"
                         && m.IsGenericMethodDefinition
                         && m.GetParameters().Length == 1)
            .MakeGenericMethod(clrType)
            .Invoke(_db, new object[] { clrType })!;

        var rows = GetQueryRowsFromTable(table, clrType); // IQueryable<SnapshotRow>

        // ✅ Use QueryContext PARAMETER (compile-time), NOT runtime instance
        var qcParam = QueryCompilationContext.QueryContextParameter;

        var trackOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
            .GetMethod(nameof(TrackFromRow), BindingFlags.Static | BindingFlags.NonPublic)!;
        var trackClosed = trackOpen.MakeGenericMethod(clrType);

        var rowParam = Expression.Parameter(typeof(SnapshotRow), "r");

        var body = Expression.Call(
            trackClosed,
            qcParam,
            Expression.Constant(efEntityType, typeof(IEntityType)),
            rowParam,
            Expression.Constant(_vbFactory),
            Expression.Constant(_materializerSource));

        var selector = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(typeof(SnapshotRow), clrType),
            body,
            rowParam);

        var selectMethod = QueryableSelect_NoIndex().MakeGenericMethod(typeof(SnapshotRow), clrType);

        var selectExpr = Expression.Call(
            selectMethod,
            Expression.Constant(rows, typeof(IQueryable<SnapshotRow>)),
            Expression.Quote(selector));

        // ✅ CreateQuery<T> from the provider
        var createQueryGeneric = rows.Provider.GetType().GetMethods()
            .Single(m => m.Name == nameof(IQueryProvider.CreateQuery)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters().Length == 1);

        var createQueryClosed = createQueryGeneric.MakeGenericMethod(clrType);

        return (IQueryable)createQueryClosed.Invoke(rows.Provider, new object[] { selectExpr })!;
    }

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        if (shapedQueryExpression.QueryExpression is not CustomMemoryQueryExpression q)
        {
            throw new NotSupportedException(
                "CustomMemory provider can only compile CustomMemoryQueryExpression at the moment. " +
                "This query shape requires additional translation/compilation support.");
        }


        // NEW: QueryRows-based path (incremental rollout)
        return CompileQueryRowsPipeline(q, shapedQueryExpression);
    }
    
// ③ 新增：把 List<T> 塞给 collection property（ICollection<T> / List<T> 都可）
    private static void SetCollectionProperty(object owner, PropertyInfo navProp, object list)
    {
        var pt = navProp.PropertyType;

        // if property type can accept the list directly
        if (pt.IsInstanceOfType(list))
        {
            navProp.SetValue(owner, list);
            return;
        }

        // if ICollection<T> and list is List<T> with same T, assignment usually works
        if (pt.IsGenericType && pt.GetGenericArguments().Length == 1)
        {
            var t = pt.GetGenericArguments()[0];
            var iColl = typeof(ICollection<>).MakeGenericType(t);
            if (iColl.IsAssignableFrom(pt))
            {
                // property expects ICollection<T> but list is List<T>; should be assignable in most cases
                navProp.SetValue(owner, list);
                return;
            }
        }

        throw new InvalidOperationException(
            $"Cannot assign list to navigation '{navProp.DeclaringType?.Name}.{navProp.Name}' type '{pt.FullName}'.");
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
}