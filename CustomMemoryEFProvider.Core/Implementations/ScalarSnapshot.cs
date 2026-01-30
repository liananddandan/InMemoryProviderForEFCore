namespace CustomMemoryEFProvider.Core.Implementations;

public sealed class ScalarSnapshot
{
    public required object?[] Values { get; init; } // 按某个 props 顺序存
}