using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderWhereSelectSmokeTest
{
    // Simple DTO projection target (avoid anonymous type)
    private sealed class IdNameDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== WHERE + SELECT + TOLIST SMOKE TEST ===");

        using var scope = rootProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed 1..5 (NOTE: if your DB isn't cleared per run, this will accumulate)
        ctx.AddRange(
            new TestEntity { Name = "Seed-1" },
            new TestEntity { Name = "Seed-2" },
            new TestEntity { Name = "Seed-3" },
            new TestEntity { Name = "Seed-4" },
            new TestEntity { Name = "Seed-5" }
        );
        ctx.SaveChanges();

        // Test 1: Where + Select scalar + ToList
        var names = ctx.Set<TestEntity>()
            .Where(x => x.Id > 3)
            .Select(x => x.Name)
            .ToList();

        Console.WriteLine($"[Where Id>3 -> Select Name] count={names.Count}");
        foreach (var n in names.OrderBy(x => x))
            Console.WriteLine($"  - name={n}");

        // Test 2: Where + Where + Select DTO + ToList
        var dtos = ctx.Set<TestEntity>()
            .Where(x => x.Id > 1)
            .Where(x => x.Name != "Seed-4")
            .Select(x => new IdNameDto { Id = x.Id, Name = x.Name })
            .ToList();

        Console.WriteLine($"[Where Id>1 & Name!=Seed-4 -> Select DTO] count={dtos.Count}");
        foreach (var d in dtos.OrderBy(x => x.Id))
            Console.WriteLine($"  - id={d.Id} name={d.Name}");

        // Test 3: Projection chaining (Select after Select)
        // This validates that your q.Projections list is applied in order.
        var lengths = ctx.Set<TestEntity>()
            .Where(x => x.Id > 0)
            .Select(x => x.Name) // TEntity -> string?
            .Select(n => n!.Length) // string -> int
            .ToList();

        Console.WriteLine($"[Where Id>0 -> Select Name -> Select Length] count={lengths.Count}");
        foreach (var len in lengths.OrderBy(x => x))
            Console.WriteLine($"  - len={len}");

        // ---------------- SelectMany SMOKE TEST ----------------
        Console.WriteLine("=== SELECTMANY SMOKE TEST ===");

        // 为了不依赖 Include，这里单独 seed 一组 BlogPost + Comment
        var post1 = new BlogPost { Title = "Post-1" };
        var post2 = new BlogPost { Title = "Post-2" };

        var c11 = new PostComment { Content = "C1-1", Post = post1 };
        var c12 = new PostComment { Content = "C1-2", Post = post1 };
        var c21 = new PostComment { Content = "C2-1", Post = post2 };

        ctx.AddRange(post1, post2, c11, c12, c21);
        ctx.SaveChanges();

        // Act: SelectMany flatten
        var flat = ctx.Set<BlogPost>()
            .OrderBy(p => p.Id)
            .SelectMany(
                p => p.Comments,
                (p, c) => new
                {
                    PostTitle = p.Title,
                    CommentContent = c.Content
                })
            .ToList();

        Console.WriteLine($"[SelectMany] count={flat.Count}");
        foreach (var x in flat)
        {
            Console.WriteLine($"  - post={x.PostTitle}, comment={x.CommentContent}");
        }

// Expected: 3 rows
        if (flat.Count != 3)
            throw new Exception($"[SelectMany] expected 3 rows but got {flat.Count}");

// Group check (logic correctness)
        var g1 = flat.Count(x => x.PostTitle == "Post-1");
        var g2 = flat.Count(x => x.PostTitle == "Post-2");

        if (g1 != 2 || g2 != 1)
            throw new Exception($"[SelectMany] grouping failed: Post-1={g1}, Post-2={g2}");

        Console.WriteLine("[SelectMany] OK: flatten + resultSelector works");
        
        Console.WriteLine("=== END ===");
    }
}