using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderIncludeCollectionSmokeTest
{
    public static void Run()
    {
        Console.WriteLine("=== INCLUDE SMOKE TESTS ===");

        Run_NoInclude_CollectionNotLoaded();
        Run_Include_BlogPosts_Loaded();
        Run_Include_Take_Correlation();
        Run_Include_TwoCollections_PostsAndNotes();

        Console.WriteLine("=== INCLUDE SMOKE TESTS PASSED ===");
    }

    private static void Run_NoInclude_CollectionNotLoaded()
    {
        using var root = TestHost.BuildRootProvider(dbName: "NoInclude_" + Guid.NewGuid().ToString("N"));

        using (var seed = root.CreateScope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<AppDbContext>();

            var blog1 = new Blog { Name = "Blog-1" };
            var blog2 = new Blog { Name = "Blog-2" };

            ctx.AddRange(
                blog1, blog2,
                new BlogPost { Title = "B1-P1", Blog = blog1 },
                new BlogPost { Title = "B1-P2", Blog = blog1 },
                new BlogPost { Title = "B2-P1", Blog = blog2 }
            );

            ctx.SaveChanges();
        }

        using (var scope = root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var blogs = ctx.Set<Blog>().OrderBy(b => b.Id).ToList();
            if (blogs.Count != 2) throw new Exception($"[NoInclude] expected 2 blogs but got {blogs.Count}");

            foreach (var b in blogs)
            {
                var nav = ctx.Entry(b).Collection(x => x.Posts);
                if (nav.IsLoaded)
                    throw new Exception($"[NoInclude] expected Blog {b.Id}.Posts IsLoaded == false");
            }
        }

        Console.WriteLine("[NoInclude] OK");
    }

    private static void Run_Include_BlogPosts_Loaded()
    {
        using var root = TestHost.BuildRootProvider(dbName: "IncludePosts_" + Guid.NewGuid().ToString("N"));

        using (var seed = root.CreateScope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<AppDbContext>();

            var blog1 = new Blog { Name = "Blog-1" };
            var blog2 = new Blog { Name = "Blog-2" };

            ctx.AddRange(
                blog1, blog2,
                new BlogPost { Title = "B1-P1", Blog = blog1 },
                new BlogPost { Title = "B1-P2", Blog = blog1 },
                new BlogPost { Title = "B2-P1", Blog = blog2 }
            );

            ctx.SaveChanges();
        }

        using (var scope = root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var blogs = ctx.Set<Blog>()
                .Include(b => b.Posts)
                .OrderBy(b => b.Id)
                .ToList();

            if (blogs.Count != 2) throw new Exception($"[Include] expected 2 blogs but got {blogs.Count}");

            foreach (var blog in blogs)
            {
                var nav = ctx.Entry(blog).Collection(x => x.Posts);
                if (!nav.IsLoaded)
                    throw new Exception($"[Include] FAILED: Blog {blog.Id}.Posts IsLoaded == false");

                var expected = blog.Name == "Blog-1" ? 2 : 1;
                var actual = blog.Posts?.Count ?? 0;
                if (actual != expected)
                    throw new Exception($"[Include] FAILED: Blog {blog.Id} expected {expected} posts but got {actual}");
            }
        }

        Console.WriteLine("[Include Posts] OK");
    }

    private static void Run_Include_Take_Correlation()
    {
        using var root = TestHost.BuildRootProvider(dbName: "IncludeTake_" + Guid.NewGuid().ToString("N"));

        using (var seed = root.CreateScope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<AppDbContext>();

            var b1 = new Blog { Name = "B1" };
            var b2 = new Blog { Name = "B2" };
            var b3 = new Blog { Name = "B3" };

            ctx.AddRange(b1, b2, b3);

            ctx.AddRange(
                new BlogPost { Title = "b1-1", Blog = b1 },
                new BlogPost { Title = "b1-2", Blog = b1 },

                new BlogPost { Title = "b2-1", Blog = b2 },
                new BlogPost { Title = "b2-2", Blog = b2 },
                new BlogPost { Title = "b2-3", Blog = b2 },
                new BlogPost { Title = "b2-4", Blog = b2 },
                new BlogPost { Title = "b2-5", Blog = b2 },

                new BlogPost { Title = "b3-1", Blog = b3 },
                new BlogPost { Title = "b3-2", Blog = b3 },
                new BlogPost { Title = "b3-3", Blog = b3 },
                new BlogPost { Title = "b3-4", Blog = b3 },
                new BlogPost { Title = "b3-5", Blog = b3 },
                new BlogPost { Title = "b3-6", Blog = b3 },
                new BlogPost { Title = "b3-7", Blog = b3 }
            );

            ctx.SaveChanges();
        }

        using (var scope = root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var one = ctx.Set<Blog>()
                .Include(b => b.Posts)
                .OrderBy(b => b.Id)
                .Take(1)
                .Single();

            if (!ctx.Entry(one).Collection(x => x.Posts).IsLoaded)
                throw new Exception("[Take Correlation] Posts should be loaded");

            var count = one.Posts?.Count ?? 0;
            if (count != 2)
                throw new Exception($"[Take Correlation] expected 2 posts but got {count}");

            foreach (var p in one.Posts!)
            {
                if (p.BlogId != one.Id)
                    throw new Exception($"[Take Correlation] post {p.Id} BlogId={p.BlogId} != blog.Id={one.Id}");
            }
        }

        Console.WriteLine("[Include + Take Correlation] OK");
    }

    public static void Run_Include_TwoCollections_PostsAndNotes()
    {
        using var root = TestHost.BuildRootProvider(dbName: "IncludeTwoCollections_" + Guid.NewGuid().ToString("N"));
        using (var seed = root.CreateScope())
        {
            var ctx = seed.ServiceProvider.GetRequiredService<AppDbContext>();

            var b1 = new Blog { Name = "B1" };
            var b2 = new Blog { Name = "B2" };

            ctx.AddRange(b1, b2);

            ctx.AddRange(
                new BlogPost { Title = "b1-p1", Blog = b1 },
                new BlogPost { Title = "b1-p2", Blog = b1 },
                new BlogPost { Title = "b2-p1", Blog = b2 },

                new BlogNote { Text = "b1-n1", Blog = b1 },
                new BlogNote { Text = "b1-n2", Blog = b1 },
                new BlogNote { Text = "b1-n3", Blog = b1 },
                new BlogNote { Text = "b2-n1", Blog = b2 }
            );

            ctx.SaveChanges();
        }

        using (var scope = root.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var blogs = ctx.Set<Blog>()
                .Include(b => b.Posts)
                .Include(b => b.Notes)
                .OrderBy(b => b.Id)
                .ToList();

            Assert.Equal(2, blogs.Count);

            foreach (var b in blogs)
            {
                Assert.True(ctx.Entry(b).Collection(x => x.Posts).IsLoaded);
                Assert.True(ctx.Entry(b).Collection(x => x.Notes).IsLoaded);

                if (b.Name == "B1")
                {
                    Assert.Equal(2, b.Posts.Count);
                    Assert.Equal(3, b.Notes.Count);
                }
                else
                {
                    Assert.Equal(1, b.Posts.Count);
                    Assert.Equal(1, b.Notes.Count);
                }

                Assert.All(b.Posts, p => Assert.Equal(b.Id, p.BlogId));
                Assert.All(b.Notes, n => Assert.Equal(b.Id, n.BlogId));
            }
        }

        Console.WriteLine("[Two collections: Posts + Notes] OK");
    }
}