using System;
using System.Linq;
using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore;
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

            // Helpful diagnostics (not assertions)
            Console.WriteLine($"[Diag] totalCount={set.Count()}");
            Console.WriteLine($"[Diag] minId={set.Min(x => x.Id)}, maxId={set.Max(x => x.Id)}");

            // 1) ToList
            var list = set.ToList();
            Require(list.Count >= 5, $"Expected at least 5 rows in table after seed, but got {list.Count}.");

            // Use seed-range to make expectations deterministic
            var inRange = set.Where(x => x.Id >= seedMinId && x.Id <= seedMaxId);

            // 2) Count (seed-range)
            Require(inRange.Count() == 5, $"Count(seed-range) expected 5, got {inRange.Count()}.");

            // 3) Any
            Require(set.Any() == true, "Any() expected True, got False.");

            // 4) First/FirstOrDefault (non-deterministic without order, so force order)
            var ordered = set.OrderBy(x => x.Id);

            var firstOrDefault = ordered.FirstOrDefault();
            Require(firstOrDefault != null, "FirstOrDefault() expected entity, got NULL.");

            var first = ordered.First();
            Require(first != null, "First() expected entity, got NULL.");

            // 5) Single (exactly one by Id)
            var single = set.Single(x => x.Id == seedMinId);
            Require(single.Id == seedMinId, $"Single(Id==minId) expected Id={seedMinId}, got {single.Id}.");

            // 6) SingleOrDefault (none) - SHOULD NOT throw, should return null
            var missing = set.SingleOrDefault(x => x.Id == -1);
            Require(missing == null, "SingleOrDefault(Id==-1) expected NULL, got entity.");

            // 7) Single (multi => should throw)
            ExpectThrows<InvalidOperationException>(
                () => inRange.Single(),
                "Single(seed-range) should throw InvalidOperationException because multiple rows exist.");

            // 8) All - deterministic
            var allTrue = inRange.All(x => x.Id >= seedMinId);
            Require(allTrue == true, "All(seed-range x.Id>=minId) expected True, got False.");

            var allFalse = inRange.All(x => x.Id > seedMinId);
            Require(allFalse == false, "All(seed-range x.Id>minId) expected False, got True.");

            var emptyAll = set.Where(x => x.Id < 0).All(x => x.Id > 0);
            Require(emptyAll == true, "All(on empty) expected True, got False.");

            int threshold = seedMinId;
            var capturedFalse = inRange.All(x => x.Id > threshold);
            Require(capturedFalse == false, "All(captured threshold=minId) expected False, got True.");

            // 9) Min / Max (seed-range)
            var minId = inRange.Min(x => x.Id);
            Require(minId == seedMinId, $"Min(seed-range Id) expected {seedMinId}, got {minId}.");

            var maxId = inRange.Max(x => x.Id);
            Require(maxId == seedMaxId, $"Max(seed-range Id) expected {seedMaxId}, got {maxId}.");

            // 10) LongCount
            var longCount = inRange.LongCount();
            Require(longCount == 5, $"LongCount(seed-range) expected 5, got {longCount}.");

            // 11) Sum
            var seedEntities = inRange.ToList(); // materialize for expected
            var expectedSum = seedEntities.Select(e => (long)e.Id).Sum();

            var sum = inRange.Sum(x => (long)x.Id);
            Require(sum == expectedSum, $"Sum(seed-range Id) expected {expectedSum}, got {sum}.");

            // 12) Average
            var expectedAvg = seedEntities.Select(e => (double)e.Id).Average();
            var avg = inRange.Average(x => (double)x.Id);
            Require(AreClose(avg, expectedAvg), $"Average(seed-range Id) expected {expectedAvg}, got {avg}.");

            // 13) Any with predicate
            Require(set.Any(x => x.Id >= seedMinId && x.Id <= seedMaxId) == true, "Any(predicate seed-range) expected True.");
            Require(set.Any(x => x.Id < 0) == false, "Any(predicate missing) expected False.");

            // 14) Count with predicate
            Require(set.Count(x => x.Id >= seedMinId && x.Id <= seedMaxId) == 5, "Count(predicate seed-range) expected 5.");
            Require(set.Count(x => x.Id < 0) == 0, "Count(predicate missing) expected 0.");

            // 15) FirstOrDefault with predicate
            var firstInRange = set.FirstOrDefault(x => x.Id >= seedMinId && x.Id <= seedMaxId);
            Require(firstInRange != null, "FirstOrDefault(predicate seed-range) expected entity, got NULL.");

            var firstMissing = set.FirstOrDefault(x => x.Id < 0);
            Require(firstMissing == null, "FirstOrDefault(predicate missing) expected NULL, got entity.");

            // 16) SingleOrDefault with predicate exact
            var singleOrDefaultExact = set.SingleOrDefault(x => x.Id == seedMinId);
            Require(singleOrDefaultExact != null, "SingleOrDefault(predicate exact) expected entity, got NULL.");
            Require(singleOrDefaultExact!.Id == seedMinId, $"SingleOrDefault(predicate exact) expected Id={seedMinId}, got {singleOrDefaultExact.Id}.");

            // 17) SingleOrDefault with predicate multi => should throw
            ExpectThrows<InvalidOperationException>(
                () => set.SingleOrDefault(x => x.Id >= seedMinId && x.Id <= seedMaxId),
                "SingleOrDefault(seed-range) should throw InvalidOperationException because multiple rows exist.");

            Console.WriteLine("✅ IMMEDIATE OPS SMOKE TEST PASSED");
        }

        Console.WriteLine("=== END ===");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("❌ Validation Failed: " + message);
    }

    private static void ExpectThrows<T>(Action action, string messageIfNotThrown) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return; // OK
        }
        catch (Exception ex)
        {
            throw new Exception($"❌ Validation Failed: Expected {typeof(T).Name}, but got {ex.GetType().Name}: {ex.Message}");
        }

        throw new Exception("❌ Validation Failed: " + messageIfNotThrown);
    }

    private static bool AreClose(double a, double b, double eps = 1e-9)
        => Math.Abs(a - b) <= eps;
}