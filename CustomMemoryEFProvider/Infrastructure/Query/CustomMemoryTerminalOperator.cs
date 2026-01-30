namespace CustomMemoryEFProvider.Infrastructure.Query;

public enum CustomMemoryTerminalOperator
{
    None = 0,
    Count = 1,
    Any = 2,
    FirstOrDefault = 3,
    First = 4,
    Single,
    SingleOrDefault,
    All,
    Max,
    Min,
    Sum,
    Average,
    LongCount
}