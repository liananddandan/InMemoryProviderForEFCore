using System.Linq.Expressions;
using CustomMemoryEFProvider.Infrastructure.Models;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CustomMemoryEFProvider.Infrastructure.Query;

/// <summary>
/// QueryExpression: 代表“从某个 entityType 的表里读数据”的 QueryRoot（整表扫描的起点）
/// 这是你自己的表达式节点，后面 compiling visitor 要认识它并把它变成 IEnumerable<TEntity>。
/// </summary>
public class CustomMemoryQueryExpression : Expression
{
    public IEntityType EntityType { get; }
    
    // NEW: pipeline steps in original LINQ order (non-terminal operator)
    public IReadOnlyList<CustomMemoryQueryStep> Steps { get; }
    public CustomMemoryTerminalOperator TerminalOperator { get; }
    // for terminal operators that require a predicate (All/Any with predicate, etc.)
    public LambdaExpression? TerminalPredicate { get; }
    public LambdaExpression? TerminalSelector { get; } // for Min/Max/Sum/Average etc
    
    public CustomMemoryQueryExpression(
        IEntityType entityType,
        IReadOnlyList<CustomMemoryQueryStep>? steps = null,
        CustomMemoryTerminalOperator terminalOperator = CustomMemoryTerminalOperator.None,
        LambdaExpression? terminalPredicate = null,
        LambdaExpression? terminalSelector = null
        )
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        Steps = steps ?? new List<CustomMemoryQueryStep>();
        
        TerminalOperator = terminalOperator;
        TerminalPredicate = terminalPredicate;
        TerminalSelector = terminalSelector;
    }

    public CustomMemoryQueryExpression AddStep(CustomMemoryQueryStep step)
    {
        if (step == null) throw new ArgumentNullException(nameof(step));

        var list = Steps.Count == 0 ? new List<CustomMemoryQueryStep>() : Steps.ToList();
        list.Add(step);

        return new CustomMemoryQueryExpression(
            EntityType, list, TerminalOperator, TerminalPredicate, TerminalSelector);
    }

    public CustomMemoryQueryExpression WithTerminalOperator(CustomMemoryTerminalOperator op,
        LambdaExpression? arg = null)
    {
        // 这里保持你现在的风格：predicate/selector 分开存
        // 注意：Sum/Average/Min/Max 也可能需要 selector，所以不能只给 Min/Max 特例
        // 最简单：按 op 决定放哪一个字段
        LambdaExpression? pred = null;
        LambdaExpression? sel = null;
        switch (op)
        {
            case CustomMemoryTerminalOperator.All:
                pred = arg;
                break;

            // 这些是 selector 型
            case CustomMemoryTerminalOperator.Min:
            case CustomMemoryTerminalOperator.Max:
            case CustomMemoryTerminalOperator.Sum:
            case CustomMemoryTerminalOperator.Average:
                sel = arg;
                break;

            default:
                // Count/Any/First/... 可能没有 arg
                // Count(predicate) / Any(predicate) 这种你若用同一个入口，也可以放 pred
                pred = arg;
                break;
        }

        return new CustomMemoryQueryExpression(
            EntityType, Steps, op, pred, sel);
    }

    public override Type Type => typeof(IEnumerable<>).MakeGenericType(EntityType.ClrType);
    public override ExpressionType NodeType => ExpressionType.Extension;
    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
    public override string ToString() => $"CustomMemoryQuery({EntityType.DisplayName()}, steps={Steps.Count}, terminal={TerminalOperator})";
}