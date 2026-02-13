using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CustomMemoryEFProvider.Infrastructure;

public sealed class CustomMemoryValueGeneratorSelector : ValueGeneratorSelector
{
    public CustomMemoryValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies)
        : base(dependencies) { }

    protected override ValueGenerator? FindForType(IProperty property, ITypeBase typeBase, Type clrType)
    {
        if (clrType == typeof(int)
            && property.ValueGenerated == ValueGenerated.OnAdd
            && property.IsPrimaryKey())
        {
            var key = $"{typeBase.Name}.{property.Name}";
            return new CustomMemoryIntValueGenerator(key);
        }

        return base.FindForType(property, typeBase, clrType);
    }
}