using CustomMemoryEFProvider.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public sealed class CustomMemoryShapedQueryCompilingExpressionVisitorFactory
    : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _deps;
    private readonly IMemoryDatabase _db;
    private readonly SnapshotValueBufferFactory _vbFactory;

    public CustomMemoryShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies deps,
        IMemoryDatabase db,
        SnapshotValueBufferFactory vbFactory)
    {
        _deps = deps;
        _db = db;
        _vbFactory = vbFactory;
    }

    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new CustomMemoryShapedQueryCompilingExpressionVisitor(_deps, queryCompilationContext, _db, _vbFactory);
}