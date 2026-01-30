namespace CustomMemoryEFProvider.Infrastructure.Query;

public enum CustomMemoryQueryStepKind
{
    Where,
    Select,

    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending,

    Skip,
    Take,
    
    LeftJoin,
    SelectMany
}