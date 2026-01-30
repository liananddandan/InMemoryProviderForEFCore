using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;


public sealed class CustomMemoryQueryableMethodTranslatingExpressionVisitorFactory
    : IQueryableMethodTranslatingExpressionVisitorFactory
{
    private readonly QueryableMethodTranslatingExpressionVisitorDependencies _deps;

    public CustomMemoryQueryableMethodTranslatingExpressionVisitorFactory(
        QueryableMethodTranslatingExpressionVisitorDependencies deps)
        => _deps = deps;

    public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new CustomMemoryQueryableMethodTranslatingExpressionVisitor(_deps, queryCompilationContext);

    // EF Core 也有 Create(IModel) 重载；你可以直接转调到 QueryCompilationContext 版本或按需实现
    public QueryableMethodTranslatingExpressionVisitor Create(Microsoft.EntityFrameworkCore.Metadata.IModel model)
        => throw new NotSupportedException("Create(IModel) is not supported by this provider yet.");
}