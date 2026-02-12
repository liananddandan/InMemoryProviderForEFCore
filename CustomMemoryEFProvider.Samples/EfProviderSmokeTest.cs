using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using CustomMemoryEFProvider.Core.Interfaces;
using CustomMemoryEFProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public sealed class EfProviderSmokeTest
{
    private readonly EfProviderSmokeTestOptions _opt;

    public EfProviderSmokeTest(EfProviderSmokeTestOptions opt)
    {
        _opt = opt ?? new EfProviderSmokeTestOptions();
    }

    public void Run()
    {
        Console.WriteLine("=== CustomMemory Provider Smoke Test ===");
        using var rootProvider = TestHost.BuildRootProvider(dbName: "ProviderSmoke_" + Guid.NewGuid().ToString("N"));

        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (_opt.CheckModel) DumpModel(ctx);
            if (_opt.CheckInternalServices) DumpServices(ctx);
            if (_opt.CheckValueGenerator) DumpGenerator(ctx, _opt.ConsumeGeneratorSampleValue);

            if (_opt.CrudSingle) TestCrudSingle(ctx);
            if (_opt.CrudMultiple) TestCrudMultiple(ctx);
        }

        if (_opt.CrudDetached)
        {
            // detached 测试必须用两个不同的 DbContext（两个 scope）
            TestCrudDetached(rootProvider);
        }

        Console.WriteLine("=== Done ===");
    }

    private static void DumpModel(DbContext ctx)
    {
        var et = ctx.Model.FindEntityType(typeof(TestEntity))!;
        var idProp = et.FindProperty(nameof(TestEntity.Id))!;

        Console.WriteLine("=== MODEL CHECK ===");
        Console.WriteLine($"EntityType: {et.Name}");
        Console.WriteLine($"Id.ValueGenerated: {idProp.ValueGenerated} (expect OnAdd)");
        Console.WriteLine($"Id.IsKey: {idProp.IsKey()}");
        Console.WriteLine($"Id.ClrType: {idProp.ClrType}");
    }

    private static void DumpServices(DbContext ctx)
    {
        Console.WriteLine("=== SERVICE CHECK ===");

        var selector = ctx.GetService<IValueGeneratorSelector>();
        Console.WriteLine($"IValueGeneratorSelector: {selector.GetType().FullName}");

        var typeMappingSource = ctx.GetService<Microsoft.EntityFrameworkCore.Storage.ITypeMappingSource>();
        Console.WriteLine($"ITypeMappingSource: {typeMappingSource.GetType().FullName}");

        var dbProvider = ctx.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabaseProvider>();
        Console.WriteLine($"IDatabaseProvider: {dbProvider.GetType().FullName}");

        var database = ctx.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabase>();
        Console.WriteLine($"IDatabase: {database.GetType().FullName}");
    }

    private static void DumpGenerator(DbContext ctx, bool consumeSample)
    {
        Console.WriteLine("=== GENERATOR CHECK ===");

        var et = ctx.Model.FindEntityType(typeof(TestEntity))!;
        var idProp = et.FindProperty(nameof(TestEntity.Id))!;
        var selector = ctx.GetService<IValueGeneratorSelector>();

        if (selector.TrySelect(idProp, et, out var vg) && vg != null)
        {
            Console.WriteLine($"ValueGenerator: {vg.GetType().FullName}");

            if (consumeSample)
            {
                var temp = new TestEntity { Name = "tmp" };
                var entry = ctx.Entry(temp);
                var generated = vg.Next(entry);
                Console.WriteLine($"Generated value sample (consumes): {generated}");
            }
            else
            {
                Console.WriteLine("Generated value sample: (skipped, to avoid consuming ID)");
            }
        }
        else
        {
            Console.WriteLine("ValueGenerator: NULL");
        }
    }

    private static void TestCrudSingle(AppDbContext ctx)
    {
        Console.WriteLine("=== CRUD TEST (Single) ===");

        var e = new TestEntity { Name = "SmokeTest User" };
        ctx.TestEntities.Add(e);

        var addCount = ctx.SaveChanges();
        Console.WriteLine($"ADD: affected={addCount}, entity.Id={e.Id}");

        var foundAfterAdd = ctx.TestEntities.FirstOrDefault(x => x.Id == e.Id);
        Console.WriteLine(foundAfterAdd != null ? $"ADD verify OK: {foundAfterAdd.Name}" : "ADD verify FAIL");

        e.Name = "SmokeTest User (Updated)";
        var updateCount = ctx.SaveChanges();
        Console.WriteLine($"UPDATE: affected={updateCount}, entity.Id={e.Id}");

        var foundAfterUpdate = ctx.TestEntities.FirstOrDefault(x => x.Id == e.Id);
        Console.WriteLine(foundAfterUpdate?.Name == e.Name
            ? $"UPDATE verify OK: {foundAfterUpdate.Name}"
            : $"UPDATE verify FAIL: got={(foundAfterUpdate?.Name ?? "null")}");

        ctx.TestEntities.Remove(e);
        var deleteCount = ctx.SaveChanges();
        Console.WriteLine($"DELETE: affected={deleteCount}, entity.Id={e.Id}");

        var foundAfterDelete = ctx.TestEntities.FirstOrDefault(x => x.Id == e.Id);
        Console.WriteLine(foundAfterDelete == null ? "DELETE verify OK" : "DELETE verify FAIL");
    }

    private static void TestCrudMultiple(AppDbContext ctx)
    {
        Console.WriteLine("=== CRUD TEST (Multiple entities) ===");

        // Batch Add
        var entities = new List<TestEntity>();
        for (int i = 1; i <= 5; i++)
        {
            entities.Add(new TestEntity { Name = $"User-{i}" });
        }

        ctx.TestEntities.AddRange(entities);
        var addCount = ctx.SaveChanges();
        Console.WriteLine($"ADD: affected={addCount}, ids=[{string.Join(",", entities.Select(e => e.Id))}]");

        var afterAdd = ctx.TestEntities.ToList();
        Console.WriteLine(afterAdd.Count >= 5
            ? $"ADD verify OK-ish (count={afterAdd.Count})"
            : $"ADD verify FAIL (count={afterAdd.Count})");

        // Update odd ids
        foreach (var e in entities.Where(e => e.Id % 2 == 1))
        {
            e.Name += "-Updated";
        }

        var updateCount = ctx.SaveChanges();
        Console.WriteLine($"UPDATE: affected={updateCount}");

        var afterUpdate = ctx.TestEntities.ToList();
        var expectedUpdated = entities.Count(e => e.Id % 2 == 1);
        var actualUpdated = afterUpdate.Count(e => e.Name.EndsWith("-Updated"));
        Console.WriteLine(actualUpdated >= expectedUpdated
            ? $"UPDATE verify OK-ish (updated={actualUpdated})"
            : $"UPDATE verify FAIL (updated={actualUpdated}, expected>={expectedUpdated})");

        // Delete first two
        var deleteTargets = entities.Take(2).ToList();
        ctx.TestEntities.RemoveRange(deleteTargets);

        var deleteCount = ctx.SaveChanges();
        Console.WriteLine($"DELETE: affected={deleteCount}");

        var afterDelete = ctx.TestEntities.ToList();
        var deletedIds = deleteTargets.Select(e => e.Id).ToHashSet();
        var stillExists = afterDelete.Any(e => deletedIds.Contains(e.Id));

        Console.WriteLine(!stillExists ? "DELETE id verify OK" : "DELETE id verify FAIL");
        Console.WriteLine($"Final count (after delete): {afterDelete.Count}");
    }

    private static void TestCrudDetached(IServiceProvider root)
    {
        Console.WriteLine("=== CRUD TEST (Detached across DbContext instances) ===");

        int id;

        // ctx1: add & save
        using (var scope1 = root.CreateScope())
        {
            var ctx1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();

            var e = new TestEntity { Name = "Detached User" };
            ctx1.TestEntities.Add(e);
            ctx1.SaveChanges();
            
            var memDb1 = ctx1.GetService<IMemoryDatabase>();
            var t1 = memDb1.GetTable<TestEntity>(typeof(TestEntity));
            Console.WriteLine($"[CTX1 AFTER ADD] query count = {t1.QueryRows.Count()}");
            id = e.Id;
            Console.WriteLine($"CTX1 ADD: id={id}");
        }

        // ctx2: update via detached stub
        using (var scope2 = root.CreateScope())
        {
            var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

            // 关键：用 stub 模拟 “只知道主键”的场景
            var stub = new TestEntity { Id = id, Name = "Detached User (Updated)" };

            // 这里有两种方式：
            // A) ctx2.Update(stub) -> 直接标记 Modified
            // B) ctx2.Attach(stub); ctx2.Entry(stub).Property(x=>x.Name).IsModified = true;
            ctx2.Update(stub);

            var upd = ctx2.SaveChanges();
            Console.WriteLine($"CTX2 UPDATE: affected={upd}");

            var found = ctx2.TestEntities.FirstOrDefault(x => x.Id == id);
            Console.WriteLine(found?.Name == stub.Name ? "CTX2 UPDATE verify OK" : $"CTX2 UPDATE verify FAIL: got={found?.Name}");
        }

        // ctx3: delete via detached stub
        using (var scope3 = root.CreateScope())
        {
            var ctx3 = scope3.ServiceProvider.GetRequiredService<AppDbContext>();

            var stub = new TestEntity { Id = id };
            ctx3.Remove(stub);

            var del = ctx3.SaveChanges();
            Console.WriteLine($"CTX3 DELETE: affected={del}");

            var found = ctx3.TestEntities.FirstOrDefault(x => x.Id == id);
            Console.WriteLine(found == null ? "CTX3 DELETE verify OK" : "CTX3 DELETE verify FAIL");
        }
    }
}