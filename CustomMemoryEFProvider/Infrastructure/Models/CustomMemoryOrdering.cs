using System.Linq.Expressions;

namespace CustomMemoryEFProvider.Infrastructure.Models;

public record CustomMemoryOrdering(LambdaExpression KeySelector, bool Descending, bool ThenBy);