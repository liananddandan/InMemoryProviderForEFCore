using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderFinderSmokeTest
{
    public static void Run( )
    {
        Console.WriteLine("=== FINDER SMOKE TEST ===");
        using var rootProvider = TestHost.BuildRootProvider(dbName: "ProviderFInder_" + Guid.NewGuid().ToString("N"));

        int id;

        // CTX1: Add + Save
        using (var scope1 = rootProvider.CreateScope())
        {
            var ctx1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();

            var e = new TestEntity { Name = "FinderTest" };
            ctx1.Add(e);
            ctx1.SaveChanges();

            id = e.Id;
            Console.WriteLine($"CTX1: Added entity id={id}");

            // Same context: Find twice (2nd time should be TRACKED HIT)
            var f1 = ctx1.Set<TestEntity>().Find(id);
            Console.WriteLine($"CTX1: Find #1 => {(f1 == null ? "NULL" : f1.Name)}");

            var f2 = ctx1.Set<TestEntity>().Find(id);
            Console.WriteLine($"CTX1: Find #2 => {(f2 == null ? "NULL" : f2.Name)}");

            // Optional: reference equality check (identity resolution)
            Console.WriteLine($"CTX1: ReferenceEquals(f1,f2) = {ReferenceEquals(f1, f2)}");
        }

        // CTX2: New scope/new DbContext: should STORE HIT, then attach
        using (var scope2 = rootProvider.CreateScope())
        {
            var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var ff = ctx2.GetService<Microsoft.EntityFrameworkCore.Internal.IEntityFinderFactory>();
            Console.WriteLine($"IEntityFinderFactory = {ff.GetType().FullName}");
            
            var f3 = ctx2.Set<TestEntity>().Find(id);
            Console.WriteLine($"CTX2: Find => {(f3 == null ? "NULL" : f3.Name)}");

            // Find again in same ctx2: should now be TRACKED HIT
            var f4 = ctx2.Set<TestEntity>().Find(id);
            Console.WriteLine($"CTX2: Find again => {(f4 == null ? "NULL" : f4.Name)}");

            Console.WriteLine($"CTX2: ReferenceEquals(f3,f4) = {ReferenceEquals(f3, f4)}");
        }

        // CTX3: Find non-existing => STORE MISS and return null
        using (var scope3 = rootProvider.CreateScope())
        {
            var ctx3 = scope3.ServiceProvider.GetRequiredService<AppDbContext>();

            var missing = ctx3.Set<TestEntity>().Find(-999999);
            Console.WriteLine($"CTX3: Find missing => {(missing == null ? "NULL (expected)" : "UNEXPECTED")}");
        }

        Console.WriteLine("===✅✅✅✅✅✅ FINDER SMOKE TEST END ===");
    }
}