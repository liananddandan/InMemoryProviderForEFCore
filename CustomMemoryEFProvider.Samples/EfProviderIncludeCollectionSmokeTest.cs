using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderIncludeCollectionSmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== INCLUDE (COLLECTION) SMOKE TEST ===");

        // ---------- Seed ----------
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var blog1 = new Blog { Name = "Blog-1" };
            var blog2 = new Blog { Name = "Blog-2" };

            var p11 = new BlogPost { Title = "B1-P1", Blog = blog1 };
            var p12 = new BlogPost { Title = "B1-P2", Blog = blog1 };
            var p21 = new BlogPost { Title = "B2-P1", Blog = blog2 };

            ctx.AddRange(blog1, blog2, p11, p12, p21);
            ctx.SaveChanges();
        }

        // ---------- Query ----------
        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A) baseline: no Include => Posts should be null (since you now return cloned entities)
            var noInclude = ctx.Set<Blog>()
                .OrderBy(b => b.Id)
                .ToList();

            if (noInclude.Count != 2)
                throw new Exception($"[NoInclude] expected 2 blogs but got {noInclude.Count}");

            foreach (var b in noInclude)
            {
                if (b.Posts != null)
                    throw new Exception($"[NoInclude] expected Blog {b.Id}.Posts == null but got non-null");
            }

            Console.WriteLine("[NoInclude] OK: all Posts == null");

            // B) Include => Posts must be populated
            var withInclude = ctx.Set<Blog>()
                .Include(b => b.Posts)
                .OrderBy(b => b.Id)
                .ToList();

            if (withInclude.Count != 2)
                throw new Exception($"[Include] expected 2 blogs but got {withInclude.Count}");

            // 期望：Blog-1 => 2 posts, Blog-2 => 1 post
            foreach (var blog in withInclude)
            {
                if (blog.Posts == null)
                    throw new Exception($"[Include] FAILED: Blog {blog.Id}.Posts is null");

                var count = blog.Posts.Count;
                var expected = blog.Name == "Blog-1" ? 2 :
                               blog.Name == "Blog-2" ? 1 : -999;

                if (expected < 0)
                    throw new Exception($"[Include] FAILED: unexpected blog name {blog.Name}");

                if (count != expected)
                    throw new Exception($"[Include] FAILED: Blog {blog.Id} expected {expected} posts but got {count}");

                // validate content + inverse fix-up
                foreach (var post in blog.Posts)
                {
                    if (post == null)
                        throw new Exception($"[Include] FAILED: Blog {blog.Id} contains null post");

                    if (string.IsNullOrWhiteSpace(post.Title))
                        throw new Exception($"[Include] FAILED: Blog {blog.Id} has a post with empty Title");

                    if (post.BlogId != blog.Id)
                        throw new Exception($"[Include] FAILED: Post {post.Id} BlogId={post.BlogId} does not match Blog {blog.Id}");

                    if (post.Blog == null)
                        throw new Exception($"[Include] FAILED: inverse navigation Post.Blog is null (Post {post.Id})");

                    if (!ReferenceEquals(blog, post.Blog))
                        throw new Exception($"[Include] FAILED: inverse navigation points to wrong blog (Post {post.Id})");
                }

                Console.WriteLine($"[Include] OK: Blog {blog.Id} ({blog.Name}) -> Posts={blog.Posts.Count}");
            }

            // C) optional: ensure outer de-dup happened (collection include必须做这个)
            // Blog-1 在 join 结果里会出现两行，如果你没做去重，很可能 withInclude.Count 会变成 3
            // 我们已经用 Count=2 兜住了这一点。
        }

        Console.WriteLine("=== INCLUDE (COLLECTION) TEST PASSED ===");
    }
}