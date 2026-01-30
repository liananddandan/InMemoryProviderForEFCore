using System.Linq.Expressions;
using System.Reflection;
using CustomMemoryEFProvider.Core.Interfaces;

namespace CustomMemoryEFProvider.Infrastructure.Query;

/// <summary>
/// Rewrites EF Core's EntityQueryRootExpression into our in-memory table IQueryable.
/// Otherwise expression compilation fails with "must be reducible node".
/// </summary>
public sealed class EntityQueryRootRewritingVisitor : ExpressionVisitor
{
    private readonly IMemoryDatabase _db;

    public EntityQueryRootRewritingVisitor(IMemoryDatabase db)
    {
        _db = db;
    }

    protected override Expression VisitExtension(Expression node)
    {
        // EF Core internal query root:
        // Microsoft.EntityFrameworkCore.Query.EntityQueryRootExpression
        if (node != null && node.GetType().FullName == "Microsoft.EntityFrameworkCore.Query.EntityQueryRootExpression")
        {
            // Try get EntityType / IEntityType (metadata) then its ClrType
            var entityTypeObj =
                node.GetType().GetProperty("EntityType")?.GetValue(node)
                ?? node.GetType().GetField("EntityType")?.GetValue(node);

            if (entityTypeObj == null)
                throw new NotSupportedException("EntityQueryRootExpression.EntityType is missing.");

            var clrType =
                entityTypeObj.GetType().GetProperty("ClrType")?.GetValue(entityTypeObj) as Type
                ?? entityTypeObj.GetType().GetField("ClrType")?.GetValue(entityTypeObj) as Type
                ?? throw new NotSupportedException("EntityType.ClrType is missing.");

            return BuildTableQueryExpression(clrType);
        }

        return base.VisitExtension(node);
    }

    private Expression BuildTableQueryExpression(Type clrType)
    {
        // _db.GetTable<TEntity>(Type) -> IMemoryTable<TEntity>
        var getTableOpen = _db.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m =>
                m.Name == "GetTable"
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length == 1);

        var getTable = getTableOpen.MakeGenericMethod(clrType);

        var tableExpr = Expression.Call(
            Expression.Constant(_db),
            getTable,
            Expression.Constant(clrType, typeof(Type)));

        var queryProp = tableExpr.Type.GetProperty("Query")
                        ?? throw new InvalidOperationException($"IMemoryTable<{clrType.Name}> has no Query property.");

        return Expression.Property(tableExpr, queryProp); // IQueryable<TEntity>
    }
}