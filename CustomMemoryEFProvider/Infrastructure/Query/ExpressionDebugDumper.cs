using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;
public static class ExpressionDebugDumper
{
    public static void Dump(Expression? expr, string title)
    {
        Console.WriteLine($"=== [EXPR DUMP] {title} ===");

        if (expr == null)
        {
            Console.WriteLine("<null>");
            return;
        }

        Visit(expr, 0);

        Console.WriteLine($"=== [EXPR DUMP END] {title} ===");
    }

    private static void Visit(Expression expr, int indent)
    {
        var pad = new string(' ', indent * 2);

        Console.WriteLine($"{pad}- NodeType={expr.NodeType}, CLR={expr.GetType().Name}, Type={expr.Type.Name}");
        Console.WriteLine($"{pad}  Text: {expr}");

        // 专门识别 IncludeExpression
        if (expr is IncludeExpression ie)
        {
            DumpInclude(ie, indent + 1);
            return;
        }

        switch (expr)
        {
            case LambdaExpression lam:
                Visit(lam.Body, indent + 1);
                break;

            case MethodCallExpression mc:
                foreach (var arg in mc.Arguments)
                    Visit(arg, indent + 1);
                break;

            case UnaryExpression ue:
                Visit(ue.Operand, indent + 1);
                break;

            case BinaryExpression be:
                Visit(be.Left, indent + 1);
                Visit(be.Right, indent + 1);
                break;

            case MemberExpression me when me.Expression != null:
                Visit(me.Expression, indent + 1);
                break;

            default:
                // 处理 Extension Node
                if (expr.NodeType == ExpressionType.Extension)
                {
                    if (expr.CanReduce)
                    {
                        Visit(expr.Reduce(), indent + 1);
                    }
                }
                break;
        }
    }

    private static void DumpInclude(IncludeExpression ie, int indent)
    {
        var pad = new string(' ', indent * 2);

        Console.WriteLine($"{pad}>>> IncludeExpression <<<");
        Console.WriteLine($"{pad}Navigation: {ie.Navigation?.Name}");
        Console.WriteLine($"{pad}IsCollection: {ie.Navigation?.IsCollection}");
        
        Console.WriteLine($"{pad}EntityExpression:");
        Visit(ie.EntityExpression, indent + 1);

        Console.WriteLine($"{pad}NavigationExpression:");
        Visit(ie.NavigationExpression, indent + 1);

        Console.WriteLine($"{pad}--- Private Fields ---");
        foreach (var f in ie.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            var val = f.GetValue(ie);
            Console.WriteLine($"{pad}{f.Name} = {val}");
        }

        Console.WriteLine($"{pad}>>> End IncludeExpression <<<");
    }
}