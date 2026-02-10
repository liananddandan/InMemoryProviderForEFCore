using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CustomMemoryEFProvider.Infrastructure.Query;

// Replaces EF's EntityQueryRootExpression (ctx.Set<T> root) with provider queryable source.
public sealed class EntityQueryRootRewritingVisitor : ExpressionVisitor
{
    private readonly Func<Type, Expression, IEntityType, Expression> _rootFactory;
    private readonly Expression _queryContextExpr;

    public EntityQueryRootRewritingVisitor(Expression queryContextExpr,
        Func<Type, Expression, IEntityType, Expression> rootFactory)
    {
        _rootFactory = rootFactory;
        _queryContextExpr = queryContextExpr; // reuse EF param
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node is EntityQueryRootExpression root)
        {
            // ✅直接把 QueryContextParameter 传进去，让 outer/inner 共用一个 StateManager
            return _rootFactory(root.EntityType.ClrType, _queryContextExpr, root.EntityType);
        }

        return base.VisitExtension(node);
    }
}