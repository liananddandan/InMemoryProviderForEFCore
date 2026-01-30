using System.Linq.Expressions;
using System.Reflection;
using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public sealed class CustomMemoryShapedQueryCompilingExpressionVisitor
    : ShapedQueryCompilingExpressionVisitor
{
    private readonly IMemoryDatabase _db;

    public CustomMemoryShapedQueryCompilingExpressionVisitor(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        IMemoryDatabase db)
        : base(dependencies, queryCompilationContext)
    {
        _db = db;
    }

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        if (shapedQueryExpression.QueryExpression is not CustomMemoryQueryExpression q)
        {
            throw new NotSupportedException(
                "CustomMemory provider can only compile CustomMemoryQueryExpression at the moment. " +
                "This query shape requires additional translation/compilation support.");
        }

        // Entity metadata EF is querying (e.g., TestEntity)
        var entityType = q.EntityType;
        var clrType = entityType.ClrType;

        // We need to produce an Expression that, when executed, returns the query results.
        // For the minimal 'table scan' implementation, we simply return IMemoryTable<TEntity>.Query.
        //
        // In other words:
        //   QueryExpression  = "where data comes from"   -> here: MemoryTable<TEntity>.Query (whole table)
        //   ShaperExpression = "how rows become results" -> for now we rely on the fact that Query already yields entities
        //
        // The caller (EF) will then apply terminal operators like ToList() by enumerating this IQueryable.

        // Find the generic method: IMemoryDatabase.GetTable<TEntity>(Type? entityType = null)
        // IMPORTANT: There may be overloads named "GetTable", so we filter carefully.
        var getTableOpen = _db.GetType()
            .GetMethods()
            .Single(m =>
                m.Name == "GetTable"
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length == 1);
        var getTable = getTableOpen.MakeGenericMethod(clrType);


        // Build expression: _db.GetTable<TEntity>(clrType)
        // This returns an IMemoryTable<TEntity> instance at runtime.
        var dbConst = Expression.Constant(_db);
        var tableExpr = Expression.Call(
            dbConst,
            getTable,
            Expression.Constant(clrType, typeof(Type)));

        // We now access the Query property on IMemoryTable<TEntity>:
        // Expression: table.Query
        //
        // This must exist and be IQueryable<TEntity>.
        var queryProp = tableExpr.Type.GetProperty("Query")
                        ?? throw new InvalidOperationException($"IMemoryTable<{clrType.Name}> has no Query property.");

        Expression sourceExpr = Expression.Property(tableExpr, queryProp); // IQueryable<TEntity>

        var currentElementType = clrType; // starts from entity type
        var qcParam = QueryCompilationContext.QueryContextParameter;
        var rewriter = new QueryParameterRewritingVisitor(qcParam);
        var efPropRewriter = new EfPropertyRewritingVisitor();
        var nullableAlign = new BinaryNullableAlignmentVisitor();
        var entityRootRewriter = new EntityQueryRootRewritingVisitor(_db);

        foreach (var step in q.Steps)
        {
            switch (step)
            {
                case WhereStep w:
                {
                    // Queryable.Where<TSource>(IQueryable<TSource>, Expression<Func<TSource,bool>>)
                    var whereOpen = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == nameof(Queryable.Where)
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 1
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)
                            && m.GetParameters()[1].ParameterType.IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericTypeDefinition() ==
                            typeof(Func<,>));

                    var whereClosed = whereOpen.MakeGenericMethod(currentElementType);

                    var rewrittenBody = rewriter.Visit(w.Predicate.Body)!;
                    var body2 = efPropRewriter.Visit(rewrittenBody)!; // 把 EF.Property 改成 CLR Property
                    var rewrittenLambda = Expression.Lambda(w.Predicate.Type, body2, w.Predicate.Parameters);

                    sourceExpr = Expression.Call(whereClosed, sourceExpr, Expression.Quote(rewrittenLambda));
                    break;
                }

                case OrderStep o:
                {
                    // pick method name
                    string methodName;
                    if (!o.ThenBy)
                        methodName = o.Descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
                    else
                        methodName = o.Descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy);

                    var expectedSourceOpenType = o.ThenBy ? typeof(IOrderedQueryable<>) : typeof(IQueryable<>);

                    // Queryable.OrderBy<TSource,TKey>(IQueryable<TSource>, Expression<Func<TSource,TKey>>)
                    // Queryable.ThenBy<TSource,TKey>(IOrderedQueryable<TSource>, Expression<Func<TSource,TKey>>)
                    var open = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == methodName
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 2
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() ==
                            expectedSourceOpenType
                            && m.GetParameters()[1].ParameterType.IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0]
                                .GetGenericTypeDefinition() ==
                            typeof(Func<,>));

                    var rewrittenBody = rewriter.Visit(o.KeySelector.Body)!;
                    var rewrittenLambda =
                        Expression.Lambda(o.KeySelector.Type, rewrittenBody, o.KeySelector.Parameters);

                    var closed = open.MakeGenericMethod(currentElementType, rewrittenLambda.ReturnType);

                    sourceExpr = Expression.Call(closed, sourceExpr, Expression.Quote(rewrittenLambda));
                    break;
                }

                case SkipStep sk:
                {
                    // Queryable.Skip<TSource>(IQueryable<TSource>, int)
                    var skipOpen = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == nameof(Queryable.Skip)
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 1
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)
                            && m.GetParameters()[1].ParameterType == typeof(int));

                    var skipClosed = skipOpen.MakeGenericMethod(currentElementType);

                    var rewrittenCount = rewriter.Visit(sk.Count)!;
                    var countInt = rewrittenCount.Type == typeof(int)
                        ? rewrittenCount
                        : Expression.Convert(rewrittenCount, typeof(int));

                    sourceExpr = Expression.Call(skipClosed, sourceExpr, countInt);
                    break;
                }

                case TakeStep tk:
                {
                    // Queryable.Take<TSource>(IQueryable<TSource>, int)
                    var takeOpen = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == nameof(Queryable.Take)
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 1
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)
                            && m.GetParameters()[1].ParameterType == typeof(int));

                    var takeClosed = takeOpen.MakeGenericMethod(currentElementType);

                    var rewrittenCount = rewriter.Visit(tk.Count)!;
                    var countInt = rewrittenCount.Type == typeof(int)
                        ? rewrittenCount
                        : Expression.Convert(rewrittenCount, typeof(int));

                    sourceExpr = Expression.Call(takeClosed, sourceExpr, countInt);
                    break;
                }

                case LeftJoinStep lj:
                {
                    // outer element type = currentElementType
                    var outerType = currentElementType;

                    // inner source: build IMemoryTable<TInner>.Query
                    var innerEntityClr = lj.InnerQuery.EntityType.ClrType;

                    var getInnerTableOpen = _db.GetType().GetMethods()
                        .Single(m =>
                            m.Name == "GetTable"
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 1
                            && m.GetParameters().Length == 1);

                    var getInnerTable = getInnerTableOpen.MakeGenericMethod(innerEntityClr);

                    var innerTableExpr = Expression.Call(
                        Expression.Constant(_db),
                        getInnerTable,
                        Expression.Constant(innerEntityClr, typeof(Type)));

                    var innerQueryProp = innerTableExpr.Type.GetProperty("Query")
                                         ?? throw new InvalidOperationException(
                                             $"IMemoryTable<{innerEntityClr.Name}> has no Query property.");

                    Expression innerSourceExpr =
                        Expression.Property(innerTableExpr, innerQueryProp); // IQueryable<TInner>

                    // Important: inner query may also have steps (rare for Include, but LEFT JOIN can be used elsewhere)
                    // We'll replay inner steps (Where/Select/Order/Skip/Take) by reusing your existing step engine:
                    // simplest: only support inner.Where for now (reference Include typically doesn't add inner steps)
                    foreach (var innerStep in lj.InnerQuery.Steps)
                    {
                        if (innerStep is WhereStep iw)
                        {
                            var whereOpen = typeof(Queryable).GetMethods()
                                .Single(m =>
                                    m.Name == nameof(Queryable.Where)
                                    && m.IsGenericMethodDefinition
                                    && m.GetGenericArguments().Length == 1
                                    && m.GetParameters().Length == 2);

                            var whereClosed = whereOpen.MakeGenericMethod(innerEntityClr);

                            var body = efPropRewriter.Visit(rewriter.Visit(iw.Predicate.Body)!)!;
                            var lam = Expression.Lambda(iw.Predicate.Type, body, iw.Predicate.Parameters);

                            innerSourceExpr = Expression.Call(whereClosed, innerSourceExpr, Expression.Quote(lam));
                        }
                        else
                        {
                            throw new NotSupportedException(
                                $"Inner query step '{innerStep.Kind}' is not supported in LeftJoin yet.");
                        }
                    }

                    // We implement LEFT JOIN via:
                    // outer.SelectMany(
                    //   o => inner.Where(i => innerKey(i) == outerKey(o)).DefaultIfEmpty(),
                    //   (o,i) => resultSelector(o,i))

                    var outerParam = Expression.Parameter(outerType, "o");
                    var innerParam = Expression.Parameter(innerEntityClr, "i");

                    // outerKey(o)
                    var outerKeyBody0 = ReplacingExpressionVisitor.Replace(
                        lj.OuterKeySelector.Parameters[0], outerParam, lj.OuterKeySelector.Body);
                    var outerKeyBody1 = rewriter.Visit(outerKeyBody0)!;
                    var outerKeyBody = efPropRewriter.Visit(outerKeyBody1)!;

                    // innerKey(i)
                    var innerKeyBody0 = ReplacingExpressionVisitor.Replace(
                        lj.InnerKeySelector.Parameters[0], innerParam, lj.InnerKeySelector.Body);
                    var innerKeyBody1 = rewriter.Visit(innerKeyBody0)!;
                    var innerKeyBody = efPropRewriter.Visit(innerKeyBody1)!;

                    // i => innerKey(i) == outerKey(o)
                    var eq = Expression.Equal(innerKeyBody, outerKeyBody);
                    var innerPredicateType = typeof(Func<,>).MakeGenericType(innerEntityClr, typeof(bool));
                    var innerPredicate = Expression.Lambda(innerPredicateType, eq, innerParam);

                    // inner.Where(...)
                    var whereInnerOpen = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == nameof(Queryable.Where)
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 1
                            && m.GetParameters().Length == 2
                            // param0: IQueryable<TSource>
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)
                            // param1: Expression<Func<TSource,bool>>  (排除 Func<TSource,int,bool>)
                            && m.GetParameters()[1].ParameterType.IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0]
                                .GetGenericTypeDefinition() ==
                            typeof(Func<,>)
                        );

                    var whereInnerClosed = whereInnerOpen.MakeGenericMethod(innerEntityClr);
                    var filteredInner = Expression.Call(whereInnerClosed, innerSourceExpr,
                        Expression.Quote(innerPredicate));

                    // DefaultIfEmpty<TInner>(filteredInner)
                    var defaultIfEmptyOpen = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == nameof(Queryable.DefaultIfEmpty)
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 1
                            && m.GetParameters().Length == 1);

                    var defaultIfEmptyClosed = defaultIfEmptyOpen.MakeGenericMethod(innerEntityClr);
                    var defaultedInner = Expression.Call(defaultIfEmptyClosed, filteredInner); // IQueryable<TInner>

                    // collectionSelector: o => defaultedInner
                    var ienumInner = typeof(IEnumerable<>).MakeGenericType(innerEntityClr);
                    var collectionSelectorType = typeof(Func<,>).MakeGenericType(outerType, ienumInner);
                    var defaultedAsEnumerable = Expression.Convert(defaultedInner, ienumInner);
                    var collectionSelector =
                        Expression.Lambda(collectionSelectorType, defaultedAsEnumerable, outerParam);

                    // resultSelector rewrite to use (outerParam, innerParam)
                    var rsBody0 = ReplacingExpressionVisitor.Replace(
                        lj.ResultSelector.Parameters[0], outerParam,
                        ReplacingExpressionVisitor.Replace(
                            lj.ResultSelector.Parameters[1], innerParam,
                            lj.ResultSelector.Body));

                    var rsBody = rewriter.Visit(rsBody0)!;
                    var rewrittenResultSelector =
                        Expression.Lambda(lj.ResultSelector.Type, rsBody, outerParam, innerParam);

                    // SelectMany<TOuter, TInner, TResult>
                    var selectManyOpen = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == nameof(Queryable.SelectMany)
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 3
                            && m.GetParameters().Length == 3
                            // param0: IQueryable<TSource>
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)
                            // param1: Expression<Func<TSource, IEnumerable<TCollection>>>   (排除 Func<TSource,int,...>)
                            && m.GetParameters()[1].ParameterType.IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0]
                                .GetGenericTypeDefinition() ==
                            typeof(Func<,>)
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments()[1]
                                .IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments()[1]
                                .GetGenericTypeDefinition() == typeof(IEnumerable<>)
                            // param2: Expression<Func<TSource, TCollection, TResult>>
                            && m.GetParameters()[2].ParameterType.IsGenericType
                            && m.GetParameters()[2].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                            && m.GetParameters()[2].ParameterType.GetGenericArguments()[0].IsGenericType
                            && m.GetParameters()[2].ParameterType.GetGenericArguments()[0]
                                .GetGenericTypeDefinition() ==
                            typeof(Func<,,>)
                        );

                    var resultType = rewrittenResultSelector.ReturnType;
                    var selectManyClosed = selectManyOpen.MakeGenericMethod(outerType, innerEntityClr, resultType);

                    sourceExpr = Expression.Call(
                        selectManyClosed,
                        sourceExpr,
                        Expression.Quote(collectionSelector),
                        Expression.Quote(rewrittenResultSelector));

                    // update current element type (important for subsequent steps/terminal ops)
                    currentElementType = resultType;

                    break;
                }

                case SelectStep s:
                {
                    // Queryable.Select<TSource,TResult>(IQueryable<TSource>, Expression<Func<TSource,TResult>>)
                    var selectOpen = typeof(Queryable).GetMethods()
                        .Single(m =>
                            m.Name == nameof(Queryable.Select)
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == 2
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType.IsGenericType
                            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)
                            && m.GetParameters()[1].ParameterType.IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].IsGenericType
                            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericTypeDefinition() ==
                            typeof(Func<,>)
                        );

                    // 1) rewrite captured vars (QueryContext param etc.)
                    var rewrittenBody0 = rewriter.Visit(s.Selector.Body)!;

                    // 2) unwrap Convert (EF sometimes wraps IncludeExpression with Convert)
                    Expression bodyX = rewrittenBody0;
                    if (bodyX is UnaryExpression u
                        && (u.NodeType == ExpressionType.Convert || u.NodeType == ExpressionType.ConvertChecked))
                    {
                        bodyX = u.Operand;
                    }

                    // 3) INCLUDE SPECIAL CASE: eliminate IncludeExpression from executable tree
                    if (bodyX.GetType().FullName == "Microsoft.EntityFrameworkCore.Query.IncludeExpression")
                    {
                        var includeExpr = bodyX;

                        // Navigation (prefer metadata interface)
                        var navObj =
                            includeExpr.GetType().GetProperty("Navigation")?.GetValue(includeExpr)
                            ?? includeExpr.GetType().GetField("Navigation")?.GetValue(includeExpr)
                            ?? throw new InvalidOperationException("IncludeExpression.Navigation missing.");

                        var navBase = navObj as Microsoft.EntityFrameworkCore.Metadata.INavigationBase
                                      ?? throw new InvalidOperationException(
                                          $"Navigation is not INavigationBase. CLR={navObj.GetType().FullName}");

                        var isCollection = navBase.IsCollection;

                        var declaringClr = navBase.DeclaringEntityType.ClrType;
                        var targetClr = navBase.TargetEntityType.ClrType;
                        var navName = navBase.Name;

                        // A) TransparentIdentifier (TI) present:
                        //    A1) reference include TI shape: include target == TI.Inner
                        //    A2) TI exists but include is NOT about TI.Inner (multiple/nested): auto-fixup + unwrap to TI.Outer and continue
                        if (TryGetTransparentIdentifierMembers(currentElementType, out var tiOuterMember,
                                out var tiInnerMember))
                        {
                            var tiType = currentElementType;
                            var tiOuterType = GetMemberType(tiOuterMember);
                            var tiInnerType = GetMemberType(tiInnerMember);

                            // A1) Reference include via TI: only when NOT collection and TI.Inner matches include target
                            if (!isCollection && tiInnerType == targetClr)
                            {
                                var navProp = tiOuterType.GetProperty(navName,
                                                  BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                              ?? throw new InvalidOperationException(
                                                  $"Outer type '{tiOuterType.Name}' does not have navigation property '{navName}'.");

                                // inverse via metadata first + fallback inference
                                PropertyInfo? inverseProp = null;
                                var invNav = navBase.Inverse;
                                if (invNav != null)
                                {
                                    inverseProp = tiInnerType.GetProperty(invNav.Name,
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                }

                                if (inverseProp == null)
                                {
                                    inverseProp = tiInnerType.GetProperties(BindingFlags.Instance |
                                                                            BindingFlags.Public |
                                                                            BindingFlags.NonPublic)
                                        .SingleOrDefault(
                                            p => p.CanWrite && p.PropertyType.IsAssignableFrom(tiOuterType));
                                }

                                // (ti) => IncludeFixup(ti.Outer, ti.Inner, navProp, inverseProp) => returns outer
                                var tiParam = Expression.Parameter(tiType, s.Selector.Parameters[0].Name ?? "ti");
                                var outerExpr = ReadMember(tiParam, tiOuterMember);
                                var innerExpr = ReadMember(tiParam, tiInnerMember);

                                var fixupOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                                    .GetMethod(nameof(IncludeFixup), BindingFlags.Static | BindingFlags.NonPublic)!
                                    .GetGenericMethodDefinition();
                                var fixupClosed = fixupOpen.MakeGenericMethod(tiOuterType, tiInnerType);

                                var call = Expression.Call(
                                    fixupClosed,
                                    outerExpr,
                                    innerExpr,
                                    Expression.Constant(navProp, typeof(PropertyInfo)),
                                    Expression.Constant(inverseProp, typeof(PropertyInfo)));

                                var selector = Expression.Lambda(
                                    typeof(Func<,>).MakeGenericType(tiType, tiOuterType),
                                    call,
                                    tiParam);

                                var selectClosed = selectOpen.MakeGenericMethod(tiType, tiOuterType);
                                sourceExpr = Expression.Call(selectClosed, sourceExpr, Expression.Quote(selector));

                                currentElementType = tiOuterType;
                                break;
                            }

                            // A2) TI exists but not the TI-inner reference include: auto-fixup the TI pair and unwrap to Outer
                            {
                                var tiParam = Expression.Parameter(tiType, "ti");
                                var outerExpr = ReadMember(tiParam, tiOuterMember);
                                var innerExpr = ReadMember(tiParam, tiInnerMember);

                                var autoFixupOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                                    .GetMethod(nameof(AutoReferenceFixupFromTransparentIdentifier),
                                        BindingFlags.Static | BindingFlags.NonPublic)!
                                    .GetGenericMethodDefinition();
                                var autoFixupClosed = autoFixupOpen.MakeGenericMethod(tiOuterType, tiInnerType);

                                // AutoReferenceFixupFromTransparentIdentifier<TOuter,TInner>(outer, inner) -> outer
                                var call = Expression.Call(autoFixupClosed, outerExpr, innerExpr);

                                var toOuter = Expression.Lambda(
                                    typeof(Func<,>).MakeGenericType(tiType, tiOuterType),
                                    call,
                                    tiParam);

                                var selectToOuter = selectOpen.MakeGenericMethod(tiType, tiOuterType);
                                sourceExpr = Expression.Call(selectToOuter, sourceExpr, Expression.Quote(toOuter));

                                currentElementType = tiOuterType;

                                // IMPORTANT: continue below with NON-TI logic (same include, but pipeline is now Outer)
                            }
                        }

                        // B) NON-TI: include on current element type (Blog / BlogPost)
                        {
                            var outerType = currentElementType;

                            // If this include's declaring type isn't the current element type, it's a real ThenInclude expressed
                            // as a separate IncludeExpression at this SelectStep (rare in your observed EF shape).
                            // Keep this branch, but your current failing case is not here (it's nested inside MCN.Subquery.Select).
                            if (!declaringClr.IsAssignableFrom(outerType))
                            {
                                var bridgeProp = FindBridgeNavigationProperty(outerType, declaringClr);

                                var navPropOnDeclaring = declaringClr.GetProperty(navName,
                                                             BindingFlags.Instance | BindingFlags.Public |
                                                             BindingFlags.NonPublic)
                                                         ?? throw new InvalidOperationException(
                                                             $"Declaring '{declaringClr.Name}' has no '{navName}'.");

                                PropertyInfo? inverseProp = null;
                                var invNav = navBase.Inverse;
                                if (invNav != null)
                                {
                                    inverseProp = targetClr.GetProperty(invNav.Name,
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                }

                                if (inverseProp == null)
                                {
                                    inverseProp = targetClr.GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                                          BindingFlags.NonPublic)
                                        .SingleOrDefault(p =>
                                            p.CanWrite && p.PropertyType.IsAssignableFrom(declaringClr));
                                }

                                var (fkPropName, pkPropName) = GetSingleFkPkNamesFromNavigation(navObj);

                                var outerParam = Expression.Parameter(outerType, "o");

                                var thenFixupOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                                    .GetMethod(nameof(ThenIncludeFixup), BindingFlags.Static | BindingFlags.NonPublic)!
                                    .GetGenericMethodDefinition();
                                var thenFixupClosed =
                                    thenFixupOpen.MakeGenericMethod(outerType, declaringClr, targetClr);

                                var call = Expression.Call(
                                    thenFixupClosed,
                                    outerParam,
                                    Expression.Constant(bridgeProp, typeof(PropertyInfo)),
                                    Expression.Constant(navPropOnDeclaring, typeof(PropertyInfo)),
                                    Expression.Constant(inverseProp, typeof(PropertyInfo)),
                                    Expression.Constant(fkPropName),
                                    Expression.Constant(pkPropName),
                                    BuildTableQueryExpression(targetClr));

                                var selector = Expression.Lambda(
                                    typeof(Func<,>).MakeGenericType(outerType, outerType),
                                    call,
                                    outerParam);

                                var selectClosed = selectOpen.MakeGenericMethod(outerType, outerType);
                                sourceExpr = Expression.Call(selectClosed, sourceExpr, Expression.Quote(selector));

                                currentElementType = outerType;
                                break;
                            }

                            // B2) Declaring == outer
                            var navProp = outerType.GetProperty(navName,
                                              BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                          ?? throw new InvalidOperationException(
                                              $"Outer '{outerType.Name}' has no property '{navName}'.");

                            // inverse via metadata + fallback inference
                            PropertyInfo? inverseProp2 = null;
                            var invNav2 = navBase.Inverse;
                            if (invNav2 != null)
                            {
                                inverseProp2 = targetClr.GetProperty(invNav2.Name,
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            }

                            if (inverseProp2 == null)
                            {
                                inverseProp2 = targetClr.GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                                       BindingFlags.NonPublic)
                                    .SingleOrDefault(p => p.CanWrite && p.PropertyType.IsAssignableFrom(outerType));
                            }

                            var (fkPropName2, pkPropName2) = GetSingleFkPkNamesFromNavigation(navObj);

                            // ----- IMPORTANT CHANGE: handle nested include encoded as:
                            // MaterializeCollectionNavigationExpression.Subquery = ...Select(x => IncludeExpression)
                            // We must (1) strip inner IncludeExpression from the subquery (identity select),
                            // and (2) apply the stripped nested include as a fixup over the materialized list.
                            // ----- IMPORTANT CHANGE (方案A): DO NOT execute or rewrite EF's MCN.Subquery -----
// We only *extract* nested include metadata (If any), then we build our own correlated queries
// using FK/PK names against our own tables, avoiding captured outer params leak.

                            object? navExprObj =
                                includeExpr.GetType().GetProperty("NavigationExpression")?.GetValue(includeExpr)
                                ?? includeExpr.GetType().GetField("NavigationExpression")?.GetValue(includeExpr);

// If this collection include encodes a nested include (ThenInclude) inside MCN.Subquery.Select(x => IncludeExpression),
// we extract ONLY the nested Navigation metadata (INavigationBase). We do NOT reuse the Subquery expression.
                            Microsoft.EntityFrameworkCore.Metadata.INavigationBase? nestedNavBase = null;

                            if (isCollection
                                && navExprObj is Expression navExpr
                                && navExpr.GetType().FullName ==
                                "Microsoft.EntityFrameworkCore.Query.MaterializeCollectionNavigationExpression")
                            {
                                var subqObj =
                                    navExpr.GetType().GetProperty("Subquery")?.GetValue(navExpr)
                                    ?? navExpr.GetType().GetField("Subquery")?.GetValue(navExpr);

                                if (subqObj is Expression subqueryExpr)
                                {
                                    // Find a Select(...) call in the subquery chain (best-effort).
                                    MethodCallExpression? selectCall = null;
                                    Expression cur = subqueryExpr;
                                    while (cur is MethodCallExpression mcc)
                                    {
                                        if (mcc.Method.Name == "Select" && mcc.Arguments.Count >= 2)
                                        {
                                            selectCall = mcc;
                                            break;
                                        }

                                        if (mcc.Arguments.Count > 0)
                                            cur = mcc.Arguments[0];
                                        else
                                            break;
                                    }

                                    if (selectCall != null)
                                    {
                                        var selectorArg = selectCall.Arguments[1];

                                        // selectorArg is usually Quote(Lambda)
                                        LambdaExpression? lam = null;
                                        if (selectorArg is UnaryExpression uq
                                            && uq.NodeType == ExpressionType.Quote
                                            && uq.Operand is LambdaExpression l1)
                                        {
                                            lam = l1;
                                        }
                                        else if (selectorArg is LambdaExpression l2)
                                        {
                                            lam = l2;
                                        }

                                        if (lam != null)
                                        {
                                            Expression lamBody = lam.Body;

                                            // unwrap Convert
                                            if (lamBody is UnaryExpression uu
                                                && (uu.NodeType == ExpressionType.Convert ||
                                                    uu.NodeType == ExpressionType.ConvertChecked))
                                            {
                                                lamBody = uu.Operand;
                                            }

                                            // If the selector body is IncludeExpression, it's the nested include (ThenInclude)
                                            if (lamBody.GetType().FullName ==
                                                "Microsoft.EntityFrameworkCore.Query.IncludeExpression")
                                            {
                                                var nestedIncludeExpr = lamBody;

                                                var nestedNavObj =
                                                    nestedIncludeExpr.GetType().GetProperty("Navigation")
                                                        ?.GetValue(nestedIncludeExpr)
                                                    ?? nestedIncludeExpr.GetType().GetField("Navigation")
                                                        ?.GetValue(nestedIncludeExpr);

                                                if (nestedNavObj is
                                                    Microsoft.EntityFrameworkCore.Metadata.INavigationBase nb)
                                                {
                                                    nestedNavBase = nb;

                                                    Console.WriteLine(
                                                        $">>> [NESTED-EXTRACT] Found nested include: Declaring={nb.DeclaringEntityType.ClrType.Name} " +
                                                        $"Name={nb.Name} Target={nb.TargetEntityType.ClrType.Name} " +
                                                        $"Inverse={(nb.Inverse == null ? "null" : nb.Inverse.Name)}");
                                                }
                                                else
                                                {
                                                    Console.WriteLine(
                                                        $">>> [NESTED-EXTRACT] Nested Navigation is not INavigationBase. CLR={nestedNavObj?.GetType().FullName ?? "null"}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }

// -------- normal outer include (Declaring == outer) continues --------
// navProp on outer, inverseProp2 on targetClr, fkPropName2/pkPropName2 already computed above

                            if (isCollection)
                            {
                                // 1) Build correlated list query ourselves (NO EF subquery reuse)
                                var innerSourceExpr = BuildTableQueryExpression(targetClr); // IQueryable<TTarget>

                                var outerParam = Expression.Parameter(outerType, "o");
                                var innerParam = Expression.Parameter(targetClr, "i");

                                var outerKey = Expression.PropertyOrField(outerParam, pkPropName2);
                                var innerKey = Expression.PropertyOrField(innerParam, fkPropName2);
                                var eq = Expression.Equal(innerKey, outerKey);

                                var predType = typeof(Func<,>).MakeGenericType(targetClr, typeof(bool));
                                var pred = Expression.Lambda(predType, eq, innerParam);

                                // inner.Where(pred)
                                var whereInnerOpen = typeof(Queryable).GetMethods()
                                    .Single(m =>
                                        m.Name == nameof(Queryable.Where)
                                        && m.IsGenericMethodDefinition
                                        && m.GetGenericArguments().Length == 1
                                        && m.GetParameters().Length == 2
                                        && m.GetParameters()[0].ParameterType.IsGenericType
                                        && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() ==
                                        typeof(IQueryable<>)
                                        && m.GetParameters()[1].ParameterType.IsGenericType
                                        && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() ==
                                        typeof(Expression<>)
                                        && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].IsGenericType
                                        && m.GetParameters()[1].ParameterType.GetGenericArguments()[0]
                                            .GetGenericTypeDefinition() == typeof(Func<,>)
                                    );

                                var whereInner = whereInnerOpen.MakeGenericMethod(targetClr);
                                var filteredInner =
                                    Expression.Call(whereInner, innerSourceExpr,
                                        Expression.Quote(pred)); // IQueryable<TTarget>

                                // ToList(filteredInner) via Enumerable.ToList(IEnumerable<T>)
                                var toList = typeof(Enumerable).GetMethods()
                                    .Single(m => m.Name == nameof(Enumerable.ToList)
                                                 && m.IsGenericMethodDefinition
                                                 && m.GetParameters().Length == 1)
                                    .MakeGenericMethod(targetClr);

                                var innerAsEnumerable = Expression.Convert(filteredInner,
                                    typeof(IEnumerable<>).MakeGenericType(targetClr));
                                var listExpr = Expression.Call(toList, innerAsEnumerable); // List<TTarget>

                                // 2) If we extracted nested include metadata, apply collection fixup with nested
                                if (nestedNavBase != null)
                                {
                                    // nested: Declaring (must be the element type of the outer collection)
                                    var nestedDeclClr = nestedNavBase.DeclaringEntityType.ClrType; // e.g. BlogPost
                                    var nestedTargetClr = nestedNavBase.TargetEntityType.ClrType; // e.g. PostComment
                                    var nestedNavName = nestedNavBase.Name; // e.g. Comments

                                    // Sanity: nestedDeclClr should match targetClr (outer collection element type)
                                    if (nestedDeclClr != targetClr)
                                    {
                                        Console.WriteLine(
                                            $">>> [NESTED-EXTRACT] WARNING: nestedDeclClr={nestedDeclClr.Name} != outerElement={targetClr.Name}. Still continuing.");
                                    }

                                    var nestedNavProp = nestedDeclClr.GetProperty(nestedNavName,
                                                            BindingFlags.Instance | BindingFlags.Public |
                                                            BindingFlags.NonPublic)
                                                        ?? throw new InvalidOperationException(
                                                            $"Nested declaring '{nestedDeclClr.Name}' has no '{nestedNavName}'.");

                                    // nested inverse (e.g. PostComment.Post)
                                    PropertyInfo? nestedInverseProp = null;
                                    var nestedInvNav = nestedNavBase.Inverse;
                                    if (nestedInvNav != null)
                                    {
                                        nestedInverseProp = nestedTargetClr.GetProperty(nestedInvNav.Name,
                                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    }

                                    if (nestedInverseProp == null)
                                    {
                                        nestedInverseProp = nestedTargetClr.GetProperties(BindingFlags.Instance |
                                                BindingFlags.Public | BindingFlags.NonPublic)
                                            .SingleOrDefault(p =>
                                                p.CanWrite && p.PropertyType.IsAssignableFrom(nestedDeclClr));
                                    }

                                    // nested FK/PK names
                                    var (nestedFk, nestedPk) = GetSingleFkPkNamesFromNavigation(nestedNavBase);

                                    // nested target query source
                                    var nestedTargetQuery =
                                        BuildTableQueryExpression(nestedTargetClr); // IQueryable<TNestedTarget>

                                    Console.WriteLine(
                                        $"[COLL+NESTED] outer={outerType.Name} nav={navName} elem={targetClr.Name} " +
                                        $"nested={nestedDeclClr.Name}.{nestedNavName}->{nestedTargetClr.Name} fk={nestedFk} pk={nestedPk}");

                                    // IncludeCollectionFixupWithNested<TOuter, TElem, TNestedDecl, TNestedTarget>(
                                    //     outer, list, outerNavProp, outerInverseProp,
                                    //     nestedNavProp, nestedInverseProp, nestedFk, nestedPk, nestedTargetQuery) -> outer
                                    var fixupOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                                        .GetMethod(nameof(IncludeCollectionFixupWithNested),
                                            BindingFlags.Static | BindingFlags.NonPublic)!
                                        .GetGenericMethodDefinition();

                                    var fixupClosed = fixupOpen.MakeGenericMethod(outerType, targetClr, nestedDeclClr,
                                        nestedTargetClr);

                                    var call = Expression.Call(
                                        fixupClosed,
                                        outerParam,
                                        listExpr,
                                        Expression.Constant(navProp, typeof(PropertyInfo)),
                                        Expression.Constant(inverseProp2, typeof(PropertyInfo)),
                                        Expression.Constant(nestedNavProp, typeof(PropertyInfo)),
                                        Expression.Constant(nestedInverseProp, typeof(PropertyInfo)),
                                        Expression.Constant(nestedFk),
                                        Expression.Constant(nestedPk),
                                        nestedTargetQuery);

                                    var selector = Expression.Lambda(
                                        typeof(Func<,>).MakeGenericType(outerType, outerType),
                                        call,
                                        outerParam);

                                    var selectClosed = selectOpen.MakeGenericMethod(outerType, outerType);
                                    sourceExpr = Expression.Call(selectClosed, sourceExpr, Expression.Quote(selector));

                                    currentElementType = outerType;
                                    break;
                                }
                                else
                                {
                                    // Normal collection include fixup
                                    var collFixupOpen = typeof(CustomMemoryShapedQueryCompilingExpressionVisitor)
                                        .GetMethod(nameof(IncludeCollectionFixup),
                                            BindingFlags.Static | BindingFlags.NonPublic)!
                                        .GetGenericMethodDefinition();

                                    var collFixupClosed = collFixupOpen.MakeGenericMethod(outerType, targetClr);

                                    var call = Expression.Call(
                                        collFixupClosed,
                                        outerParam,
                                        listExpr,
                                        Expression.Constant(navProp, typeof(PropertyInfo)),
                                        Expression.Constant(inverseProp2, typeof(PropertyInfo)));

                                    var selector = Expression.Lambda(
                                        typeof(Func<,>).MakeGenericType(outerType, outerType),
                                        call,
                                        outerParam);

                                    var selectClosed = selectOpen.MakeGenericMethod(outerType, outerType);
                                    sourceExpr = Expression.Call(selectClosed, sourceExpr, Expression.Quote(selector));

                                    currentElementType = outerType;
                                    break;
                                }
                            }
                        }
                    }

                    // 4) NORMAL SELECT
                    {
                        var rewrittenLambdaNormal =
                            Expression.Lambda(s.Selector.Type, rewrittenBody0, s.Selector.Parameters);
                        var selectClosedNormal =
                            selectOpen.MakeGenericMethod(currentElementType, rewrittenLambdaNormal.ReturnType);
                        sourceExpr = Expression.Call(selectClosedNormal, sourceExpr,
                            Expression.Quote(rewrittenLambdaNormal));
                        currentElementType = rewrittenLambdaNormal.ReturnType;
                        break;
                    }
                }

                case SelectManyStep sm:
                {
                    // 0) 先把“目前累计的 sourceExpr”扫一遍，避免之前步骤残留 EF Root
                    sourceExpr = entityRootRewriter.Visit(sourceExpr)!;

                    // Queryable.SelectMany<TSource, TCollection, TResult>(
                    //   IQueryable<TSource>,
                    //   Expression<Func<TSource, IEnumerable<TCollection>>>,
                    //   Expression<Func<TSource, TCollection, TResult>>)

                    var selectManyOpen = typeof(Queryable)
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == nameof(Queryable.SelectMany))
                        .Where(m => m.IsGenericMethodDefinition)
                        .Where(m => m.GetGenericArguments().Length == 3)
                        .Single(m =>
                        {
                            var ps = m.GetParameters();
                            if (ps.Length != 3) return false;

                            // ps[0] : IQueryable<TSource>
                            if (!ps[0].ParameterType.IsGenericType) return false;
                            if (ps[0].ParameterType.GetGenericTypeDefinition() != typeof(IQueryable<>)) return false;

                            // ps[1] : Expression<Func<TSource, IEnumerable<TCollection>>>
                            if (!IsExpressionOfFunc(ps[1].ParameterType, 2)) return false;
                            var colRet = ps[1].ParameterType.GetGenericArguments()[0].GetGenericArguments()[1];
                            if (TryGetIEnumerableElementType(colRet) == null) return false;

                            // ps[2] : Expression<Func<TSource, TCollection, TResult>>
                            if (!IsExpressionOfFunc(ps[2].ParameterType, 3)) return false;

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

                    static Expression UnwrapConvert(Expression e)
                    {
                        while (e is UnaryExpression u &&
                               (u.NodeType == ExpressionType.Convert || u.NodeType == ExpressionType.ConvertChecked))
                            e = u.Operand;
                        return e;
                    }

                    static Type? TryGetIEnumerableElementType(Type seqType)
                    {
                        if (seqType == typeof(string)) return null;
                        if (seqType.IsArray) return seqType.GetElementType();

                        if (seqType.IsGenericType && seqType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                            return seqType.GetGenericArguments()[0];

                        var iface = seqType.GetInterfaces()
                            .FirstOrDefault(i =>
                                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                        return iface?.GetGenericArguments()[0];
                    }

                    Expression RewriteForExecution(Expression expr)
                    {
                        expr = rewriter.Visit(expr)!;
                        expr = entityRootRewriter.Visit(expr)!; // ⭐关键：干掉 EntityQueryRootExpression
                        expr = efPropRewriter.Visit(expr)!;
                        expr = UnwrapConvert(expr);
                        return expr;
                    }

                    var outerType = currentElementType;

                    // 1) rewrite collectionSelector body
                    var colBody = RewriteForExecution(sm.CollectionSelector.Body);

                    // SelectMany 的第二参数要求 IEnumerable<TCollection>
                    // 但 EF 生成的常常是 Queryable.Where(...) => IQueryable<TCollection>
                    // 这里把静态类型统一成 IEnumerable<TCollection>
                    var colElemType = TryGetIEnumerableElementType(colBody.Type)
                                      ?? throw new InvalidOperationException(
                                          $"SelectMany collectionSelector must return IEnumerable<T>. BodyType={colBody.Type}");

                    var ienumColType = typeof(IEnumerable<>).MakeGenericType(colElemType);
                    if (!ienumColType.IsAssignableFrom(colBody.Type))
                    {
                        // 理论上不会发生，但防御一下
                        colBody = Expression.Convert(colBody, ienumColType);
                    }
                    else if (colBody.Type != ienumColType)
                    {
                        // 让表达式静态类型变成 IEnumerable<T>（避免后续推导成 IQueryable<T> 造成签名不匹配）
                        colBody = Expression.Convert(colBody, ienumColType);
                    }

                    // ⭐不要用 sm.CollectionSelector.Type，自己构造 delegateType
                    var colDelegateType = typeof(Func<,>).MakeGenericType(outerType, ienumColType);
                    var rewrittenCollection = Expression.Lambda(
                        colDelegateType,
                        colBody,
                        sm.CollectionSelector.Parameters);

                    // 2) rewrite resultSelector body
                    var resBody = RewriteForExecution(sm.ResultSelector.Body);
                    var resultType = resBody.Type;

                    // ⭐同样不要用 sm.ResultSelector.Type，自己构造 delegateType
                    var resDelegateType = typeof(Func<,,>).MakeGenericType(outerType, colElemType, resultType);
                    var rewrittenResult = Expression.Lambda(
                        resDelegateType,
                        resBody,
                        sm.ResultSelector.Parameters);

                    // 3) call Queryable.SelectMany
                    var selectManyClosed = selectManyOpen.MakeGenericMethod(outerType, colElemType, resultType);

                    sourceExpr = Expression.Call(
                        selectManyClosed,
                        sourceExpr,
                        Expression.Quote(rewrittenCollection),
                        Expression.Quote(rewrittenResult));

                    // 4) 再兜底扫一次：保证最终树里没有 EF 的 Extension query root
                    sourceExpr = entityRootRewriter.Visit(sourceExpr)!;

                    currentElementType = resultType;
                    break;
                }

                default:
                    throw new NotSupportedException($"Unknown step kind: {step.Kind}");
            }
        }

        // // 在 VisitShapedQuery 末尾，switch(terminal) 之前也行，return 之前最好
        // ExpressionDebug.DumpExpression("FINAL sourceExpr", sourceExpr);
        // // // 如果你还会构造 shaper / resultSelector / newShaper，也把它们 dump
        // ExpressionDebug.DumpExpression("ShaperExpression", shapedQueryExpression.ShaperExpression);
        // ExpressionPathDebug.DumpExtensionPaths(sourceExpr);
        // ExpressionPathDebug.DumpExtensionPaths(shapedQueryExpression.ShaperExpression);
        // IncludeExpressionDebug.DumpIncludes("SHAPER", shapedQueryExpression.ShaperExpression);

        // terminal operator => scalar or sequence
        switch (q.TerminalOperator)
        {
            case CustomMemoryTerminalOperator.None:
                return sourceExpr;

            case CustomMemoryTerminalOperator.Count:
            {
                var countMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.Count) && m.GetParameters().Length == 1)
                    .MakeGenericMethod(currentElementType);

                return Expression.Call(countMethod, sourceExpr);
            }

            case CustomMemoryTerminalOperator.LongCount:
            {
                var longCountMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.LongCount) && m.GetParameters().Length == 1)
                    .MakeGenericMethod(currentElementType);

                return Expression.Call(longCountMethod, sourceExpr);
            }

            case CustomMemoryTerminalOperator.Any:
            {
                var anyMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.Any) && m.GetParameters().Length == 1)
                    .MakeGenericMethod(currentElementType);

                return Expression.Call(anyMethod, sourceExpr);
            }

            case CustomMemoryTerminalOperator.First:
            {
                var firstMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.First) && m.GetParameters().Length == 1)
                    .MakeGenericMethod(currentElementType);

                return Expression.Call(firstMethod, sourceExpr);
            }

            case CustomMemoryTerminalOperator.FirstOrDefault:
            {
                var firstOrDefaultMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.FirstOrDefault) && m.GetParameters().Length == 1)
                    .MakeGenericMethod(currentElementType);

                return Expression.Call(firstOrDefaultMethod, sourceExpr);
            }

            case CustomMemoryTerminalOperator.Single:
            {
                var singleMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.Single) && m.GetParameters().Length == 1)
                    .MakeGenericMethod(currentElementType);

                return Expression.Call(singleMethod, sourceExpr);
            }

            case CustomMemoryTerminalOperator.SingleOrDefault:
            {
                var singleOrDefaultMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.SingleOrDefault) && m.GetParameters().Length == 1)
                    .MakeGenericMethod(currentElementType);

                return Expression.Call(singleOrDefaultMethod, sourceExpr);
            }

            case CustomMemoryTerminalOperator.All:
            {
                if (q.TerminalPredicate == null)
                    throw new InvalidOperationException("ALL requires a predicate lambda.");

                // Queryable.All<TSource>(IQueryable<TSource>, Expression<Func<TSource,bool>>)
                var allMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.All) && m.GetParameters().Length == 2)
                    .MakeGenericMethod(currentElementType);

                var terminalLambda = q.TerminalPredicate;

                var rewrittenBody = rewriter.Visit(terminalLambda.Body)!;
                var rewrittenLambda =
                    Expression.Lambda(terminalLambda.Type, rewrittenBody, terminalLambda.Parameters);

                return Expression.Call(allMethod, sourceExpr, Expression.Quote(rewrittenLambda));
            }

            case CustomMemoryTerminalOperator.Min:
            {
                if (q.TerminalSelector == null)
                    throw new InvalidOperationException("MIN requires a selector lambda.");

                var selector = q.TerminalSelector;

                var minMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.Min)
                                 && m.IsGenericMethodDefinition
                                 && m.GetGenericArguments().Length == 2
                                 && m.GetParameters().Length == 2);

                var rewrittenBody = rewriter.Visit(selector.Body)!;
                var rewrittenSelector = Expression.Lambda(selector.Type, rewrittenBody, selector.Parameters);

                var minClosed = minMethod.MakeGenericMethod(currentElementType, rewrittenSelector.ReturnType);
                return Expression.Call(minClosed, sourceExpr, Expression.Quote(rewrittenSelector));
            }

            case CustomMemoryTerminalOperator.Max:
            {
                if (q.TerminalSelector == null)
                    throw new InvalidOperationException("MAX requires a selector lambda.");

                var selector = q.TerminalSelector;

                var maxMethod = typeof(Queryable).GetMethods()
                    .Single(m => m.Name == nameof(Queryable.Max)
                                 && m.IsGenericMethodDefinition
                                 && m.GetGenericArguments().Length == 2
                                 && m.GetParameters().Length == 2);

                var rewrittenBody = rewriter.Visit(selector.Body)!;
                var rewrittenSelector = Expression.Lambda(selector.Type, rewrittenBody, selector.Parameters);

                var maxClosed = maxMethod.MakeGenericMethod(currentElementType, rewrittenSelector.ReturnType);
                return Expression.Call(maxClosed, sourceExpr, Expression.Quote(rewrittenSelector));
            }

            case CustomMemoryTerminalOperator.Sum:
            {
                if (q.TerminalSelector == null)
                    throw new InvalidOperationException("SUM requires a selector lambda.");

                var selector = q.TerminalSelector;

                var rewrittenBody = rewriter.Visit(selector.Body)!;
                var rewrittenSelector = Expression.Lambda(selector.Type, rewrittenBody, selector.Parameters);

                // Queryable.Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, number>>)
                var sumOpen = typeof(Queryable).GetMethods()
                    .Where(m =>
                        m.Name == nameof(Queryable.Sum)
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 1
                        && m.GetParameters().Length == 2)
                    .Single(m =>
                    {
                        var p1 = m.GetParameters()[1].ParameterType;
                        if (!p1.IsGenericType || p1.GetGenericTypeDefinition() != typeof(Expression<>))
                            return false;

                        var funcType = p1.GetGenericArguments()[0];
                        if (!funcType.IsGenericType || funcType.GetGenericTypeDefinition() != typeof(Func<,>))
                            return false;

                        var numberType = funcType.GetGenericArguments()[1];
                        return numberType == rewrittenSelector.ReturnType;
                    });

                var sumClosed = sumOpen.MakeGenericMethod(currentElementType);
                return Expression.Call(sumClosed, sourceExpr, Expression.Quote(rewrittenSelector));
            }

            case CustomMemoryTerminalOperator.Average:
            {
                if (q.TerminalSelector == null)
                    throw new InvalidOperationException("AVERAGE requires a selector lambda.");

                var selector = q.TerminalSelector;

                var rewrittenBody = rewriter.Visit(selector.Body)!;
                var rewrittenSelector = Expression.Lambda(selector.Type, rewrittenBody, selector.Parameters);

                // Queryable.Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, number>>)
                var avgOpen = typeof(Queryable).GetMethods()
                    .Where(m =>
                        m.Name == nameof(Queryable.Average)
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 1
                        && m.GetParameters().Length == 2)
                    .Single(m =>
                    {
                        var p1 = m.GetParameters()[1].ParameterType;
                        if (!p1.IsGenericType || p1.GetGenericTypeDefinition() != typeof(Expression<>))
                            return false;

                        var funcType = p1.GetGenericArguments()[0];
                        if (!funcType.IsGenericType || funcType.GetGenericTypeDefinition() != typeof(Func<,>))
                            return false;

                        var numberType = funcType.GetGenericArguments()[1];
                        return numberType == rewrittenSelector.ReturnType;
                    });

                var avgClosed = avgOpen.MakeGenericMethod(currentElementType);
                return Expression.Call(avgClosed, sourceExpr, Expression.Quote(rewrittenSelector));
            }

            default:
                throw new NotSupportedException($"Unsupported terminal operator {q.TerminalOperator}");
        }
    }


    private static TOuter IncludeFixup<TOuter, TInner>(
        TOuter outer,
        TInner? inner,
        PropertyInfo navigationProperty,
        PropertyInfo? inverseProperty)
        where TOuter : class
        where TInner : class
    {
        // outer.Nav = inner
        navigationProperty.SetValue(outer, inner);

        // inverse fix-up：只做一跳，不递归
        if (inner != null && inverseProperty != null)
        {
            // 只有当 inverseProperty 的类型能接 outer 时才 set（更安全）
            if (inverseProperty.PropertyType.IsAssignableFrom(typeof(TOuter)))
            {
                inverseProperty.SetValue(inner, outer);
            }
        }

        return outer;
    }

    private Expression BuildTableQueryExpression(Type clrType)
    {
        var getTableOpen = _db.GetType().GetMethods()
            .Single(m => m.Name == "GetTable"
                         && m.IsGenericMethodDefinition
                         && m.GetGenericArguments().Length == 1
                         && m.GetParameters().Length == 1);

        var getTable = getTableOpen.MakeGenericMethod(clrType);

        var tableExpr = Expression.Call(
            Expression.Constant(_db),
            getTable,
            Expression.Constant(clrType, typeof(Type)));

        var queryProp = tableExpr.Type.GetProperty("Query")
                        ?? throw new InvalidOperationException(
                            $"IMemoryTable<{clrType.Name}> has no Query property.");

        return Expression.Property(tableExpr, queryProp); // IQueryable<T>
    }

    private static string GetSingleFkPropertyName(object navigation)
    {
        // navigation.ForeignKey.Properties[0].Name
        var fkObj = navigation.GetType().GetProperty("ForeignKey")?.GetValue(navigation)
                    ?? navigation.GetType().GetField("ForeignKey")?.GetValue(navigation);
        if (fkObj == null) throw new InvalidOperationException("Navigation.ForeignKey missing.");

        var propsObj = fkObj.GetType().GetProperty("Properties")?.GetValue(fkObj)
                       ?? fkObj.GetType().GetField("Properties")?.GetValue(fkObj);
        if (propsObj is not System.Collections.IEnumerable propsEnum)
            throw new InvalidOperationException("ForeignKey.Properties missing.");

        var first = propsEnum.Cast<object>().FirstOrDefault()
                    ?? throw new InvalidOperationException("ForeignKey.Properties empty.");

        var nameObj = first.GetType().GetProperty("Name")?.GetValue(first)
                      ?? first.GetType().GetField("Name")?.GetValue(first);
        return nameObj as string ?? throw new InvalidOperationException("FK property Name missing.");
    }

    private static string GetSinglePkPropertyName(object navigation)
    {
        // navigation.ForeignKey.PrincipalKey.Properties[0].Name
        var fkObj = navigation.GetType().GetProperty("ForeignKey")?.GetValue(navigation)
                    ?? navigation.GetType().GetField("ForeignKey")?.GetValue(navigation);
        if (fkObj == null) throw new InvalidOperationException("Navigation.ForeignKey missing.");

        var pkObj = fkObj.GetType().GetProperty("PrincipalKey")?.GetValue(fkObj)
                    ?? fkObj.GetType().GetField("PrincipalKey")?.GetValue(fkObj);
        if (pkObj == null) throw new InvalidOperationException("ForeignKey.PrincipalKey missing.");

        var propsObj = pkObj.GetType().GetProperty("Properties")?.GetValue(pkObj)
                       ?? pkObj.GetType().GetField("Properties")?.GetValue(pkObj);
        if (propsObj is not System.Collections.IEnumerable propsEnum)
            throw new InvalidOperationException("PrincipalKey.Properties missing.");

        var first = propsEnum.Cast<object>().FirstOrDefault()
                    ?? throw new InvalidOperationException("PrincipalKey.Properties empty.");

        var nameObj = first.GetType().GetProperty("Name")?.GetValue(first)
                      ?? first.GetType().GetField("Name")?.GetValue(first);
        return nameObj as string ?? throw new InvalidOperationException("PK property Name missing.");
    }

    private static MethodInfo QueryableWhere(Type elementType)
    {
        var whereOpen = typeof(Queryable).GetMethods()
            .Single(m =>
                m.Name == nameof(Queryable.Where)
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType.IsGenericType
                && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)
                && m.GetParameters()[1].ParameterType.IsGenericType
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].IsGenericType
                && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericTypeDefinition() ==
                typeof(Func<,>)
            );

        return whereOpen.MakeGenericMethod(elementType);
    }

    private static TOuter IncludeCollectionFixup<TOuter, TInner>(
        TOuter outer,
        List<TInner> inners,
        PropertyInfo navigationProperty,
        PropertyInfo? inverseProperty)
        where TOuter : class
        where TInner : class
    {
        // outer.Nav (ICollection<TInner>/List<TInner>/IEnumerable<TInner> ...)
        var navValue = navigationProperty.GetValue(outer);

        // 如果属性可写，最简单：直接 Set 一个 List<TInner>（适配 ICollection<T> 很常见）
        // 如果不可写/是现有集合实例：就往里面 Add
        if (navigationProperty.CanWrite)
        {
            navigationProperty.SetValue(outer, inners);
        }
        else if (navValue is System.Collections.IList list)
        {
            list.Clear();
            foreach (var i in inners) list.Add(i);
        }
        else if (navValue is System.Collections.ICollection coll)
        {
            // 非泛型 ICollection 没法 Add(TInner)；这种很少见，先不支持
            throw new NotSupportedException(
                $"Navigation '{navigationProperty.Name}' is non-generic ICollection and not supported.");
        }
        else if (navValue != null)
        {
            // 你也可以在这里用反射找 Add 方法：Add(TInner)
            var add = navValue.GetType().GetMethod("Add", new[] { typeof(TInner) });
            if (add == null)
                throw new NotSupportedException(
                    $"Navigation '{navigationProperty.Name}' has no setter and no Add(TInner).");

            var clear = navValue.GetType().GetMethod("Clear", Type.EmptyTypes);
            clear?.Invoke(navValue, null);
            foreach (var i in inners) add.Invoke(navValue, new object[] { i });
        }
        else
        {
            // 属性不可写且为 null：没法放进去
            throw new NotSupportedException($"Navigation '{navigationProperty.Name}' is null and not settable.");
        }

        // inverse fixup
        if (inverseProperty != null)
        {
            foreach (var i in inners)
                inverseProperty.SetValue(i, outer);
        }

        return outer;
    }

// stage-1: 只支持单 key
    private static (string fkPropName, string pkPropName) GetSingleFkPkNamesFromNavigation(object navigation)
    {
        // navigation.ForeignKey.Properties[0].Name  (dependent FK)
        // navigation.ForeignKey.PrincipalKey.Properties[0].Name (principal PK)
        var fkObj =
            navigation.GetType().GetProperty("ForeignKey")?.GetValue(navigation)
            ?? navigation.GetType().GetField("ForeignKey")?.GetValue(navigation);

        if (fkObj == null)
            throw new InvalidOperationException("Navigation.ForeignKey missing.");

        var propsObj =
            fkObj.GetType().GetProperty("Properties")?.GetValue(fkObj)
            ?? fkObj.GetType().GetField("Properties")?.GetValue(fkObj);

        if (propsObj is not System.Collections.IEnumerable propsEnum)
            throw new InvalidOperationException("ForeignKey.Properties missing.");

        var fkProps = propsEnum.Cast<object>().ToList();
        if (fkProps.Count != 1)
            throw new NotSupportedException("Stage-1 collection include only supports single-column FK.");

        var fkName =
            fkProps[0].GetType().GetProperty("Name")?.GetValue(fkProps[0]) as string
            ?? fkProps[0].GetType().GetField("Name")?.GetValue(fkProps[0]) as string
            ?? throw new InvalidOperationException("FK property name missing.");

        var pkObj =
            fkObj.GetType().GetProperty("PrincipalKey")?.GetValue(fkObj)
            ?? fkObj.GetType().GetField("PrincipalKey")?.GetValue(fkObj);

        if (pkObj == null)
            throw new InvalidOperationException("ForeignKey.PrincipalKey missing.");

        var pkPropsObj =
            pkObj.GetType().GetProperty("Properties")?.GetValue(pkObj)
            ?? pkObj.GetType().GetField("Properties")?.GetValue(pkObj);

        if (pkPropsObj is not System.Collections.IEnumerable pkPropsEnum)
            throw new InvalidOperationException("PrincipalKey.Properties missing.");

        var pkProps = pkPropsEnum.Cast<object>().ToList();
        if (pkProps.Count != 1)
            throw new NotSupportedException("Stage-1 collection include only supports single-column PK.");

        var pkName =
            pkProps[0].GetType().GetProperty("Name")?.GetValue(pkProps[0]) as string
            ?? pkProps[0].GetType().GetField("Name")?.GetValue(pkProps[0]) as string
            ?? throw new InvalidOperationException("PK property name missing.");

        return (fkName, pkName);
    }

    static bool TryGetTransparentIdentifierMembers(Type tiType, out MemberInfo outerMember,
        out MemberInfo innerMember)
    {
        var outerMembers =
            tiType.GetMember("Outer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var innerMembers =
            tiType.GetMember("Inner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (outerMembers.Length == 1 && innerMembers.Length == 1)
        {
            outerMember = outerMembers[0];
            innerMember = innerMembers[0];
            return true;
        }

        outerMember = default!;
        innerMember = default!;
        return false;
    }

    static Type GetMemberType(MemberInfo m) =>
        m switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => throw new NotSupportedException($"Unsupported member kind: {m.GetType().Name}")
        };

    static Expression ReadMember(Expression instance, MemberInfo m) =>
        m switch
        {
            PropertyInfo p => Expression.Property(instance, p),
            FieldInfo f => Expression.Field(instance, f),
            _ => throw new NotSupportedException($"Unsupported member kind: {m.GetType().Name}")
        };

    private static TOuter AutoReferenceFixupFromTransparentIdentifier<TOuter, TInner>(
        TOuter outer,
        TInner? inner)
        where TOuter : class
        where TInner : class
    {
        if (inner == null) return outer;

        var outerType = typeof(TOuter);
        var innerType = typeof(TInner);

        // 找 outer 上唯一一个“可写 + 非集合 + 能接收 innerType”的属性（比如 Blog.Detail）
        var candidates = outerType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.CanWrite
                        && !typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) // 排除集合
                        && p.PropertyType.IsAssignableFrom(innerType))
            .ToList();

        if (candidates.Count != 1) return outer;

        var navProp = candidates[0];

        // 只在目前为 null 时写入，避免覆盖用户手工赋值或前序 fixup
        if (navProp.GetValue(outer) == null)
            navProp.SetValue(outer, inner);

        // 尝试反向：inner 上唯一一个可写、能接收 outerType 的属性（比如 BlogDetail.Blog）
        var inverse = innerType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.CanWrite && p.PropertyType.IsAssignableFrom(outerType))
            .ToList();

        if (inverse.Count == 1 && inverse[0].GetValue(inner) == null)
            inverse[0].SetValue(inner, outer);

        return outer;
    }

    // ① 新增：给“真正的 ThenInclude（作为独立 IncludeExpression 出现）”找 bridge（Blog -> Posts -> BlogPost）
    private static PropertyInfo FindBridgeNavigationProperty(Type outerType, Type declaringClr)
    {
        // outerType 上找：属性类型 == declaringClr（reference） 或者 IEnumerable<declaringClr>（collection）
        var props = outerType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // 1) reference bridge: Detail : BlogDetail (declaringClr == BlogDetail)
        var refMatch = props.FirstOrDefault(p =>
            p.CanRead && p.PropertyType.IsAssignableFrom(declaringClr));
        if (refMatch != null) return refMatch;

        // 2) collection bridge: Posts : ICollection<BlogPost> (declaringClr == BlogPost)
        foreach (var p in props)
        {
            if (!p.CanRead) continue;

            var pt = p.PropertyType;
            if (pt == typeof(string)) continue;

            if (pt.IsGenericType)
            {
                var ga = pt.GetGenericArguments();
                if (ga.Length == 1 && ga[0] == declaringClr)
                {
                    // ICollection<T> / IEnumerable<T> / List<T> etc.
                    if (typeof(IEnumerable<>).MakeGenericType(declaringClr).IsAssignableFrom(pt))
                        return p;
                }
            }

            // non-generic IEnumerable fallback is not supported
        }

        throw new InvalidOperationException(
            $"Cannot find bridge navigation on '{outerType.Name}' to '{declaringClr.Name}'.");
    }

// ② 新增：外层 collection include + 内层 nested include（Posts.ThenInclude(Comments)）一次性 fixup
// 说明：
// - TOuter: Blog
// - TInner: BlogPost
// - TNestedDecl: BlogPost (declaring of Comments)
// - TNestedTarget: PostComment
    private static TOuter IncludeCollectionFixupWithNested<TOuter, TInner, TNestedDecl, TNestedTarget>(
        TOuter outer,
        List<TInner> inners,
        PropertyInfo outerNavProp, // Blog.Posts
        PropertyInfo? outerInverseProp, // BlogPost.Blog
        PropertyInfo nestedNavProp, // BlogPost.Comments
        PropertyInfo? nestedInverseProp, // PostComment.Post
        string nestedFkPropName, // BlogPostId
        string nestedPkPropName, // Id
        IQueryable<TNestedTarget> nestedTargetQuery)
        where TOuter : class
        where TInner : class
        where TNestedDecl : class
        where TNestedTarget : class
    {
        // 1) outer collection fixup: Blog.Posts = inners, and set BlogPost.Blog = Blog
        // reuse your existing IncludeCollectionFixup if you want, but inline is fine:
        SetCollectionProperty(outer, outerNavProp, inners);

        if (outerInverseProp != null)
        {
            foreach (var i in inners)
            {
                if (i == null) continue;
                outerInverseProp.SetValue(i, outer);
            }
        }

        // 2) nested fixup: for each Post, fill Comments
        foreach (var inner in inners)
        {
            if (inner == null) continue;

            // only apply if inner is the declaring type (BlogPost)
            if (inner is not TNestedDecl decl) continue;

            var pkObj = decl.GetType().GetProperty(nestedPkPropName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(decl);

            // build predicate: t => t.FK == pkObj  (pkObj as constant)
            var tParam = Expression.Parameter(typeof(TNestedTarget), "t");
            var fkMember = Expression.PropertyOrField(tParam, nestedFkPropName);

            Expression pkConst = Expression.Constant(pkObj, fkMember.Type);
            if (pkObj != null && pkObj.GetType() != fkMember.Type)
            {
                pkConst = Expression.Convert(Expression.Constant(pkObj), fkMember.Type);
            }

            var eq = Expression.Equal(fkMember, pkConst);
            var pred = Expression.Lambda<Func<TNestedTarget, bool>>(eq, tParam);

            var filtered = Queryable.Where(nestedTargetQuery, pred);
            var list = Enumerable.ToList(filtered);

            // set BlogPost.Comments = list
            SetCollectionProperty(decl, nestedNavProp, list);

            // inverse: Comment.Post = BlogPost
            if (nestedInverseProp != null)
            {
                foreach (var c in list)
                {
                    if (c == null) continue;
                    nestedInverseProp.SetValue(c, decl);
                }
            }
        }

        return outer;
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

// ④（可选但建议保留）：真正独立的 ThenInclude expression fixup（你前面 B1 分支会用到）
    private static TOuter ThenIncludeFixup<TOuter, TDeclaring, TTarget>(
        TOuter outer,
        PropertyInfo bridgeProp, // Blog.Posts
        PropertyInfo navPropOnDeclaring, // BlogPost.Comments
        PropertyInfo? inversePropOnTarget, // PostComment.Post
        string fkPropName, // BlogPostId
        string pkPropName, // Id
        IQueryable<TTarget> targetQuery)
        where TOuter : class
        where TDeclaring : class
        where TTarget : class
    {
        // bridgeProp can be ref or collection
        var bridgeVal = bridgeProp.GetValue(outer);

        if (bridgeVal == null) return outer;

        // collection bridge: iterate each declaring entity
        if (bridgeVal is IEnumerable<TDeclaring> decls)
        {
            foreach (var decl in decls)
            {
                if (decl == null) continue;

                var pkObj = decl.GetType().GetProperty(pkPropName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(decl);

                var tParam = Expression.Parameter(typeof(TTarget), "t");
                var fkMember = Expression.PropertyOrField(tParam, fkPropName);

                Expression pkConst = Expression.Constant(pkObj, fkMember.Type);
                if (pkObj != null && pkObj.GetType() != fkMember.Type)
                    pkConst = Expression.Convert(Expression.Constant(pkObj), fkMember.Type);

                var eq = Expression.Equal(fkMember, pkConst);
                var pred = Expression.Lambda<Func<TTarget, bool>>(eq, tParam);

                var list = Enumerable.ToList(Queryable.Where(targetQuery, pred));

                // set declaring nav
                SetCollectionProperty(decl, navPropOnDeclaring, list);

                if (inversePropOnTarget != null)
                {
                    foreach (var t in list)
                    {
                        if (t == null) continue;
                        inversePropOnTarget.SetValue(t, decl);
                    }
                }
            }

            return outer;
        }

        // reference bridge (rare for ThenInclude): treat as single declaring entity
        if (bridgeVal is TDeclaring one)
        {
            // if nav is collection: same as above but single
            var pkObj = one.GetType()
                .GetProperty(pkPropName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(one);

            var tParam = Expression.Parameter(typeof(TTarget), "t");
            var fkMember = Expression.PropertyOrField(tParam, fkPropName);

            Expression pkConst = Expression.Constant(pkObj, fkMember.Type);
            if (pkObj != null && pkObj.GetType() != fkMember.Type)
                pkConst = Expression.Convert(Expression.Constant(pkObj), fkMember.Type);

            var eq = Expression.Equal(fkMember, pkConst);
            var pred = Expression.Lambda<Func<TTarget, bool>>(eq, tParam);

            var list = Enumerable.ToList(Queryable.Where(targetQuery, pred));
            SetCollectionProperty(one, navPropOnDeclaring, list);

            if (inversePropOnTarget != null)
            {
                foreach (var t in list)
                {
                    if (t == null) continue;
                    inversePropOnTarget.SetValue(t, one);
                }
            }

            return outer;
        }

        return outer;
    }
}