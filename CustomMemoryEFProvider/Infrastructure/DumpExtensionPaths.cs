using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

public static class ExpressionPathDebug
{
    public static void DumpExtensionPaths(Expression expr)
    {
        var stack = new Stack<string>();
        new Walker(stack).Visit(expr);
    }

    private sealed class Walker : ExpressionVisitor
    {
        private readonly Stack<string> _stack;
        public Walker(Stack<string> stack) => _stack = stack;

        public override Expression? Visit(Expression? node)
        {
            if (node == null) return null;

            if (node.NodeType == ExpressionType.Extension)
            {
                Console.WriteLine("---- EXTENSION FOUND ----");
                Console.WriteLine("Path: " + string.Join(" -> ", _stack.Reverse()));
                Console.WriteLine($"CLR: {node.GetType().FullName}");
                Console.WriteLine($"Type: {node.Type}");
                Console.WriteLine($"CanReduce: {node.CanReduce}");
                Console.WriteLine($"Text: {node}");

                // ✅ 新增：如果是 IncludeExpression，把 Navigation 打出来
                if (node.GetType().FullName == "Microsoft.EntityFrameworkCore.Query.IncludeExpression")
                {
                    var navObj =
                        node.GetType().GetProperty("Navigation")?.GetValue(node)
                        ?? node.GetType().GetField("Navigation")?.GetValue(node);

                    if (navObj is Microsoft.EntityFrameworkCore.Metadata.INavigationBase navBase)
                    {
                        Console.WriteLine($"[IncludeMeta] Name={navBase.Name} IsCollection={navBase.IsCollection}");
                        Console.WriteLine($"[IncludeMeta] Declaring={navBase.DeclaringEntityType.ClrType.FullName}");
                        Console.WriteLine($"[IncludeMeta] Target={navBase.TargetEntityType.ClrType.FullName}");
                        Console.WriteLine($"[IncludeMeta] Inverse={(navBase.Inverse == null ? "null" : navBase.Inverse.Name)}");
                    }
                }

                Console.WriteLine("-------------------------");

                // ✅ 关键：不要让 base.Visit(node) 继续走 VisitChildren -> ReduceAndCheck
                if (!node.CanReduce) return node;
            }

            return base.Visit(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            // 关键：不可 Reduce 的 Extension 不要交给 base（否则会 ReduceAndCheck -> must be reducible）
            if (!node.CanReduce)
                return node;

            // 可 Reduce 的，交给 base（它会 Reduce 后继续遍历）
            return base.VisitExtension(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var gen = node.Method.IsGenericMethod
                ? "<" + string.Join(",", node.Method.GetGenericArguments().Select(t => t.Name)) + ">"
                : "";

            _stack.Push($"Call:{node.Method.Name}{gen}");
            var res = base.VisitMethodCall(node);
            _stack.Pop();
            return res;
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            _stack.Push("Lambda");
            var res = base.VisitLambda(node);
            _stack.Pop();
            return res;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _stack.Push($"Member:{node.Member.DeclaringType?.Name}.{node.Member.Name}");
            var res = base.VisitMember(node);
            _stack.Pop();
            return res;
        }

        protected override Expression VisitNew(NewExpression node)
        {
            _stack.Push($"New:{node.Type.Name}");
            var res = base.VisitNew(node);
            _stack.Pop();
            return res;
        }
    }
}