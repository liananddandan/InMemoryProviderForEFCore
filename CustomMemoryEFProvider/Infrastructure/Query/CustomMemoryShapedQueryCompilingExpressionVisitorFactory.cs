using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public sealed class CustomMemoryShapedQueryCompilingExpressionVisitorFactory
    : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _deps;
    private readonly IMemoryDatabase _db;

    public CustomMemoryShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies deps,
        IMemoryDatabase db)
    {
        _deps = deps;
        _db = db;
    }

    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new CustomMemoryShapedQueryCompilingExpressionVisitor(_deps, queryCompilationContext, _db);
}