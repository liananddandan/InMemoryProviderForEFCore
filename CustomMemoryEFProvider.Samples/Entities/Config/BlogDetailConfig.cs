using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomEFCoreProvider.Samples.Entities.Config;

public class BlogDetailConfig : IEntityTypeConfiguration<BlogDetail>
{
    public void Configure(EntityTypeBuilder<BlogDetail> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Description)
            .IsRequired();

        builder.HasIndex(x => x.BlogId)
            .IsUnique(); // 1:1 强约束（重要）    
    }
}