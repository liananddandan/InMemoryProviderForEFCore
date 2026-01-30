using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomEFCoreProvider.Samples.Entities.Config;

public class PostCommentConfig : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Content).IsRequired();

        builder.HasOne(x => x.Post)
            .WithMany(p => p.Comments)
            .HasForeignKey(x => x.BlogPostId);    }
}