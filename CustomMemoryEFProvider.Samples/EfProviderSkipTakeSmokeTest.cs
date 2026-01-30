using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderSkipTakeSmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== SKIP / TAKE SMOKE TEST ===");

        // ---------- Seed ----------
        // 注意：如果你的 provider 不会清库，多次运行会累加。
        // 工业级做法：在 Program 里支持 clearOnCreate 或提供 db.ClearAllTables()。
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            ctx.AddRange(
                new TestEntity { Name = "Seed-1" },
                new TestEntity { Name = "Seed-2" },
                new TestEntity { Name = "Seed-3" },
                new TestEntity { Name = "Seed-4" },
                new TestEntity { Name = "Seed-5" }
            );
            ctx.SaveChanges();
        }

        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var set = ctx.Set<TestEntity>();

            var total = set.Count();
            Console.WriteLine($"[Diag] total count = {total}");
            Assert(total >= 5, $"Expected total >= 5 after seeding, but got {total}. (Are you clearing DB between runs?)");

            // Snapshot the whole table once (for relationship asserts).
            // We do NOT assume ordering.
            var allEntities = set.ToList();
            Assert(allEntities.Count == total, $"Sanity check failed: ToList count {allEntities.Count} != Count {total}");

            // ---------- Take ----------
            var take2 = set.Take(2).ToList();
            Assert(take2.Count == Math.Min(2, total), $"[Take 2] expected {Math.Min(2, total)} but got {take2.Count}");
            Assert(IsSubset(take2, allEntities), "[Take 2] result contains entity not present in base set");

            var take0 = set.Take(0).ToList();
            Assert(take0.Count == 0, $"[Take 0] expected 0 but got {take0.Count}");

            var takeBig = set.Take(100).ToList();
            Assert(takeBig.Count == total, $"[Take big] expected {total} but got {takeBig.Count}");
            Assert(SameSet(takeBig, allEntities), "[Take big] expected same set as base query");

            // ---------- Skip ----------
            var skip2 = set.Skip(2).ToList();
            var expectedSkip2 = Math.Max(0, total - 2);
            Assert(skip2.Count == expectedSkip2, $"[Skip 2] expected {expectedSkip2} but got {skip2.Count}");
            Assert(IsSubset(skip2, allEntities), "[Skip 2] result contains entity not present in base set");

            var skip0 = set.Skip(0).ToList();
            Assert(skip0.Count == total, $"[Skip 0] expected {total} but got {skip0.Count}");
            Assert(SameSet(skip0, allEntities), "[Skip 0] expected same set as base query");

            var skipBig = set.Skip(100).ToList();
            Assert(skipBig.Count == 0, $"[Skip big] expected 0 but got {skipBig.Count}");

            // ---------- Skip + Take ----------
            var skipTake = set.Skip(1).Take(2).ToList();
            var expectedSkipTake = Math.Min(2, Math.Max(0, total - 1));
            Assert(skipTake.Count == expectedSkipTake, $"[Skip 1 Take 2] expected {expectedSkipTake} but got {skipTake.Count}");
            Assert(IsSubset(skipTake, allEntities), "[Skip 1 Take 2] result contains entity not present in base set");

            var takeSkip = set.Take(2).Skip(1).ToList();
            var expectedTakeSkip = Math.Max(0, Math.Min(2, total) - 1);
            Assert(takeSkip.Count == expectedTakeSkip, $"[Take 2 Skip 1] expected {expectedTakeSkip} but got {takeSkip.Count}");
            Assert(IsSubset(takeSkip, allEntities), "[Take 2 Skip 1] result contains entity not present in base set");

            // ---------- Where + Skip + Take ----------
            // 这里只断言 count 与 subset（因为没 OrderBy）。
            var whereBase = set.Where(x => x.Id > 1).ToList();
            var whereSkipTake = set
                .Where(x => x.Id > 1)
                .Skip(1)
                .Take(2)
                .ToList();

            var expectedWhereSkipTake = Math.Min(2, Math.Max(0, whereBase.Count - 1));
            Assert(whereSkipTake.Count == expectedWhereSkipTake,
                $"[Where + Skip + Take] expected {expectedWhereSkipTake} but got {whereSkipTake.Count}");
            Assert(IsSubset(whereSkipTake, whereBase),
                "[Where + Skip + Take] result is not a subset of the base Where(x.Id>1) result");

            Console.WriteLine($"[Where + Skip + Take] count={whereSkipTake.Count}");
            foreach (var e in whereSkipTake)
                Console.WriteLine($"  - id={e.Id} name={e.Name}");

            // ---------- Select + Skip + Take ----------
            // 重要：这会测试 projection 后 element type 推进是否正确。
            // 这里只对数量和“名字来自原始集合”做断言，不对顺序断言。
            var allNames = allEntities.Select(e => e.Name).ToList();
            var projected = set
                .Select(x => x.Name)
                .Skip(1)
                .Take(2)
                .ToList();

            var expectedProjected = Math.Min(2, Math.Max(0, allNames.Count - 1));
            Assert(projected.Count == expectedProjected,
                $"[Select + Skip + Take] expected {expectedProjected} but got {projected.Count}");

            foreach (var name in projected)
            {
                Assert(allNames.Contains(name),
                    $"[Select + Skip + Take] projected name '{name}' not found in base set names");
            }

            Console.WriteLine($"[Select + Skip + Take] count={projected.Count}");
            foreach (var name in projected)
                Console.WriteLine($"  - name={name}");

            // ---------- Empty ----------
            var empty = set
                .Where(x => x.Id < 0)
                .Skip(2)
                .Take(3)
                .ToList();

            Assert(empty.Count == 0, $"[Empty Skip/Take] expected 0 but got {empty.Count}");

            Console.WriteLine("✅ SKIP/TAKE TEST PASSED");
        }

        Console.WriteLine("=== END SKIP / TAKE TEST ===");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Subset check by key (Id). This avoids reference equality issues across contexts.
    private static bool IsSubset(IEnumerable<TestEntity> subset, IEnumerable<TestEntity> superset)
    {
        var sup = new HashSet<int>(superset.Select(x => x.Id));
        foreach (var e in subset)
        {
            if (!sup.Contains(e.Id)) return false;
        }
        return true;
    }

    // Same set check by key (Id).
    private static bool SameSet(IEnumerable<TestEntity> a, IEnumerable<TestEntity> b)
    {
        var setA = new HashSet<int>(a.Select(x => x.Id));
        var setB = new HashSet<int>(b.Select(x => x.Id));
        return setA.SetEquals(setB);
    }
}