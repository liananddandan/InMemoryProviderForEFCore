namespace CustomMemoryEFProvider.Core.Implementations;

public sealed class ScalarSnapshot
{
    public required Dictionary<string, object?> ValuesByName { get; init; }
}