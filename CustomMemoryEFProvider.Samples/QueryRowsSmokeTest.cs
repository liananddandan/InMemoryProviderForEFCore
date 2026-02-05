using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using CustomMemoryEFProvider.Core.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class QueryRowsSmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== QUERYROWS SMOKE TEST (ROOT QUERY ONLY) ===");

        ProviderDiagnostics.Reset();

        // ---------- Seed ----------
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!ctx.Set<BlogPost>().Any())
            {
                ctx.AddRange(
                    new BlogPost { Title = "P1" },
                    new BlogPost { Title = "P2" }
                );
                ctx.SaveChanges();
            }
        }

        // ---------- Act #1: QueryRows-only + result correctness ----------
        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            ProviderDiagnostics.Reset();

            var list = ctx.Set<BlogPost>()
                .OrderBy(p => p.Id)
                .ToList();

            // 1) 结果正确性：数量 + 排序 + 字段
            if (list.Count != 2)
                throw new Exception($"Expected 2 BlogPosts, but got {list.Count}.");

            if (list[0].Title != "P1" || list[1].Title != "P2")
                throw new Exception($"Unexpected titles: [{string.Join(", ", list.Select(x => x.Title))}]");

            if (list[0].Id <= 0 || list[1].Id <= 0)
                throw new Exception($"Expected generated Ids > 0, got: {list[0].Id}, {list[1].Id}");

            if (list[0].Id >= list[1].Id)
                throw new Exception($"Expected ascending Id order, got: {list[0].Id} then {list[1].Id}");

            Console.WriteLine($"Result: {string.Join(" | ", list.Select(x => $"Id={x.Id},Title={x.Title}"))}");

            // 2) QueryRows-only 验证
            Console.WriteLine(
                $"QueryRowsCalled={ProviderDiagnostics.QueryRowsCalled}, QueryCalled={ProviderDiagnostics.QueryCalled}");
            if (ProviderDiagnostics.QueryRowsCalled <= 0)
                throw new Exception("Expected QueryRows to be used, but QueryRowsCalled == 0.");
            if (ProviderDiagnostics.QueryCalled > 0)
                throw new Exception("Query was used unexpectedly. Must use QueryRows only.");

            Console.WriteLine("✅ QueryRows path verified + results validated.");

            // 注意：到这里为止，这个 ctx 已经被污染（StateManager 已经跟踪 2 个 BlogPost）。
            // 所以 identity 接管测试不要在这个 ctx 上继续做“首次 materialize+track”的断言。
        }

        // ---------- Act #2: Identity Resolution / EF StateManager takeover ----------
        // 用全新的 scope/ctx，保证 StateManager 是干净的
        using (var idScope = rootProvider.CreateScope())
        {
            var ctx = idScope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 确保 ctx 起始没有 tracked entries（有的话就说明别的地方提前触发了 query）
            ctx.ChangeTracker.Clear();

            ProviderDiagnostics.Reset();

            // Q1: 首次查询应该 Miss=2, StartTracking=2
            var q1 = ctx.Set<BlogPost>().OrderBy(x => x.Id).ToList();

            var hit1 = ProviderDiagnostics.IdentityHit;
            var miss1 = ProviderDiagnostics.IdentityMiss;
            var st1 = ProviderDiagnostics.StartTrackingCalled;

            Console.WriteLine($"[Identity Q1] Hit={hit1}, Miss={miss1}, StartTracking={st1}");

            if (miss1 != 2 || st1 != 2)
                throw new Exception("Expected first query to materialize+track 2 entities (Miss=2, StartTracking=2).");

            // Q2: 第二次查询应该全部 Hit，不再 StartTracking
            var q2 = ctx.Set<BlogPost>().OrderBy(x => x.Id).ToList();

            var hit2 = ProviderDiagnostics.IdentityHit - hit1;
            var miss2 = ProviderDiagnostics.IdentityMiss - miss1;
            var st2 = ProviderDiagnostics.StartTrackingCalled - st1;

            var sameRef = ReferenceEquals(q1[0], q2[0]) && ReferenceEquals(q1[1], q2[1]);

            Console.WriteLine($"[Identity Q2 Delta] Hit={hit2}, Miss={miss2}, StartTracking={st2}");
            Console.WriteLine($"IdentityResolution(2-queries): sameRef={sameRef}");

            if (!sameRef)
                throw new Exception("Identity resolution failed: instances not reused within same DbContext.");

            if (hit2 != 2 || miss2 != 0 || st2 != 0)
                throw new Exception("Expected second query to hit identity map only (Hit=2, Miss=0, StartTrackingDelta=0).");

            // 再用 Single 验证同一个 key 返回同一个引用（加固）
            var id1 = q1[0].Id;
            var again = ctx.Set<BlogPost>().Single(p => p.Id == id1);
            if (!ReferenceEquals(q1[0], again))
                throw new Exception("Identity resolution failed: Single() returned a different instance for same key.");

            // ChangeTracker 状态验证
            var tracked = ctx.ChangeTracker.Entries<BlogPost>().ToList();
            if (tracked.Count != 2)
                throw new Exception($"Expected 2 tracked entries, got {tracked.Count}.");

            if (tracked.Any(e => e.State != EntityState.Unchanged))
                throw new Exception("Expected tracked entries to be Unchanged (from query).");

            Console.WriteLine("✅ EF Core identity map takeover verified (StartTracking + identity hits).");
        }

        Console.WriteLine("=== END ===");
    }
}