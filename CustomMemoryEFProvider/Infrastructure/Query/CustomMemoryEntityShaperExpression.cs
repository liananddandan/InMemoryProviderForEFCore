using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CustomMemoryEFProvider.Infrastructure.Query;

/// <summary>
/// ShaperExpression: 代表“如何把 QueryExpression 的一行/一个元素变成最终结果对象”
/// 这里先做最小：结果就是 entity 本身（TEntity）
/// 后面 compiling visitor 里你可以把它编译成 identity shaper（x => x）。
/// </summary>
public sealed class CustomMemoryEntityShaperExpression : Expression
{
    public CustomMemoryEntityShaperExpression(IEntityType entityType)
        => EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));

    public IEntityType EntityType { get; }

    public override Type Type => EntityType.ClrType;

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

    public override string ToString()
        => $"CustomMemoryShaper({EntityType.DisplayName()})";
}