using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure;

public sealed class CustomMemoryQueryContextFactory : IQueryContextFactory
{
    private readonly QueryContextDependencies _dependencies;

    public CustomMemoryQueryContextFactory(QueryContextDependencies dependencies)
        => _dependencies = dependencies;

    public QueryContext Create() => new CustomMemoryQueryContext(_dependencies);
}