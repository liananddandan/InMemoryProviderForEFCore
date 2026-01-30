using System.Linq.Expressions;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public class CustomMemoryScalarShaperExpression : Expression
{
    public CustomMemoryScalarShaperExpression(Type scalarType)
    {
        Type = scalarType ?? throw new ArgumentNullException(nameof(scalarType));
    }

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type { get; }
}