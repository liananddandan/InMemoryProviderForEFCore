using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

public static class IncludeExpressionDebug
{
    public static void DumpIncludes(string tag, Expression expr)
    {
        Console.WriteLine($"==== [IncludeScan:{tag}] ====");
        new Walker().Visit(expr);
        Console.WriteLine($"==== [IncludeScan:{tag}] END ====");
    }

    private sealed class Walker : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            if (node == null) return null;

            // ✅ 关键：不进入不可 Reduce 的 Extension，否则 VisitChildren 会触发 ReduceAndCheck()
            if (node.NodeType == ExpressionType.Extension && !node.CanReduce)
            {
                // 你想看就打印一行
                // Console.WriteLine($"[IncludeScan] stop at non-reducible extension: {node.GetType().FullName}");
                return node;
            }

            var clr = node.GetType().FullName;

            if (clr == "Microsoft.EntityFrameworkCore.Query.IncludeExpression")
            {
                Console.WriteLine("---- INCLUDE EXPRESSION FOUND ----");
                Console.WriteLine($"NodeType={node.NodeType} CanReduce={node.CanReduce} Type={node.Type}");
                Console.WriteLine($"Text={node}");

                var navObj =
                    node.GetType().GetProperty("Navigation")?.GetValue(node)
                    ?? node.GetType().GetField("Navigation")?.GetValue(node);

                Console.WriteLine($"Navigation CLR={navObj?.GetType().FullName ?? "null"}");

                if (navObj is Microsoft.EntityFrameworkCore.Metadata.INavigationBase navBase)
                {
                    Console.WriteLine($"Name={navBase.Name}");
                    Console.WriteLine($"IsCollection={navBase.IsCollection}");
                    Console.WriteLine($"Declaring={navBase.DeclaringEntityType.ClrType.FullName}");
                    Console.WriteLine($"Target={navBase.TargetEntityType.ClrType.FullName}");
                    Console.WriteLine($"Inverse={(navBase.Inverse == null ? "null" : navBase.Inverse.Name)}");
                }

                Console.WriteLine("-------------------------------");
            }

            return base.Visit(node);
        }
    }
}