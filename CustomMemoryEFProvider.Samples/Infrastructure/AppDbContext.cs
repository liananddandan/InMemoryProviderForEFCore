using CustomEFCoreProvider.Samples.Entities;
using CustomMemoryEFProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CustomEFCoreProvider.Samples.Infrastructure;

public class AppDbContext : DbContext
{
    // todo: using EFCore to DI this DbSet
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<BlogDetail> BlogDetails => Set<BlogDetail>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    
    // ✅ 核心修复：添加带 DbContextOptions 的构造函数（必须传给基类）
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // 保留无参构造（可选，兜底用）
    public AppDbContext() { }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder
                .UseCustomMemoryDb("MyTestDatabase")
                .LogTo(Console.WriteLine); // Enable EF Core logging to console
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(this.GetType().Assembly);
    }
}