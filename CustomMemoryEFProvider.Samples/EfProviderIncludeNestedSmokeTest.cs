using CustomEFCoreProvider.Samples.Entities;
using CustomEFCoreProvider.Samples.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomEFCoreProvider.Samples;

public static class EfProviderIncludeNestedSmokeTest
{
    public static void Run(IServiceProvider rootProvider)
    {
        Console.WriteLine("=== INCLUDE (NESTED + MULTIPLE) SMOKE TEST ===");

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

            // Optional seed for a 3rd-level nav:
            // If you add CommentAuthor/Author navigation later, seed it here.

            ctx.SaveChanges();
        }

        using (var scope = rootProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A) baseline: no include => navs should be null (clone behavior)
            var noInclude = ctx.Set<Blog>()
                .OrderBy(b => b.Id)
                .ToList();

            if (noInclude.Count != 2) throw new Exception($"[NoInclude] expected 2 but got {noInclude.Count}");

            foreach (var b in noInclude)
            {
                if (b.Detail != null) throw new Exception($"[NoInclude] expected Blog {b.Id}.Detail == null");
                if (b.Posts != null) throw new Exception($"[NoInclude] expected Blog {b.Id}.Posts == null");
            }

            Console.WriteLine("[NoInclude] OK: Detail/Posts are null");

            // B) Multiple + Nested:
            // Include Detail (reference) + Include Posts (collection) + ThenInclude Comments (collection)
            var withNested = ctx.Set<Blog>()
                .Include(b => b.Detail)
                .Include(b => b.Posts)
                    .ThenInclude(p => p.Comments)
                .OrderBy(b => b.Id)
                .ToList();

            AssertBlogsDetailPostsComments(withNested);

            // C) Two sibling ThenInclude on same collection (requires a second nav under BlogPost)
            //
            // If you DON'T have a second navigation (e.g. Tags), keep this block disabled.
            // Once you add BlogPost.Tags, enable it and fill the asserts.
            //
            // var withSiblingThenIncludes = ctx.Set<Blog>()
            //     .Include(b => b.Posts)
            //         .ThenInclude(p => p.Comments)
            //     .Include(b => b.Posts)
            //         .ThenInclude(p => p.Tags) // <- you need to add this navigation
            //     .OrderBy(b => b.Id)
            //     .ToList();
            //
            // AssertBlogsPostsCommentsAndTags(withSiblingThenIncludes);

            // D) Deep ThenInclude (2-level ThenInclude): Posts -> Comments -> (3rd level)
            //
            // This requires PostComment has another navigation (e.g. Author).
            // If you add it, enable this block and seed accordingly.
            //
            // var withDeepThenInclude = ctx.Set<Blog>()
            //     .Include(b => b.Posts)
            //         .ThenInclude(p => p.Comments)
            //             .ThenInclude(c => c.Author) // <- you need to add this navigation
            //     .OrderBy(b => b.Id)
            //     .ToList();
            //
            // AssertBlogsPostsCommentsAuthors(withDeepThenInclude);

            Console.WriteLine("=== INCLUDE (NESTED + MULTIPLE) TEST PASSED ===");
        }
    }

    private static void AssertBlogsDetailPostsComments(List<Blog> blogs)
    {
        if (blogs.Count != 2) throw new Exception($"[Include] expected 2 but got {blogs.Count}");

        foreach (var blog in blogs)
        {
            // reference include
            if (blog.Detail == null)
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Detail is null");
            if (blog.Detail.Blog == null || !ReferenceEquals(blog, blog.Detail.Blog))
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Detail.Blog fix-up missing/wrong");

            // collection include
            if (blog.Posts == null)
                throw new Exception($"[Include] FAILED: Blog {blog.Id}.Posts is null");

            if (blog.Id == 1 && blog.Posts.Count != 2)
                throw new Exception($"[Include] FAILED: Blog 1 expected 2 posts but got {blog.Posts.Count}");
            if (blog.Id == 2 && blog.Posts.Count != 1)
                throw new Exception($"[Include] FAILED: Blog 2 expected 1 post but got {blog.Posts.Count}");

            foreach (var post in blog.Posts)
            {
                // inverse for posts
                if (post.Blog == null || !ReferenceEquals(blog, post.Blog))
                    throw new Exception($"[Include] FAILED: Post {post.Id}.Blog fix-up missing/wrong");

                // nested collection include
                if (post.Comments == null)
                    throw new Exception($"[ThenInclude] FAILED: Post {post.Id}.Comments is null");

                // inverse for comments
                foreach (var c in post.Comments)
                {
                    if (c.Post == null || !ReferenceEquals(post, c.Post))
                        throw new Exception($"[ThenInclude] FAILED: Comment {c.Id}.Post fix-up missing/wrong");
                }
            }

            Console.WriteLine($"[Include] OK: Blog {blog.Id} Detail+Posts+Comments loaded");
        }
    }

    // Enable once you have BlogPost.Tags (or any 2nd nav) and seed them.
    // private static void AssertBlogsPostsCommentsAndTags(List<Blog> blogs) { ... }

    // Enable once you have PostComment.Author (or any 3rd-level nav) and seed them.
    // private static void AssertBlogsPostsCommentsAuthors(List<Blog> blogs) { ... }
}