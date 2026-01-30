namespace CustomMemoryEFProvider.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

public static class ExpressionDebug
{
    public static void DumpExpression(string title, Expression expr)
    {
        Console.WriteLine($"==== {title} ====");
        Console.WriteLine($"Type: {expr.Type}");
        Console.WriteLine($"NodeType: {expr.NodeType}");
        Console.WriteLine($"CLR: {expr.GetType().FullName}");
        Console.WriteLine(expr.ToString());
        Console.WriteLine();

        var exts = FindExtensions(expr).ToList();
        Console.WriteLine($"[Extension nodes] count = {exts.Count}");
        for (int i = 0; i < exts.Count; i++)
        {
            var e = exts[i];
            Console.WriteLine($"  #{i} NodeType={e.NodeType} Type={e.Type} CLR={e.GetType().FullName}");
            Console.WriteLine($"     CanReduce={e.CanReduce}");
            // 注意：有些 ToString 可能很长
            Console.WriteLine($"     ToString={e}");
        }
        Console.WriteLine("==== END ====");
    }

    public static IEnumerable<Expression> FindExtensions(Expression expr)
    {
        var collector = new ExtensionCollector();
        collector.Visit(expr);
        return collector.Extensions;
    }

    private sealed class ExtensionCollector : ExpressionVisitor
    {
        private readonly List<Expression> _extensions = new();

        public IReadOnlyList<Expression> Extensions => _extensions;

        protected override Expression VisitExtension(Expression node)
        {
            // 记录一下
            _extensions.Add(node);

            // 重点：不可 Reduce 的 extension 不要去 VisitChildren()
            // 否则 Expression.VisitChildren 会 ReduceAndCheck -> must be reducible node
            if (!node.CanReduce)
                return node;

            // 可 reduce 的，按默认逻辑走也行（或者你也可以只记录不深入）
            return base.VisitExtension(node);
        }

        public override Expression Visit(Expression? node)
        {
            if (node is null) return null!;
            return base.Visit(node);
        }
    }
}