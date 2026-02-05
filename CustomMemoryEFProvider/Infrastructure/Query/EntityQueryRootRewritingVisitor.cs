using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CustomMemoryEFProvider.Infrastructure.Query;

// Replaces EF's EntityQueryRootExpression (ctx.Set<T> root) with provider queryable source.
public sealed class EntityQueryRootRewritingVisitor : ExpressionVisitor
{
    private readonly Func<Type, QueryContext, IEntityType, IQueryable> _rootFactory;
    private readonly ParameterExpression _qcParam;

    public EntityQueryRootRewritingVisitor(Func<Type, QueryContext, IEntityType, IQueryable> rootFactory)
    {
        _rootFactory = rootFactory;
        _qcParam = QueryCompilationContext.QueryContextParameter; // reuse EF param
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is EntityQueryRootExpression eqr)
        {
            var et = eqr.EntityType;
            var clrType = et.ClrType;

            // Call rootFactory(clrType, qc, et) at runtime.
            var invoke = _rootFactory.GetType().GetMethod("Invoke")!;

            var call = Expression.Call(
                Expression.Constant(_rootFactory),
                invoke,
                Expression.Constant(clrType, typeof(Type)),
                _qcParam,
                Expression.Constant(et, typeof(IEntityType)));

            // ✅ IMPORTANT: make the expression type IQueryable<T> not IQueryable
            var typedQueryableType = typeof(IQueryable<>).MakeGenericType(clrType);
            return Expression.Convert(call, typedQueryableType);
        }

        return base.VisitExtension(node);
    }
}