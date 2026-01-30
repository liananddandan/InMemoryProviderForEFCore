using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderOrderBySmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== ORDERBY SMOKE TEST ===");

        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            ctx.AddRange(
                new TestEntity { Name = "B" },
                new TestEntity { Name = "A" },
                new TestEntity { Name = "C" },
                new TestEntity { Name = "A" } // duplicate for ThenBy test
            );
            ctx.SaveChanges();
        }

        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var set = ctx.Set<TestEntity>();

            // 1) OrderBy Name ASC, then Id ASC
            var asc = set
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Id)
                .ToList();

            Console.WriteLine("[OrderBy Name ASC, ThenBy Id ASC]");
            foreach (var e in asc)
                Console.WriteLine($"  - id={e.Id} name={e.Name}");

            // 2) OrderBy Name DESC, then Id DESC
            var desc = set
                .OrderByDescending(x => x.Name)
                .ThenByDescending(x => x.Id)
                .ToList();

            Console.WriteLine("[OrderBy Name DESC, ThenBy Id DESC]");
            foreach (var e in desc)
                Console.WriteLine($"  - id={e.Id} name={e.Name}");

            // 3) Combine with Where
            var combo = set
                .Where(x => x.Name != "C")
                .OrderBy(x => x.Name)
                .ThenByDescending(x => x.Id)
                .ToList();

            Console.WriteLine("[Where Name!=C, OrderBy Name ASC, ThenBy Id DESC]");
            foreach (var e in combo)
                Console.WriteLine($"  - id={e.Id} name={e.Name}");
        }

        Console.WriteLine("=== END ===");
    }
}