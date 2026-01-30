using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public class QueryParameterRewritingVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _queryContextParam;
    private static readonly System.Reflection.PropertyInfo ParameterValuesProp =
        typeof(QueryContext).GetProperty(nameof(QueryContext.ParameterValues))!;

    public QueryParameterRewritingVisitor(ParameterExpression queryContextParam)
    {
        _queryContextParam = queryContextParam;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        // This is the key: EF may introduce parameters like "__id_0" for captured values.
        // Those are NOT the entity lambda parameter, and are not defined in our final lambda,
        // so we rewrite them to read from QueryContext.ParameterValues.
        if (!string.IsNullOrEmpty(node.Name) && node.Name.StartsWith("__", StringComparison.Ordinal))
        {
            // QueryContext.ParameterValues["__id_0"]  -> object?
            var dictExpr = Expression.Property(_queryContextParam, ParameterValuesProp);
            var indexer = dictExpr.Type.GetProperty("Item")!;
            var valueObj = Expression.Property(dictExpr, indexer, Expression.Constant(node.Name));

            // Cast object? -> node.Type
            return Expression.Convert(valueObj, node.Type);
        }

        return base.VisitParameter(node);
    }
}