using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using CustomMemoryEFProvider.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

/// <summary>
/// Smoke test for QueryRows pipeline + per-execution identity resolution.
/// Uses SelectMany to force duplicate outer entity keys in a single execution.
/// Also asserts QueryRows was used (not Query) via diagnostics counters.
/// </summary>
public static class QueryRowsSmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== QUERYROWS SMOKE TEST (SELECTMANY) ===");

        ProviderDiagnostics.Reset();

        // ---------- Seed ----------
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var p1 = new BlogPost { Title = "P1" };
            var p2 = new BlogPost { Title = "P2" };

            // Make sure p1 has 2 comments => p1 will appear twice after SelectMany
            var c11 = new PostComment { Content = "C11", Post = p1 };
            var c12 = new PostComment { Content = "C12", Post = p1 };
            var c21 = new PostComment { Content = "C21", Post = p2 };

            ctx.AddRange(p1, p2, c11, c12, c21);
            ctx.SaveChanges();
        }

        // ---------- Act ----------
        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Force duplicate outer key in a single execution:
            // p1 appears once per comment => two rows share the same p1.Id
            var rows = ctx.Set<BlogPost>()
                .OrderBy(p => p.Id)
                .SelectMany(
                    p => p.Comments,
                    (p, c) => new { p, c })
                .ToList();

            // ---------- Assert: pipeline used QueryRows ----------
            Console.WriteLine($"Diagnostics: QueryCalled={ProviderDiagnostics.QueryCalled}, QueryRowsCalled={ProviderDiagnostics.QueryRowsCalled}, MaterializeCalled={ProviderDiagnostics.MaterializeCalled}");

            // You want this test to PROVE you're not using Query anymore.
            // Adjust expected values based on how you wire the pipeline,
            // but the key is: QueryRowsCalled should be > 0.
            if (ProviderDiagnostics.QueryRowsCalled <= 0)
                throw new Exception("Expected QueryRows to be used, but QueryRowsCalled == 0.");

            // If Query should no longer be used at all, enforce it:
            // (If you still have Query in some fallback path, relax this assertion.)
            if (ProviderDiagnostics.QueryCalled > 0)
                throw new Exception("Query was used unexpectedly. The pipeline should be QueryRows-based.");

            // ---------- Assert: per-execution identity resolution ----------
            var p1Rows = rows.Where(x => x.p.Title == "P1").ToList();
            if (p1Rows.Count < 2)
                throw new Exception($"Expected P1 to appear at least twice, but got {p1Rows.Count}");

            var a = p1Rows[0].p;
            var b = p1Rows[1].p;

            Console.WriteLine($"P1 appears {p1Rows.Count} times.");
            Console.WriteLine($"ReferenceEquals(p1_row0, p1_row1)={ReferenceEquals(a, b)}");
            Console.WriteLine($"HashCodes: a={a.GetHashCode()}, b={b.GetHashCode()}");

            if (!ReferenceEquals(a, b))
                throw new Exception("Identity resolution FAILED: same key in same execution produced different instances.");

            Console.WriteLine("✅ QueryRows path verified + per-execution identity resolution OK.");
        }

        Console.WriteLine("=== END ===");
    }
}