using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomEFCoreProvider.Samples.Entities.Config;

public class BlogNoteConfig : IEntityTypeConfiguration<BlogNote>
{
    public void Configure(EntityTypeBuilder<BlogNote> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Text).IsRequired();
        
    }
}