using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderImmediateExecutionSmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== IMMEDIATE OPS SMOKE TEST ===");

        int seedMinId;
        int seedMaxId;

        // Seed 5 rows (do NOT assume IDs start from 1)
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var e1 = new TestEntity { Name = "Seed-1" };
            var e2 = new TestEntity { Name = "Seed-2" };
            var e3 = new TestEntity { Name = "Seed-3" };
            var e4 = new TestEntity { Name = "Seed-4" };
            var e5 = new TestEntity { Name = "Seed-5" };

            ctx.AddRange(e1, e2, e3, e4, e5);
            ctx.SaveChanges();

            var ids = new[] { e1.Id, e2.Id, e3.Id, e4.Id, e5.Id };
            seedMinId = ids.Min();
            seedMaxId = ids.Max();

            Console.WriteLine($"[Seed] ids = {string.Join(",", ids.OrderBy(x => x))}");
            Console.WriteLine($"[Seed] minId={seedMinId}, maxId={seedMaxId}");
        }

        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var set = ctx.Set<TestEntity>();

            // Helpful diagnostics: shows what this context can see
            var totalCount = set.Count();
            Console.WriteLine($"[Diag] totalCount={totalCount}");
            Console.WriteLine($"[Diag] minId={set.Min(x => x.Id)}, maxId={set.Max(x => x.Id)}");

            // 1) ToList
            var list = set.ToList();
            Console.WriteLine($"[ToList] count={list.Count}");

            // 2) Count (seed-range)
            var seedRangeCount = set.Count(x => x.Id >= seedMinId && x.Id <= seedMaxId);
            Console.WriteLine($"[Count seed-range] {seedRangeCount} (expected 5)");

            // 3) Any
            var any = set.Any();
            Console.WriteLine($"[Any] {any} (expected True)");

            // 4) First/FirstOrDefault (ordering undefined unless OrderBy)
            var firstOrDefault = set.FirstOrDefault();
            Console.WriteLine(
                $"[FirstOrDefault] {(firstOrDefault == null ? "NULL" : $"{firstOrDefault.Id} {firstOrDefault.Name}")}");

            var first = set.First();
            Console.WriteLine($"[First] {first.Id} {first.Name}");

            // 5) Single (exactly one inside seed-range by Id)
            try
            {
                var single = set.Single(x => x.Id == seedMinId);
                Console.WriteLine($"[Single] OK: {single.Id} {single.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Single] FAIL (should not throw): {ex.GetType().Name} {ex.Message}");
            }

            // 6) SingleOrDefault (none)
            try
            {
                var missing = set.SingleOrDefault(x => x.Id == -1);
                Console.WriteLine(missing == null
                    ? "[SingleOrDefault missing] OK: NULL"
                    : "[SingleOrDefault missing] FAIL: expected NULL");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SingleOrDefault missing] FAIL (should not throw): {ex.GetType().Name} {ex.Message}");
            }

            // 7) Single (multi => should throw) - use seed-range, guaranteed >= 5 rows
            try
            {
                var boom = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId).Single();
                Console.WriteLine("[Single multi] FAIL (should throw)");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("[Single multi] OK (threw InvalidOperationException)");
            }

            // 8) All - deterministic predicates based on seedMinId/seedMaxId
            var allTrue = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId)
                .All(x => x.Id >= seedMinId);
            Console.WriteLine($"[All seed-range x.Id>=minId] {allTrue} (expected True)");

            var allFalse = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId)
                .All(x => x.Id > seedMinId); // row with Id==seedMinId breaks it
            Console.WriteLine($"[All seed-range x.Id>minId] {allFalse} (expected False)");

            var emptyAll = set.Where(x => x.Id < 0).All(x => x.Id > 0);
            Console.WriteLine($"[All on empty] {emptyAll} (expected True)");

            // captured variable test (forces parameterization)
            int threshold = seedMinId;
            var capturedFalse = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId)
                .All(x => x.Id > threshold);
            Console.WriteLine($"[All captured threshold=minId] {capturedFalse} (expected False)");

            // 9) Min / Max (seed-range)
            var minId = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId).Min(x => x.Id);
            Console.WriteLine($"[Min seed-range Id] {minId} (expected {seedMinId})");

            var maxId = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId).Max(x => x.Id);
            Console.WriteLine($"[Max seed-range Id] {maxId} (expected {seedMaxId})");

            // 10) LongCount
            var longCount = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId).LongCount();
            Console.WriteLine($"[LongCount seed-range] {longCount} (expected 5)");

            // 11) Sum (NO IQueryable.Select, because TranslateSelect not implemented)
            var seedEntities = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId).ToList(); // materialize first
            var expectedSum = seedEntities.Select(e => (long)e.Id).Sum();

            var sum = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId)
                .Sum(x => (long)x.Id);
            Console.WriteLine($"[Sum seed-range Id] {sum} (expected {expectedSum})");

            // 12) Average (NO IQueryable.Select)
            var expectedAvg = seedEntities.Select(e => (double)e.Id).Average();

            var avg = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId)
                .Average(x => (double)x.Id);
            Console.WriteLine($"[Average seed-range Id] {avg} (expected {expectedAvg})");

            // 13) Any with predicate
            var anyInRange = set.Any(x => x.Id >= seedMinId && x.Id <= seedMaxId);
            Console.WriteLine($"[Any predicate seed-range] {anyInRange} (expected True)");

            var anyMissing = set.Any(x => x.Id < 0);
            Console.WriteLine($"[Any predicate missing] {anyMissing} (expected False)");

            // 14) Count with predicate
            var countInRange = set.Count(x => x.Id >= seedMinId && x.Id <= seedMaxId);
            Console.WriteLine($"[Count predicate seed-range] {countInRange} (expected 5)");

            var countMissing = set.Count(x => x.Id < 0);
            Console.WriteLine($"[Count predicate missing] {countMissing} (expected 0)");

            // 15) FirstOrDefault with predicate
            var firstInRange = set.FirstOrDefault(x => x.Id >= seedMinId && x.Id <= seedMaxId);
            Console.WriteLine(firstInRange == null
                ? "[FirstOrDefault predicate seed-range] FAIL (expected entity)"
                : $"[FirstOrDefault predicate seed-range] OK: {firstInRange.Id} {firstInRange.Name}");

            var firstMissing = set.FirstOrDefault(x => x.Id < 0);
            Console.WriteLine(firstMissing == null
                ? "[FirstOrDefault predicate missing] OK: NULL"
                : "[FirstOrDefault predicate missing] FAIL (expected NULL)");

            // 16) SingleOrDefault with predicate exact
            var singleOrDefaultInRange = set.SingleOrDefault(x => x.Id == seedMinId);
            Console.WriteLine(singleOrDefaultInRange == null
                ? "[SingleOrDefault predicate exact] FAIL (expected entity)"
                : $"[SingleOrDefault predicate exact] OK: {singleOrDefaultInRange.Id} {singleOrDefaultInRange.Name}");
        }

        Console.WriteLine("=== END ===");
    }
}