namespace CustomEFCoreProvider.Samples;

public sealed class EfProviderSmokeTestOptions
{
    public bool CheckModel { get; init; } = true;
    public bool CheckInternalServices { get; init; } = true;
    public bool CheckValueGenerator { get; init; } = true;

    public bool CrudSingle { get; init; } = true;
    public bool CrudMultiple { get; init; } = true;
    public bool CrudDetached { get; init; } = true;

    // 如果你不想 generator check “消耗一个 id”，设为 false
    public bool ConsumeGeneratorSampleValue { get; init; } = false;
}