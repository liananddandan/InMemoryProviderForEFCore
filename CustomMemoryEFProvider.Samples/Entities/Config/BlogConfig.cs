using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomEFCoreProvider.Samples.Entities.Config;

public class BlogConfig : IEntityTypeConfiguration<Blog>
{
    public void Configure(EntityTypeBuilder<Blog> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired();

        // 1:1 reference navigation
        builder.HasOne(x => x.Detail)
            .WithOne(x => x.Blog)
            .HasForeignKey<BlogDetail>(x => x.BlogId);
        
        // NEW: 1:N collection navigation
        builder.HasMany(x => x.Posts)
            .WithOne(x => x.Blog)
            .HasForeignKey(x => x.BlogId);
    }
}