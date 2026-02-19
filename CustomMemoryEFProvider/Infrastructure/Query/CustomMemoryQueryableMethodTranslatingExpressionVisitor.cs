using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public sealed class CustomMemoryQueryableMethodTranslatingExpressionVisitor
    : QueryableMethodTranslatingExpressionVisitor
{
    private readonly QueryableMethodTranslatingExpressionVisitorDependencies _deps;
    private readonly QueryCompilationContext _qcc;

    public CustomMemoryQueryableMethodTranslatingExpressionVisitor(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        bool subquery = false)
        : base(dependencies, queryCompilationContext, subquery)
    {
        _deps = dependencies;
        _qcc = queryCompilationContext;
    }

    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor()
        => new CustomMemoryQueryableMethodTranslatingExpressionVisitor(_deps, _qcc, subquery: true);

    // 你可以先不改 Translate，本类的抽象 TranslateXxx 会在 pipeline 中被调用
    private static Exception NotSupported(string method)
        => new NotSupportedException($"CustomMemory provider doesn't translate '{method}' yet. " +
                                     $"Use AsEnumerable()/ToList() after materialization, or implement translation.");

    // 示例：其中一个抽象方法（你的 IDE 会帮你生成一大串）
    protected override ShapedQueryExpression? TranslateUnion(ShapedQueryExpression source1,
        ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateWhere(ShapedQueryExpression source, LambdaExpression predicate)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
        {
            return null;
        }

        var q2 = q.AddStep(new WhereStep(predicate));

        return new ShapedQueryExpression(q2, source.ShaperExpression);
    }

    /// <summary>
    /// Entry point from QueryRoot to ShapedQuery.
    /// For a full table scan:
    /// QueryExpression = CustomMemoryQueryExpression(entityType)
    /// ShaperExpression = CustomMemoryEntityShaperExpression(entityType)
    /// </summary>
    protected override ShapedQueryExpression? CreateShapedQueryExpression(IEntityType entityType)
    {
        if (entityType == null) throw new ArgumentNullException(nameof(entityType));
        var queryExpression = new CustomMemoryQueryExpression(entityType);
        var shaperExpression = new CustomMemoryEntityShaperExpression(entityType);
        return new ShapedQueryExpression(queryExpression, shaperExpression);
    }

    protected override ShapedQueryExpression? TranslateAll(ShapedQueryExpression source, LambdaExpression predicate)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
        {
            return null;
        }

        q = q.WithTerminalOperator(CustomMemoryTerminalOperator.All, predicate);
        var scalarShaper = new CustomMemoryScalarShaperExpression(typeof(bool));

        return new ShapedQueryExpression(q, scalarShaper);
    }

    protected override ShapedQueryExpression? TranslateAny(ShapedQueryExpression source, LambdaExpression? predicate)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
        {
            return null;
        }

        if (predicate != null)
        {
            q = q.AddStep(new WhereStep(predicate));
        }

        q = q.WithTerminalOperator(CustomMemoryTerminalOperator.Any);

        var scalarShaper = new CustomMemoryScalarShaperExpression(typeof(bool));

        return new ShapedQueryExpression(q, scalarShaper);
    }

    protected override ShapedQueryExpression? TranslateAverage(ShapedQueryExpression source, LambdaExpression? selector,
        Type resultType)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q) return null;

        q = q.WithTerminalOperator(CustomMemoryTerminalOperator.Average, selector);

        return new ShapedQueryExpression(q, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateCast(ShapedQueryExpression source, Type castType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateConcat(ShapedQueryExpression source1,
        ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateContains(ShapedQueryExpression source, Expression item)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateCount(
        ShapedQueryExpression source,
        LambdaExpression? predicate)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression customQuery)
        {
            return null;
        }

        if (predicate != null)
        {
            customQuery = customQuery.AddStep(new WhereStep(predicate));
        }

        var countedQuery = customQuery.WithTerminalOperator(CustomMemoryTerminalOperator.Count);

        var scalarShaper = new CustomMemoryScalarShaperExpression(typeof(int));

        return new ShapedQueryExpression(countedQuery, scalarShaper);
    }

    protected override ShapedQueryExpression? TranslateDefaultIfEmpty(ShapedQueryExpression source,
        Expression? defaultValue)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateDistinct(ShapedQueryExpression source)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateElementAtOrDefault(ShapedQueryExpression source,
        Expression index, bool returnDefault)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateExcept(ShapedQueryExpression source1,
        ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateFirstOrDefault(ShapedQueryExpression source,
        LambdaExpression? predicate,
        Type returnType, bool returnDefault)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
        {
            return null;
        }

        if (predicate != null)
        {
            q = q.AddStep(new WhereStep(predicate));
        }

        q = q.WithTerminalOperator(returnDefault
            ? CustomMemoryTerminalOperator.FirstOrDefault
            : CustomMemoryTerminalOperator.First);

        return new ShapedQueryExpression(q, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateGroupBy(ShapedQueryExpression source,
        LambdaExpression keySelector,
        LambdaExpression? elementSelector, LambdaExpression? resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateGroupJoin(ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector, LambdaExpression innerKeySelector, LambdaExpression resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateIntersect(ShapedQueryExpression source1,
        ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateJoin(ShapedQueryExpression outer, ShapedQueryExpression inner,
        LambdaExpression outerKeySelector, LambdaExpression innerKeySelector, LambdaExpression resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateLeftJoin(ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector, LambdaExpression innerKeySelector, LambdaExpression resultSelector)
    {
        if (outer.QueryExpression is not CustomMemoryQueryExpression oq)
            return null;

        if (inner.QueryExpression is not CustomMemoryQueryExpression iq)
            return null;

        // 1) record as a step (keep original LINQ order)
        var oq2 = oq.AddStep(new LeftJoinStep(iq, outerKeySelector, innerKeySelector, resultSelector));

        // 2) build new shaper by applying resultSelector to (outerShaper, innerShaper)
        // resultSelector: (outerElem, innerElem) => TResult
        // We replace its parameters with the two shaper expressions.
        var newShaper = ReplacingExpressionVisitor.Replace(
            resultSelector.Parameters[0], outer.ShaperExpression,
            ReplacingExpressionVisitor.Replace(
                resultSelector.Parameters[1], inner.ShaperExpression,
                resultSelector.Body));

        return new ShapedQueryExpression(oq2, newShaper);
    }

    protected override ShapedQueryExpression? TranslateLastOrDefault(ShapedQueryExpression source,
        LambdaExpression? predicate,
        Type returnType, bool returnDefault)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateLongCount(ShapedQueryExpression source,
        LambdaExpression? predicate)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q) return null;

        // 如果有 predicate，把它也记进去（LongCount(predicate)）
        q = q.WithTerminalOperator(CustomMemoryTerminalOperator.LongCount, predicate);

        return new ShapedQueryExpression(q, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateMax(ShapedQueryExpression source, LambdaExpression? selector,
        Type resultType)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;

        q = q.WithTerminalOperator(CustomMemoryTerminalOperator.Max, selector);

        return new ShapedQueryExpression(q, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateMin(ShapedQueryExpression source, LambdaExpression? selector,
        Type resultType)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;

        q = q.WithTerminalOperator(CustomMemoryTerminalOperator.Min, selector);

        return new ShapedQueryExpression(q, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateOfType(ShapedQueryExpression source, Type resultType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateOrderBy(ShapedQueryExpression source,
        LambdaExpression keySelector, bool ascending)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;
        var kind = ascending ? CustomMemoryQueryStepKind.OrderBy : CustomMemoryQueryStepKind.OrderByDescending;
        // OrderBy: descending = !ascending, thenBy = false
        var q2 = q.AddStep(new OrderStep(kind, keySelector));

        return new ShapedQueryExpression(q2, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateReverse(ShapedQueryExpression source)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSelect(ShapedQueryExpression source, LambdaExpression selector)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;

        // 记录 projection（工业级思路：翻译阶段只“描述”，不执行）
        var q2 = q.AddStep(new SelectStep(selector));

        // Shaper 的 Type 必须变成 selector.ReturnType，否则 EF 认为结果还是 entity
        var newShaper = new CustomMemoryProjectionShaperExpression(selector.ReturnType);

        return new ShapedQueryExpression(q2, newShaper);
    }

    protected override ShapedQueryExpression? TranslateSelectMany(ShapedQueryExpression source,
        LambdaExpression collectionSelector,
        LambdaExpression resultSelector)
    {
    
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;

        var q2 = q.AddStep(new SelectManyStep(collectionSelector, resultSelector));

        // Shaper 类型必须变成 TResult
        var newShaper = new CustomMemoryProjectionShaperExpression(resultSelector.ReturnType);

        return new ShapedQueryExpression(q2, newShaper);
    }

    protected override ShapedQueryExpression? TranslateSelectMany(ShapedQueryExpression source,
        LambdaExpression selector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSingleOrDefault(ShapedQueryExpression source,
        LambdaExpression? predicate,
        Type returnType, bool returnDefault)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
        {
            return null;
        }

        if (predicate != null)
        {
            q = q.AddStep(new WhereStep(predicate));
        }

        var op = returnDefault
            ? CustomMemoryTerminalOperator.SingleOrDefault
            : CustomMemoryTerminalOperator.Single;
        var q2 = q.WithTerminalOperator(op);

        return new ShapedQueryExpression(q2, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateSkip(ShapedQueryExpression source, Expression count)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;

        // record skip
        var q2 = q.AddStep(new SkipStep(count));

        return new ShapedQueryExpression(q2, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateSkipWhile(ShapedQueryExpression source,
        LambdaExpression predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSum(ShapedQueryExpression source, LambdaExpression? selector,
        Type resultType)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q) return null;

        q = q.WithTerminalOperator(CustomMemoryTerminalOperator.Sum, selector);

        return new ShapedQueryExpression(q, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateTake(ShapedQueryExpression source, Expression count)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;

        var q2 = q.AddStep(new TakeStep(count));

        return new ShapedQueryExpression(q2, source.ShaperExpression);
    }

    protected override ShapedQueryExpression? TranslateTakeWhile(ShapedQueryExpression source,
        LambdaExpression predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateThenBy(ShapedQueryExpression source,
        LambdaExpression keySelector, bool ascending)
    {
        if (source.QueryExpression is not CustomMemoryQueryExpression q)
            return null;
        var kind = ascending ? CustomMemoryQueryStepKind.ThenBy : CustomMemoryQueryStepKind.ThenByDescending;

        // ThenBy: descending = !ascending, thenBy = true
        var q2 = q.AddStep(new OrderStep(kind, keySelector));

        return new ShapedQueryExpression(q2, source.ShaperExpression);
    }
    
    // ... 其余 TranslateOrderBy/TranslateJoin/TranslateGroupBy/... 全部先 throw
}