using Microsoft.EntityFrameworkCore.Storage;

namespace CustomMemoryEFProvider.Infrastructure;

public sealed class CustomMemoryTypeMappingSource : TypeMappingSource
{
    public CustomMemoryTypeMappingSource(TypeMappingSourceDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override CoreTypeMapping? FindMapping(in TypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        return clrType == null ? null : new CustomMemoryCoreTypeMapping(clrType);
    }
}