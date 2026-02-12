using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class IdentityResolutionProblemSmokeTest
{
    public static void Run()
    {
        Console.WriteLine("=== IDENTITY ACROSS QUERIES SMOKE TEST ===");
        using var rootProvider = TestHost.BuildRootProvider(dbName: "ProviderIdentityResoltution_" + Guid.NewGuid().ToString("N"));

        // ---------- Seed (deterministic) ----------
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var blog = new Blog { Name = "Blog-IR" };
            var p1 = new BlogPost { Title = "P1", Blog = blog };
            var p2 = new BlogPost { Title = "P2", Blog = blog };

            ctx.AddRange(blog, p1, p2);
            ctx.SaveChanges();
        }

        // ------------------------------------------------------------
        // A) Same DbContext, tracking (default): should be SAME instance
        // ------------------------------------------------------------
        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Query #1
            var a = ctx.Set<BlogPost>()
                .OrderBy(p => p.Id)
                .First(); // tracking by default

            // Query #2 (same key)
            var b = ctx.Set<BlogPost>()
                .Single(p => p.Id == a.Id); // tracking by default

            Console.WriteLine("[A] Same DbContext + tracking");
            Console.WriteLine($"    a.Id={a.Id}, b.Id={b.Id}");
            Console.WriteLine($"    ReferenceEquals(a,b)={ReferenceEquals(a, b)}");
            Console.WriteLine($"    HashCodes: a={a.GetHashCode()}, b={b.GetHashCode()}");

            if (!ReferenceEquals(a, b))
                Console.WriteLine("❌ Expected TRUE in EF Core tracking mode, but got FALSE.");
            else
                Console.WriteLine("✅ OK (tracking identity map works within same DbContext).");

            // ------------------------------------------------------------
            // B) Same DbContext, AsNoTracking: should be DIFFERENT instances
            // ------------------------------------------------------------
            var c = ctx.Set<BlogPost>()
                .AsNoTracking()
                .Single(p => p.Id == a.Id);

            var d = ctx.Set<BlogPost>()
                .AsNoTracking()
                .Single(p => p.Id == a.Id);

            Console.WriteLine("[B] Same DbContext + AsNoTracking");
            Console.WriteLine($"    c.Id={c.Id}, d.Id={d.Id}");
            Console.WriteLine($"    ReferenceEquals(c,d)={ReferenceEquals(c, d)}");
            Console.WriteLine($"    HashCodes: c={c.GetHashCode()}, d={d.GetHashCode()}");

            if (ReferenceEquals(c, d))
                Console.WriteLine("❌ Expected FALSE in AsNoTracking, but got TRUE.");
            else
                Console.WriteLine("✅ OK (no-tracking produces new instances).");
        }

        // ------------------------------------------------------------
        // C) Different DbContext (different scope), tracking: should be DIFFERENT instances
        // ------------------------------------------------------------
        BlogPost a2;
        using (var scope1 = rootProvider.CreateScope())
        {
            var ctx1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            a2 = ctx1.Set<BlogPost>().OrderBy(p => p.Id).First(); // tracking in ctx1
            Console.WriteLine("[C1] Scope1 fetched a2");
            Console.WriteLine($"     a2.Id={a2.Id}, a2.Hash={a2.GetHashCode()}");
        }

        using (var scope2 = rootProvider.CreateScope())
        {
            var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var b2 = ctx2.Set<BlogPost>().Single(p => p.Id == a2.Id); // tracking in ctx2

            Console.WriteLine("[C2] Scope2 fetched b2 (same key as a2)");
            Console.WriteLine($"     b2.Id={b2.Id}, b2.Hash={b2.GetHashCode()}");
            Console.WriteLine($"     ReferenceEquals(a2,b2)={ReferenceEquals(a2, b2)}");

            if (ReferenceEquals(a2, b2))
                Console.WriteLine("❌ Expected FALSE across DbContext instances, but got TRUE.");
            else
                Console.WriteLine("✅ OK (identity map does NOT cross DbContext).");
        }

        Console.WriteLine("=== END ===");
    }
}