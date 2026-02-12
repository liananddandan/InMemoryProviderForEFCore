using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderIncludeNestedSmokeTest
{
    public static void Run()
    {
        Console.WriteLine("=== INCLUDE (NESTED + MULTIPLE) SMOKE TEST ===");
        using var rootProvider = TestHost.BuildRootProvider(dbName: "ProviderIncludeNested_" + Guid.NewGuid().ToString("N"));
        
        // ---------- Seed ----------
        using (var seedScope = rootProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var blog1 = new Blog { Name = "Blog-1" };
            var blog2 = new Blog { Name = "Blog-2" };

            var detail1 = new BlogDetail { Description = "Detail for Blog-1", Blog = blog1 };
            var detail2 = new BlogDetail { Description = "Detail for Blog-2", Blog = blog2 };

            var p11 = new BlogPost { Title = "B1-P1", Blog = blog1 };
            var p12 = new BlogPost { Title = "B1-P2", Blog = blog1 };
            var p21 = new BlogPost { Title = "B2-P1", Blog = blog2 };

            var c111 = new PostComment { Content = "C1", Post = p11 };
            var c112 = new PostComment { Content = "C2", Post = p11 };
            var c121 = new PostComment { Content = "C3", Post = p12 };
            var c211 = new PostComment { Content = "C4", Post = p21 };

            ctx.AddRange(
                blog1, blog2,
                detail1, detail2,
                p11, p12, p21,
                c111, c112, c121, c211
            );

            ctx.SaveChanges();
        }

        // ---------- Query ----------
        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A) baseline: no include
            var noInclude = ctx.Set<Blog>()
                .OrderBy(b => b.Id)
                .ToList();

            if (noInclude.Count != 2)
                throw new Exception($"[NoInclude] expected 2 but got {noInclude.Count}");

            foreach (var b in noInclude)
            {
                // reference nav: should not be loaded and should remain null
                var detailRef = ctx.Entry(b).Reference(x => x.Detail);
                if (detailRef.IsLoaded)
                    throw new Exception($"[NoInclude] expected Blog {b.Id}.Detail IsLoaded == false");
                if (b.Detail != null)
                    throw new Exception($"[NoInclude] expected Blog {b.Id}.Detail == null");

                // collection nav: should not be loaded; collection exists (non-null) but must be empty
                var postsCol = ctx.Entry(b).Collection(x => x.Posts);
                if (postsCol.IsLoaded)
                    throw new Exception($"[NoInclude] expected Blog {b.Id}.Posts IsLoaded == false");

                // 你的实体 Posts 默认 new List<>，所以这里应该永远不为 null
                if (b.Posts == null)
                    throw new Exception($"[NoInclude] Blog {b.Id}.Posts should not be null (entity initializes it)");

                if (b.Posts.Count != 0)
                    throw new Exception($"[NoInclude] expected Blog {b.Id}.Posts empty but got {b.Posts.Count}");
            }

            Console.WriteLine("[NoInclude] OK: Detail not loaded (null), Posts not loaded (empty list)");

            // B) Multiple + Nested:
            // Include Detail (reference) + Include Posts (collection) + ThenInclude Comments (collection)
            var withNested = ctx.Set<Blog>()
                .Include(b => b.Detail)
                .Include(b => b.Posts)
                    .ThenInclude(p => p.Comments)
                .OrderBy(b => b.Id)
                .ToList();

            AssertBlogsDetailPostsComments(ctx, withNested);

            Console.WriteLine("=== INCLUDE (NESTED + MULTIPLE) TEST PASSED ===");
        }
    }

    private static void AssertBlogsDetailPostsComments(AppDbContext ctx, List<Blog> blogs)
    {
        if (blogs.Count != 2)
            throw new Exception($"[Include] expected 2 but got {blogs.Count}");

        foreach (var blog in blogs)
        {
            // reference include: loaded + non-null
            var detailRef = ctx.Entry(blog).Reference(x => x.Detail);
            if (!detailRef.IsLoaded)
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Detail IsLoaded == false");
            if (blog.Detail == null)
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Detail is null");

            // fixup: Detail.Blog should point back
            if (blog.Detail.Blog == null || !ReferenceEquals(blog, blog.Detail.Blog))
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Detail.Blog fix-up missing/wrong");

            // collection include: loaded + counts
            var postsCol = ctx.Entry(blog).Collection(x => x.Posts);
            if (!postsCol.IsLoaded)
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Posts IsLoaded == false");
            if (blog.Posts == null)
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Posts is null (entity initializes it)");

            var expectedPosts = blog.Name == "Blog-1" ? 2 : 1;
            if (blog.Posts.Count != expectedPosts)
                throw new Exception($"[Include] FAILED: Blog {blog.Id} expected {expectedPosts} posts but got {blog.Posts.Count}");

            foreach (var post in blog.Posts)
            {
                // inverse fixup for posts
                if (post.Blog == null || !ReferenceEquals(blog, post.Blog))
                    throw new Exception($"[Include] FAILED: Post {post.Id}.Blog fix-up missing/wrong");

                // nested collection include: Comments should be loaded
                var commentsCol = ctx.Entry(post).Collection(x => x.Comments);
                if (!commentsCol.IsLoaded)
                    throw new Exception($"[ThenInclude] FAILED: Post {post.Id}.Comments IsLoaded == false");

                if (post.Comments == null)
                    throw new Exception($"[ThenInclude] FAILED: Post {post.Id}.Comments is null (entity initializes it)");

                // verify inverse fixup for comments
                foreach (var c in post.Comments)
                {
                    if (c.Post == null || !ReferenceEquals(post, c.Post))
                        throw new Exception($"[ThenInclude] FAILED: Comment {c.Id}.Post fix-up missing/wrong");
                }
            }

            Console.WriteLine($"[Include] OK: Blog {blog.Id} Detail+Posts+Comments loaded");
        }
    }
}