using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure;

public class CustomMemoryQueryContext : QueryContext
{
    public CustomMemoryQueryContext(QueryContextDependencies dependencies) : base(dependencies)
    {
    }
}