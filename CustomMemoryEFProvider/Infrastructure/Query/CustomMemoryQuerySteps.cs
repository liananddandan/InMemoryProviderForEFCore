using System.Linq.Expressions;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public abstract record CustomMemoryQueryStep(CustomMemoryQueryStepKind Kind);

public sealed record WhereStep(LambdaExpression Predicate)
    : CustomMemoryQueryStep(CustomMemoryQueryStepKind.Where);

public sealed record SelectStep(LambdaExpression Selector)
    : CustomMemoryQueryStep(CustomMemoryQueryStepKind.Select);

public sealed record OrderStep(CustomMemoryQueryStepKind Kind, LambdaExpression KeySelector)
    : CustomMemoryQueryStep(Kind)
{
    public bool ThenBy =>
        Kind is CustomMemoryQueryStepKind.ThenBy or CustomMemoryQueryStepKind.ThenByDescending;

    public bool Descending =>
        Kind is CustomMemoryQueryStepKind.OrderByDescending or CustomMemoryQueryStepKind.ThenByDescending;
}

public sealed record SkipStep(Expression Count)
    : CustomMemoryQueryStep(CustomMemoryQueryStepKind.Skip);

public sealed record TakeStep(Expression Count)
    : CustomMemoryQueryStep(CustomMemoryQueryStepKind.Take);

public sealed record LeftJoinStep(
    CustomMemoryQueryExpression innerQuery,
    LambdaExpression outerKeySelector,
    LambdaExpression innerKeySelector,
    LambdaExpression resultSelector)
    : CustomMemoryQueryStep(CustomMemoryQueryStepKind.LeftJoin)
{
    public CustomMemoryQueryExpression InnerQuery { get; } =
        innerQuery ?? throw new ArgumentNullException(nameof(innerQuery));

    public LambdaExpression OuterKeySelector { get; } =
        outerKeySelector ?? throw new ArgumentNullException(nameof(outerKeySelector));

    public LambdaExpression InnerKeySelector { get; } =
        innerKeySelector ?? throw new ArgumentNullException(nameof(innerKeySelector));

    public LambdaExpression ResultSelector { get; } =
        resultSelector ?? throw new ArgumentNullException(nameof(resultSelector));
}

public sealed record SelectManyStep(
    LambdaExpression CollectionSelector,
    LambdaExpression ResultSelector
) : CustomMemoryQueryStep(CustomMemoryQueryStepKind.SelectMany);