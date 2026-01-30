using System.Linq.Expressions;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public class CustomMemoryProjectionShaperExpression : Expression
{
    private readonly Type _type;

    public CustomMemoryProjectionShaperExpression(Type type)
    {
        _type = type ?? throw new ArgumentNullException(nameof(type));
    }
    
    public override ExpressionType NodeType => ExpressionType.Extension;
    public override Type Type => _type;
}