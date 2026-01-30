using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderIncludeReferenceSmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== INCLUDE (REFERENCE) SMOKE TEST ===");

        // ---------- Seed ----------
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var blog1 = new Blog { Name = "Blog-1" };
            var blog2 = new Blog { Name = "Blog-2" };

            var detail1 = new BlogDetail { Description = "Detail for Blog-1", Blog = blog1 };
            var detail2 = new BlogDetail { Description = "Detail for Blog-2", Blog = blog2 };

            ctx.AddRange(blog1, blog2, detail1, detail2);
            ctx.SaveChanges();
        }

        // ---------- Query ----------
        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A) baseline: no Include => Detail should be null (since you now return cloned entities)
            var noInclude = ctx.Set<Blog>()
                .OrderBy(b => b.Id)
                .ToList();

            if (noInclude.Count != 2)
                throw new Exception($"[NoInclude] expected 2 blogs but got {noInclude.Count}");

            foreach (var b in noInclude)
            {
                if (b.Detail != null)
                    throw new Exception($"[NoInclude] expected Blog {b.Id}.Detail == null but got non-null");
            }

            Console.WriteLine("[NoInclude] OK: all Detail == null");

            // B) Include => Detail must be populated
            var withInclude = ctx.Set<Blog>()
                .Include(b => b.Detail)
                .OrderBy(b => b.Id)
                .ToList();

            if (withInclude.Count != 2)
                throw new Exception($"[Include] expected 2 blogs but got {withInclude.Count}");

            foreach (var blog in withInclude)
            {
                if (blog.Detail == null)
                    throw new Exception($"[Include] FAILED: Blog {blog.Id}.Detail is null");

                if (string.IsNullOrWhiteSpace(blog.Detail.Description))
                    throw new Exception($"[Include] FAILED: Blog {blog.Id}.Detail.Description is empty");

                // Inverse fix-up (depends on how EF shaper/materializer behaves for your provider,
                // but it's a very good signal. If you haven't supported it yet, you can comment it out for now.)
                if (blog.Detail.Blog == null)
                    throw new Exception($"[Include] FAILED: inverse navigation Detail.Blog is null (Blog {blog.Id})");

                if (!ReferenceEquals(blog, blog.Detail.Blog))
                    throw new Exception($"[Include] FAILED: inverse navigation points to wrong blog (Blog {blog.Id})");

                Console.WriteLine($"[Include] OK: Blog {blog.Id} -> Detail {blog.Detail.Id}, Desc={blog.Detail.Description}");
            }
        }

        Console.WriteLine("=== INCLUDE (REFERENCE) TEST PASSED ===");
    }
}