using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CustomMemoryEFProvider.Infrastructure;

public sealed class CustomMemoryValueGeneratorSelector : ValueGeneratorSelector
{
    public CustomMemoryValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies)
        : base(dependencies) { }

    protected override ValueGenerator? FindForType(IProperty property, ITypeBase typeBase, Type clrType)
    {
        // 只给需要 OnAdd 的 int key 生成值（避免把普通 int 属性也自动塞值）
        if (clrType == typeof(int)
            && property.ValueGenerated == ValueGenerated.OnAdd
            && property.IsPrimaryKey())
        {
            // 这里的 key 建议至少包含实体类型+属性名；后面你可以再把 DatabaseName 拼进去防串号
            var key = $"{typeBase.Name}.{property.Name}";
            return new CustomMemoryIntValueGenerator(key);
        }

        return base.FindForType(property, typeBase, clrType);
    }
}