using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace CustomMemoryEFProvider.Infrastructure.Query;

public class ExprTreeDumper
{
        public static void Dump(string title, Expression? expr)
    {
        Console.WriteLine($"=== [EXPR TREE] {title} ===");
        if (expr == null)
        {
            Console.WriteLine("<null>");
            Console.WriteLine($"=== [EXPR TREE END] {title} ===");
            return;
        }

        new Visitor().Visit(expr);
        Console.WriteLine($"=== [EXPR TREE END] {title} ===");
    }

    private sealed class Visitor : ExpressionVisitor
    {
        private int _indent;

        private void Line(string text)
        {
            Console.WriteLine(new string(' ', _indent * 2) + text);
        }

        public override Expression? Visit(Expression? node)
        {
            if (node == null) return null;

            Line($"- {node.NodeType}  CLR={node.GetType().Name}  Type={Short(node.Type)}");
            Line($"  Text: {SafeToString(node)}");

            _indent++;
            var r = base.Visit(node);
            _indent--;
            return r;
        }

        protected override Expression VisitExtension(Expression node)
        {
            // 关键：IncludeExpression 在这里能看到
            if (node is IncludeExpression ie)
            {
                Line($"[IncludeExpression]");
                Line($"  Navigation: {ie.Navigation?.Name}");
                Line($"  IsCollection: {ie.Navigation?.IsCollection}");
                Line($"  SetLoaded: {ie.SetLoaded}");

                _indent++;
                Line("EntityExpression:");
                _indent++;
                Visit((Expression?)ie.EntityExpression);
                _indent--;

                Line("NavigationExpression:");
                _indent++;
                Visit((Expression?)ie.NavigationExpression);
                _indent--;

                _indent--;
                return node;
            }

            // 避免 “must be reducible node”
            if (!node.CanReduce)
            {
                Line("[NonReducible Extension] (skip children)");
                return node;
            }

            return base.VisitExtension(node);
        }

        private static string SafeToString(Expression e)
        {
            try { return e.ToString(); }
            catch { return "<ToString() failed>"; }
        }

        private static string Short(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            var name = t.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name[..tick];
            var args = string.Join(",", t.GetGenericArguments().Select(Short));
            return $"{name}<{args}>";
        }
    }
}