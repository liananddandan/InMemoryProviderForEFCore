using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CustomMemoryEFProvider.Infrastructure;


public sealed class CustomMemoryCoreTypeMapping : CoreTypeMapping
{
    public CustomMemoryCoreTypeMapping(Type clrType)
        : base(new CoreTypeMappingParameters(clrType))
    {
    }

    private CustomMemoryCoreTypeMapping(CoreTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override CoreTypeMapping Clone(CoreTypeMappingParameters parameters)
        => new CustomMemoryCoreTypeMapping(parameters);

    public override CoreTypeMapping WithComposedConverter(ValueConverter? converter, ValueComparer? comparer = null,
        ValueComparer? keyComparer = null, CoreTypeMapping? elementMapping = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null)
    {
        return Clone(Parameters.WithComposedConverter(
            converter,
            comparer,
            keyComparer,
            elementMapping,
            jsonValueReaderWriter));
    }
}